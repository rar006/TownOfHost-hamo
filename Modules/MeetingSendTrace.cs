using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using HarmonyLib;
using Hazel;
using InnerNet;
using UnityEngine;

namespace TownOfHost.Modules;

[HarmonyPatch]
public static class MeetingSendTrace
{
    public static OptionItem Enabled;
    private static bool IsOn => Enabled?.GetBool() == true && AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost;

    private static readonly List<string> buffer = new();
    private static string path;
    private static float lastFlushAt;

    private static void Write(string line)
    {
        lock (buffer) buffer.Add($"[{DateTime.Now:HH:mm:ss.fff}] {line}");
    }

    public static void Flush()
    {
        string[] lines;
        lock (buffer)
        {
            if (buffer.Count == 0) return;
            lines = buffer.ToArray();
            buffer.Clear();
        }
        try
        {
            if (path == null)
            {
                var dir = WatchdogLauncher.DataDirectory;
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "MeetingSend.log");
                if (File.Exists(path))
                {
                    var prev = Path.Combine(dir, "MeetingSend.prev.log");
                    if (File.Exists(prev)) File.Delete(prev);
                    File.Move(path, prev);
                }
            }
            File.AppendAllLines(path, lines);
        }
        catch { }
    }

    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.SendOrDisconnect)), HarmonyPrefix, HarmonyPriority(Priority.First)]
    public static void SendPrefix(MessageWriter msg)
    {
        if (msg == null || !GameStates.IsMeeting || !IsOn) return;
        try { Write(Describe(msg)); }
        catch (Exception e) { Write($"(読み取り失敗: {e.Message})"); }
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start)), HarmonyPostfix]
    public static void MeetingStart()
    {
        if (IsOn) Write($"===== 会議開始 day:{UtilsGameLog.day} =====");
    }

    [HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.OnDestroy)), HarmonyPostfix]
    public static void MeetingEnd()
    {
        if (!IsOn) return;
        Write("===== 会議終了 =====");
        Flush();
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected)), HarmonyPostfix]
    public static void Disconnected()
    {
        // キックされた時も、そこまでの記録が残るようにする
        Write("===== 切断 =====");
        Flush();
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update)), HarmonyPostfix]
    public static void Tick()
    {
        if (Time.time - lastFlushAt < 1f) return;
        lastFlushAt = Time.time;
        Flush();
    }

    // ===== パケットの読み取り =====
    private static string Describe(MessageWriter msg)
    {
        var len = msg.Length;
        var buf = new byte[len];
        for (var i = 0; i < len; i++) buf[i] = msg.Buffer[i];
        var pos = msg.SendOption == SendOption.Reliable ? 3 : 1;
        var sb = new StringBuilder($"{(msg.SendOption == SendOption.Reliable ? "R" : "U")} {len}B");
        while (pos + 3 <= len)
        {
            int size = buf[pos] | (buf[pos + 1] << 8);
            int tag = buf[pos + 2];
            var start = pos + 3;
            var end = Math.Min(len, start + size);
            if (tag is 5 or 6)
            {
                var p = start + 4; // gameId
                sb.Append(tag == 5 ? " to=全員" : $" to={ClientName(ReadPacked(buf, ref p))}");
                while (p + 3 <= end)
                {
                    int subSize = buf[p] | (buf[p + 1] << 8);
                    int subTag = buf[p + 2];
                    var q = p + 3;
                    switch (subTag)
                    {
                        case 1:
                            sb.Append($" DATA({ObjName(ReadPacked(buf, ref q))})");
                            break;
                        case 2:
                            var net = ReadPacked(buf, ref q);
                            var call = q < end ? buf[q] : -1;
                            sb.Append($" RPC({ObjName(net)}:{CallName(call)})");
                            break;
                        default:
                            sb.Append($" tag{subTag}");
                            break;
                    }
                    p += 3 + subSize;
                }
            }
            else sb.Append($" root{tag}");
            pos = start + size;
        }
        return sb.ToString();
    }

    private static uint ReadPacked(byte[] buf, ref int p)
    {
        uint value = 0;
        var shift = 0;
        while (p < buf.Length)
        {
            var b = buf[p++];
            value |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return value;
    }

    private static string CallName(int call)
    {
        if (call < 0) return "?";
        if (IsDefined(typeof(RpcCalls), call)) return ((RpcCalls)call).ToString();
        if (IsDefined(typeof(CustomRPC), call)) return "Mod." + Enum.ToObject(typeof(CustomRPC), call);
        return call.ToString();
    }

    private static bool IsDefined(Type enumType, int value)
    {
        try { return Enum.IsDefined(enumType, Convert.ChangeType(value, Enum.GetUnderlyingType(enumType))); }
        catch { return false; }
    }

    private static string ObjName(uint netId)
    {
        foreach (var pc in PlayerControl.AllPlayerControls)
        {
            if (pc == null) continue;
            if (pc.NetId == netId) return $"{netId}:{pc.Data?.GetLogPlayerName()}";
            if (pc.Data != null && pc.Data.NetId == netId) return $"{netId}:Info({pc.Data.GetLogPlayerName()})";
            if (pc.NetTransform != null && pc.NetTransform.NetId == netId) return $"{netId}:Move({pc.Data?.GetLogPlayerName()})";
            if (pc.MyPhysics != null && pc.MyPhysics.NetId == netId) return $"{netId}:Physics({pc.Data?.GetLogPlayerName()})";
        }
        if (MeetingHud.Instance != null && MeetingHud.Instance.NetId == netId) return $"{netId}:MeetingHud";
        if (ShipStatus.Instance != null && ShipStatus.Instance.NetId == netId) return $"{netId}:Ship";
        return netId.ToString();
    }

    private static string ClientName(uint clientId)
    {
        foreach (var pc in PlayerControl.AllPlayerControls)
            if (pc != null && pc.GetClientId() == (int)clientId) return $"{clientId}:{pc.Data?.GetLogPlayerName()}";
        return clientId.ToString();
    }
}
