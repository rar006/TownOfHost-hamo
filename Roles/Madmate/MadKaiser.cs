using AmongUs.GameOptions;

using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Madmate;

// ===== マッドカイザー (MadKaiser) =====
// From: TownOfHost_hamo
// イントロ：皇帝として君臨せよ
// 陣営：マッドメイト / 判定：クルーメイト
//
// 自身をクルーメイトだと勘違いしている(自分視点では特に何も表示されない)マッドメイト。
// クルーメイト視点からは常に「君臨者」という怪しい肩書きとして認識される。
// キルガードなどの耐性は無く、キルされれば普通に死亡する。
// 全体タスク完了率が設定割合(既定80%)に達すると、クルーメイトからの見え方が
// 「マッドカイザー」であるとバレてしまう表示に切り替わる。
public sealed class MadKaiser : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MadKaiser),
            player => new MadKaiser(player),
            CustomRoles.MadKaiser,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            23500,
            SetupOptionItem,
            "mk",
            "#8b0000",
            (2, 6),
            countType: CountTypes.Crew,
            introSound: () => GetIntroSound(RoleTypes.Crewmate),
            from: From.TownOfHost_hamo
        );

    public MadKaiser(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        RequiredTaskPercent = OptionRequiredTaskPercent.GetInt();
    }

    static OptionItem OptionRequiredTaskPercent;

    enum OptionName
    {
        MadKaiserRequiredTaskPercent
    }

    private int RequiredTaskPercent;

    private static void SetupOptionItem()
    {
        OptionRequiredTaskPercent = IntegerOptionItem.Create(RoleInfo, 10, OptionName.MadKaiserRequiredTaskPercent, new(5, 95, 5), 80, false)
            .SetValueFormat(OptionFormat.Percent);
    }

    public override void ApplyGameOptions(IGameOptions opt) { }

    // 全体のタスク完了率(0~100)を計算する
    private float GetTaskCompletePercent()
    {
        if (GameData.Instance == null || GameData.Instance.TotalTasks <= 0) return 0f;
        return 100f * GameData.Instance.CompletedTasks / GameData.Instance.TotalTasks;
    }

    // クルーメイトからの見え方: 通常時は「君臨者」という金色の肩書き(君臨者と同じ表示方式)、
    // タスク完了率が閾値を超えると「マッドカイザー」だとバレる表示に切り替わる。
    // 自分自身から見た場合は何も表示しない(自身をクルーメイトだと勘違いしているため)。
    // ※ 君臨者(King)のOverrideDisplayRoleNameAsSeenと同じ仕組みを使うが、
    //   君臨者特有の「投票で追放されるとクルーを巻き込む」等の能力は一切持たない。表示だけの模倣。
    public override void OverrideDisplayRoleNameAsSeen(PlayerControl seer, ref bool enabled, ref UnityEngine.Color roleColor, ref string roleText, ref bool addon)
    {
        seer ??= Player;
        if (seer == Player) return;
        if (!seer.Is(CustomRoleTypes.Crewmate)) return;

        enabled = true;
        addon = false;
        if (GetTaskCompletePercent() >= RequiredTaskPercent)
        {
            roleColor = RoleInfo.RoleColor;
            roleText = GetString("MadKaiserRevealedTitle");
        }
        else
        {
            roleColor = StringHelper.CodeColor("#FFD700");
            roleText = GetString("MadKaiserDisguiseTitle");
        }
    }
}
