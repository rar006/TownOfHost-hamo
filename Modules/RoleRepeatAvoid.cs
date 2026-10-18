using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TownOfHost.Roles.Core;

namespace TownOfHost.Modules;

/// <summary>
/// 同じ役職が続かないようにする(便利機能・ホストのみ)。
/// 直前の2試合で引いた役職は、ほかに引ける人がいる時はその人に回す(全員が直前に引いていた時はふつうに選ぶ)。
/// 人はフレンドコード(無ければ名前)で覚えるので、抜けて入り直しても続く。
/// </summary>
[HarmonyPatch]
public static class RoleRepeatAvoid
{
    public static OptionItem Enabled;
    private const int RememberGames = 2;

    /// <summary>人 → 直前の試合の役職(新しい順・最大 RememberGames 件)</summary>
    private static readonly Dictionary<string, List<CustomRoles>> history = new();

    private static string KeyOf(PlayerControl pc)
    {
        var fc = pc?.Data?.FriendCode;
        return string.IsNullOrEmpty(fc) ? pc?.Data?.PlayerName ?? "" : fc;
    }

    /// <summary>この人がこの役職を直前に引いていたか</summary>
    public static bool HadRecently(PlayerControl pc, CustomRoles role)
        => history.TryGetValue(KeyOf(pc), out var list) && list.Contains(role);

    /// <summary>役職を渡す人を選ぶ。ONなら、直前にその役職を引いていない人を優先する</summary>
    public static PlayerControl Pick(List<PlayerControl> candidates, CustomRoles role, IRandom rand)
    {
        var pool = candidates;
        if (Enabled?.GetBool() == true)
        {
            var fresh = candidates.Where(p => !HadRecently(p, role)).ToList();
            if (fresh.Count > 0) pool = fresh;
        }
        return pool[rand.Next(0, pool.Count)];
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd)), HarmonyPostfix]
    public static void OnGameEnd()
    {
        try
        {
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            foreach (var pc in PlayerControl.AllPlayerControls)
            {
                if (pc == null || pc.Data == null) continue;
                var role = PlayerState.GetByPlayerId(pc.PlayerId)?.MainRole ?? CustomRoles.NotAssigned;
                if (role is CustomRoles.NotAssigned) continue;
                var key = KeyOf(pc);
                if (!history.TryGetValue(key, out var list)) history[key] = list = new();
                list.Insert(0, role);
                if (list.Count > RememberGames) list.RemoveRange(RememberGames, list.Count - RememberGames);
            }
        }
        catch (System.Exception e)
        {
            Logger.Warn($"役職の履歴の記録に失敗: {e.Message}", "RoleRepeatAvoid");
        }
    }
}
