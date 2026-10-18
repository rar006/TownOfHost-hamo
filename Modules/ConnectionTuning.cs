using System;
using HarmonyLib;
using Hazel.Udp;
using InnerNet;
using UnityEngine;

namespace TownOfHost.Modules;

/// <summary>
/// ホストの回線が一瞬だけ途切れた時に、すぐ「サーバー接続が切れました」で部屋ごと落ちないようにする。
/// Among Us(Hazel)は次の2つのどちらかで自分から切断する。
///  1. 生存確認(ping)の返事が6回(約9秒)来ない → 「Sent 6 pings that remote has not responded to」
///  2. 送ったデータ(確実に届ける必要があるもの)の返事が5秒来ない → 「Reliable packet ... was not ack'd」
/// どちらも待つ時間を延ばして、少しの途切れなら持ち直せるようにする(サーバー側が切った場合は防げない)。
/// 接続は部屋に入るたびに作り直されるので、ときどき確認して設定し直す。
/// </summary>
public static class ConnectionTuning
{
    /// <summary>返事がない生存確認を何回まで待つか(既定は6回 = 約9秒 → 20回 = 約30秒)</summary>
    private const int MissingPingsUntilDisconnect = 20;
    /// <summary>送ったデータの返事を何ミリ秒まで待つか(既定は5秒 → 20秒)</summary>
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

    /// <summary>部屋に入る前(接続した直後)や、接続が作り直された時にも効くように、2秒ごとに確認する</summary>
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
