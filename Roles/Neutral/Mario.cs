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
            () => OptionVentCooldown.GetFloat() < 2f ? RoleTypes.Phantom : RoleTypes.Engineer,
            CustomRoleTypes.Neutral,
            552900,
            SetupOptionItem,
            "Mi",
            "#ff6201",
            (5, 7),
            from: From.TownOfHost_Enhanced,
            Desc: () => string.Format(GetString("MarioDesc"), OptionWinVentCount.GetInt())
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
        AURoleOptions.PhantomCooldown = VentCooldownTimer ?? 0f;
    }
    float IKiller.CalculateKillCooldown() => 999f;
    bool IKiller.CanUseSabotageButton() => false;
    bool IKiller.CanUseImpostorVentButton() => true;
    bool IKiller.CanUseKillButton() => false;
    bool IKiller.CanKill => false;
    public override bool CanVentMoving(PlayerPhysics physics, int ventId) => false;
    bool IUsePhantomButton.IsPhantomRole => true;
    bool IUsePhantomButton.IsresetAfterKill => false;
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (OptionVentCooldown.GetFloat() >= 2f) return;

        if (VentCooldownTimer != null && VentCooldownTimer > 0f)
        {
            VentCooldownTimer -= Time.fixedDeltaTime;
            AURoleOptions.PhantomCooldown = (float)VentCooldownTimer;
            Player.RpcResetAbilityCooldown();
        }
    }
    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
<<<<<<< HEAD
        // 出たばかりの間(null)も入れない。前は null の間に入り直せて、数え直しと追い出しが重なり、入ったり出たりを繰り返していた
        if (VentCooldownTimer == null || VentCooldownTimer > 0.1f) return false;
=======
        if (OptionVentCooldown.GetFloat() >= 2f && VentCooldownTimer > 0.1f) return false;
>>>>>>> 56c7b58ea5046de5c0a8b51960962f93d8453042
        --Count;
        SendRPC();
        if (Count <= 0)
        {
            Win();
        }
        // エンジニアの時は自分で 0.1 秒で出るので、まだ中にいる時だけ追い出す
        // (もう出ている人に送ると、もう一度入って出る動きになり、しばらく抜け出せないように見えていた)
        _ = new LateTask(() =>
        {
            if (Player != null && Player.inVent) Player.MyPhysics.RpcBootFromVent(ventId);
        }, 0.5f, "Mario.Boot", true);
        VentCooldownTimer = null;
        _ = new LateTask(() => VentCooldownTimer = OptionVentCooldown.GetFloat(), 1.2f, "", true);
        return true;
    }
    public override string GetProgressText(bool comms = false, bool gamelog = false)
    {
        var progress = Utils.ColorString(RoleInfo.RoleColor, $"({Count})");
        return progress;
    }
    private void Win()
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