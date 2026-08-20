using System.Linq;
using HarmonyLib;
using TownOfHost.Roles.Core;
using static TownOfHost.Options;
using static TownOfHost.Translator;
using TownOfHost;

namespace TownOfHost.Roles.AddOns.Common;

/// <summary>
/// 全体の生存人数が設定人数以下になった場合、クルー陣営の1人に付与される属性。
/// 付与された日から設定ターン数だけキルを防ぐ。
/// </summary>
public static class LastCrewmate
{
    private static readonly int Id = 25120;
    public static string SubRoleMark = Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.LastCrewmate), "L");

    private static OptionItem OptionThresholdAliveCount;
    private static int lastCheckedAliveCount = -1;
    private static OptionItem OptionProtectTurns;

    // 付与された時点でのUtilsGameLog.day(このターン数からの経過を数える)
    private static System.Collections.Generic.Dictionary<byte, int> grantedAtDay = new();

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.Addons, CustomRoles.LastCrewmate, fromtext: UtilsOption.GetFrom(From.TownOfHost_hamo));
        // 初期配役では付与せず、残り生存人数が設定値以下になった時だけSetSubRoleで動的に付与する。
        // ラストインポスターと同じく、属性として設定されていても開始時には誰にも付かない。
        AddOnsAssignData.Create(Id + 10, CustomRoles.LastCrewmate, false, false, false, false);
        OptionThresholdAliveCount = IntegerOptionItem.Create(Id + 50, "LastCrewmateThresholdAliveCount", new(1, 15, 1), 3, TabGroup.Addons, false)
            .SetSubRoleOptionItem(CustomRoles.LastCrewmate);
        OptionProtectTurns = IntegerOptionItem.Create(Id + 51, "LastCrewmateProtectTurns", new(1, 10, 1), 2, TabGroup.Addons, false)
            .SetSubRoleOptionItem(CustomRoles.LastCrewmate);
    }

    public static void Init()
    {
        grantedAtDay = new();
        lastCheckedAliveCount = -1;
    }

    // キャッシュや特殊な除外カウントを使わず、画面上で実際に生存している全プレイヤーを数える。
    // この値だけをラストクルーメイトの発動条件に用いる。
    private static System.Collections.Generic.List<PlayerControl> GetActualAlivePlayers() =>
        PlayerControl.AllPlayerControls.ToArray()
            .Where(pc => pc != null && pc.Data != null && !pc.Data.IsDead && !pc.Data.Disconnected && pc.PlayerId <= 15)
            .ToList();

    /// <summary>
    /// 全体の生存人数が閾値以下になった時点で、まだ誰もラストクルーメイトを
    /// 持っていなければ、生存しているクルー役職の中からランダムに1人を選び動的に付与する。
    /// </summary>
    public static void SetSubRole()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!CustomRoles.LastCrewmate.IsPresent()) return;

        var actualAlivePlayers = GetActualAlivePlayers();
        if (actualAlivePlayers.Count > OptionThresholdAliveCount.GetInt()) return;

        var aliveCrews = actualAlivePlayers
            .Where(pc => pc.GetCustomRole().IsCrewmate())
            .ToList();

        // すでに誰かがLastCrewmateを持っていれば処理不要
        if (aliveCrews.Any(pc => pc.Is(CustomRoles.LastCrewmate))) return;
        if (actualAlivePlayers.Any(pc => pc.Is(CustomRoles.LastCrewmate))) return;

        var candidates = aliveCrews.Where(pc => !pc.Is(CustomRoles.LastCrewmate)).ToList();
        if (candidates.Count == 0) return;

        var chosen = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        chosen.RpcSetCustomRole(CustomRoles.LastCrewmate);
        chosen.SyncSettings();
        UtilsNotifyRoles.NotifyRoles();
    }

    /// <summary>
    /// 全体の生存人数が閾値以下なら、ラストクルーメイト属性を持つプレイヤーに
    /// キル無効化ガードを付与する。ホスト側から定期的に呼び出す想定。
    /// </summary>
    public static void CheckAndGrantGuard()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInTask) return;

        var actualAlivePlayers = GetActualAlivePlayers();
        if (actualAlivePlayers.Count > OptionThresholdAliveCount.GetInt()) return;

        foreach (var pc in actualAlivePlayers)
        {
            if (pc == null || !pc.Is(CustomRoles.LastCrewmate)) continue;
            if (grantedAtDay.ContainsKey(pc.PlayerId)) continue; // 既に付与済み

            grantedAtDay[pc.PlayerId] = UtilsGameLog.day;
            var state = pc.GetPlayerState();
            state.HaveGuard[1] += 1;
        }
    }

    /// <summary>
    /// 生存人数が減少して設定値以下に到達した時だけ配役条件を確認する。
    /// 初回観測・ロビー・人数増加では付与せず、ゲーム中の死亡または切断による減少だけを契機にする。
    /// </summary>
    public static void OnAlivePlayerCountChanged(int alivePlayerCount)
    {
        var actualAliveCount = GetActualAlivePlayers().Count;
        if (!GameStates.IsInTask)
        {
            lastCheckedAliveCount = actualAliveCount;
            return;
        }

        if (lastCheckedAliveCount < 0)
        {
            lastCheckedAliveCount = actualAliveCount;
            return;
        }

        if (actualAliveCount >= lastCheckedAliveCount)
        {
            lastCheckedAliveCount = actualAliveCount;
            return;
        }

        lastCheckedAliveCount = actualAliveCount;
        SetSubRole();
    }

    /// <summary>
    /// 設定ターン数を過ぎたプレイヤーのガードを取り除く。会議終了時に呼び出す想定。
    /// </summary>
    public static void ExpireOldGuards()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        var expired = grantedAtDay
            .Where(kv => UtilsGameLog.day - kv.Value >= OptionProtectTurns.GetInt())
            .Select(kv => kv.Key)
            .ToList();

        foreach (var playerId in expired)
        {
            grantedAtDay.Remove(playerId);
            var pc = playerId.GetPlayerControl();
            if (pc == null) continue;
            var state = pc.GetPlayerState();
            if (state.HaveGuard.ContainsKey(1) && state.HaveGuard[1] > 0)
                state.HaveGuard[1]--;
        }
    }
}

[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
internal static class LastCrewmateMeetingStartPatch
{
    public static void Postfix()
    {
        LastCrewmate.ExpireOldGuards();
    }
}
