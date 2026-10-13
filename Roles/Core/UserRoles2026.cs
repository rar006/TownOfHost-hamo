using System.Linq;
using HarmonyLib;
using UnityEngine;
using TownOfHost.Roles.Crewmate;

namespace TownOfHost.Roles.Core;

/// <summary>
/// 2026-10 ユーザー提案の役職で共通に使う処理(チャットコマンド・毎フレームの確認)
/// </summary>
public static class UserRoles2026
{
    /// <summary>
    /// /cmd のコマンド(args[0] は "/mes" のように / 付き)。処理したら true(ホストのみ)
    /// </summary>
    public static bool TryHandleCommand(PlayerControl player, string[] args)
    {
        if (player == null || args == null || args.Length == 0) return false;
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return false;
        switch (args[0].ToLowerInvariant())
        {
            case "/mes":
                if (player.GetRoleClass() is not Messenger messenger) return false;
                messenger.HandleCommand(args);
                return true;
        }
        return false;
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    static class TickPatch
    {
        static float next;
        public static void Postfix()
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
                if (!GameStates.IsInTask || GameStates.IsMeeting) return;
                if (Time.time < next) return;
                next = Time.time + 0.25f;
                foreach (var role in CustomRoleManager.AllActiveRoles.Values.ToArray())
                {
                    if (role is Fugitive f) f.CheckFleeTargetDead();
                }
            }
            catch (System.Exception e) { Logger.Warn($"UserRoles2026.Tick: {e.Message}", "UserRoles2026"); }
        }
    }
}
