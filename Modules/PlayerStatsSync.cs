using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using HarmonyLib;
using InnerNet;
using TownOfHost.Roles.Core;

namespace TownOfHost.Modules;

[HarmonyPatch]
public static class PlayerStatsSync
{
    public static OptionItem RecordEnabled;   // 戦績を記録する(/me)
    public static OptionItem FirstTimerEnabled; // 初めての人にマークを付ける

    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static string BaseUrl => LobbyChatRemoteConfig.EndpointUrl.Replace("/lobbychat", "/stats").TrimEnd('/');

    internal static readonly HashSet<byte> FirstTimers = new();
    private static readonly HashSet<string> checkedFcs = new();

    public const string FirstTimerMark = "<color=#7fff7f>[初]</color>";

    private class PlayerResponse
    {
        public bool Found { get; set; }
        public int Games { get; set; }
        public int Wins { get; set; }
        public int Kills { get; set; }
        public int Deaths { get; set; }
        public List<RoleCount> TopRoles { get; set; } = new();
    }
    private class RoleCount
    {
        public string Name { get; set; } = "";
        public int Count { get; set; }
    }
    private static readonly JsonSerializerOptions jsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static HttpRequestMessage Request(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        if (!string.IsNullOrEmpty(LobbyChatRemoteConfig.ApiKey))
            req.Headers.Add("Authorization", $"Bearer {LobbyChatRemoteConfig.ApiKey}");
        return req;
    }

