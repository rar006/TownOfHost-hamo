using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Hazel;
using InnerNet;
using UnityEngine;

namespace TownOfHost.Modules;

/// <summary>
/// 公式サーバーで切断されにくくするための送信の安全装置(ホスト・参加者どちらでも動く)。
/// 参考: EndKnot の OFFICIAL-SERVER-NOTES(公式サーバーの観測メモ)
///  1. 1回の送信(パケット)が約1200バイトを超えると、サーバーが受け取らずに捨てる。
///     確実に届ける送信だと、送り直しを続けた末に「サーバー接続が切れました」になる。2000バイトを超えると Hacking で即切断。
///     → まとめて送るデータ(GameData)が大きすぎる時は、中身を分けて複数のパケットで送る。
///  2. 名前1つだけで大きすぎる時は分けられないので、名前をバイト数で切り詰める(日本語は1文字3バイト)。
///  3. テレポート(位置の強制移動)は、直近約6秒に約330本を超えると切断される → 数えて、超えそうなら送らない。
///  4. 参加して十数秒以内に Hacking で切られることがある(バニラのホストでも起きる・サーバー側の原因)
///     → 参加者は同じ部屋へ1回だけ自動で入り直す。
/// </summary>
public static class NetworkSafety
{
    /// <summary>1パケットの目安の上限(サーバーの上限1200バイトより余裕を持たせる)</summary>
    public const int PacketLimit = 1000;
    /// <summary>
    /// これより大きいパケットだけ分ける。確実に届ける送信は順番どおりに届くとは限らないので、
    /// そのままでも届く大きさのパケットまで分けると、中身の順番が入れ替わるおそれがある
    /// </summary>
    public const int SplitThreshold = 1150;
    /// <summary>名前1つの上限(UTF-8のバイト数)</summary>
    public const int NameByteLimit = 800;

    // ===================== 2. 名前の切り詰め =====================

    /// <summary>名前が大きすぎる時は、文字の区切りで切り詰め、閉じていないタグ(&lt;...)を消す</summary>
    public static string ClampName(string name)
    {
        if (string.IsNullOrEmpty(name) || Encoding.UTF8.GetByteCount(name) <= NameByteLimit) return name;
        var sb = new StringBuilder();
        var bytes = 0;
        for (var i = 0; i < name.Length; i++)
        {
            var len = char.IsHighSurrogate(name[i]) && i + 1 < name.Length ? 2 : 1;
            var piece = name.Substring(i, len);
            var b = Encoding.UTF8.GetByteCount(piece);
            if (bytes + b > NameByteLimit) break;
            sb.Append(piece);
            bytes += b;
            i += len - 1;
        }
        var s = sb.ToString();
        var lt = s.LastIndexOf('<');
        if (lt >= 0 && s.IndexOf('>', lt) < 0) s = s.Substring(0, lt); // 途中で切れたタグを消す
        clampLogCount++;
        if (clampLogCount <= 20) Logger.Warn($"名前が大きすぎるので切り詰めました({Encoding.UTF8.GetByteCount(name)}→{Encoding.UTF8.GetByteCount(s)}バイト)", "NetworkSafety");
        return s;
    }
    private static int clampLogCount;

    // ===================== 3. テレポートの本数 =====================

    private static readonly Queue<float> teleportTimes = new();
    private const float TeleportWindow = 6f;
    private const int TeleportMax = 260; // サーバーの境目(約330本/6秒)より余裕を持たせる
    private static float lastTeleportWarnAt = -99f;

    /// <summary>テレポートを送ってよいか(直近6秒の本数を数える)。送る時は数に入れる</summary>
    public static bool AllowTeleport()
    {
        var now = Time.realtimeSinceStartup;
        while (teleportTimes.Count > 0 && now - teleportTimes.Peek() > TeleportWindow) teleportTimes.Dequeue();
        if (teleportTimes.Count >= TeleportMax)
        {
            if (now - lastTeleportWarnAt > 5f)
            {
                lastTeleportWarnAt = now;
                Logger.Warn($"テレポートの送信が多すぎるので一部を送りません(直近{TeleportWindow}秒で{teleportTimes.Count}本)", "NetworkSafety");
            }
            return false;
        }
        teleportTimes.Enqueue(now);
        return true;
    }

