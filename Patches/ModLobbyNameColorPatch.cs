using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TownOfHost.Patches;

/// <summary>ロビー内に限り、このMODを導入しているプレイヤーの名前を識別色で表示する。</summary>
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
public static class ModLobbyNameColorPatch
{
    private static readonly Color ModPlayerNameColor = new(1f, 0.7176471f, 0.8352941f, 1f);
    private static readonly HashSet<byte> ColoredPlayers = new();

    public static void Postfix(PlayerControl __instance)
    {
        if (__instance == null || __instance.cosmetics == null || __instance.cosmetics.nameText == null)
            return;

        try
        {
            if (__instance.Data == null)
                return;

            if (GameStates.IsLobby && Main.playerVersion.ContainsKey(__instance.PlayerId))
            {
                __instance.cosmetics.nameText.color = ModPlayerNameColor;
                ColoredPlayers.Add(__instance.PlayerId);
            }
            else if (ColoredPlayers.Remove(__instance.PlayerId))
            {
                // ロビーを出た後に色が残らないよう、元の標準色へ戻す。
                __instance.cosmetics.nameText.color = Color.white;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"ロビーのMOD導入者名色の更新に失敗しました: {ex.Message}", nameof(ModLobbyNameColorPatch));
        }
    }
}
