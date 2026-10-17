using System;
using HarmonyLib;
using Hazel.Udp;

namespace TownOfHost.Modules;

/// <summary>
/// ホストの回線が一瞬だけ途切れた時に、すぐ「サーバー接続が切れました(Sent 6 pings that remote has not responded to)」で
/// 部屋ごと落ちないようにする。
/// Among Us は約1.5秒ごとに生存確認(ping)を送り、返事が6回(約9秒)来ないと自分から切断する。
/// これを増やして、少しの途切れなら待って持ち直せるようにする(サーバー側が切った場合は防げない)。
/// </summary>
public static class ConnectionTuning
{
    /// <summary>返事がない生存確認を何回まで待つか(既定は6回 = 約9秒 → 20回 = 約30秒)</summary>
    private const int MissingPingsUntilDisconnect = 20;

    public static void Apply()
    {
        try
        {
            var conn = AmongUsClient.Instance?.connection;
            if (conn == null) return;
            var udp = conn.TryCast<UdpConnection>();
            if (udp == null) return;
            if (udp._MissingPingsUntilDisconnect_k__BackingField >= MissingPingsUntilDisconnect) return;
            udp._MissingPingsUntilDisconnect_k__BackingField = MissingPingsUntilDisconnect;
            Logger.Info($"切断までに待つ生存確認の回数を {MissingPingsUntilDisconnect} 回にしました", "ConnectionTuning");
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
}
