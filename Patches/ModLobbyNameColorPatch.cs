using System;
using System.Collections.Generic;
using HarmonyLib;

namespace TownOfHost.Patches;

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
[HarmonyPriority(Priority.Last)]
public static class ModLobbyNameColorPatch
{
    private const string ColorOpen = "<#FFB7D5>";
    private const string ColorClose = "</color>";
    // 色を付けた人。付けていない人の名前は、ロビー外で毎フレーム読まなくてよい
    private static readonly HashSet<byte> tagged = new();

    [HarmonyPostfix]
    public static void Postfix(PlayerControl __instance)
    {
        if (__instance == null || __instance.Data == null || __instance.cosmetics == null || __instance.cosmetics.nameText == null)
            return;

        try
        {
            bool isModdedPlayer = Main.playerVersion.ContainsKey(__instance.PlayerId);
            var wantTag = GameStates.IsLobby && isModdedPlayer;
            if (!wantTag && !tagged.Contains(__instance.PlayerId)) return;
            var text = __instance.cosmetics.nameText.text ?? string.Empty;

            if (wantTag)
            {
                tagged.Add(__instance.PlayerId);
                // 参加者専用モードではFixedUpdatePatchが早期returnするため、独立した最終Postfixで表示文字列を着色する。
                if (!text.StartsWith(ColorOpen, StringComparison.OrdinalIgnoreCase))
                    __instance.cosmetics.nameText.text = $"{ColorOpen}{text}{ColorClose}";
            }
            else if (text.StartsWith(ColorOpen, StringComparison.OrdinalIgnoreCase)
                && text.EndsWith(ColorClose, StringComparison.OrdinalIgnoreCase))
            {
                // ロビー外、またはMOD判定が外れたときは、このパッチ自身が付けた外側タグだけを取り除く。
                __instance.cosmetics.nameText.text = text.Substring(ColorOpen.Length, text.Length - ColorOpen.Length - ColorClose.Length);
                tagged.Remove(__instance.PlayerId);
            }
            else tagged.Remove(__instance.PlayerId);
        }
        catch (Exception ex)
        {
            Logger.Warn($"ロビーのMOD導入者名色の更新に失敗しました: {ex.Message}", nameof(ModLobbyNameColorPatch));
        }
    }
}
