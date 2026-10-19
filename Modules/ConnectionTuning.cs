using System;
using HarmonyLib;
using Hazel.Udp;
using InnerNet;
using UnityEngine;

namespace TownOfHost.Modules;

public static class ConnectionTuning
{
    private const int MissingPingsUntilDisconnect = 20;
    private const int DisconnectTimeoutMs = 20000;

    private static UdpConnection lastTuned;
    private static float nextCheckAt;

    public static void Apply()
    {
        try
        {
            var conn = AmongUsClient.Instance?.connection;
            if (conn == null) return;
            var udp = conn.TryCast<UdpConnection>();
            if (udp == null) return;
            if (lastTuned != null && lastTuned.Pointer == udp.Pointer
                && udp._MissingPingsUntilDisconnect_k__BackingField >= MissingPingsUntilDisconnect
                && udp.DisconnectTimeoutMs >= DisconnectTimeoutMs) return;
            if (udp._MissingPingsUntilDisconnect_k__BackingField < MissingPingsUntilDisconnect)
                udp._MissingPingsUntilDisconnect_k__BackingField = MissingPingsUntilDisconnect;
            if (udp.DisconnectTimeoutMs < DisconnectTimeoutMs)
                udp.DisconnectTimeoutMs = DisconnectTimeoutMs;
            lastTuned = udp;
            Logger.Info($"切断までの待ち時間を延ばしました(生存確認 {MissingPingsUntilDisconnect} 回 / 返事待ち {DisconnectTimeoutMs / 1000} 秒)", "ConnectionTuning");
        }
        catch (Exception e)
        {
            Logger.Warn($"接続設定の変更に失敗: {e.Message}", "ConnectionTuning");
        }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]
    public static class OnGameJoinedPatch
    {
        public static void Postfix() => Apply();
    }

    [HarmonyPatch(typeof(InnerNetClient), nameof(InnerNetClient.FixedUpdate))]
    public static class FixedUpdatePatch
    {
        public static void Postfix()
        {
            if (Time.realtimeSinceStartup < nextCheckAt) return;
            nextCheckAt = Time.realtimeSinceStartup + 2f;
            Apply();
        }
    }
}
