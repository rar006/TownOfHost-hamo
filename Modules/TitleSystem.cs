using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HarmonyLib;
using Hazel;
using TMPro;
using UnityEngine;

namespace TownOfHost;

public static partial class TitleSystem
{
    public sealed class TitleDef
    {
        public string Name = "";
        public string Color = "#ffffff";
        public double Weight;
        public double Probability;
        public long SellPrice;
    }

    private sealed class UserRecord
    {
        public readonly HashSet<string> Owned = new(StringComparer.Ordinal);
        public readonly List<string> Equipped = new();
        public readonly Dictionary<string, string> Marks = new(StringComparer.Ordinal);
        public readonly Dictionary<string, long> Counts = new(StringComparer.Ordinal);
        public readonly Dictionary<string, long> Prices = new(StringComparer.Ordinal);
        public readonly Dictionary<string, double> SellMults = new(StringComparer.Ordinal);
        public long Wallet;
        public DateTime FetchedAt = DateTime.MinValue;
    }

    public static readonly bool Enabled = true;
    private static bool stateLogged;

    internal static bool RoomEnabled => Enabled && (Options.OptionTitleEnabled?.GetBool() ?? true);

    // nis = 売る / nip = SHOP / nib = 買う / nim = 持っている hm
    // nic = カジノ
    private static readonly string[] TitleSubs = { "nil", "nii", "nih", "nio", "nit", "code", "nis", "nip", "nib", "nim", "nic" };

    private static bool TryParseCommand(string raw, out string sub, out string[] args)
    {
        sub = null; args = Array.Empty<string>();
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var tokens = raw.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;
        int start;
        if (tokens[0].Equals(CmdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (tokens.Length < 2) return false;
            sub = tokens[1].ToLowerInvariant();
            start = 2;
        }
        else if (tokens[0].Length > 1 && tokens[0][0] == '/')
        {
            sub = tokens[0].Substring(1).ToLowerInvariant();
            start = 1;
        }
        else return false;
        if (Array.IndexOf(TitleSubs, sub) < 0) { sub = null; return false; }
        args = tokens.Skip(start).ToArray();
        return true;
    }

    private const string Tag = "TitleSystem";
    private const string CmdPrefix = "/cmd";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly TimeSpan UserTtl = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan CatalogTtl = TimeSpan.FromMinutes(2);

    // 二つ名APIは、アンケートAPIと同じDiscord Bot(同じURL・同じ認証キー)の /api/titles にある。
    // 例: https://xxxx.trycloudflare.com/api/survey -> https://xxxx.trycloudflare.com/api/titles
    private static string BaseUrl
    {
        get
        {
            var u = (TownOfHost.Modules.SurveyRemoteConfig.EndpointUrl ?? "").Trim().TrimEnd('/');
            return u.EndsWith("/survey", StringComparison.Ordinal)
                ? u.Substring(0, u.Length - "/survey".Length) + "/titles"
                : "";
        }
    }
    private static bool IsConfigured => BaseUrl.Length > 0;

    // ---- 状態 ----
    private static readonly ConcurrentDictionary<string, TitleDef> Catalog = new();
    private static DateTime catalogFetchedAt = DateTime.MinValue;
    private static volatile int maxEquipped = 3;
    private static volatile int drawCooldownNow = 60;
    private static volatile bool cooldownEventActive;
    private static string CooldownText => cooldownEventActive ? $"<color=#ffd36e>イベント中! 引くクールタイム{drawCooldownNow}秒</color>" : $"引くクールタイム{drawCooldownNow}秒";
    private static readonly ConcurrentDictionary<string, UserRecord> Users = new();
    private static readonly ConcurrentDictionary<byte, (string Fc, string[] Ids)> Shown = new();
    private static readonly Dictionary<byte, (string Fc, float At)> FcCache = new();

    // ---- メインスレッドへ処理を戻すためのキュー(通信後の継続はワーカースレッドで動くため) ----
    private static readonly ConcurrentQueue<Action> MainQueue = new();
    private static void RunOnMain(Action a) => MainQueue.Enqueue(a);
    private static void DrainMainQueue()
    {
        while (MainQueue.TryDequeue(out var a))
        {
            try { a(); } catch (Exception e) { Logger.Exception(e, Tag); }
        }
    }

    private static bool initDone;
    private static float notifyAt = -1f;
    private static int lastPlayerCount = -1;

    // =====================================================================
    //  サーバー通信
    // =====================================================================
    public static string NormalizeFriendCode(string raw)
        => string.IsNullOrWhiteSpace(raw) ? "" : raw.Trim().ToLowerInvariant();

    private static void Authorize(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(TownOfHost.Modules.SurveyRemoteConfig.ApiKey))
            request.Headers.Add("Authorization", $"Bearer {TownOfHost.Modules.SurveyRemoteConfig.ApiKey}");
    }

    private static async Task<(int Status, JsonDocument Doc)> RequestAsync(HttpMethod method, string url, object body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        Authorize(request);
        if (body != null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var res = await Client.SendAsync(request).ConfigureAwait(false);
        var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        JsonDocument doc = null;
        try { doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text); } catch { }
        return ((int)res.StatusCode, doc);
    }

    private static double GetDouble(JsonElement e, string name, double def = 0)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : def;
    private static string GetString(JsonElement e, string name, string def = "")
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : def;

    private static async Task EnsureCatalogAsync(bool force = false)
    {
        if (!IsConfigured) return;
        if (!force && Catalog.Count > 0 && DateTime.UtcNow - catalogFetchedAt < CatalogTtl) return;
        try
        {
            var (status, doc) = await RequestAsync(HttpMethod.Get, $"{BaseUrl}/catalog").ConfigureAwait(false);
            using (doc)
            {
                if (status != 200 || doc == null) { Logger.Warn($"catalog failed: {status}", Tag); return; }
                var root = doc.RootElement;
                if (!root.TryGetProperty("titles", out var titles) || titles.ValueKind != JsonValueKind.Object) return;
                var fresh = new Dictionary<string, TitleDef>();
                foreach (var p in titles.EnumerateObject())
                {
                    var name = GetString(p.Value, "name");
                    if (name.Length == 0) continue;
                    fresh[p.Name] = new TitleDef
                    {
                        Name = name,
                        Color = GetString(p.Value, "color", "#ffffff"),
                        Weight = GetDouble(p.Value, "weight"),
                        Probability = GetDouble(p.Value, "probability"),
                        SellPrice = (long)GetDouble(p.Value, "sellPrice"),
                    };
                }
                Catalog.Clear();
                foreach (var kv in fresh) Catalog[kv.Key] = kv.Value;
                if (root.TryGetProperty("settings", out var st))
                {
                    var m = (int)GetDouble(st, "maxEquipped", 3);
                    if (m >= 1 && m <= 10) maxEquipped = m;
                    drawCooldownNow = (int)GetDouble(st, "drawCooldownSecNow", GetDouble(st, "drawCooldownSec", 60));
                    cooldownEventActive = st.TryGetProperty("cooldownEvent", out var ce) && ce.ValueKind == JsonValueKind.Object;
                }
                catalogFetchedAt = DateTime.UtcNow;
            }
        }
        catch (Exception e) { Logger.Exception(e, Tag); }
    }

    private static UserRecord ParseUser(JsonElement root)
    {
        var rec = new UserRecord { FetchedAt = DateTime.UtcNow };
        if (root.TryGetProperty("titles", out var owned) && owned.ValueKind == JsonValueKind.Array)
            foreach (var e in owned.EnumerateArray()) if (e.ValueKind == JsonValueKind.String) rec.Owned.Add(e.GetString());
        if (root.TryGetProperty("equipped", out var eq) && eq.ValueKind == JsonValueKind.Array)
            foreach (var e in eq.EnumerateArray()) if (e.ValueKind == JsonValueKind.String) rec.Equipped.Add(e.GetString());
        if (root.TryGetProperty("marks", out var mk) && mk.ValueKind == JsonValueKind.Object)
            foreach (var p in mk.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.String) { rec.Marks[p.Name] = p.Value.GetString(); KeyMarks[p.Name] = p.Value.GetString(); }
        if (root.TryGetProperty("counts", out var ct) && ct.ValueKind == JsonValueKind.Object)
            foreach (var p in ct.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt64(out var n)) rec.Counts[p.Name] = n;
        if (root.TryGetProperty("prices", out var pr) && pr.ValueKind == JsonValueKind.Object)
            foreach (var p in pr.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetInt64(out var n)) rec.Prices[p.Name] = n;
        if (root.TryGetProperty("sellMults", out var sm) && sm.ValueKind == JsonValueKind.Object)
            foreach (var p in sm.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Number) rec.SellMults[p.Name] = p.Value.GetDouble();
        rec.Wallet = (long)GetDouble(root, "wallet");
        return rec;
    }

    private static string MarkOf(string fc, string id)
    {
        var key = NormalizeFriendCode(fc ?? "");
        if (key.Length > 0 && Users.TryGetValue(key, out var rec) && rec.Marks.TryGetValue(id, out var m)) return m;
        return KeyMarks.TryGetValue(id, out var km) ? km : "";
    }

    // ===== 属性ごとに分けて持つ =====
    // インベントリの1つ1つは「二つ名ID」か「二つ名ID+属性ID+属性ID…」のキー(属性違いは別のもの)
    private const char KeySep = '+';
    internal static string TitleOf(string key) => string.IsNullOrEmpty(key) ? "" : key.Split(KeySep)[0];
    private static readonly ConcurrentDictionary<string, string> KeyMarks = new(StringComparer.Ordinal);
    private static bool HasTitle(string key) => Catalog.ContainsKey(TitleOf(key));
    private static string KeyLabel(string key) => HasTitle(key) ? (KeyMarks.TryGetValue(key, out var m) ? m : "") + Colored(Catalog[TitleOf(key)]) : key;

