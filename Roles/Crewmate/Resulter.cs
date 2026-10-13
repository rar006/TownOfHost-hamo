using AmongUs.GameOptions;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using static TownOfHost.Modules.SelfVoteManager;

namespace TownOfHost.Roles.Crewmate;

// ===== リザルター (Resulter) =====
// イントロ：その会議の行方は...
// 陣営：クルーメイト / 置き換え：クルーメイト
//
// 会議中に自投票する事で、そのターン誰かが吊られた時、吊られた人の役職を皆に公開する。
// スキップだった場合は何も起こらない(設定によっては使用回数は減る)。
public sealed class Resulter : RoleBase, ISelfVoter
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Resulter),
            player => new Resulter(player),
            CustomRoles.Resulter,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            96200,
            SetupOptionItem,
            "Rst",
            "#F5A623",
            (5, 2),
            from: From.TownOfHost_hamo
        );

    public Resulter(PlayerControl player) : base(RoleInfo, player)
    {
        maxUseCount = OptionUseCount.GetInt();
        taskCountToUnlock = OptionTaskCountToUnlock.GetInt();
        taskCountToSelfAware = OptionTaskCountToSelfAware.GetInt();
        consumeOnSkip = OptionConsumeOnSkip.GetBool();

        remainingUses = maxUseCount;
        isArmedThisMeeting = false;
    }

    static OptionItem OptionUseCount;
    static OptionItem OptionTaskCountToUnlock;
    static OptionItem OptionTaskCountToSelfAware;
    static OptionItem OptionConsumeOnSkip;

    enum OptionName
    {
        ResulterUseCount,
        ResulterTaskCountToUnlock,
        ResulterTaskCountToSelfAware,
        ResulterConsumeOnSkip,
    }

    private readonly int maxUseCount;
    private readonly int taskCountToUnlock;
    private readonly int taskCountToSelfAware;
    private readonly bool consumeOnSkip;

    private int remainingUses;
    // このターン、自投票によって能力を「セット」しているか
    private bool isArmedThisMeeting;

    static void SetupOptionItem()
    {
        OptionUseCount = IntegerOptionItem.Create(RoleInfo, 10, OptionName.ResulterUseCount,
            new(1, 99, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
        OptionTaskCountToUnlock = IntegerOptionItem.Create(RoleInfo, 11, OptionName.ResulterTaskCountToUnlock,
            new(0, 255, 1), 5, false)
            .SetValueFormat(OptionFormat.Pieces);
        OptionTaskCountToSelfAware = IntegerOptionItem.Create(RoleInfo, 12, OptionName.ResulterTaskCountToSelfAware,
            new(0, 255, 1), 3, false)
            .SetValueFormat(OptionFormat.Pieces);
        OptionConsumeOnSkip = BooleanOptionItem.Create(RoleInfo, 13, OptionName.ResulterConsumeOnSkip, true, false);
    }

    bool ISelfVoter.CanUseVoted() => false;

    private bool CanUseAbility()
    {
        if (!Player.IsAlive() || remainingUses <= 0) return false;
        // 自覚するまでのタスク数に達していない場合、能力そのものを使えない。
        if (taskCountToSelfAware > 0 && MyTaskState.CompletedTasksCount < taskCountToSelfAware) return false;
        // 能力解放に必要なタスク数が設定されている場合、それを満たすまで使用不可。
        if (taskCountToUnlock > 0 && MyTaskState.CompletedTasksCount < taskCountToUnlock) return false;
        return true;
    }

    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)
    {
        if (!Is(voter) || !Canuseability() || !CanUseAbility()) return true;

        if (CheckSelfVoteMode(Player, votedForId, out var status))
        {
            if (status is VoteStatus.Self)
            {
                isArmedThisMeeting = true;
                Utils.SendMessage(GetString("Resulter.Armed"), Player.PlayerId);
                SetMode(Player, false);
                return false;
            }

            if (status is VoteStatus.Skip)
            {
                isArmedThisMeeting = false;
                SetMode(Player, false);
                return false;
            }
        }

        return true;
    }

    public override void OnStartMeeting()
    {
        isArmedThisMeeting = false;
    }

    public override void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner)
    {
        if (!isArmedThisMeeting) return;
        isArmedThisMeeting = false;

        if (exiled is null || exiled.PlayerId == byte.MaxValue)
        {
            // スキップだった場合は何も起こらない(設定によっては使用回数のみ減らす)。
            if (consumeOnSkip) remainingUses--;
            return;
        }

        remainingUses--;

        var roleName = UtilsRoleText.GetRoleName(exiled.GetCustomRole());
        Utils.SendMessage(string.Format(GetString("Resulter.RevealResult"), exiled.PlayerName, roleName));

        UtilsGameLog.AddGameLog("Resulter",
            $"{UtilsName.GetPlayerColor(Player)}が{exiled.PlayerName}({roleName})の役職を公開した");
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!isForMeeting || !Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";

        // 自覚するまでのタスク数に達していない場合、能力自体を教えない(通常のクルーメイトの表示のまま)。
        if (taskCountToSelfAware > 0 && MyTaskState.CompletedTasksCount < taskCountToSelfAware) return "";

        var prefix = isForHud ? "" : "<size=60%>";
        var c = RoleInfo.RoleColorCode;

        if (!CanUseAbility() && remainingUses <= 0)
            return $"{prefix}<color={c}>公開能力: 使用回数なし</color>";

        if (taskCountToUnlock > 0 && MyTaskState.CompletedTasksCount < taskCountToUnlock)
            return $"{prefix}<color={c}>タスクを進めると能力が解放されます</color>";

        var stateText = isArmedThisMeeting ? "発動準備完了" : "会議自投票→発動セット";
        return $"{prefix}<color={c}>{stateText}(残り{remainingUses}回)</color>";
    }

    public static System.Collections.Generic.Dictionary<int, Achievement> achievements = new();
    [Attributes.PluginModuleInitializer]
    public static void Load()
    {
        var n1 = new Achievement(RoleInfo, 0, 1, 0, 1);
        achievements.Add(0, n1);
    }
}
