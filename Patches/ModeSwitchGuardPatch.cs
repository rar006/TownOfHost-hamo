using HarmonyLib;
using TownOfHost.Modules;

namespace TownOfHost;

/// <summary>
    /// 同じDLLでホスト・参加者の両方を扱うため、作成と参加をどちらも許可する。
/// </summary>
[HarmonyPatch]
public static class ModeSwitchGuardPatch
{
    [HarmonyPatch(typeof(EnterCodeManager), nameof(EnterCodeManager.ClickJoin))]
    [HarmonyPrefix]
    public static bool EnterCodeManagerClickJoinPrefix()
    {
        return true;
    }

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenCreateGame))]
    [HarmonyPrefix]
    public static bool MainMenuManagerOpenCreateGamePrefix()
    {
        return true;
    }
}