    private static async Task<UserRecord> FetchUserAsync(string friendCode, bool force = false)
    {
        var fc = NormalizeFriendCode(friendCode);
        if (fc.Length == 0 || !IsConfigured) return new UserRecord();
        if (!force && Users.TryGetValue(fc, out var cached) && DateTime.UtcNow - cached.FetchedAt < UserTtl) return cached;
        try
        {
            var (status, doc) = await RequestAsync(HttpMethod.Get, $"{BaseUrl}/user/{Uri.EscapeDataString(fc)}").ConfigureAwait(false);
            using (doc)
            {
                if (status != 200 || doc == null)
                {
                    Logger.Warn($"user failed: {status}", Tag);
                    return Users.TryGetValue(fc, out var old) ? old : new UserRecord();
                }
                var rec = ParseUser(doc.RootElement);
                Users[fc] = rec;
                return rec;
            }
        }
        catch (Exception e)
        {
            Logger.Exception(e, Tag);
            return Users.TryGetValue(fc, out var old) ? old : new UserRecord();
        }
    }

    // =====================================================================
    //  プレイヤー情報
    // =====================================================================
    private static string FriendCodeOf(PlayerControl pc)
    {
        try
        {
            if (pc == null) return "";
            var client = AmongUsClient.Instance?.GetClientFromCharacter(pc);
            return NormalizeFriendCode(client?.FriendCode);
        }
        catch { return ""; }
    }

    private static string CachedFriendCode(PlayerControl pc)
    {
        if (FcCache.TryGetValue(pc.PlayerId, out var c) && Time.time - c.At < 3f) return c.Fc;
        var fc = FriendCodeOf(pc);
        FcCache[pc.PlayerId] = (fc, Time.time);
        return fc;
    }

    private static string[] ValidIds(IEnumerable<string> ids) => ids.Where(HasTitle).ToArray();

    // =====================================================================
    //  ロビー処理(毎フレーム軽量に呼ばれる)
    // =====================================================================
    private static void LobbyTick()
    {
        if (AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost) HostTradeTick();
        if (!GameStates.IsLobby)
        {
            gameStarting = false; // ロビーを出た(試合が始まった)ので、次にロビーに戻ったらまた送る
            lobbyEnteredAt = -1f;
            PendingForce.Clear();
            initDone = false;
            claimDone = false;
            lastPlayerCount = -1;
            notifyAt = -1f;
            // 試合が始まったら(ロビーを離れたら)交換は強制キャンセル
            if (trade != null) CancelTrade("試合が始まったため、二つ名の交換はキャンセルされました。", true);
            return;
        }
        TradeTick();
        VanillaNameTick();
        // hamoのロビー(ホストがhamo)に入ったら、今やっている「参加者限定配布」を受け取る(ロビーごとに1回)
        if (!claimDone && GameStates.IsModHost && PlayerControl.LocalPlayer != null)
        {
            claimDone = true;
            ClaimEventsAsync(FriendCodeOf(PlayerControl.LocalPlayer));
        }
        if (!initDone)
        {
            initDone = true;
            InitLocalAsync();
        }
        // 参加者が増減したら、少し待って「更新したよ」を送り直す(途中参加者にも見えるように)
        int count = PlayerControl.AllPlayerControls.Count;
        if (count != lastPlayerCount)
        {
            lastPlayerCount = count;
            notifyAt = Time.time + 1.5f;
        }
        if (notifyAt > 0f && Time.time >= notifyAt)
        {
            notifyAt = -1f;
            SendNotify();
            RefreshVanillaPlayers();
        }
    }

    private static bool claimDone;

    // =====================================================================
    //  MOD未導入の人にも二つ名を見せる(ホストのみ・ロビーのみ)
    //  MOD導入者は自分の画面で名前の上に描くので、未導入の人にだけ「名前そのもの」に二つ名を付けて送る。
    //  ホスト側の本当の名前は変えない(未導入の人それぞれにだけ見える名前を送る)。
    // =====================================================================
    private static float nextVanillaNameAt;
    private static float nextVanillaForceAt;
    private static float lobbyEnteredAt = -1f;
    private const float LobbySendDelay = 6f;
    private const int VanillaNameBudget = 10;
    private static readonly HashSet<(byte, byte)> PendingForce = new();