    private static async Task<PlayerResponse> FetchAsync(string fc)
    {
        using var req = Request(HttpMethod.Get, $"{BaseUrl}/player?fc={Uri.EscapeDataString(fc)}");
        using var res = await http.SendAsync(req).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) return null;
        return JsonSerializer.Deserialize<PlayerResponse>(await res.Content.ReadAsStringAsync().ConfigureAwait(false), jsonOptions);
    }

    // ===== 試合が終わったら全員分を送る =====
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd)), HarmonyPostfix]
    public static void OnGameEnd()
    {
        // 1試合遊んだら「初めての人」ではなくなるので、マークは外す
        FirstTimers.Clear();
        firstTimerFcs.Clear();
        try
        {
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            if (!GameModeManager.IsStandardClass() || CustomWinnerHolder.WinnerTeam is CustomWinner.Draw) return;
            var winners = CustomWinnerHolder.WinnerIds ?? new HashSet<byte>();
            var players = new List<object>();
            foreach (var pc in PlayerControl.AllPlayerControls)
            {
                if (pc == null || pc.Data == null || string.IsNullOrEmpty(pc.Data.FriendCode)) continue;
                var state = PlayerState.GetByPlayerId(pc.PlayerId);
                if (state == null || state.MainRole is CustomRoles.NotAssigned or CustomRoles.GM) continue;
                players.Add(new
                {
                    fc = pc.Data.FriendCode,
                    name = pc.Data.PlayerName,
                    role = UtilsRoleText.GetRoleName(state.MainRole).RemoveHtmlTags(),
                    win = winners.Contains(pc.PlayerId),
                    kills = state.GetKillCount(true),
                    dead = state.IsDead,
                });
            }
            if (players.Count == 0) return;
            var json = JsonSerializer.Serialize(new { players });
            _ = Task.Run(async () =>
            {
                try
                {
                    using var req = Request(HttpMethod.Post, $"{BaseUrl}/game");
                    req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                    using var res = await http.SendAsync(req).ConfigureAwait(false);
                    if (!res.IsSuccessStatusCode) Logger.Warn($"戦績を送れませんでした(HTTP {(int)res.StatusCode})", "PlayerStats");
                }
                catch (Exception e) { Logger.Warn($"戦績を送れませんでした: {e.Message}", "PlayerStats"); }
            });
        }
        catch (Exception e) { Logger.Warn($"戦績の記録に失敗: {e.Message}", "PlayerStats"); }
    }

    // ===== /me =====
    public static bool TryHandle(string text, PlayerControl who)
    {
        if (who == null || string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim().ToLowerInvariant();
        if (t != "/me" && t != "/cmd me") return false;
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return false;
        var fc = who.Data?.FriendCode ?? "";
        var sendTo = who.PlayerId;
        var name = who.Data?.PlayerName ?? "";
        if (fc.Length == 0) { Utils.SendMessage("フレンドコードが無いため、戦績を表示できません。", sendTo); return true; }
        _ = Task.Run(async () =>
        {
            string msg;
            try
            {
                var r = await FetchAsync(fc).ConfigureAwait(false);
                msg = r == null ? "戦績を取得できませんでした。しばらくしてからもう一度試してください。" : Format(name, r);
            }
            catch (Exception e)
            {
                Logger.Warn($"戦績の取得に失敗: {e.Message}", "PlayerStats");
                msg = "戦績を取得できませんでした。しばらくしてからもう一度試してください。";
            }
            MainThreadDispatcher.Enqueue(() => Utils.SendMessage(msg, sendTo, "<#7fd8ff>【あなたの戦績】</color>"));
        });
        return true;
    }

    private static string Format(string name, PlayerResponse r)
    {
        if (!r.Found || r.Games == 0) return $"{name} さんの記録はまだありません。試合が終わると記録されます。";
        var lf = ((char)10).ToString();
        var sb = new StringBuilder($"{name} さん");
        sb.Append(lf).Append($"試合数: {r.Games} / 勝ち: {r.Wins} (勝率 {r.Wins * 100 / Math.Max(1, r.Games)}%)");
        sb.Append(lf).Append($"キル: {r.Kills} / 死亡: {r.Deaths}");
        if (r.TopRoles.Count > 0)
            sb.Append(lf).Append("よく引く役職: ").Append(string.Join("、", r.TopRoles.Select(x => $"{x.Name}({x.Count}回)")));
        return sb.ToString();
    }

    // ===== 初めての人 =====
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined)), HarmonyPostfix]
    public static void OnPlayerJoined([HarmonyArgument(0)] ClientData client)
    {
        try
        {
            if (FirstTimerEnabled?.GetBool() != true || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            var fc = client?.FriendCode ?? "";
            if (fc.Length == 0 || client.Id == AmongUsClient.Instance.ClientId) return;
            if (!checkedFcs.Add(fc.ToLowerInvariant())) { ResendMarkLater(client); return; } // この部屋で確認済みなら、マークだけ付け直す
            _ = Task.Run(async () =>
            {
                try
                {
                    var r = await FetchAsync(fc).ConfigureAwait(false);
                    if (r == null || r.Games > 0) return;
                    // キャラが出てくるまで少し待ってから付ける
                    MainThreadDispatcher.Enqueue(() => _ = new LateTask(() => MarkFirstTimer(client), 3f, "FirstTimerMark", true));
                }
                catch (Exception e) { Logger.Warn($"初めての人の確認に失敗: {e.Message}", "PlayerStats"); }
            });
        }
        catch (Exception e) { Logger.Warn($"初めての人の確認に失敗: {e.Message}", "PlayerStats"); }
    }

    private static readonly HashSet<string> firstTimerFcs = new();

    private static void ResendMarkLater(ClientData client)
    {
        if (!firstTimerFcs.Contains((client?.FriendCode ?? "").ToLowerInvariant())) return;
        _ = new LateTask(() => MarkFirstTimer(client, announce: false), 3f, "FirstTimerMark", true);
    }

    private static void MarkFirstTimer(ClientData client, bool announce = true)
    {
        var pc = client?.Character;
        if (pc == null || !GameStates.IsLobby) return;
        firstTimerFcs.Add((client.FriendCode ?? "").ToLowerInvariant());
        FirstTimers.Add(pc.PlayerId);
        TitleSystem.BroadcastFirstTimer(pc.PlayerId);
        if (announce)
            Utils.SendMessage($"{pc.Data?.PlayerName} さんは初めての参加です！みんなで教えてあげてね。", byte.MaxValue, "<#7fff7f>【はじめまして】</color>");
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerLeft)), HarmonyPostfix]
    public static void OnPlayerLeft([HarmonyArgument(0)] ClientData data)
    {
        if (data?.Character != null) FirstTimers.Remove(data.Character.PlayerId);
    }
}

[HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
[HarmonyPriority(Priority.First)]
class PlayerStatsChatPatch
{
    public static bool Prefix(ChatController __instance)
    {
        var textArea = __instance?.freeChatField?.textArea;
        if (textArea == null || PlayerControl.LocalPlayer == null) return true;
        if (!PlayerStatsSync.TryHandle(textArea.text, PlayerControl.LocalPlayer)) return true;
        textArea.Clear();
        return false;
    }
}
