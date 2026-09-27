using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using UnityEngine;

namespace TownOfHost.Roles.Neutral;

public sealed class Mario : RoleBase, IKiller, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Mario),
            player => new Mario(player),
            CustomRoles.Mario,
            //ベントクール2秒未満にできない制限突破するため。
            () => OptionVentCooldown.GetFloat() < 2f ? RoleTypes.Phantom : RoleTypes.Engineer,
            CustomRoleTypes.Neutral,
            552900,
            SetupOptionItem,
            "Mi",
            "#ff6201",
            (5, 7),
            from: From.TownOfHost_Enhanced,
            isNewRole: true,
            Desc: () =>
            {
                return string.Format(GetString("MarioDesc"), OptionWinVentCount.GetInt());
            }
        );

    public Mario(PlayerControl player)
        : base(RoleInfo, player)
    {
        VentCooldownTimer = OptionVentCooldown.GetFloat();
        Count = OptionWinVentCount.GetInt();
    }
    static OptionItem OptionVentCooldown;
    static OptionItem OptionWinVentCount;

    float? VentCooldownTimer;
    int Count;
    enum OptionName
    {
        MarioVentCooldown,
        MarioVentCount
    }

    private static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, 9, defo: 15);

        OptionVentCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.MarioVentCooldown, new(0f, 180f, 0.5f), 2f, false)
            .SetValueFormat(OptionFormat.Seconds);

        OptionWinVentCount = IntegerOptionItem.Create(RoleInfo, 11, OptionName.MarioVentCount, new(1, 999, 1), 30, false)
            .SetValueFormat(OptionFormat.Times);
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(false);
        AURoleOptions.EngineerCooldown = OptionVentCooldown.GetFloat();
        AURoleOptions.EngineerInVentMaxTime = 0.1f;
        AURoleOptions.PhantomCooldown = (float)VentCooldownTimer;
    }
    bool IKiller.CanUseSabotageButton() => false;
    bool IKiller.CanUseImpostorVentButton() => true;
    bool IKiller.CanUseKillButton() => false;
    bool IKiller.CanKill => false;
    public override bool CanVentMoving(PlayerPhysics physics, int ventId) => false;
    bool IUsePhantomButton.IsPhantomRole => true;
    bool IUsePhantomButton.IsresetAfterKill => false;
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (VentCooldownTimer != null && VentCooldownTimer > 0f)
        {
            VentCooldownTimer -= Time.fixedDeltaTime;
            if (OptionVentCooldown.GetFloat() >= 2f) return;
            AURoleOptions.PhantomCooldown = (float)VentCooldownTimer;
            Player.RpcResetAbilityCooldown();
        }
    }
    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        if (VentCooldownTimer > 0.1f) return false;
        --Count;
        SendRPC();
        if (Count <= 0)
        {
            ForceSoloWin();
        }
        _ = new LateTask(() => Player.MyPhysics.RpcBootFromVent(ventId), 0.9f, "", true);
        VentCooldownTimer = null;
        _ = new LateTask(() => VentCooldownTimer = OptionVentCooldown.GetFloat(), 1.2f, "", true);
        return true;
    }
    public override string GetProgressText(bool comms = false, bool gamelog = false)
    {
        var progress = Utils.ColorString(RoleInfo.RoleColor, $"({Count})");
        return progress;
    }
    private void ForceSoloWin()
    {
        if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Mario, Player.PlayerId))
        {
            CustomWinnerHolder.WinnerIds.Add(Player.PlayerId);
            CustomWinnerHolder.NeutralWinnerIds.Add(Player.PlayerId);
        }
    }
    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = false;
        ResetCooldown = false;
    }
    private void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(Count);
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        Count = reader.ReadInt32();
    }
    public override string GetAbilityButtonText() => GetString("TriggerVent");
    public override bool OverrideAbilityButton(out string text)
    {
        text = "Comebacker_Ability";
        return true;
    }
}