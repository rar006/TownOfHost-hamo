using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using Hazel;
using InnerNet;
using UnityEngine;

namespace TownOfHost.Modules;

public static class NetworkSafety
{
    public const int PacketLimit = 1000;
    public const int SplitThreshold = 1150;
    public const int NameByteLimit = 800;

    // ===================== 2. 名前の切り詰め =====================

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