    // ===================== 1. 大きすぎるパケットを分ける =====================

    private static bool splitting;

    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.SendOrDisconnect))]
    private static class SendOrDisconnectPatch
    {
        public static bool Prefix(InnerNetClient __instance, [HarmonyArgument(0)] MessageWriter msg)
        {
            try
            {
                if (splitting || msg == null || msg.Length <= SplitThreshold) return true;
                var parts = Split(msg);
                if (parts == null || parts.Count <= 1)
                {
                    Logger.Warn($"大きすぎるパケット({msg.Length}バイト)を分けられませんでした。そのまま送ります", "NetworkSafety");
                    return true;
                }
                splitting = true;
                try
                {
                    foreach (var p in parts)
                    {
                        __instance.SendOrDisconnect(p);
                        p.Recycle();
                    }
                }
                finally { splitting = false; }
                Logger.Info($"大きすぎるパケット({msg.Length}バイト)を{parts.Count}個に分けて送りました", "NetworkSafety");
                return false; // 元のパケットは送らない(元のパケットの後片付けは呼び出し元がする)
            }
            catch (Exception e)
            {
                splitting = false;
                Logger.Warn($"パケットの分割に失敗: {e.Message}", "NetworkSafety");
                return true;
            }
        }
    }

    /// <summary>
    /// パケットの中身(メッセージの並び)を読み、GameData(5)・GameDataTo(6) の中の小さなメッセージを、
    /// PacketLimit に収まるように何個かのパケットに分け直す。分けられなければ null。
    /// パケットの形: [ヘッダー(確実=3バイト/そうでない=1バイト)] + { [長さ2バイト][種類1バイト][中身] } の繰り返し
    /// GameData の中身: [部屋番号4バイト] + { [長さ2バイト][種類1バイト][中身] } の繰り返し
    /// GameDataTo の中身: [部屋番号4バイト][宛先(可変長の数)] + 同上
    /// </summary>
    private static List<MessageWriter> Split(MessageWriter msg)
    {
        var buf = (byte[])msg.ToByteArray(true);
        var header = msg.SendOption == SendOption.Reliable ? 3 : 1;
        if (buf.Length <= header) return null;
        // 1つ1つの「送る単位」(そのままのバイト列)を作る
        var units = new List<(byte tag, byte[] prefix, List<byte[]> subs, byte[] raw)>();
        var pos = header;
        while (pos + 3 <= buf.Length)
        {
            var len = buf[pos] | (buf[pos + 1] << 8);
            var tag = buf[pos + 2];
            var start = pos + 3;
            if (start + len > buf.Length) return null;
            if (tag is 5 or 6)
            {
                var p = start + 4; // 部屋番号
                if (tag == 6)
                {
                    // 宛先(7ビットずつの可変長の数)を読み飛ばす
                    while (p < start + len && (buf[p] & 0x80) != 0) p++;
                    p++;
                }
                if (p > start + len) return null;
                var prefix = new byte[p - start];
                Array.Copy(buf, start, prefix, 0, prefix.Length);
                var subs = new List<byte[]>();
                while (p + 3 <= start + len)
                {
                    var sl = buf[p] | (buf[p + 1] << 8);
                    var total = 3 + sl;
                    if (p + total > start + len) return null;
                    var sub = new byte[total];
                    Array.Copy(buf, p, sub, 0, total);
                    subs.Add(sub);
                    p += total;
                }
                units.Add((tag, prefix, subs, null));
            }
            else
            {
                var raw = new byte[3 + len];
                Array.Copy(buf, pos, raw, 0, raw.Length);
                units.Add((tag, null, null, raw));
            }
            pos = start + len;
        }
        // 詰め直す
        var result = new List<MessageWriter>();
        MessageWriter cur = null;
        var curSize = 0;
        void NewWriter()
        {
            cur = MessageWriter.Get(msg.SendOption);
            curSize = header;
            result.Add(cur);
        }
        NewWriter();
        foreach (var u in units)
        {
            if (u.raw != null)
            {
                if (curSize + u.raw.Length > PacketLimit && curSize > header) NewWriter();
                cur.Write(u.raw);
                curSize += u.raw.Length;
                continue;
            }
            // GameData / GameDataTo: 中の小さなメッセージを、収まるだけ1つの入れ物に入れる
            var i = 0;
            while (i < u.subs.Count)
            {
                var overhead = 3 + u.prefix.Length;
                if (curSize + overhead + u.subs[i].Length > PacketLimit && curSize > header) NewWriter();
                cur.StartMessage(u.tag);
                cur.Write(u.prefix);
                curSize += overhead;
                var added = 0;
                while (i < u.subs.Count && (added == 0 || curSize + u.subs[i].Length <= PacketLimit))
                {
                    cur.Write(u.subs[i]);
                    curSize += u.subs[i].Length;
                    i++;
                    added++;
                }
                cur.EndMessage();
            }
        }
        result.RemoveAll(w =>
        {
            if (w.Length > header) return false;
            w.Recycle();
            return true;
        });
        return result;
    }

    // ===================== 4. 参加直後の Hacking 切断で、1回だけ入り直す =====================

    private static float joinedAt = -1f;
    private static bool guestOnline;
    private static int joinedGameId;
    private static int rejoinGameId = -1;
    private static float rejoinSince;
    private static readonly HashSet<int> triedGameIds = new();
    private const float MaxJoinAge = 25f;

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    private static class OnGameJoinedPatch
    {
        public static void Postfix()
        {
            try
            {
                var c = AmongUsClient.Instance;
                if (c == null) return;
                joinedAt = Time.realtimeSinceStartup;
                guestOnline = !c.AmHost && c.NetworkMode == NetworkModes.OnlineGame;
                joinedGameId = c.GameId;
                if (rejoinGameId != -1 && c.GameId == rejoinGameId)
                {
                    Logger.Info($"部屋 {GameCode.IntToGameName(rejoinGameId)} に入り直しました", "NetworkSafety");
                    rejoinGameId = -1;
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.ExitGame))]
    private static class ExitGamePatch
    {
        public static void Prefix([HarmonyArgument(0)] DisconnectReasons reason)
        {
            try
            {
                var wasGuest = guestOnline;
                guestOnline = false;
                if (reason != DisconnectReasons.Hacking || !wasGuest || rejoinGameId != -1) return;
                var c = AmongUsClient.Instance;
                if (c == null || c.AmHost || GameStates.InGame) return;
                var age = joinedAt < 0f ? float.MaxValue : Time.realtimeSinceStartup - joinedAt;
                if (age > MaxJoinAge || !triedGameIds.Add(joinedGameId)) return;
                rejoinGameId = joinedGameId;
                rejoinSince = Time.realtimeSinceStartup;
                Logger.Info($"参加して{age:0.0}秒で Hacking 切断されたので、{GameCode.IntToGameName(rejoinGameId)} に1回だけ入り直します", "NetworkSafety");
            }
            catch { }
        }
    }

    private static float cleanSince;
    private static bool rejoinRequested;

    /// <summary>メインメニューに完全に戻って3秒たってから入り直す(後片付け中に入ると、また切られるため)</summary>
    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.LateUpdate))]
    private static class RejoinTickPatch
    {
        public static void Postfix()
        {
            if (rejoinGameId == -1) { rejoinRequested = false; cleanSince = 0f; return; }
            try
            {
                var now = Time.realtimeSinceStartup;
                if (now - rejoinSince > 30f)
                {
                    Logger.Warn("入り直しが終わらなかったのでやめました", "NetworkSafety");
                    rejoinGameId = -1;
                    return;
                }
                if (rejoinRequested) return;
                var clean = AmongUsClient.Instance != null && AmongUsClient.Instance.GameState == InnerNetClient.GameStates.NotJoined && LobbyBehaviour.Instance == null;
                if (!clean) { cleanSince = 0f; return; }
                if (cleanSince == 0f) cleanSince = now;
                if (now - cleanSince < 3f) return;
                rejoinRequested = true;
                AmongUsClient.Instance.StartCoroutine(AmongUsClient.Instance.CoFindGameInfoFromCodeAndJoin(rejoinGameId));
                Logger.Info($"部屋 {GameCode.IntToGameName(rejoinGameId)} に入り直します", "NetworkSafety");
            }
            catch (Exception e)
            {
                Logger.Warn($"入り直しに失敗: {e.Message}", "NetworkSafety");
                rejoinGameId = -1;
            }
        }
    }
}
