using HarmonyLib;
using TownOfHost.Roles.Core;

namespace TownOfHost.Modules;

[HarmonyPatch]
public static class WinStreak
{
    public static OptionItem Enabled;
    public static bool IsOn => Enabled?.GetBool() == true;

    public const float StopperBonus = 3f;

    private static CustomWinner lastTeam = CustomWinner.Default;
    private static int streak;
    public static bool StoppedThisGame { get; private set; }
    private static bool processed;

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoStartGame)), HarmonyPrefix]
    public static void OnGameStart()
    {
        processed = false;
        StoppedThisGame = false;
    }

    public static void EnsureUpdated()
    {
        if (processed) return;
        processed = true;
        StoppedThisGame = false;
        var team = CustomWinnerHolder.WinnerTeam;
        if (team is CustomWinner.Draw or CustomWinner.None or CustomWinner.Default) return;
        var prevStreak = streak;
        var prevTeam = lastTeam;
        if (team == lastTeam) streak++;
        else
        {
            StoppedThisGame = prevStreak >= 2;
            streak = 1;
            lastTeam = team;
        }
        if (!IsOn || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
        if (StoppedThisGame)
            Utils.SendMessage($"{TeamName(prevTeam)}の{prevStreak}連勝がストップ！止めたのは{TeamName(team)}です。", byte.MaxValue, "<#ffd36e>【連勝ストップ】</color>");
        else if (streak >= 2)
            Utils.SendMessage($"{TeamName(team)}が{streak}連勝中！", byte.MaxValue, "<#ffd36e>【連勝】</color>");
    }

    private static string TeamName(CustomWinner team)
    {
        try { return UtilsRoleText.GetRoleName((CustomRoles)team).RemoveHtmlTags() + "陣営"; }
        catch { return team + "陣営"; }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd)), HarmonyPostfix]
    public static void OnGameEnd()
    {
        try
        {
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || !GameModeManager.IsStandardClass()) return;
            EnsureUpdated();
        }
        catch (System.Exception e) { Logger.Warn($"連勝の記録に失敗: {e.Message}", "WinStreak"); }
    }
}
