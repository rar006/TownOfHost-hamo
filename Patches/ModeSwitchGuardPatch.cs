using HarmonyLib;
using TownOfHost.Modules;

namespace TownOfHost;

/// <summary>
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