    public static void InvalidateVanillaNames()
    {
        try
        {
            if (!Enabled || !GameStates.IsLobby || Main.LastNotifyNames == null) return;
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            var removed = false;
            foreach (var key in Main.LastNotifyNames.Keys.ToList())
            {
                var v = Main.LastNotifyNames[key];
                if (v != null && v.StartsWith(VanillaNameHead)) { Main.LastNotifyNames.Remove(key); removed = true; }
            }
            // 吹き出しの表示より後に送り直したいので、少しだけ待つ
            if (removed) nextVanillaNameAt = Time.time + 0.3f;
        }
        catch { }
    }
    private static float nextHostRefreshAt;

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.HandleRpc))]
    private static class IncomingSetNameInvalidatePatch
    {
        public static void Postfix(PlayerControl __instance, [HarmonyArgument(0)] byte callId)
        {
            try
            {
                if (callId != (byte)RpcCalls.SetName && callId != (byte)RpcCalls.CheckName) return;
                if (__instance == null || Main.LastNotifyNames == null || !GameStates.IsLobby) return;
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                foreach (var key in Main.LastNotifyNames.Keys.Where(k => k.Item1 == __instance.PlayerId).ToList())
                    Main.LastNotifyNames.Remove(key);
                nextVanillaNameAt = Time.time + 0.3f;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.RpcSetName))]
    private static class RpcSetNameInvalidatePatch
    {
        public static void Postfix(PlayerControl __instance)
        {
            try
            {
                if (__instance == null || Main.LastNotifyNames == null || !GameStates.IsLobby) return;
                foreach (var key in Main.LastNotifyNames.Keys.Where(k => k.Item1 == __instance.PlayerId).ToList())
                    Main.LastNotifyNames.Remove(key);
                nextVanillaNameAt = 0f; // 次のフレームですぐ送り直す
            }
            catch { }
        }
    }

    private static string TitlePrefixOf(PlayerControl pc)
    {
        // 初めての人のマーク(便利機能)は、二つ名を使わない部屋でも出す
        var mark = pc != null && TownOfHost.Modules.PlayerStatsSync.FirstTimers.Contains(pc.PlayerId) ? TownOfHost.Modules.PlayerStatsSync.FirstTimerMark : "";
        if (pc == null || !RoomEnabled || !Shown.TryGetValue(pc.PlayerId, out var shown) || shown.Fc != CachedFriendCode(pc)) return mark;
        var parts = new List<string>();
        foreach (var id in shown.Ids)
        {
            if (!Catalog.TryGetValue(TitleOf(id), out var def)) continue;
            var color = (def.Color ?? "#ffffff").Trim();
            if (!color.StartsWith("#")) color = "#" + color;
            parts.Add($"{MarkOf(shown.Fc, id)}<color={color}>{def.Name}</color>");
        }
        if (mark.Length > 0) parts.Insert(0, mark);
        return parts.Count == 0 ? "" : string.Join(" ", parts);
    }

    private const string VanillaNameHead = "<line-height=-72%>"; // -100%だと二つ名が上に行きすぎてチャット欄からはみ出るので少し下げる
    internal static string VanillaChatName(PlayerControl target, string baseName)
    {
        if (!Enabled || !GameStates.IsLobby || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return null;
        var n = VanillaNameOf(target, baseName);
        return ReferenceEquals(n, baseName) ? null : n;
    }

    private static string VanillaNameOf(PlayerControl target, string baseName)
    {
        // 部屋の設定で二つ名がOFFの時は、TitlePrefixOf が二つ名を返さない(初めての人のマークだけ)
        var titles = TitlePrefixOf(target);
        if (titles.Length == 0) return baseName;
        return $"{VanillaNameHead}{baseName}\r\n<size=60%>{titles}</size></line-height>";
    }

    private static bool gameStarting;
    private static float gameStartingAt;

    private static void VanillaNameTick()
    {
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
        // 開始が取り消されてロビーに残った時は、15秒たったら元に戻す
        if (gameStarting && Time.time - gameStartingAt > 15f) gameStarting = false;
        if (gameStarting) return;
        if (lobbyEnteredAt < 0f) lobbyEnteredAt = Time.time;
        if (Time.time - lobbyEnteredAt < LobbySendDelay) return;
        if (Time.time < nextVanillaNameAt) return;
        nextVanillaNameAt = Time.time + 1f;
        var budget = VanillaNameBudget;
        // 他の処理(チャット送信時の一時的な名前変更など)で名前が上書きされても、5秒ごとに送り直して直す
        var forceResend = Time.time >= nextVanillaForceAt;
        if (forceResend) nextVanillaForceAt = Time.time + 5f;
        // 30秒ごとに全員の二つ名をサーバーから読み直す(通知の取りこぼし対策)
        if (Time.time >= nextHostRefreshAt) { nextHostRefreshAt = Time.time + 30f; RefreshVanillaPlayers(); }
        try
        {
            var all = PlayerControl.AllPlayerControls.ToArray().Where(p => p != null && p.Data != null).ToArray();
            foreach (var seer in all)
            {
                if (seer.AmOwner || seer.IsModClient()) continue; // 未導入の人だけ
                foreach (var target in all)
                {
                    var baseName = target.Data.PlayerName ?? target.name;
                    // ホストの名前は「ホスト：」の欄にも出るので、二つ名は付けない
                    var name = VanillaNameOf(target, baseName); // ホスト本人にも付ける
                    var key = (target.PlayerId, seer.PlayerId);
                    // 二つ名がある人の分は、ときどき強制で送り直す(上書きされて消えたままになるのを防ぐ)。
                    // 1回で送り切れなかった分は覚えておき、次の回の続きから送る
                    if (forceResend && !ReferenceEquals(name, baseName)) PendingForce.Add(key);
                    var force = PendingForce.Contains(key);
                    // 前回と同じ名前なら送らない(上限にも数えない)
                    if (!force && Main.LastNotifyNames != null && Main.LastNotifyNames.TryGetValue(key, out var last) && last == name) continue;
                    if (budget <= 0) { nextVanillaNameAt = Time.time + 0.5f; continue; } // 残りは0.5秒後に(強制の分は覚えたまま)
                    target.RpcSetNamePrivate(name, false, seer, force);
                    PendingForce.Remove(key);
                    budget--;
                }
            }
        }
        catch (Exception e) { Logger.Warn($"VanillaNameTick: {e.Message}", Tag); }
    }

    public static void StripVanillaNamesForGame()
    {
        if (!Enabled || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
        gameStarting = true;
        gameStartingAt = Time.time;
        PendingForce.Clear();
        // 「前回送った名前」に二つ名付きの名前が残っていると、試合中の名前の更新で同じだと思われて送られないことがあるので消す
        if (Main.LastNotifyNames != null)
            foreach (var key in Main.LastNotifyNames.Keys.ToList())
                if (Main.LastNotifyNames[key]?.StartsWith(VanillaNameHead) == true) Main.LastNotifyNames.Remove(key);
        try
        {
            var all = PlayerControl.AllPlayerControls.ToArray().Where(p => p != null && p.Data != null).ToArray();
            foreach (var seer in all)
            {
                if (seer.AmOwner || seer.IsModClient()) continue;
                foreach (var target in all)
                    if (TitlePrefixOf(target).Length > 0) target.RpcSetNamePrivate(target.Data.PlayerName ?? target.name, false, seer, true);
            }
        }
        catch (Exception e) { Logger.Warn($"StripVanillaNamesForGame: {e.Message}", Tag); }
    }

    private static async void ClaimEventsAsync(string fc)
    {
        try
        {
            if (!IsConfigured || string.IsNullOrEmpty(fc)) return;
            var (status, doc) = await RequestAsync(HttpMethod.Post, $"{BaseUrl}/claim", new { friendCode = fc }).ConfigureAwait(false);
            using (doc)
            {
                if (status != 200 || doc == null) return;
                if (!doc.RootElement.TryGetProperty("granted", out var g) || g.ValueKind != JsonValueKind.Array || g.GetArrayLength() == 0) return;
                var names = new List<string>();
                foreach (var e in g.EnumerateArray())
                    names.Add($"<color={GetString(e, "color", "#ffffff")}>{GetString(e, "name")}</color>");
                await FetchUserAsync(fc, true).ConfigureAwait(false);
                Reply($"限定配布の二つ名を受け取りました!  {string.Join(" ", names)}\n/cmd nii で確認・/cmd nih で付けられます。");
            }
        }
        catch (Exception e) { Logger.Exception(e, Tag); }
    }

    private static async void InitLocalAsync()
    {
        try
        {
            // ゲームのオブジェクトに触る値は、await の前(メインスレッド)で取っておく
            // (ロビーに入った直後は LocalPlayer がまだ無いことがあるので、その時は次のフレームでやり直す)
            if (PlayerControl.LocalPlayer == null) { initDone = false; return; }
            var fc = FriendCodeOf(PlayerControl.LocalPlayer);
            var localId = PlayerControl.LocalPlayer.PlayerId;
            await EnsureCatalogAsync(true).ConfigureAwait(false);
            if (fc.Length == 0) return;
            var rec = await FetchUserAsync(fc, true).ConfigureAwait(false);
            SetShown(localId, fc, rec.Equipped);
            if (rec.Equipped.Count > 0) RunOnMain(SendNotify);
        }
        catch (Exception e) { Logger.Exception(e, Tag); }
    }

    private static void SetShown(byte playerId, string fc, IEnumerable<string> ids)
    {
        var valid = ValidIds(ids);
        if (AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost)
            Logger.Info($"表示する二つ名: player={playerId} fc={fc} ids=[{string.Join(",", valid)}]", Tag);
        if (valid.Length == 0) Shown.TryRemove(playerId, out _);
        else Shown[playerId] = (fc, valid);
    }

    // =====================================================================
    //  RPC(「二つ名を変えたよ」の通知だけ。中身はサーバーの記録を読み直して表示する=なりすまし防止)
    // =====================================================================
    private static void SendNotify()
    {
        try
        {
            if (AmongUsClient.Instance == null || PlayerControl.LocalPlayer == null) return;
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SyncTitle, SendOption.Reliable, -1);
            writer.Write((byte)1);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
        catch (Exception e) { Logger.Exception(e, Tag); }
    }

    // RPCの種類(1バイト目)。1 = 二つ名を変えた / 2〜5 = 交換 / 6 = ホストが「この人(MOD未導入)の二つ名が変わった」と知らせる
    private const byte RpcNotify = 1, RpcTradePropose = 2, RpcTradeAccept = 3, RpcTradeCancel = 4, RpcTradeDone = 5, RpcNotifyFor = 6, RpcFirstTimer = 7;

    internal static void BroadcastFirstTimer(byte playerId)
    {
        try
        {
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || PlayerControl.LocalPlayer == null) return;
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SyncTitle, SendOption.Reliable, -1);
            writer.Write(RpcFirstTimer);
            writer.Write(playerId);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
            InvalidateVanillaNames();
        }
        catch (Exception e) { Logger.Exception(e, Tag); }
    }

    private static void SendNotifyFor(byte playerId)
    {
        try
        {
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || PlayerControl.LocalPlayer == null) return;
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SyncTitle, SendOption.Reliable, -1);
            writer.Write(RpcNotifyFor);
            writer.Write(playerId);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
        catch (Exception e) { Logger.Exception(e, Tag); }
    }

    private static void RefreshVanillaPlayers()
    {
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
        foreach (var pc in PlayerControl.AllPlayerControls.ToArray())
        {
            if (pc == null || pc.AmOwner) continue;
            var id = pc.PlayerId;
            var fc = FriendCodeOf(pc);
            if (fc.Length == 0) continue;
            // MOD導入者の分もホストが自分で読む(相手からの通知が届かなかった時でも、未導入の人に見せられるように)
            RefreshForHostAsync(id, fc, notify: !pc.IsModClient());
        }
    }

    private static async void RefreshForHostAsync(byte id, string fc, bool notify = true)
    {
        try
        {
            await EnsureCatalogAsync().ConfigureAwait(false);
            var rec = await FetchUserAsync(fc, true).ConfigureAwait(false);
            SetShown(id, fc, rec.Equipped);
            if (notify && rec.Equipped.Count > 0) RunOnMain(() => SendNotifyFor(id));
        }
        catch (Exception e) { Logger.Exception(e, Tag); }
    }

    public static void ReceiveRpc(PlayerControl sender, MessageReader reader)
    {
        byte kind = 1;
        try { kind = reader.ReadByte(); } catch { }
        if (!Enabled) return;
        if (sender == null || sender.AmOwner) return;
        if (kind == RpcFirstTimer)
        {
            // ホストからの「この人は初めての参加」(ホストからのものだけ信じる)
            if (AmongUsClient.Instance == null || sender.OwnerId != AmongUsClient.Instance.HostId) return;
            try { TownOfHost.Modules.PlayerStatsSync.FirstTimers.Add(reader.ReadByte()); } catch { }
            return;
        }
        if (kind == RpcNotifyFor)
        {
            // ホストからの「この人の二つ名が変わった」(ホストからのものだけ信じる。中身はサーバーから読み直す)
            if (!GameStates.IsLobby || AmongUsClient.Instance == null || sender.OwnerId != AmongUsClient.Instance.HostId) return;
            byte target;
            try { target = reader.ReadByte(); } catch { return; }
            var pc = PlayerControl.AllPlayerControls.ToArray().FirstOrDefault(p => p != null && p.PlayerId == target);
            if (pc != null) RefreshPlayerAsync(target, FriendCodeOf(pc));
            return;
        }
        if (kind == RpcNotify || kind > RpcTradeDone)
        {
            if (!GameStates.IsLobby) return;
            RefreshPlayerAsync(sender.PlayerId, FriendCodeOf(sender));
            return;
        }
        try { ReceiveTradeRpc(sender, kind, reader); }
        catch (Exception e) { Logger.Exception(e, Tag); }
    }

    private static async void RefreshPlayerAsync(byte playerId, string fc)
    {
        try
        {
            if (fc.Length == 0) { Shown.TryRemove(playerId, out _); return; }
            await EnsureCatalogAsync().ConfigureAwait(false);
            var rec = await FetchUserAsync(fc, true).ConfigureAwait(false);
            SetShown(playerId, fc, rec.Equipped);
        }
        catch (Exception e) { Logger.Exception(e, Tag); }
    }

    // =====================================================================
    //  表示(名前の上)
    // =====================================================================
    internal static void UpdateNameTag(PlayerControl pc)
    {
        try
        {
            if (pc == null) return;
            if (!stateLogged && pc.AmOwner)
            {
                // どの機能が入ったDLLかをログで確認できるようにする(切り分け用)
                stateLogged = true;
                Logger.Info($"二つ名={Enabled}, 五十音={RoleGojuon.Enabled}, SyncTitle RPC={(byte)CustomRPC.SyncTitle}, API={(IsConfigured ? "設定あり" : "未設定")}", Tag);
            }
            if (!Enabled) return;
            if (pc.AmOwner) { DrainMainQueue(); LobbyTick(); }
            var nameText = pc.cosmetics?.nameText;
            if (nameText == null) return;

            var tr = nameText.transform.Find("TitleText");
            string text = null;
            if (GameStates.IsLobby && !gameStarting && !GameStates.InGame && RoomEnabled && Shown.TryGetValue(pc.PlayerId, out var shown) && shown.Fc == CachedFriendCode(pc))
            {
                var parts = new List<string>();
                foreach (var id in shown.Ids)
                {
                    if (!Catalog.TryGetValue(TitleOf(id), out var def)) continue;
                    var color = (def.Color ?? "#ffffff").Trim();
                    if (!color.StartsWith("#")) color = "#" + color;
                    parts.Add($"{MarkOf(shown.Fc, id)}<color={color}>{def.Name}</color>");
                }
                if (parts.Count > 0) text = "<size=65%>" + string.Join(" ", parts) + "</size>"; // 名前より小さく表示
            }
            // 初めての人のマーク(便利機能)
            if (GameStates.IsLobby && TownOfHost.Modules.PlayerStatsSync.FirstTimers.Contains(pc.PlayerId))
                text = text == null ? "<size=65%>" + TownOfHost.Modules.PlayerStatsSync.FirstTimerMark + "</size>" : text.Replace("<size=65%>", "<size=65%>" + TownOfHost.Modules.PlayerStatsSync.FirstTimerMark + " ");
            if (text == null)
            {
                if (tr != null && tr.gameObject.activeSelf) tr.gameObject.SetActive(false);
                return;
            }
            TextMeshPro tmp;
            if (tr == null)
            {
                tmp = UnityEngine.Object.Instantiate(nameText);
                // 複製元の子要素(RoleTextなど)は不要なので消す
                for (int i = tmp.transform.childCount - 1; i >= 0; i--)
                    UnityEngine.Object.Destroy(tmp.transform.GetChild(i).gameObject);
                tmp.transform.SetParent(nameText.transform);
                tmp.transform.localPosition = new Vector3(0f, 0.2f, 0f);
                tmp.transform.localScale = new Vector3(1f, 1f, 1f);
                tmp.gameObject.name = "TitleText";
            }
            else tmp = tr.GetComponent<TextMeshPro>();
            if (tmp == null) return;
            tmp.fontSize = nameText.fontSize;
            if (tmp.transform.localPosition.y > 0.21f) tmp.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            tmp.gameObject.SetActive(true);
            tmp.enabled = true;
            if (tmp.text != text) tmp.text = text;
        }
        catch (Exception e) { Logger.Exception(e, Tag); }
    }

    // =====================================================================
    //  チャットコマンド  /cmd nil | nii | nih | nio | nit   (ロビーのみ)
    // =====================================================================
    internal static bool TryHandleCommand(string raw)
    {
        if (!Enabled || !TryParseCommand(raw, out var sub, out var args)) return false;
        // ゲーム中は別のコマンドかもしれないので、二つ名では横取りしない
        if (!GameStates.IsLobby) return false;

        if (!RoomEnabled) { Reply("この部屋では二つ名が使えない設定になっています。"); return true; }
        if (IsCommandDisabled(sub)) { Reply("このコマンドは部屋の設定で無効化されています。"); return true; }

        if (sub == "nit")
        {
            if (UseHostTrade(args)) RouteToHostTrade(raw, args);
            else HandleTradeCommand(args);
        }
        else RunCommandAsync(sub, args);
        return true;
    }

    private static bool UseHostTrade(string[] args)
    {
        if (args.Length == 0) return false;
        var first = args[0].ToLowerInvariant();
        if (args.Length == 1 && (IsYes(first) || IsNo(first)))
        {
            if (trade != null) return false; // 端末どうしの交換中
            return AmongUsClient.Instance != null && (!AmongUsClient.Instance.AmHost || InHostTrade(PlayerControl.LocalPlayer.PlayerId));
        }
        int dummy;
        var isPropose = args.Length >= 3 && int.TryParse(args[^2], out dummy) && int.TryParse(args[^1], out dummy);
        var name = string.Join(" ", isPropose ? args[..^2] : args);
        var target = FindPlayerByName(name, out _);
        return target != null && !target.AmOwner && !target.IsModClient();
    }

    private static void RouteToHostTrade(string raw, string[] args)
    {
        if (AmongUsClient.Instance.AmHost) HostTradeCommand(PlayerControl.LocalPlayer, args);
        else ChatCommands.RequestCommandProcessingFromHost(raw.Trim()); // ホストに処理してもらう
    }

    internal static bool IsCommandDisabled(string sub)
    {
        if (!(Options.OptionCommandSetting?.GetBool() ?? false)) return false;
        var opt = sub switch
        {
            "nil" => Options.OptionCommandTitleList,
            "nii" => Options.OptionCommandTitleInventory,
            "nih" => Options.OptionCommandTitleEquip,
            "nio" => Options.OptionCommandTitleDraw,
            "nit" => Options.OptionCommandTitleTrade,
            "nis" or "nip" or "nib" or "nim" => Options.OptionCommandTitleShop,
            "nic" => Options.OptionCommandTitleCasino,
            _ => null,
        };
        return opt?.GetBool() ?? false;
    }

    private static void Reply(string msg)
    {
        // ホストが他のプレイヤー(MOD未導入)のコマンドを代わりに処理している時は、その人に返す
        var to = ReplyTo.Value;
        RunOnMain(() => Utils.SendMessage(msg, to != byte.MaxValue ? to : PlayerControl.LocalPlayer.PlayerId));
    }

    private static readonly System.Threading.AsyncLocal<byte> ReplyTo = new();

    internal static bool TryHandleRemoteCommand(PlayerControl player, string raw)
    {
        if (!Enabled || player == null || player.AmOwner || string.IsNullOrWhiteSpace(raw)) return false;
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return false;
        if (!TryParseCommand(raw, out var sub, out var args)) return false;
        if (!GameStates.IsLobby) return false;
        var id = player.PlayerId;
        if (!RoomEnabled) { Utils.SendMessage("この部屋では二つ名が使えない設定になっています。", id); return true; }
        if (IsCommandDisabled(sub)) { Utils.SendMessage("このコマンドは部屋の設定で無効化されています。", id); return true; }
        if (sub == "nit") { HostTradeCommand(player, args); return true; } // MOD未導入の人の交換はホストが仲介
        RunCommandAsync(sub, args, player);
        return true;
    }

    private static string Pct(double p) => (p * 100).ToString(p >= 0.1 ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "%";
    private static string Colored(TitleDef d) => $"<color={d.Color}>{d.Name}</color>";

    private static async void RunCommandAsync(string sub, string[] args, PlayerControl target = null)
    {
        var remote = target != null && !target.AmOwner;
        ReplyTo.Value = remote ? target.PlayerId : byte.MaxValue;
        try
        {
            if (!IsConfigured) { Reply("二つ名サーバー(Bot)のURLが設定されていません。"); return; }
            // ゲームのオブジェクトに触る値は、await の前(メインスレッド)で取っておく
            var who = target ?? PlayerControl.LocalPlayer;
            var fc = FriendCodeOf(who);
            var localId = who.PlayerId;
            if (fc.Length == 0) { Reply("フレンドコードを取得できませんでした。"); return; }

            await EnsureCatalogAsync(true).ConfigureAwait(false);
            if (Catalog.Count == 0) { Reply("二つ名の一覧を取得できませんでした。しばらくしてからもう一度お試しください。"); return; }

            if (sub == "nil") { await ListAllAsync(fc).ConfigureAwait(false); return; }
            if (sub == "nii") { await ShowInventoryAsync(fc).ConfigureAwait(false); return; }
            if (sub == "nih") { await EquipAsync(fc, localId, args).ConfigureAwait(false); return; }
            if (sub == "nio") { await DrawAsync(fc, localId).ConfigureAwait(false); return; }
            if (sub == "code") { await RedeemAsync(fc, args).ConfigureAwait(false); return; }
            if (sub == "nis") { await SellAsync(fc, localId, args).ConfigureAwait(false); return; }
            if (sub == "nip") { await ShowShopAsync(fc).ConfigureAwait(false); return; }
            if (sub == "nib") { await BuyAsync(fc, args).ConfigureAwait(false); return; }
            if (sub == "nim") { await ShowWalletAsync(fc).ConfigureAwait(false); return; }
            if (sub == "nic") { await CasinoAsync(fc, args).ConfigureAwait(false); return; }
        }
        catch (Exception e) { Logger.Exception(e, Tag); Reply("二つ名の処理でエラーが起きました。"); }
    }

    private static List<string> InventoryOf(UserRecord rec)
        => rec.Owned.Where(HasTitle).OrderBy(TitleOf, StringComparer.Ordinal).ThenBy(x => x.Length).ThenBy(x => x, StringComparer.Ordinal).ToList();

    private static async Task ListAllAsync(string fc)
    {
        var rec = await FetchUserAsync(fc, true).ConfigureAwait(false);
        var ids = Catalog.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList();
        var sb = new StringBuilder("<b>二つ名一覧</b>(✔=所持)\n");
        for (int i = 0; i < ids.Count; i++)
        {
            var d = Catalog[ids[i]];
            sb.Append(rec.Owned.Any(k => TitleOf(k) == ids[i]) ? "✔" : "　").Append(i + 1).Append(". ").Append(Colored(d))
              .Append(d.Probability > 0 ? $"  {Pct(d.Probability)}" : "  配布のみ").Append('\n');
        }
        sb.Append($"\n{CooldownText}");
        sb.Append("\n/cmd nio で引く(無料) / /cmd nii で持っているものを見る / /cmd nit で交換 / /cmd nip でSHOP");
        Reply(sb.ToString());
    }

    private static async Task ShowInventoryAsync(string fc)
    {
        var rec = await FetchUserAsync(fc, true).ConfigureAwait(false);
        var inv = InventoryOf(rec);
        if (inv.Count == 0) { Reply("持っている二つ名はありません。 /cmd nio で引けます(無料)。"); return; }
        // 付けている印は ▶(★は属性マークと紛らわしいので)
        var sb = new StringBuilder($"<b>インベントリ</b>({inv.Count}種類 / ▶=付けている / 所持 <color=#ffd36e>{Abbrev(rec.Wallet)}hm</color>)\n");
        for (int i = 0; i < inv.Count; i++)
            sb.Append(rec.Equipped.Contains(inv[i]) ? "▶" : "　").Append(i + 1).Append(". ")
              .Append(rec.Marks.TryGetValue(inv[i], out var mk) ? mk : "").Append(Colored(Catalog[TitleOf(inv[i])]))
              .Append(CountOf(rec, inv[i]) > 1 ? $" <color=#aaaaaa>×{Abbrev(CountOf(rec, inv[i]))}</color>" : "")
              .Append(rec.Prices.TryGetValue(inv[i], out var price) && price > 0
                  ? $" <size=70%><color=#ffd36e>売値{Abbrev(price)}hm</color>{(rec.SellMults.TryGetValue(inv[i], out var mult) && mult > 1 ? $"(属性×{mult.ToString("0.##", CultureInfo.InvariantCulture)})" : "")}</size>" : "").Append('\n');
        sb.Append($"\n/cmd nih 1.2.3 のように番号で付けます(最大{maxEquipped}個) / /cmd nih 0 で外す\n/cmd nis 番号 個数 で売って hm にする / /cmd nip でSHOP");
        Reply(sb.ToString());
    }

    private static async Task EquipAsync(string fc, byte localId, string[] args)
    {
        var rec = await FetchUserAsync(fc, true).ConfigureAwait(false);
        var inv = InventoryOf(rec);
        var text = string.Join(" ", args).Trim();
        if (text.Length == 0)
        {
            Reply($"使い方: /cmd nih 1.2.3  (インベントリの番号。最大{maxEquipped}個まで) / /cmd nih 0 で外す\n番号は /cmd nii で確認できます。");
            return;
        }
        List<string> chosen;
        if (text is "0" or "off" or "none" or "なし" or "外す") chosen = new List<string>();
        else
        {
            chosen = new List<string>();
            foreach (var part in text.Split(new[] { '.', ',', '、', '・', ' ', '　' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) || n < 1 || n > inv.Count)
                {
                    Reply($"「{part}」は使えません。 /cmd nii の番号(1〜{inv.Count})で指定してください。");
                    return;
                }
                if (chosen.Contains(inv[n - 1])) { Reply($"{n}番が重複しています。"); return; }
                chosen.Add(inv[n - 1]);
            }
            if (chosen.Count > maxEquipped) { Reply($"付けられるのは最大{maxEquipped}個までです。"); return; }
        }

        var (status, doc) = await RequestAsync(HttpMethod.Post, $"{BaseUrl}/equip", new { friendCode = fc, ids = chosen }).ConfigureAwait(false);
        using (doc)
        {
            if (status != 200 || doc == null)
            {
                var err = doc != null ? GetString(doc.RootElement, "error") : "";
                Reply(err == "not_owned" ? "持っていない二つ名は付けられません。" : err == "too_many" ? $"付けられるのは最大{(int)GetDouble(doc.RootElement, "max", maxEquipped)}個までです。" : "二つ名を付けられませんでした。");
                return;
            }
        }
        var after = await FetchUserAsync(fc, true).ConfigureAwait(false);
        SetShown(localId, fc, after.Equipped);
        if (ReplyTo.Value != byte.MaxValue) RunOnMain(() => SendNotifyFor(localId));
        else RunOnMain(SendNotify);
        Reply(chosen.Count == 0
            ? "二つ名を外しました。"
            : "二つ名を付けました: " + string.Join(" ", chosen.Select(KeyLabel)));
    }

    private static async Task RedeemAsync(string fc, string[] args)
    {
        if (args.Length == 0) { Reply("使い方: /cmd code コード"); return; }
        var (status, doc) = await RequestAsync(HttpMethod.Post, $"{BaseUrl}/redeem", new { friendCode = fc, code = args[0] }).ConfigureAwait(false);
        using (doc)
        {
            if (doc == null) { Reply("コードを使えませんでした(サーバーに接続できません)。"); return; }
            var root = doc.RootElement;
            if (status == 200 && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                var name = GetString(root, "name");
                var color = GetString(root, "color", "#ffffff");
                var marks = GetString(root, "marks");
                var already = root.TryGetProperty("already", out var a) && a.ValueKind == JsonValueKind.True;
                await FetchUserAsync(fc, true).ConfigureAwait(false);
                Reply($"コードを使いました!  {marks}<color={color}>{name}</color>\n"
                    + (already ? "すでに持っている二つ名でした。" : "インベントリに追加されました。/cmd nii で確認・/cmd nih で付けられます。"));
                return;
            }
            Reply(GetString(root, "error") switch
            {
                "code_not_found" => "そのコードはありません。",
                "code_expired" => "そのコードは期限が切れています。",
                "code_already_used" => "そのコードはもう使っています。",
                "code_used_up" => "そのコードは使える人数に達しました。",
                "inventory_full" => "インベントリがいっぱいです。",
                _ => "コードを使えませんでした。",
            });
        }
    }

    // =====================================================================
    //  個数・hm・SHOP   (/cmd nis 売る / nip SHOP / nib 買う / nim 所持 hm)
    //  hm の残高・SHOPの商品(値段・何人限定・期間)はすべてサーバーで決めて保存する
    // =====================================================================

    private static long CountOf(UserRecord rec, string id)
        => !rec.Owned.Contains(id) ? 0 : rec.Counts.TryGetValue(id, out var n) && n > 0 ? n : 1;

    internal static string Abbrev(long n)
    {
        if (n < 0) return "-" + Abbrev(-n);
        if (n < 1000) return n.ToString(CultureInfo.InvariantCulture);
        string[] units = { "k", "M", "B", "T" };
        double v = n;
        var u = -1;
        while (v >= 1000 && u < units.Length - 1) { v /= 1000; u++; }
        // 10未満は小数1桁(1.2k)、それ以上は整数(15k)。切り捨てて、実際より多く見えないようにする
        var shown = v < 10 ? Math.Floor(v * 10) / 10 : Math.Floor(v);
        return shown.ToString(v < 10 ? "0.#" : "0", CultureInfo.InvariantCulture) + units[u];
    }

    private static async Task ShowWalletAsync(string fc)
    {
        var me = await FetchUserAsync(fc, true).ConfigureAwait(false);
        Reply($"持っている hm: <color=#ffd36e>{Abbrev(me.Wallet)}hm</color>\n/cmd nis 番号 個数 で二つ名を売る / /cmd nip でSHOPを見る / /cmd nib 番号 で買う");
    }

    private static async Task SellAsync(string fc, byte localId, string[] args)
    {
        var rec = await FetchUserAsync(fc, true).ConfigureAwait(false);
        var inv = InventoryOf(rec);
        if (args.Length == 0)
        {
            Reply("使い方: /cmd nis 番号 [個数]  (個数を省略すると1個 / all で全部)\n番号は /cmd nii で確認できます。");
            return;
        }
        if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var no) || no < 1 || no > inv.Count)
        {
            Reply($"番号は /cmd nii の 1〜{inv.Count} で指定してください。");
            return;
        }
        var id = inv[no - 1];
        object qty = 1;
        if (args.Length >= 2)
        {
            var q = args[1].ToLowerInvariant();
            if (q is "all" or "全部" or "ぜんぶ") qty = "all";
            else if (int.TryParse(q, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n >= 1) qty = n;
            else { Reply("個数は1以上の数か all で指定してください。"); return; }
        }
        var (status, doc) = await RequestAsync(HttpMethod.Post, $"{BaseUrl}/sell", new { friendCode = fc, id, qty }).ConfigureAwait(false);
        using (doc)
        {
            if (doc == null) { Reply("売れませんでした(サーバーに接続できません)。"); return; }
            var root = doc.RootElement;
            if (status == 200 && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                var sold = (long)GetDouble(root, "sold");
                var left = (long)GetDouble(root, "left");
                var after = await FetchUserAsync(fc, true).ConfigureAwait(false);
                if (left == 0 && rec.Equipped.Contains(id))
                {
                    // 付けていた二つ名を全部売ったら、表示も外す
                    SetShown(localId, fc, after.Equipped);
                    if (ReplyTo.Value != byte.MaxValue) RunOnMain(() => SendNotifyFor(localId));
                    else RunOnMain(SendNotify);
                }
                Reply($"{GetString(root, "marks")}{(HasTitle(id) ? Colored(Catalog[TitleOf(id)]) : id)} を{Abbrev(sold)}個売りました! <color=#ffd36e>+{Abbrev((long)GetDouble(root, "earned"))}hm</color>"
                    + $"\n残り {Abbrev(left)}個 / 持っている hm: <color=#ffd36e>{Abbrev((long)GetDouble(root, "wallet"))}hm</color>"
                    + (left == 0 && rec.Equipped.Contains(id) ? "\n付けていた二つ名だったので、外しました。" : ""));
                return;
            }
            Reply(GetString(root, "error") switch
            {
                "not_owned" => "その二つ名は持っていません。",
                "not_enough" => $"そんなに持っていません(持っているのは{Abbrev((long)GetDouble(root, "have"))}個)。",
                "not_sellable" => "その二つ名は売れません。",
                _ => "売れませんでした。",
            });
        }
    }

    private sealed class ShopItem
    {
        public string Id = "", Name = "", Color = "#ffffff";
        public long Price;
        public int? Remaining;
        public int PerUser = 1;
        public bool SoldOut;
    }

    private static async Task<List<ShopItem>> FetchShopAsync()
    {
        var list = new List<ShopItem>();
        var (status, doc) = await RequestAsync(HttpMethod.Get, $"{BaseUrl}/shop").ConfigureAwait(false);
        using (doc)
        {
            if (status != 200 || doc == null) return null;
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in items.EnumerateArray())
            {
                list.Add(new ShopItem
                {
                    Id = GetString(e, "id"),
                    Name = GetString(e, "name"),
                    Color = GetString(e, "color", "#ffffff"),
                    Price = (long)GetDouble(e, "price"),
                    Remaining = e.TryGetProperty("remaining", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : null,
                    PerUser = (int)GetDouble(e, "perUser", 1),
                    SoldOut = e.TryGetProperty("soldOut", out var so) && so.ValueKind == JsonValueKind.True,
                });
            }
        }
        return list;
    }

    private static async Task ShowShopAsync(string fc)
    {
        var items = await FetchShopAsync().ConfigureAwait(false);
        if (items == null) { Reply("SHOPを開けませんでした(サーバーに接続できません)。"); return; }
        var me = await FetchUserAsync(fc, true).ConfigureAwait(false);
        if (items.Count == 0) { Reply($"今SHOPで売っているものはありません。\n持っている hm: <color=#ffd36e>{Abbrev(me.Wallet)}hm</color>"); return; }
        var sb = new StringBuilder($"<b>SHOP</b>(持っている hm: <color=#ffd36e>{Abbrev(me.Wallet)}hm</color>)\n");
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            sb.Append(i + 1).Append(". ").Append($"<color={it.Color}>{it.Name}</color>  <color=#ffd36e>{Abbrev(it.Price)}hm</color>");
            if (it.SoldOut) sb.Append("  <color=#ff6666>売り切れ</color>");
            else if (it.Remaining != null) sb.Append($"  残り{it.Remaining}人");
            if (it.PerUser > 1) sb.Append($"  (1人{it.PerUser}回まで)");
            sb.Append('\n');
        }
        sb.Append("\n/cmd nib 番号 で買えます");
        Reply(sb.ToString());
    }

    private static async Task BuyAsync(string fc, string[] args)
    {
        if (args.Length == 0) { Reply("使い方: /cmd nib 番号  (番号は /cmd nip で確認できます)"); return; }
        var items = await FetchShopAsync().ConfigureAwait(false);
        if (items == null) { Reply("買えませんでした(サーバーに接続できません)。"); return; }
        if (!int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var no) || no < 1 || no > items.Count)
        {
            Reply(items.Count == 0 ? "今SHOPで売っているものはありません。" : $"番号は /cmd nip の 1〜{items.Count} で指定してください。");
            return;
        }
        var item = items[no - 1];
        var (status, doc) = await RequestAsync(HttpMethod.Post, $"{BaseUrl}/buy", new { friendCode = fc, itemId = item.Id }).ConfigureAwait(false);
        using (doc)
        {
            if (doc == null) { Reply("買えませんでした(サーバーに接続できません)。"); return; }
            var root = doc.RootElement;
            if (status == 200 && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                await FetchUserAsync(fc, true).ConfigureAwait(false);
                var count = (long)GetDouble(root, "count", 1);
                Reply($"買いました!  {GetString(root, "marks")}<color={GetString(root, "color", "#ffffff")}>{GetString(root, "name")}</color>  <color=#ffd36e>-{Abbrev((long)GetDouble(root, "price"))}hm</color>"
                    + $"\n持っている hm: <color=#ffd36e>{Abbrev((long)GetDouble(root, "wallet"))}hm</color>"
                    + (count > 1 ? $" / この二つ名は{Abbrev(count)}個になりました" : "\n/cmd nii で確認・/cmd nih で付けられます。"));
                return;
            }
            Reply(GetString(root, "error") switch
            {
                "not_enough_hm" => $"hm が足りません(値段 {Abbrev((long)GetDouble(root, "price"))}hm / 持っている {Abbrev((long)GetDouble(root, "wallet"))}hm)。",
                "sold_out" => "売り切れです。",
                "limit_reached" => $"これ以上は買えません(1人{(int)GetDouble(root, "perUser", 1)}回まで)。",
                "not_on_sale" => "今は売っていません。",
                "item_not_found" => "その商品はもうありません。",
                "inventory_full" => "インベントリがいっぱいです。",
                _ => "買えませんでした。",
            });
        }
    }

    // =====================================================================
    //  カジノ  /cmd nic   (抽選・hmの増減はすべてサーバーで行う。ゲームは結果を出すだけ)
    //   /cmd nic coin 100 表   コイン(表か裏を当てる)
    //   /cmd nic dice 100 3    サイコロ(1〜6を当てる)
    //   /cmd nic slot 100      スロット(7が3つ・同じ絵が3つ・2つで当たり)
    // =====================================================================
    private static async Task CasinoAsync(string fc, string[] args)
    {
        if (args.Length == 0)
        {
            var me = await FetchUserAsync(fc, true).ConfigureAwait(false);
            Reply("<b>カジノ</b>(持っている hm: <color=#ffd36e>" + Abbrev(me.Wallet) + "hm</color>)\n"
                + "/cmd nic coin 金額 表/裏 … コイン(表か裏を当てる)\n"
                + "/cmd nic dice 金額 1〜6 … サイコロ(出る目を当てる)\n"
                + "/cmd nic slot 金額 … スロット(7が3つで大当たり・同じ絵が3つ・2つでも当たり)\n"
                + "倍率や賭けられる金額はサーバーで決まっています");
            return;
        }
        var game = args[0].ToLowerInvariant() switch
        {
            "coin" or "c" or "コイン" => "coin",
            "dice" or "d" or "サイコロ" or "さいころ" => "dice",
            "slot" or "s" or "スロット" => "slot",
            _ => null,
        };
        if (game == null) { Reply("ゲームは coin / dice / slot のどれかです。/cmd nic で説明を見られます。"); return; }
        if (args.Length < 2 || !long.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var bet) || bet <= 0)
        {
            Reply("賭ける金額(hm)を数字で書いてください。例: /cmd nic " + game + " 100" + (game == "coin" ? " 表" : game == "dice" ? " 3" : ""));
            return;
        }
        object choice = null;
        if (game == "coin")
        {
            var c = args.Length >= 3 ? args[2].ToLowerInvariant() : "";
            choice = c is "表" or "おもて" or "h" or "heads" ? "heads" : c is "裏" or "うら" or "t" or "tails" ? "tails" : null;
            if (choice == null) { Reply("表か裏を選んでください。例: /cmd nic coin " + bet + " 表"); return; }
        }
        else if (game == "dice")
        {
            if (args.Length < 3 || !int.TryParse(args[2], out var n) || n < 1 || n > 6) { Reply("1〜6の数を選んでください。例: /cmd nic dice " + bet + " 3"); return; }
            choice = n;
        }
        var (status, doc) = await RequestAsync(HttpMethod.Post, $"{BaseUrl}/casino", new { friendCode = fc, game, bet, choice }).ConfigureAwait(false);
        using (doc)
        {
            if (doc == null) { Reply("カジノに接続できませんでした。"); return; }
            var root = doc.RootElement;
            if (status == 200 && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                var net = (long)GetDouble(root, "net");
                string show;
                if (game == "coin")
                {
                    string Face(string v) => v == "heads" ? "表" : "裏";
                    show = $"コイン: <b>{Face(GetString(root, "result"))}</b>(あなたは{Face(GetString(root, "pick"))})";
                }
                else if (game == "dice") show = $"サイコロ: <b>{(int)GetDouble(root, "result")}</b>(あなたは{(int)GetDouble(root, "pick")})";
                else
                {
                    var reels = new List<string>();
                    if (root.TryGetProperty("reels", out var r) && r.ValueKind == JsonValueKind.Array)
                        foreach (var x in r.EnumerateArray()) reels.Add(x.GetString());
                    show = "スロット: <b><size=130%>[ " + string.Join(" | ", reels) + " ]</size></b>";
                }
                var result = net > 0 ? $"<color=#7fff7f>当たり! +{Abbrev(net)}hm</color>(×{GetDouble(root, "mult").ToString("0.##", CultureInfo.InvariantCulture)})"
                    : net == 0 ? "<color=#ffffff>±0hm</color>"
                    : $"<color=#ff7f7f>はずれ… {Abbrev(net)}hm</color>";
                Reply($"{show}\n{result}\n持っている hm: <color=#ffd36e>{Abbrev((long)GetDouble(root, "wallet"))}hm</color>");
                return;
            }
            Reply(GetString(root, "error") switch
            {
                "casino_closed" => "カジノは今お休みです。",
                "invalid_bet" => $"賭けられるのは {Abbrev((long)GetDouble(root, "minBet", 1))}〜{Abbrev((long)GetDouble(root, "maxBet", 0))}hm です。",
                "not_enough_hm" => $"hm が足りません(持っている {Abbrev((long)GetDouble(root, "wallet"))}hm)。",
                "cooldown" => $"少し待ってからもう一度どうぞ(あと{(int)GetDouble(root, "retryAfterSec")}秒)。",
                "invalid_choice" => "選び方が正しくありません。/cmd nic で説明を見られます。",
                _ => "カジノで遊べませんでした。",
            });
        }
    }

    private const double RareProbability = 0.01;

    private static void ShowRareGuard(byte playerId)
    {
        if (!GameStates.IsLobby || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
        var pc = FindPlayerById(playerId);
        if (pc == null || pc.Data == null || pc.Data.Disconnected) return;
        foreach (var seer in PlayerControl.AllPlayerControls.ToArray())
        {
            if (seer == null || seer.Data == null || seer.Data.Disconnected) continue;
            if (seer == PlayerControl.LocalPlayer)
            {
                pc.MurderPlayer(pc, MurderResultFlags.FailedProtected);
                continue;
            }
            var clientId = seer.GetClientId();
            if (clientId == -1) continue;
            var writer = AmongUsClient.Instance.StartRpcImmediately(pc.NetId, (byte)RpcCalls.MurderPlayer, Hazel.SendOption.None, clientId);
            InnerNet.MessageExtensions.WriteNetObject(writer, pc);
            writer.Write((int)MurderResultFlags.FailedProtected);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
        Logger.Info($"{pc.Data.GetLogPlayerName()} がレアな二つ名を引いたので、ガードの演出を出しました", "TitleSystem");
    }

    private static async Task DrawAsync(string fc, byte localId)
    {
        var (status, doc) = await RequestAsync(HttpMethod.Post, $"{BaseUrl}/draw", new { friendCode = fc }).ConfigureAwait(false);
        using (doc)
        {
            if (doc == null) { Reply("二つ名を引けませんでした(サーバーに接続できません)。"); return; }
            var root = doc.RootElement;
            if (status == 200 && root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True)
            {
                var name = GetString(root, "name");
                var color = GetString(root, "color", "#ffffff");
                var dup = root.TryGetProperty("duplicate", out var d) && d.ValueKind == JsonValueKind.True;
                var p = GetDouble(root, "probability");
                var marks = GetString(root, "marks");
                var traitText = new StringBuilder();
                if (root.TryGetProperty("newTraits", out var nt) && nt.ValueKind == JsonValueKind.Array)
                    foreach (var t in nt.EnumerateArray())
                        traitText.Append($"\n属性が付いた!  <color={GetString(t, "color", "#ffffff")}>{GetString(t, "mark")}</color> {GetString(t, "name")} 【{GetString(t, "rarityName")}】");
                // レアな二つ名(確率1%以下)を引いたら、キルをガードした時の演出を出す(ロビーだけ)
                if (p > 0 && p <= RareProbability) RunOnMain(() => ShowRareGuard(localId));
                await FetchUserAsync(fc, true).ConfigureAwait(false);
                Reply($"二つ名を引きました!  {marks}<color={color}>{name}</color>  (確率 {Pct(p)})"
                    + traitText
                    + "\n" + (dup ? $"かぶり! {Abbrev((long)GetDouble(root, "count", 2))}個になりました(/cmd nis で売って hm にできます)。" : "インベントリに追加されました。/cmd nii で確認・/cmd nih で付けられます。"));
                return;
            }
            var err = GetString(root, "error");
            if (err == "cooldown") Reply($"まだ引けません。あと{(int)GetDouble(root, "retryAfterSec")}秒お待ちください。{(cooldownEventActive ? $"(イベント中はクールタイム{drawCooldownNow}秒)" : "")}");
            else if (err == "no_pool") Reply("引ける二つ名がまだありません。");
            else if (err == "inventory_full") Reply("インベントリがいっぱいです。");
            else Reply("二つ名を引けませんでした。");
        }
    }

    // =====================================================================
    //  交換  /cmd nit   (ロビーのみ。状態はすべてメインスレッドで触る)
    // =====================================================================
    private sealed class TradeState
    {
        public int Id;
        public bool IAmProposer;
        public byte PartnerId;
        public string MyFc = "", PartnerFc = "";
        public string ProposerGives = "", ProposerGets = "";
        public bool MyAccept1, PartnerAccept1, MyAccept2, PartnerAccept2;
        public float BothAcceptedAt = -1f;
        public float CreatedAt;
        public bool Executing;
        public string MyGive => IAmProposer ? ProposerGives : ProposerGets;
        public string MyGet => IAmProposer ? ProposerGets : ProposerGives;
    }

    private static TradeState trade;
    private const float TradeConfirmDelay = 3f;
    private const float TradeTimeout = 120f;
    private static readonly System.Random TradeRandom = new();

    private static string TitleLabel(string id) => KeyLabel(id);
    private static string PlayerLabel(byte id)
    {
        var pc = PlayerControl.AllPlayerControls.ToArray().FirstOrDefault(p => p != null && p.PlayerId == id);
        return pc?.Data?.PlayerName ?? $"ID{id}";
    }
    private static PlayerControl FindPlayerById(byte id)
        => PlayerControl.AllPlayerControls.ToArray().FirstOrDefault(p => p != null && p.PlayerId == id);

    private static void SendTradeRpc(byte kind, Action<MessageWriter> write)
    {
        try
        {
            if (AmongUsClient.Instance == null || PlayerControl.LocalPlayer == null) return;
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.SyncTitle, SendOption.Reliable, -1);
            writer.Write(kind);
            write(writer);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
        catch (Exception e) { Logger.Exception(e, Tag); }
    }

    private static void CancelTrade(string message, bool notifyPartner)
    {
        var t = trade;
        trade = null;
        if (t == null) return;
        if (notifyPartner)
            SendTradeRpc(RpcTradeCancel, w => { w.Write(t.PartnerId); w.Write(t.Id); });
        if (!string.IsNullOrEmpty(message) && PlayerControl.LocalPlayer != null)
            Utils.SendMessage(message, PlayerControl.LocalPlayer.PlayerId);
    }

    private static void TradeTick()
    {
        // 申し込んだ側はサーバーの応答待ちの間は待つ(応答で必ず終わる)。受けた側は相手が落ちても時間切れで抜けられるようにする
        if (trade == null || (trade.Executing && trade.IAmProposer)) return;
        if (FindPlayerById(trade.PartnerId) == null)
            CancelTrade("相手がいなくなったため、二つ名の交換はキャンセルされました。", false);
        else if (Time.time - trade.CreatedAt > TradeTimeout)
            CancelTrade("時間切れのため、二つ名の交換はキャンセルされました。", true);
    }

    private static bool IsYes(string s) => s is "y" or "yes" or "ok" or "はい" or "承諾";
    private static bool IsNo(string s) => s is "n" or "no" or "cancel" or "いいえ" or "キャンセル" or "やめる";

    private static void HandleTradeCommand(string[] args)
    {
        if (!IsConfigured) { Reply("二つ名サーバー(Bot)のURLが設定されていません。"); return; }
        if (args.Length == 0)
        {
            Reply("<b>二つ名の交換</b>(ロビーのみ)\n"
                + "/cmd nit 名前 … 相手のインベントリを見る(名前のかわりに 色(赤 など) や 番号 でもOK)\n"
                + "/cmd nit 名前 a b … 自分の a 番と相手の b 番の交換を申し込む(番号は /cmd nii と /cmd nit 名前 で確認)\n"
                + "/cmd nit y … 承諾(両方が承諾したら、3秒後にもう一度 /cmd nit y で成立)\n"
                + "/cmd nit n … キャンセル/断る\n"
                + "試合が始まると交換は自動でキャンセルされます。");
            return;
        }
        var first = args[0].ToLowerInvariant();
        if (args.Length == 1 && IsYes(first)) { AcceptTrade(); return; }
        if (args.Length == 1 && IsNo(first))
        {
            if (trade == null) Reply("進行中の交換はありません。");
            else CancelTrade("二つ名の交換をキャンセルしました。", true);
            return;
        }

        // 最後の2つが番号なら申し込み。それ以外は全部を名前として、相手のインベントリを見る
        int mine = 0, theirs = 0;
        bool isPropose = args.Length >= 3
            && int.TryParse(args[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out mine)
            && int.TryParse(args[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out theirs);
        var name = string.Join(" ", isPropose ? args[..^2] : args);
        var target = FindPlayerByName(name, out var error);
        if (target == null) { Reply(error); return; }
        if (target.AmOwner) { Reply("自分とは交換できません。"); return; }
        var targetFc = FriendCodeOf(target);
        var myFc = FriendCodeOf(PlayerControl.LocalPlayer);
        if (targetFc.Length == 0 || myFc.Length == 0) { Reply("フレンドコードを取得できませんでした。"); return; }

        if (!isPropose) { ShowPartnerInventoryAsync(target.PlayerId, targetFc); return; }
        if (trade != null) { Reply("すでに交換が進行中です。 /cmd nit n でキャンセルできます。"); return; }
        ProposeTradeAsync(target.PlayerId, myFc, targetFc, mine, theirs);
    }

    private static PlayerControl FindPlayerByName(string name, out string error)
    {
        error = "";
        var players = PlayerControl.AllPlayerControls.ToArray().Where(p => p != null && p.Data != null).ToList();
        // 1. 名前がぴったり同じ人
        var exact = players.Where(p => string.Equals(p.Data.PlayerName, name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count == 1) return exact[0];
        // 2. 色の名前(赤 / red / レッド など)
        var colorId = ColorIdOf(name);
        if (colorId >= 0)
        {
            var byColor = players.Where(p => p.Data.DefaultOutfit.ColorId == colorId).ToList();
            if (byColor.Count == 1) return byColor[0];
        }
        // 3. 番号(プレイヤーID。会議の /cmd mes ID などと同じ番号)
        if (byte.TryParse(name, out var id))
        {
            var byId = players.FirstOrDefault(p => p.PlayerId == id);
            if (byId != null) return byId;
        }
        // 4. 名前の一部
        var hits = exact.Count > 0 ? exact
            : players.Where(p => (p.Data.PlayerName ?? "").IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        if (hits.Count == 1) return hits[0];
        error = hits.Count == 0
            ? $"「{name}」というプレイヤーが見つかりません。"
            : $"「{name}」に当てはまるプレイヤーが複数います: " + string.Join(" / ", hits.Select(p => p.Data.PlayerName));
        return null;
    }

    private static int ColorIdOf(string value)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        var id = v switch
        {
            "red" or "赤" or "レッド" => 0,
            "blue" or "青" or "ブルー" => 1,
            "green" or "緑" or "グリーン" => 2,
            "pink" or "ピンク" => 3,
            "orange" or "オレンジ" => 4,
            "yellow" or "黄" or "黄色" or "イエロー" => 5,
            "black" or "黒" or "ブラック" => 6,
            "white" or "白" or "ホワイト" => 7,
            "purple" or "紫" or "パープル" => 8,
            "brown" or "茶" or "茶色" or "ブラウン" => 9,
            "cyan" or "水色" or "シアン" => 10,
            "lime" or "黄緑" or "ライム" => 11,
            "maroon" or "マルーン" => 12,
            "rose" or "ローズ" => 13,
            "banana" or "バナナ" => 14,
            "gray" or "grey" or "灰色" or "グレー" => 15,
            "tan" or "タン" => 16,
            "coral" or "コーラル" => 17,
            _ => -1,
        };
        if (id >= 0) return id;
        try
        {
            for (int i = 0; i < Palette.PlayerColors.Length; i++)
                if (string.Equals(Palette.GetColorName(i), value?.Trim(), StringComparison.OrdinalIgnoreCase)) return i;
        }
        catch { }
        return -1;
    }

    private static async void ShowPartnerInventoryAsync(byte partnerId, string partnerFc)
    {
        try
        {
            var partnerName = PlayerLabel(partnerId);
            await EnsureCatalogAsync().ConfigureAwait(false);
            var rec = await FetchUserAsync(partnerFc, true).ConfigureAwait(false);
            var inv = InventoryOf(rec);
            if (inv.Count == 0) { Reply($"{partnerName} は二つ名を持っていません。"); return; }
            var sb = new StringBuilder($"<b>{partnerName} のインベントリ</b>({inv.Count}個)\n");
            for (int i = 0; i < inv.Count; i++)
                sb.Append(i + 1).Append(". ").Append(KeyLabel(inv[i])).Append(CountOf(rec, inv[i]) > 1 ? $" <color=#aaaaaa>×{Abbrev(CountOf(rec, inv[i]))}</color>" : "").Append('\n');
            sb.Append($"\n/cmd nit {partnerName} (自分の番号) (相手の番号) で交換を申し込めます");
            Reply(sb.ToString());
        }
        catch (Exception e) { Logger.Exception(e, Tag); Reply("相手のインベントリを取得できませんでした。"); }
    }

    private static async void ProposeTradeAsync(byte partnerId, string myFc, string partnerFc, int mine, int theirs)
    {
        try
        {
            await EnsureCatalogAsync().ConfigureAwait(false);
            var me = await FetchUserAsync(myFc, true).ConfigureAwait(false);
            var partner = await FetchUserAsync(partnerFc, true).ConfigureAwait(false);
            var myInv = InventoryOf(me);
            var partnerInv = InventoryOf(partner);
            if (mine < 1 || mine > myInv.Count) { Reply($"自分の番号は 1〜{myInv.Count} で指定してください(/cmd nii で確認)。"); return; }
            if (theirs < 1 || theirs > partnerInv.Count) { Reply($"相手の番号は 1〜{partnerInv.Count} で指定してください(/cmd nit 名前 で確認)。"); return; }
            var give = myInv[mine - 1];
            var get = partnerInv[theirs - 1];
            if (give == get) { Reply("同じ二つ名どうしは交換できません。"); return; }


            RunOnMain(() =>
            {
                if (!GameStates.IsLobby) return;
                if (trade != null) { Utils.SendMessage("すでに交換が進行中です。", PlayerControl.LocalPlayer.PlayerId); return; }
                if (FindPlayerById(partnerId) == null) { Utils.SendMessage("相手が見つかりません。", PlayerControl.LocalPlayer.PlayerId); return; }
                trade = new TradeState
                {
                    Id = TradeRandom.Next(1, int.MaxValue),
                    IAmProposer = true,
                    PartnerId = partnerId,
                    MyFc = myFc,
                    PartnerFc = partnerFc,
                    ProposerGives = give,
                    ProposerGets = get,
                    CreatedAt = Time.time,
                };
                var t = trade;
                SendTradeRpc(RpcTradePropose, w => { w.Write(t.PartnerId); w.Write(t.Id); w.Write(t.ProposerGives); w.Write(t.ProposerGets); });
                Utils.SendMessage($"{PlayerLabel(partnerId)} に交換を申し込みました。\n"
                    + $"渡す: {TitleLabel(give)}  ⇔  もらう: {TitleLabel(get)}\n"
                    + "/cmd nit y で承諾 / /cmd nit n でキャンセル", PlayerControl.LocalPlayer.PlayerId);
            });
        }
        catch (Exception e) { Logger.Exception(e, Tag); Reply("交換を申し込めませんでした。"); }
    }

    private static void AcceptTrade()
    {
        var t = trade;
        if (t == null) { Reply("進行中の交換はありません。"); return; }
        if (t.Executing) { Reply("交換の処理中です。少しお待ちください。"); return; }
        byte localId = PlayerControl.LocalPlayer.PlayerId;

        if (!t.MyAccept1)
        {
            t.MyAccept1 = true;
            SendTradeRpc(RpcTradeAccept, w => { w.Write(t.PartnerId); w.Write(t.Id); w.Write((byte)1); });
            if (t.PartnerAccept1) OnBothAccepted1(t);
            else Utils.SendMessage("承諾しました。相手の承諾を待っています。", localId);
            return;
        }
        if (!t.PartnerAccept1) { Utils.SendMessage("相手の承諾を待っています。", localId); return; }
        var wait = TradeConfirmDelay - (Time.time - t.BothAcceptedAt);
        if (wait > 0f) { Utils.SendMessage($"あと{Mathf.CeilToInt(wait)}秒待ってから、もう一度 /cmd nit y で確定してください。", localId); return; }
        if (t.MyAccept2) { Utils.SendMessage("確定済みです。相手の確定を待っています。", localId); return; }

        t.MyAccept2 = true;
        SendTradeRpc(RpcTradeAccept, w => { w.Write(t.PartnerId); w.Write(t.Id); w.Write((byte)2); });
        if (t.PartnerAccept2) OnBothAccepted2(t);
        else Utils.SendMessage("確定しました。相手の確定を待っています。", localId);
    }

    private static void OnBothAccepted1(TradeState t)
    {
        t.BothAcceptedAt = Time.time;
        Utils.SendMessage($"両方が承諾しました。{TradeConfirmDelay:0}秒後にもう一度 /cmd nit y を送ると交換が成立します。\n"
            + $"渡す: {TitleLabel(t.MyGive)}  ⇔  もらう: {TitleLabel(t.MyGet)}", PlayerControl.LocalPlayer.PlayerId);
    }

    private static void OnBothAccepted2(TradeState t)
    {
        if (!t.IAmProposer)
        {
            Utils.SendMessage("両方が確定しました。交換を処理しています…", PlayerControl.LocalPlayer.PlayerId);
            t.Executing = true;
            return;
        }
        // サーバーへの依頼は申し込んだ側だけが行う(二重に処理しないため)
        t.Executing = true;
        ExecuteTradeAsync(t);
    }

    private static async void ExecuteTradeAsync(TradeState t)
    {
        string error = "";
        bool ok = false;
        try
        {
            var (status, doc) = await RequestAsync(HttpMethod.Post, $"{BaseUrl}/trade", new
            {
                friendCodeA = t.MyFc,
                titleA = t.ProposerGives,
                friendCodeB = t.PartnerFc,
                titleB = t.ProposerGets,
            }).ConfigureAwait(false);
            using (doc)
            {
                ok = status == 200 && doc != null && doc.RootElement.TryGetProperty("ok", out var o) && o.ValueKind == JsonValueKind.True;
                if (!ok) error = doc != null ? GetString(doc.RootElement, "error") : "";
                if (!ok && status == 404 && error.Length == 0) error = "not_supported";
            }
            if (ok) await FetchUserAsync(t.MyFc, true).ConfigureAwait(false);
        }
        catch (Exception e) { Logger.Exception(e, Tag); error = "network"; }

        var localRec = ok && Users.TryGetValue(t.MyFc, out var r) ? r : null;
        RunOnMain(() =>
        {
            if (trade == t) trade = null;
            if (ok)
            {
                SendTradeRpc(RpcTradeDone, w => { w.Write(t.PartnerId); w.Write(t.Id); });
                if (localRec != null) SetShown(PlayerControl.LocalPlayer.PlayerId, t.MyFc, localRec.Equipped);
                SendNotify();
                Utils.SendMessage($"交換が成立しました!  {TitleLabel(t.MyGive)} → {TitleLabel(t.MyGet)}", PlayerControl.LocalPlayer.PlayerId);
            }
            else
            {
                SendTradeRpc(RpcTradeCancel, w => { w.Write(t.PartnerId); w.Write(t.Id); });
                Utils.SendMessage(TradeErrorMessage(error), PlayerControl.LocalPlayer.PlayerId);
            }
        });
    }

    private static string TradeErrorMessage(string error) => error switch
    {
        "not_owned" => "交換できませんでした(どちらかがもうその二つ名を持っていません)。",
        "already_owned" => "交換できませんでした(どちらかがすでに同じ二つ名を持っています)。",
        "not_supported" => "交換できませんでした(二つ名サーバーが交換に対応していません)。",
        "network" => "交換できませんでした(サーバーに接続できません)。",
        _ => "二つ名を交換できませんでした。",
    };

    private static void ReceiveTradeRpc(PlayerControl sender, byte kind, MessageReader reader)
    {
        byte targetId = reader.ReadByte();
        int id = reader.ReadInt32();
        var local = PlayerControl.LocalPlayer;
        if (local == null || targetId != local.PlayerId) return;

        if (kind == RpcTradePropose)
        {
            var proposerGives = reader.ReadString();
            var proposerGets = reader.ReadString();
            if (!GameStates.IsLobby) return;
            if (trade != null)
            {
                // すでに別の交換中なので断る
                SendTradeRpc(RpcTradeCancel, w => { w.Write(sender.PlayerId); w.Write(id); });
                return;
            }
            trade = new TradeState
            {
                Id = id,
                IAmProposer = false,
                PartnerId = sender.PlayerId,
                MyFc = FriendCodeOf(local),
                PartnerFc = FriendCodeOf(sender),
                ProposerGives = proposerGives,
                ProposerGets = proposerGets,
                CreatedAt = Time.time,
            };
            Utils.SendMessage($"<b>{PlayerLabel(sender.PlayerId)} から二つ名の交換の申し込み</b>\n"
                + $"渡す: {TitleLabel(proposerGets)}  ⇔  もらう: {TitleLabel(proposerGives)}\n"
                + "/cmd nit y で承諾 / /cmd nit n で断る", local.PlayerId);
            return;
        }

        // ここから下は進行中の交換に対するもの(相手とIDが一致しないものは無視)
        var t = trade;
        if (t == null || t.Id != id || t.PartnerId != sender.PlayerId) return;

        if (kind == RpcTradeAccept)
        {
            var stage = reader.ReadByte();
            if (stage == 1 && !t.PartnerAccept1)
            {
                t.PartnerAccept1 = true;
                if (t.MyAccept1) OnBothAccepted1(t);
                else Utils.SendMessage($"{PlayerLabel(t.PartnerId)} が承諾しました。 /cmd nit y で承諾 / /cmd nit n で断る", local.PlayerId);
            }
            else if (stage == 2 && !t.PartnerAccept2 && t.PartnerAccept1)
            {
                t.PartnerAccept2 = true;
                if (t.MyAccept2) OnBothAccepted2(t);
                else Utils.SendMessage($"{PlayerLabel(t.PartnerId)} が確定しました。 /cmd nit y で確定すると交換が成立します。", local.PlayerId);
            }
        }
        else if (kind == RpcTradeCancel)
        {
            trade = null;
            Utils.SendMessage(t.Executing
                ? "二つ名の交換は成立しませんでした。"
                : $"{PlayerLabel(t.PartnerId)} が二つ名の交換をキャンセルしました。", local.PlayerId);
        }
        else if (kind == RpcTradeDone)
        {
            trade = null;
            RefreshAfterTradeAsync(local.PlayerId, t);
        }
    }

    private static async void RefreshAfterTradeAsync(byte localId, TradeState t)
    {
        try
        {
            var rec = await FetchUserAsync(t.MyFc, true).ConfigureAwait(false);
            SetShown(localId, t.MyFc, rec.Equipped);
            RunOnMain(SendNotify);
            Reply($"交換が成立しました!  {TitleLabel(t.MyGive)} → {TitleLabel(t.MyGet)}");
        }
        catch (Exception e) { Logger.Exception(e, Tag); }
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
class TitleNameTagPatch
{
    public static void Postfix(PlayerControl __instance) => TitleSystem.UpdateNameTag(__instance);
}

[HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
[HarmonyPriority(Priority.First)]
class TitleChatCommandPatch
{
    public static bool Prefix(ChatController __instance)
    {
        var textArea = __instance?.freeChatField?.textArea;
        if (textArea == null) return true;
        if (!TitleSystem.TryHandleCommand(textArea.text)) return true;
        textArea.Clear();
        return false;
    }
}
