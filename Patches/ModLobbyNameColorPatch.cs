using System;
using HarmonyLib;

namespace TownOfHost.Patches;

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
[HarmonyPriority(Priority.Last)]
public static class ModLobbyNameColorPatch
{
    private const string ColorOpen = "<#FFB7D5>";
    private const string ColorClose = "</color>";

    [HarmonyPostfix]
    public static void Postfix(PlayerControl __instance)
    {
        if (__instance == null || __instance.Data == null || __instance.cosmetics == null || __instance.cosmetics.nameText == null)
            return;

        try
        {
            var text = __instance.cosmetics.nameText.text ?? string.Empty;
            bool isModdedPlayer = Main.playerVersion.ContainsKey(__instance.PlayerId);

            if (GameStates.IsLobby && isModdedPlayer)
            {
                // 参加者専用モードではFixedUpdatePatchが早期returnするため、独立した最終Postfixで表示文字列を着色する。
                if (!text.StartsWith(ColorOpen, StringComparison.OrdinalIgnoreCase))
                    __instance.cosmetics.nameText.text = $"{ColorOpen}{text}{ColorClose}";
            }
            else if (text.StartsWith(ColorOpen, StringComparison.OrdinalIgnoreCase)
                && text.EndsWith(ColorClose, StringComparison.OrdinalIgnoreCase))
            {
                // ロビー外、またはMOD判定が外れたときは、このパッチ自身が付けた外側タグだけを取り除く。
                __instance.cosmetics.nameText.text = text.Substring(ColorOpen.Length, text.Length - ColorOpen.Length - ColorClose.Length);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"ロビーのMOD導入者名色の更新に失敗しました: {ex.Message}", nameof(ModLobbyNameColorPatch));
        }
    }
}
