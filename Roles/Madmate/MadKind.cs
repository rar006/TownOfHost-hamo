using AmongUs.GameOptions;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Madmate;

// マッドカインド: タスクを終わらせると、インポスターとは勝てなくなる代わりにクルーメイトとして勝てるマッドメイト。
// 死亡した時は、その時点でタスクが終わっていればクルーメイトとして、終わっていなければインポスター陣営として勝てる。
public sealed class MadKind : RoleBase, IKillFlashSeeable, IDeathReasonSeeable
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MadKind),
            player => new MadKind(player),
            CustomRoles.MadKind,
            () => OptionCanVent.GetBool() ? RoleTypes.Engineer : RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            992400,
            SetupOptionItem,
            "mkd",
            "#FF1919",
            OptionSort: (4, 2),
            introSound: () => GetIntroSound(RoleTypes.Impostor),
            from: From.TownOfHost_hamo,
            isNewRole: true
        );

    public MadKind(PlayerControl player)
    : base(RoleInfo, player, () => HasTask.ForRecompute)
    {
        canSeeKillFlash = Options.MadmateCanSeeKillFlash.GetBool();
        canSeeDeathReason = Options.MadmateCanSeeDeathReason.GetBool();
        diedWithTasksDone = null;
    }

    private static OptionItem OptionCanVent;
    private static bool canSeeKillFlash;
    private static bool canSeeDeathReason;
    // 死亡した時にタスクが終わっていたか(生きている間は null)
    private bool? diedWithTasksDone;

    public bool? CheckKillFlash(MurderInfo info) => canSeeKillFlash;
    public bool? CheckSeeDeathReason(PlayerControl seen) => canSeeDeathReason;
    public override CustomRoles TellResults(PlayerControl player) => Options.MadTellOpt();

    private static void SetupOptionItem()
    {
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, 10, GeneralOption.CanVent, false, false);
        // コモン3・ロング3・ショート4
        OverrideTasksData.Create(RoleInfo, 11, tasks: (true, 3, 3, 4));
    }

    // クルーメイト側に寝返ったか(生きていれば今のタスク、死んでいれば死んだ時のタスクで決まる)
    private bool IsCrewSide => diedWithTasksDone ?? IsTaskFinished;

    public override void OnDead(PlayerControl player)
    {
        if (player != Player || diedWithTasksDone != null) return;
        diedWithTasksDone = IsTaskFinished;
    }

    public override bool OnCompleteTask(uint taskid)
    {
        if (IsTaskFinished) UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
        return true;
    }

    public override void CheckWinner(GameOverReason reason)
    {
        if (!IsCrewSide) return; // タスクが終わっていなければ、普通のマッドメイトと同じ
        var id = Player.PlayerId;
        if (CustomWinnerHolder.WinnerTeam == CustomWinner.Impostor)
        {
            CustomWinnerHolder.WinnerIds.Remove(id);
            CustomWinnerHolder.CantWinPlayerIds.Add(id);
        }
        else if (CustomWinnerHolder.WinnerTeam == CustomWinner.Crewmate)
        {
            CustomWinnerHolder.WinnerIds.Add(id);
            CustomWinnerHolder.CantWinPlayerIds.Remove(id);
        }
    }

    public override string GetProgressText(bool comms = false, bool gamelog = false)
        => IsCrewSide ? Utils.ColorString(Palette.CrewmateBlue, " [クルー側]") : "";
}
