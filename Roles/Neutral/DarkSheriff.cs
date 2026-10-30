using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Roles.Crewmate;
using UnityEngine;
using static TownOfHost.Roles.Core.Interfaces.ISchrodingerCatOwner;

namespace TownOfHost.Roles.Neutral;

public sealed class DarkSheriff : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(DarkSheriff),
            player => new DarkSheriff(player),
            CustomRoles.DarkSheriff,
            () => PetModeSwitch.Enabled ? RoleTypes.Crewmate : RoleTypes.Impostor,
            CustomRoleTypes.Neutral,
            77150,
            SetupOptionItem,
            "dsh",
            "#f8cd46",
            OptionSort: (7, 1),
            introSound: () => GetIntroSound(RoleTypes.Crewmate),
            isDesyncImpostor: true,
            from: From.TownOfHost_hamo
        );

    public DarkSheriff(PlayerControl player) : base(RoleInfo, player)
    {
        Awakened = false;
        CurrentKillCooldown = Sheriff.KillCooldown.GetFloat();
        mode = new PetModeSwitch(player);
        ShotLimit = Sheriff.ShotLimitOpt.GetInt();
    }
    readonly PetModeSwitch mode;
    public override void Add()
    {
        Patches.PetActionManager.Register(Player.PlayerId, () => mode.Toggle());
        mode.Start();
    }
    public override void OnDestroy() => Patches.PetActionManager.Unregister(Player.PlayerId);
    int ShotLimit;
    public bool CanUseKillButton() => Player.IsAlive() && !mode.TaskMode;
    // 会議が始まったらタスクモードに戻す(シェリフと同じ)。会議の後は、役職が元に戻されることがあるのでもう一度合わせる
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (PetModeSwitch.Enabled && Player.IsAlive()) mode.Set(true);
    }
    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost || !PetModeSwitch.Enabled || !Player.IsAlive()) return;
        _ = new LateTask(() => { if (Player.IsAlive()) mode.Set(mode.TaskMode); }, Main.LagTime + 0.5f, "DarkSheriff.Mode", true);
    }

    bool CanUseSheriffMode()
        => Player.IsAlive()
        && (Sheriff.staticGetCanKillAllAlive() || GameStates.AlreadyDied)
        && ShotLimit > 0;

    public override string GetProgressText(bool comms = false, bool gamelog = false)
    {
        if (Awakened || !Player.IsAlive()) return "";
        var progress = Utils.ColorString(CanUseSheriffMode() ? Color.yellow : Color.gray, $"({ShotLimit})");

        var m = GameStates.CalledMeeting || gamelog ? "" : mode.ModeText();

        return progress + m;
    }

    //static OptionItem OptionKillCooldownBefore;
    static OptionItem OptionKillCooldownAfter;
    static OptionItem OptionAwakenKillCount;
    static OptionItem OptionCanVentAfterAwaken;

    enum OptionName
    {
        DarkSheriffAwakenKillCount,
        DarkSheriffKillCooldownAfter,
        DarkSheriffCanVentAfterAwaken,
    }

    static void SetupOptionItem()
    {
        //OptionKillCooldownBefore = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 30f, false)
        //    .SetValueFormat(OptionFormat.Seconds);
        OptionAwakenKillCount = IntegerOptionItem.Create(RoleInfo, 11, OptionName.DarkSheriffAwakenKillCount, new(1, 15, 1), 3, false);
        OptionKillCooldownAfter = FloatOptionItem.Create(RoleInfo, 12, OptionName.DarkSheriffKillCooldownAfter, new(0f, 180f, 0.5f), 20f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionCanVentAfterAwaken = BooleanOptionItem.Create(RoleInfo, 13, OptionName.DarkSheriffCanVentAfterAwaken, true, false);
    }

    public bool Awakened;
    float CurrentKillCooldown;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(false);
        AURoleOptions.KillCooldown = CurrentKillCooldown;
    }

    public float CalculateKillCooldown() => CurrentKillCooldown;

    // HELP／マイロール表示でも自分をシェリフとして認識する。
    public override CustomRoles Misidentify() => Awakened || !Player.IsAlive() ? CustomRoles.NotAssigned : CustomRoles.Sheriff;
    public override void OverrideDisplayRoleNameAsSeen(PlayerControl seer, ref bool enabled, ref Color roleColor, ref string roleText, ref bool addon)
    {
        if (!seer.IsAlive() && !Awakened)
        {
            roleText += $"{UtilsRoleText.GetRoleColorAndtext(CustomRoles.DarkSheriff)}";
        }
    }
    public bool CanUseSabotageButton() => false;

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        ShotLimit--;
        if (info.DoKill && info.AttemptKiller != info.AttemptTarget)
        {
            var kills = Player.GetPlayerState().GetKillCount();
            if (!Awakened && (kills >= OptionAwakenKillCount.GetInt() || ShotLimit <= 0))
            {
                Awaken();
            }
            SendRpc();
        }
    }

    void Awaken()
    {
        Awakened = true;
        CurrentKillCooldown = OptionKillCooldownAfter.GetFloat();
        AURoleOptions.KillCooldown = CurrentKillCooldown;
        Player.RpcResetAbilityCooldown(Sync: true);
        Player.MarkDirtySettings();
    }

    public override bool CanClickUseVentButton => !Awakened || OptionCanVentAfterAwaken.GetBool();

    /// <summary>
    /// 自覚後、生存者が残り2人(1人)になったらダークシェリフの単独勝利。
    /// 他の陣営の勝利判定には一切干渉しない独立チェック。
    /// </summary>
    public static bool CheckWin(ref GameOverReason reason)
    {
        foreach (var pc in PlayerCatch.AllPlayerControls)
        {
            if (pc == null || pc.GetRoleClass() is not DarkSheriff darkSheriff) continue;
            if (!darkSheriff.Awakened) continue;
            if (!pc.IsAlive()) continue;

            var aliveCount = PlayerCatch.AllAlivePlayerControls.Count();
            if (aliveCount > 2) continue;

            if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.DarkSheriff, pc.PlayerId))
            {
                CustomWinnerHolder.NeutralWinnerIds.Add(pc.PlayerId);
                reason = GameOverReason.ImpostorsByKill;
                return true;
            }
        }
        return false;
    }

    void SendRpc()
    {
        using var sender = CreateSender();
        sender.Writer.Write(Awakened);
        sender.Writer.Write(CurrentKillCooldown);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        Awakened = reader.ReadBoolean();
        CurrentKillCooldown = reader.ReadSingle();
    }
}
