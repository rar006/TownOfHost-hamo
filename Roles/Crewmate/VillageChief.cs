using System;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Patches;
using static TownOfHost.PlayerCatch;
using static TownOfHost.Utils;
using static TownOfHost.Translator;

namespace TownOfHost.Roles.Crewmate;

public sealed class VillageChief : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(VillageChief),
            player => new VillageChief(player),
            CustomRoles.VillageChief,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Crewmate,
            160000,
            SetupOptionItem,
            "vc",
            "#f5a623",
            (2, 0),
            true,
            from: From.SuperNewRoles
        );

    public VillageChief(PlayerControl player)
        : base(RoleInfo, player, () => HasTask.True)
    {
        appointMode = false;
        hasAppointed = false;
        nowcool = AppointCooldown.GetFloat();
        LastCooltime = -1;
    }

    private static OptionItem NotifyTarget;
    private static OptionItem AppointCooldown;

    private static readonly string[] NotifyTargetOptions =
        ["None", "Everyone", "VillageChiefOnly", "SheriffOnly", "VillageChiefAndSheriff"];

    bool appointMode;
    bool hasAppointed;
    float nowcool;
    int LastCooltime;

    private static void SetupOptionItem()
    {
        NotifyTarget = StringOptionItem.Create(
            RoleInfo, 12, "VillageChiefNotifyTarget",
            NotifyTargetOptions, 0, false
        );
        AppointCooldown = FloatOptionItem.Create(
            RoleInfo, 13, "AppointCooldown",
            new(0f, 120f, 2.5f), 30f, false
        ).SetValueFormat(OptionFormat.Seconds);
    }

    public float CalculateKillCooldown() => CanUseKillButton() ? AppointCooldown.GetFloat() : 999f;
    public bool CanUseKillButton() => Player.IsAlive() && appointMode && !hasAppointed;
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;
    public override bool CanUseAbilityButton() => false;

    public override bool CanClickUseVentButton => !appointMode;
    public override bool OnEnterVent(PlayerPhysics physics, int ventId) => false;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(false);
        if (!appointMode)
        {
            AURoleOptions.EngineerCooldown = Mathf.Max(nowcool, 0.1f);
            AURoleOptions.EngineerInVentMaxTime = 0f;
        }
    }

    public override RoleTypes? AfterMeetingRole
        => (appointMode && !hasAppointed) ? RoleTypes.Impostor : RoleTypes.Engineer;

    public override void Add()
    {
        nowcool = AppointCooldown.GetFloat();
        LastCooltime = -1;
        PetActionManager.Register(Player.PlayerId, OnPetUsed);
    }

    public override void OnDestroy()
    {
        PetActionManager.Unregister(Player.PlayerId);
    }

    private void OnPetUsed()
    {
        if (!Player.IsAlive() || hasAppointed) return;

        appointMode = !appointMode;
        ApplyModeDesync(appointMode);
        SendRPC();
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: Player);
    }

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (!Is(info.AttemptKiller) || info.IsSuicide) return;

        info.DoKill = false;
        if (!appointMode || hasAppointed) return;
        if (nowcool > 0f) return;

        (_, var target) = info.AttemptTuple;
        DoAppoint(target);
    }

    private void DoAppoint(PlayerControl target)
    {
        hasAppointed = true;

        if (target.GetCustomRole().IsImpostor())
        {
            PlayerState.GetByPlayerId(Player.PlayerId).DeathReason = CustomDeathReason.Suicide;
            Player.RpcMurderPlayer(Player);
            appointMode = false;
            ApplyModeDesync(false);
            SendRPC();
            return;
        }

        appointMode = false;
        ApplyModeDesync(false);

        Sheriff.AppointedPlayerIds.Add(target.PlayerId);
        foreach (var task in target.Data.Tasks.ToArray())
            target.RpcCompleteTask(task.Id);

        if (!Utils.RoleSendList.Contains(target.PlayerId))
            Utils.RoleSendList.Add(target.PlayerId);

        var previousRole = target.GetCustomRole();
        target.RpcSetCustomRole(CustomRoles.Sheriff, log: null);

        target.ResetKillCooldown();
        target.SetKillCooldown();
        target.RpcResetAbilityCooldown();

        NameColorManager.Add(Player.PlayerId, target.PlayerId, "#f5a623");

        UtilsGameLog.AddGameLog(
            "VillageChief",
            $"{UtilsName.GetPlayerColor(Player)}({UtilsRoleText.GetRoleName(CustomRoles.VillageChief)})が" +
            $"{UtilsName.GetPlayerColor(target)}({UtilsRoleText.GetRoleName(previousRole)})をシェリフに任命した"
        );

        SendRPC();
        UtilsNotifyRoles.NotifyRoles();
    }
    private void ApplyModeDesync(bool toAppointMode)
    {
        if (!Player.IsAlive()) return;

        foreach (var pc in PlayerCatch.AllAlivePlayerControls)
        {
            var role = pc.GetCustomRole();

            if (role.IsImpostor())
            {
                pc.RpcSetRoleDesync(
                    toAppointMode ? RoleTypes.Scientist : role.GetRoleTypes(),
                    Player.GetClientId());
            }

            if (Is(pc))
            {
                pc.RpcSetRoleDesync(
                    toAppointMode ? RoleTypes.Impostor : RoleTypes.Engineer,
                    Player.GetClientId());
            }
        }

        if (toAppointMode)
        {
            Player.SetKillCooldown(Mathf.Max(nowcool, 0.1f), delay: true);
        }
        else
        {
            Player.MarkDirtySettings();
            _ = new LateTask(() =>
            {
                if (!Player.IsAlive() || appointMode) return;
                Player.RpcResetAbilityCooldown(Sync: true);
            }, 0.1f, "VillageChief.EngineerReset", true);
        }
    }

    private void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(appointMode);
        sender.Writer.Write(hasAppointed);
        sender.Writer.Write(nowcool);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        appointMode = reader.ReadBoolean();
        hasAppointed = reader.ReadBoolean();
        nowcool = reader.ReadSingle();
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (GameStates.CalledMeeting || GameStates.Intro) return;

        if (!Player.IsAlive() && appointMode)
        {
            appointMode = false;
            ApplyModeDesync(false);
            SendRPC();
            return;
        }

        if (!Player.IsAlive()) return;

        if (nowcool > 0f) nowcool -= Time.fixedDeltaTime;
        else nowcool = 0f;

        var now = Mathf.FloorToInt(nowcool);
        if (now != LastCooltime)
        {
            LastCooltime = now;

            if (!appointMode && !hasAppointed)
            {
                Player.MarkDirtySettings();
                _ = new LateTask(() =>
                {
                    if (Player.IsAlive() && !appointMode)
                        Player.RpcResetAbilityCooldown(Sync: true);
                }, 0.1f, "VillageChief.VentCDSync", true);
            }

            if (player != PlayerControl.LocalPlayer)
                UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: player);
        }
    }

    public override void AfterMeetingTasks()
    {
        if (!Player.IsAlive()) return;
        _ = new LateTask(() =>
        {
            nowcool = AppointCooldown.GetFloat();
            LastCooltime = -1;
            ApplyModeDesync(appointMode);
        }, Main.LagTime, "Reset-VillageChief");
    }

    public override string GetProgressText(bool comms = false, bool gamelog = false)
    {
        if (hasAppointed) return "<color=#f5a623>(任命済)</color>";
        if (!GameStates.CalledMeeting && !gamelog)
            return Utils.ColorString(Color.yellow, appointMode ? " [任命]" : " [Task]");
        return "<color=#808080>(未任命)</color>";
    }

    public override bool CanTask() => true;

    public bool OverrideKillButton(out string text)
    {
        text = "VillageChief_Appoint";
        return true;
    }
}