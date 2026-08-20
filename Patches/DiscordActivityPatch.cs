using System;
using AmongUs.Data;
using Assets.InnerNet;
using Discord;
using HarmonyLib;
using TMPro;

namespace TownOfHost.Patches;

/// <summary>
/// Among Us標準のDiscord Activityを利用する。
/// 外部DiscordRPCクライアントを起動しないため、Newtonsoft.Jsonの競合やバックグラウンド例外を避けられる。
/// </summary>
[HarmonyPatch(typeof(ActivityManager), nameof(ActivityManager.UpdateActivity))]
public static class DiscordActivityPatch
{
    private static string lobbyCode = "";
    private static string region = "";

    [HarmonyPrefix]
    public static void Prefix([HarmonyArgument(0)] Activity activity)
    {
        if (activity == null || Main.IsAndroid() || Main.EnableDiscordRichPresence?.Value is not true) return;

        try
        {
            var details = "TownOfHost-hamo";
            if (activity.State != "In Menus" && !DataManager.Settings.Gameplay.StreamerMode)
            {
                if (GameStates.IsLobby && GameStartManager.Instance != null)
                {
                    var roomCodeText = GameStartManager.Instance.GameRoomNameCode;
                    lobbyCode = roomCodeText != null ? roomCodeText.text : "";
                    region = ServerManager.Instance?.CurrentRegion != null
                        ? ServerManager.Instance.CurrentRegion.TranslateName.ToString()
                        : "";
                }

                if (!string.IsNullOrEmpty(lobbyCode) && !string.IsNullOrEmpty(region))
                    details = $"TownOfHost-hamo - {lobbyCode} ({region})";
            }

            // Discord側に表示される大きな行をMOD名へ置き換える。
            activity.Details = details;
        }
        catch (Exception e)
        {
            Logger.Warn($"Discord activity update failed: {e.Message}", "DiscordActivity");
        }
    }
}
