using AmongUs.GameOptions;
using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Vanilla;

/// <summary>
/// Among Us V11公式クルー役職「ジャッジ」。
/// Overruleのボタン・RPC・使用回数はゲーム本体のJudgeOverruleVoteに委ねる。
/// </summary>
public sealed class Judge : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Judge),
            player => new Judge(player),
            CustomRoles.Judge,
            () => RoleTypes.Judge,
            CustomRoleTypes.Crewmate,
            25100,
            SetUpCustomOption,
            "ju",
            "#a1472c",
            (0, 6),
            from: From.AmongUs
        );

    private static OptionItem TaskRequirement;
    private static OptionItem LongTasks;
    private static OptionItem NormalTasks;
    private static OptionItem ShortTasks;
    private static OptionItem CanKillMadmate;
    private static OptionItem CanKillNeutrals;
    private static OptionItem CanKillLovers;

    /// <summary>
    /// タスク再配布パッチが使用する、通常・ロング・ショートの個別タスク数。
    /// </summary>
    public static (bool hasCommonTasks, int numCommonTasks, int numLongTasks, int numShortTasks) TaskData =>
        (true, NormalTasks.GetInt(), LongTasks.GetInt(), ShortTasks.GetInt());

    public Judge(PlayerControl player)
        : base(RoleInfo, player)
    {
    }

    public static void SetUpCustomOption()
    {
        // 「必要タスク」を親見出しにし、その直下へ3種類のタスク数を配置する。
        TaskRequirement = BooleanOptionItem.Create(25110, "JudgeTaskRequirement", true, TabGroup.CrewmateRoles, false)
            .SetParentRole(CustomRoles.Judge)
            .SetHeader(true);

        // ユーザー指定: 全項目1～99、刻み1。
        LongTasks = IntegerOptionItem.Create(25111, "JudgeLongTasks", new(1, 99, 1), 1, TabGroup.CrewmateRoles, false)
            .SetParentRole(CustomRoles.Judge)
            .SetParent(TaskRequirement)
            .SetValueFormat(OptionFormat.Pieces);
        NormalTasks = IntegerOptionItem.Create(25112, "JudgeNormalTasks", new(1, 99, 1), 1, TabGroup.CrewmateRoles, false)
            .SetParentRole(CustomRoles.Judge)
            .SetParent(TaskRequirement)
            .SetValueFormat(OptionFormat.Pieces);
        ShortTasks = IntegerOptionItem.Create(25113, "JudgeShortTasks", new(1, 99, 1), 1, TabGroup.CrewmateRoles, false)
            .SetParentRole(CustomRoles.Judge)
            .SetParent(TaskRequirement)
            .SetValueFormat(OptionFormat.Pieces);

        // 添付画像のジャッジ対象設定。いずれも初期値はオン。
        CanKillMadmate = BooleanOptionItem.Create(25114, "MeetingSheriffCanKillMadMate", true, TabGroup.CrewmateRoles, false)
            .SetParentRole(CustomRoles.Judge);
        CanKillNeutrals = BooleanOptionItem.Create(25115, "MeetingSheriffCanKillNeutrals", true, TabGroup.CrewmateRoles, false)
            .SetParentRole(CustomRoles.Judge);
        CanKillLovers = BooleanOptionItem.Create(25116, "SheriffCanKillLovers", true, TabGroup.CrewmateRoles, false)
            .SetParentRole(CustomRoles.Judge);
    }
}
