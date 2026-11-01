using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

public sealed class LifeSaver : RoleBase, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(LifeSaver),
            player => new LifeSaver(player),
            CustomRoles.LifeSaver,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Neutral,
            97600,
            SetupOptionItem,
            "lsv",
            "#43FF00",
            (6, 6),
            introSound: () => GetIntroSound(RoleTypes.Engineer),
            from: From.TownOfHost_hamo,
            isNewRole: true
        );

    public LifeSaver(PlayerControl player)
    : base(RoleInfo, player, () => HasTask.ForRecompute)
    {
        cooldown = OptionCooldown.GetFloat();
        usesLeft = OptionUseCount.GetInt();
        winMode = false;
    }

    static OptionItem OptionCooldown;
    static OptionItem OptionUseCount;
    enum OptionName { LifeSaverCooldown, LifeSaverUseCount }

    static float cooldown;
    int usesLeft;
    bool winMode;

    static void SetupOptionItem()
    {
        OverrideTasksData.Create(RoleInfo, 10, tasks: (true, 2, 2, 3));
        OptionCooldown = FloatOptionItem.Create(RoleInfo, 20, OptionName.LifeSaverCooldown, new(0.5f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionUseCount = IntegerOptionItem.Create(RoleInfo, 21, OptionName.LifeSaverUseCount, new(0, 99, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
    }

    bool CanSwitch => IsTaskFinished && usesLeft > 0 && Player.IsAlive();

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.EngineerCooldown = cooldown;
        AURoleOptions.EngineerInVentMaxTime = 0.5f;
    }

    public override bool CanClickUseVentButton => CanSwitch;
    public override bool CanVentMoving(PlayerPhysics physics, int ventId) => false;

    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        if (!CanSwitch) return false;
        if (AmongUsClient.Instance.AmHost)
        {
            usesLeft--;
            SetMode(!winMode);
            Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} がベントでモードを変えました: {(winMode ? "勝利" : "バリア")} (残り{usesLeft}回)", "LifeSaver");
        }
        return false;
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;
        if (CustomWinnerHolder.WinnerTeam is not CustomWinner.Default) return;
        SetMode(!winMode);
    }

    void SetMode(bool win)
    {
        winMode = win;
        SendRPC();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    public override bool OnCheckMurderAsTarget(MurderInfo info)
    {
        if (winMode || info.IsFakeSuicide) return true;
        var (killer, target) = info.AttemptTuple;
        if (killer == target) return true;
        killer.RpcProtectedMurderPlayer(target);
        info.GuardPower = 1;
        Logger.Info($"{target.GetNameWithRole().RemoveHtmlTags()} がバリアモードで {killer.GetNameWithRole().RemoveHtmlTags()} のキルを防ぎました", "LifeSaver");
        return true;
    }

    public bool CheckWin(ref CustomRoles winnerRole) => Player.IsAlive() && winMode;

    public override void CheckWinner(GameOverReason reason)
    {
        if (winMode) return;
        CustomWinnerHolder.CantWinPlayerIds.Add(Player.PlayerId);
        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} はバリアモードで試合が終わったので勝利できません", "LifeSaver");
    }

    public override string GetProgressText(bool comms = false, bool gamelog = false)
    {
        var mode = winMode
            ? Utils.ColorString(RoleInfo.RoleColor, GetString("LifeSaverWinMode"))
            : Utils.ColorString(Color.cyan, GetString("LifeSaverBarrierMode"));
        if (gamelog) return mode;
        return mode + (IsTaskFinished ? Utils.ColorString(usesLeft > 0 ? Color.yellow : Color.gray, $"({usesLeft})") : "");
    }

    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        return seen == seer && Is(seen) && winMode && Player.IsAlive() ? Utils.AdditionalAliveWinnerMark : "";
    }

    public override string GetAbilityButtonText() => GetString("LifeSaverButton");

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(winMode);
        sender.Writer.Write(usesLeft);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        winMode = reader.ReadBoolean();
        usesLeft = reader.ReadInt32();
    }
}
