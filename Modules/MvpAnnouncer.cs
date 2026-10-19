using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TownOfHost.Roles.Core;

namespace TownOfHost.Modules;

[HarmonyPatch]
public static class MvpAnnouncer
{
    public static OptionItem Enabled;

    private static readonly Dictionary<byte, int> correctVotes = new();

    public static void OnMeetingEnd(IEnumerable<(byte voter, byte votedFor)> votes)
    {
        if (Enabled?.GetBool() != true) return;
        try
        {
            foreach (var (voter, votedFor) in votes)
            {
                var voterRole = PlayerState.GetByPlayerId(voter)?.MainRole ?? CustomRoles.NotAssigned;
                var targetRole = PlayerState.GetByPlayerId(votedFor)?.MainRole ?? CustomRoles.NotAssigned;
                if (voterRole is CustomRoles.NotAssigned || targetRole is CustomRoles.NotAssigned) continue;
                if (!voterRole.IsCrewmate()) continue;
                if (!(targetRole.IsImpostorTeam() || targetRole.IsNeutral())) continue;
                correctVotes[voter] = correctVotes.TryGetValue(voter, out var c) ? c + 1 : 1;
            }
        }
        catch (Exception e) { Logger.Warn($"投票の記録に失敗: {e.Message}", "MvpAnnouncer"); }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd)), HarmonyPostfix]
    public static void OnGameEnd()
    {
        try
        {
            if (Enabled?.GetBool() != true || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            if (!GameModeManager.IsStandardClass() || CustomWinnerHolder.WinnerTeam is CustomWinner.Draw) return;
            WinStreak.EnsureUpdated(); // 連勝を止めたかどうかを先に数える
            var stopper = WinStreak.IsOn && WinStreak.StoppedThisGame;
            var winners = CustomWinnerHolder.WinnerIds ?? new HashSet<byte>();
            var best = PlayerState.AllPlayerStates
                .Where(kv => kv.Value != null && kv.Value.MainRole is not (CustomRoles.NotAssigned or CustomRoles.GM))
                .Select(kv =>
                {
                    var id = kv.Key;
                    var state = kv.Value;
                    var kills = state.GetKillCount(true);
                    var votes = correctVotes.TryGetValue(id, out var v) ? v : 0;
                    var task = state.taskState;
                    var taskRate = task != null && task.hasTasks && task.AllTasksCount > 0 ? (float)task.CompletedTasksCount / task.AllTasksCount : 0f;
                    var win = winners.Contains(id);
                    var score = kills * 3 + votes * 2 + taskRate * 3 + (win ? 2 : 0) + (state.IsDead ? 0 : 1) + (stopper && win ? WinStreak.StopperBonus : 0);
                    return (id, state, kills, votes, taskRate, win, score);
                })
                .OrderByDescending(x => x.score).ThenByDescending(x => x.kills).ThenByDescending(x => x.votes)
                .FirstOrDefault();
            if (best.state == null || best.score <= 0) return;
            var name = Main.AllPlayerNames.TryGetValue(best.id, out var n) ? n : "?";
            var role = UtilsRoleText.GetRoleName(best.state.MainRole).RemoveHtmlTags();
            var reasons = new List<string>();
            if (best.kills > 0) reasons.Add($"キル{best.kills}");
            if (best.votes > 0) reasons.Add($"正しい投票{best.votes}");
            if (best.taskRate > 0) reasons.Add($"タスク{(int)(best.taskRate * 100)}%");
            if (best.win) reasons.Add("勝利");
            if (best.win && WinStreak.IsOn && WinStreak.StoppedThisGame) reasons.Add("連勝ストップ");
            if (!best.state.IsDead) reasons.Add("生存");
            // 試合後ロビーに戻ってから送られる
            Utils.SendMessage($"{name} さん({role})\n{string.Join(" / ", reasons)}", byte.MaxValue, "<#ffd36e>【今回のMVP】</color>");
            Logger.Info($"MVP: {name}({role}) score={best.score:0.0}", "MvpAnnouncer");
        }
        catch (Exception e) { Logger.Warn($"MVPの計算に失敗: {e.Message}", "MvpAnnouncer"); }
        finally { correctVotes.Clear(); }
    }
}
