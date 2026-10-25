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
    // 1000バイトを超えるパケットも分ける(以前は1150。Switch・スマホの回線では1000バイト超が届きにくかった)
    public const int SplitThreshold = 1000;
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
    public static int TeleportLimit => TeleportMax;
    // 直近6秒に送ったテレポートの本数(通信モニター用)
    public static int RecentTeleports
    {
        get
        {
            var now = Time.realtimeSinceStartup;
            while (teleportTimes.Count > 0 && now - teleportTimes.Peek() > TeleportWindow) teleportTimes.Dequeue();
            return teleportTimes.Count;
        }
    }
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

    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.SendOrDisconnect)), HarmonyPriority(Priority.First)] // 参加者が送ってはいけないRPCは、ほかの処理より先に取り除く
    private static class SendOrDisconnectPatch
    {
        public static bool Prefix(InnerNetClient __instance, [HarmonyArgument(0)] MessageWriter msg, bool __runOriginal)
        {
            // 先に別の処理(ClientPatch)が分けて送った時は、ここで送り直さない(同じ通信を2回送っていた)
            if (!__runOriginal) return false;
            if (!splitting && msg != null) NetMonitor.CountSend(msg.Length);
            try
            {
                if (!splitting && msg != null && ShouldFilterForNonHost())
                {
                    var filtered = FilterNonHostRpcs(msg, out var removed);
                    if (removed > 0)
                    {
                        if (filtered != null)
                        {
                            splitting = true;
                            try { __instance.SendOrDisconnect(filtered); }
                            finally { splitting = false; filtered.Recycle(); }
                        }
                        return false;
                    }
                }
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

    // ===================== 2. 非ホストは MOD 独自の RPC を送らない =====================
    // 参加者(ホスト以外)が MOD を入れている時に MOD 独自の RPC を送ると、ホストに「不正なRPC」としてキックされたり、
    // 公式サーバーに弾かれて切断されたりしていた。参加者は普通のプレイヤーと同じ通信だけを送る。
    // ホストしか送らないはずのバニラの RPC(名前・役職・キルの確定など)も、念のため送らない。
    private static readonly HashSet<string> HostOnlyVanillaRpcNames = new()
    {
        "SetName", "SetColor", "MurderPlayer", "Exiled", "StartMeeting", "SetRole", "ProtectPlayer",
        "Shapeshift", "RejectShapeshift", "StartVanish", "StartAppear", "SetInfected",
    };
    private static HashSet<byte> hostOnlyVanillaRpcs;
    private static readonly HashSet<byte> loggedBlocked = new();

    private static bool ShouldFilterForNonHost()
        => AmongUsClient.Instance != null && AmongUsClient.Instance.AmConnected && !AmongUsClient.Instance.AmHost
           && AmongUsClient.Instance.NetworkMode == NetworkModes.OnlineGame;

    private static bool IsBlockedForNonHost(byte callId)
    {
        if (hostOnlyVanillaRpcs == null)
        {
            hostOnlyVanillaRpcs = new();
            foreach (var name in HostOnlyVanillaRpcNames)
                if (Enum.TryParse<RpcCalls>(name, out var rc)) hostOnlyVanillaRpcs.Add((byte)rc);
        }
        if (hostOnlyVanillaRpcs.Contains(callId)) return true;
        return Enum.IsDefined(typeof(CustomRPC), (int)callId) && !Enum.IsDefined(typeof(RpcCalls), callId);
    }

    // 送るパケットから、非ホストが送ってはいけない RPC だけを取り除く。取り除いたものが無ければ removed=0
    private static MessageWriter FilterNonHostRpcs(MessageWriter msg, out int removed)
    {
        removed = 0;
        var buf = (byte[])msg.ToByteArray(true);
        var header = msg.SendOption == SendOption.Reliable ? 3 : 1;
        if (buf.Length <= header) return null;
        var output = new List<byte[]>();
        var pos = header;
        while (pos + 3 <= buf.Length)
        {
            var len = buf[pos] | (buf[pos + 1] << 8);
            var tag = buf[pos + 2];
            var start = pos + 3;
            if (start + len > buf.Length) { removed = 0; return null; }
            if (tag is 5 or 6)
            {
                var p = start + 4;
                if (tag == 6)
                {
                    while (p < start + len && (buf[p] & 0x80) != 0) p++;
                    p++;
                }
                var prefixLen = p - start;
                var kept = new List<byte[]>();
                while (p + 3 <= start + len)
                {
                    var sl = buf[p] | (buf[p + 1] << 8);
                    var total = 3 + sl;
                    if (p + total > start + len) { removed = 0; return null; }
                    var subTag = buf[p + 2];
                    var drop = false;
                    if (subTag == 2)
                    {
                        // RPC: [netId(可変長)][callId]
                        var q = p + 3;
                        while (q < p + total && (buf[q] & 0x80) != 0) q++;
                        q++;
                        if (q < p + total && IsBlockedForNonHost(buf[q]))
                        {
                            drop = true;
                            if (loggedBlocked.Add(buf[q]))
                                Logger.Info($"参加者なので RPC {buf[q]}({RPC.GetRpcName(buf[q])}) は送りません", "NetworkSafety");
                        }
                    }
                    if (drop) removed++;
                    else
                    {
                        var sub = new byte[total];
                        Array.Copy(buf, p, sub, 0, total);
                        kept.Add(sub);
                    }
                    p += total;
                }
                if (kept.Count > 0)
                {
                    var bodyLen = prefixLen;
                    foreach (var k in kept) bodyLen += k.Length;
                    var unit = new byte[3 + bodyLen];
                    unit[0] = (byte)(bodyLen & 0xff);
                    unit[1] = (byte)(bodyLen >> 8);
                    unit[2] = tag;
                    Array.Copy(buf, start, unit, 3, prefixLen);
                    var o = 3 + prefixLen;
                    foreach (var k in kept) { Array.Copy(k, 0, unit, o, k.Length); o += k.Length; }
                    output.Add(unit);
                }
            }
            else
            {
                var raw = new byte[3 + len];
                Array.Copy(buf, pos, raw, 0, raw.Length);
                output.Add(raw);
            }
            pos = start + len;
        }
        if (removed == 0 || output.Count == 0) return null;
        var w = MessageWriter.Get(msg.SendOption);
        foreach (var u in output) w.Write(u);
        return w;
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
