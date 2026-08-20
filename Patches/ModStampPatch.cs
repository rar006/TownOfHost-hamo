using System;

using HarmonyLib;

namespace TownOfHost.Patches;

/// <summary>
/// 公式MOD方針で求められるMODスタンプを、ModManagerの初期化後に表示する。
/// スタンプの標準UIはAmong Us側のModManagerに任せる。
/// </summary>
[HarmonyPatch(typeof(ModManager), "Awake")]
public static class ModStampPatch
{
    [HarmonyPostfix]
    public static void ShowRequiredModStamp()
    {
        try
        {
            ModManager.Instance?.ShowModStamp();
        }
        catch (Exception ex)
        {
            Main.Logger?.LogWarning($"MODスタンプの表示に失敗しました: {ex.Message}");
        }
    }
}
