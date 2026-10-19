using AmongUs.GameOptions;
using UnityEngine;

namespace TownOfHost.Roles.Core;

public sealed class PetModeSwitch
{
    private readonly PlayerControl player;
    public bool TaskMode { get; private set; }

    public PetModeSwitch(PlayerControl player) => this.player = player;

    public static bool Enabled => Roles.Crewmate.Sheriff.StartInTaskMode?.GetBool() == true;

    public void Start()
    {
        if (!AmongUsClient.Instance.AmHost || !Enabled) { TaskMode = false; return; }
        Set(true);
    }

    public bool Toggle()
    {
        if (!AmongUsClient.Instance.AmHost || !Enabled || player == null || !player.IsAlive()) return false;
        Set(!TaskMode);
        return true;
    }

    public void Set(bool taskMode)
    {
        TaskMode = taskMode;
        if (!AmongUsClient.Instance.AmHost || player == null || !player.IsAlive()) return;
        var role = taskMode ? RoleTypes.Crewmate : RoleTypes.Impostor;
        if (player == PlayerControl.LocalPlayer) RoleManager.Instance.SetRole(PlayerControl.LocalPlayer, role);
        else
        {
            var clientId = player.GetClientId();
            if (clientId != -1) player.RpcSetRoleDesync(role, clientId);
        }
        if (!taskMode) player.SetKillCooldown(delay: true);
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: player);
    }

    public string ModeText()
        => !Enabled ? "" : Utils.ColorString(Color.yellow, TaskMode ? " [タスク]" : " [シェリフ]");
}
