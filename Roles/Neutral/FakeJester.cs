using AmongUs.GameOptions;
using UnityEngine;

using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

// ===== フェイクジェスター (Fake Jester) =====
// イントロ：ジェスターと同じ
// 陣営：ニュートラル / 置き換え：クルー
//
// ジェスターと自覚しているフェイクジェスター。実際にはジェスターではなく、
// 会議の数ターン目に「自分がフェイクジェスターだと自覚できる」。
// 勝利条件：生き残れれば乗っ取り(設定次第で追加勝利/単独勝利)。
public sealed class FakeJester : RoleBase, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(FakeJester),
            player => new FakeJester(player),
            CustomRoles.FakeJester,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            85400,
            SetupOptionItem,
            "fje",
            "#ec62a5",
            (4, 21),
            introSound: () => GetIntroSound(RoleTypes.Crewmate),
            from: From.TownOfHost_hamo
        );

    public FakeJester(PlayerControl player) : base(RoleInfo, player)
    {
    }

    static OptionItem OptionAwakenMeetingCount;
    static OptionItem OptionIsAdditionalWin;

    enum OptionName
    {
        FakeJesterAwakenMeetingCount,
        FakeJesterIsAdditionalWin,
    }

    static void SetupOptionItem()
    {
        OptionAwakenMeetingCount = IntegerOptionItem.Create(RoleInfo, 10, OptionName.FakeJesterAwakenMeetingCount,
            new(3, 5, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
        OptionIsAdditionalWin = BooleanOptionItem.Create(RoleInfo, 11, OptionName.FakeJesterIsAdditionalWin, true, false);
    }

    // 通過した会議の回数(会議が開始されるたびに増える)
    private int passedMeetingCount;
    // 自分がフェイクジェスターだと自覚したかどうか
    private bool isAwakened;

    public override void OnStartMeeting()
    {
        passedMeetingCount++;
        if (!isAwakened && passedMeetingCount >= OptionAwakenMeetingCount.GetInt())
        {
            isAwakened = true;
            Utils.SendMessage(GetString("FakeJester.Awaken"), Player.PlayerId);
            UtilsGameLog.AddGameLog("FakeJester", string.Format(GetString("FakeJester.log"), UtilsName.GetPlayerColor(Player)));
        }
    }

    // 自認するまでは、自分自身を含め、名前欄・役職紹介・イントロ等あらゆる場所で
    // 「ジェスター」として誤認させる(=本人もジェスターだと思い込んでいる状態)。
    // 自認した後は誤認を解除し、本来のフェイクジェスターとして表示されるようにする。
    public override CustomRoles Misidentify() => isAwakened ? CustomRoles.NotAssigned : CustomRoles.Jester;

    // バケネコと同様に「フェイクジェスター/ジェスター」の併記表示を行う。
    // 自認前は通常通り「ジェスター」とだけ見える(Misidentifyのみで完結)。
    // 自認後は、本人には素の役職名(フェイクジェスター)が見えつつ、
    // 他プレイヤーからは引き続き「ジェスター」に見えるよう、Misidentifyで誤認は継続させ、
    // その上で名前欄に小さく「フェイクジェスター」の表記を併記する。
    public override void OverrideDisplayRoleNameAsSeen(PlayerControl seer, ref bool enabled, ref Color roleColor, ref string roleText, ref bool addon)
    {
        if (!isAwakened) return;

        if (seer.PlayerId == Player.PlayerId)
        {
            // 本人には自認後の素の姿(フェイクジェスター)がそのまま見えていればよいので、
            // 特に上書きは行わない(既定の表示ロジックに委ねる)。
            return;
        }

        // 他プレイヤーからは、これまで通り「ジェスター」に見えたままにしつつ、
        // バケネコと同じ形式で「/フェイクジェスター」を併記する。
        roleText += $"/{UtilsRoleText.GetRoleColorAndtext(CustomRoles.FakeJester)}";
    }

    public bool CheckWin(ref CustomRoles winnerRole)
    {
        if (!isAwakened || !Player.IsAlive()) return false;

        if (OptionIsAdditionalWin.GetBool())
        {
            winnerRole = CustomRoles.FakeJester;
            return true;
        }

        // 単独勝利にする場合は、他の勝者をリセットして自分だけを勝者にする。
        if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.FakeJester, Player.PlayerId))
        {
            CustomWinnerHolder.NeutralWinnerIds.Add(Player.PlayerId);
        }
        return false;
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || !Is(seen) || !Player.IsAlive()) return "";
        if (!isAwakened) return "";

        var mes = $"<color={RoleInfo.RoleColorCode}>{GetString("FakeJester.AwakenedLabel")}</color>";
        return isForHud ? mes : $"<size=40%>{mes}</size>";
    }
}
