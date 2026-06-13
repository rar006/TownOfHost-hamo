using System.Linq;
using AmongUs.GameOptions;
using TownOfHost.Roles.Core;
using UnityEngine;
using static TownOfHost.PlayerCatch;
using static TownOfHost.Translator;

namespace TownOfHost.Roles.Neutral;
public sealed class PokerFace : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(PokerFace),
            player => new PokerFace(player),
            CustomRoles.PokerFace,
            () => OptionCanVent.GetBool() ? RoleTypes.Engineer : RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            270600,
            SetupOptionItem,
            "pf",
            "#72d16b",
            (7, 3),
            true,
            tab: TabGroup.Combinations,
            countType: CountTypes.None,
            from: From.SuperNewRoles,
            assignInfo: new RoleAssignInfo(CustomRoles.PokerFace, CustomRoleTypes.Neutral)
            {
                AssignCountRule = new(1, 1, 1),
                AssignUnitRoles = new CustomRoles[3]
                {
                    CustomRoles.PokerFace,
                    CustomRoles.PokerFace,
                    CustomRoles.PokerFace
                }
            },
            combination: CombinationRoles.PokerFace
        );

    public PokerFace(PlayerControl player)
        : base(RoleInfo, player, () => HasTask.False)
    {
    }

    // ── オプション ──────────────────────────────────────────────────
    static OptionItem OptionAdditionalWin;
    static OptionItem OptionCanVent;
    static OptionItem OptionAddOns;

    static bool AdditionalWin;
    static bool CanVent;

    enum OptionName
    {
        PokerFaceAdditionalWin,
        PokerFaceAddOns,
    }

    private static void SetupOptionItem()
    {
        // 追加勝利が OFF のときだけ有効（追加勝利 ON なら優先度は無意味）
        OptionAdditionalWin = BooleanOptionItem.Create(
            RoleInfo, 10, OptionName.PokerFaceAdditionalWin, false, false);

        // SoloWinOption は int ではなく TOH-P 内部の勝利優先度システムで管理
        // 追加勝利 ON のときは実質無視される
        SoloWinOption.Create(RoleInfo, 11, defo: 15);

        OptionCanVent = BooleanOptionItem.Create(
            RoleInfo, 12, GeneralOption.CanVent, false, false);

        OptionAddOns = BooleanOptionItem.Create(
            RoleInfo, 13, OptionName.PokerFaceAddOns, false, false);
        // OptionAddOns が ON のときだけ属性が実際に適用されるよう Add() で制御する
        RoleAddAddons.Create(RoleInfo, 14, NeutralKiller: false);
    }

    public override void Add()
    {
        AdditionalWin = OptionAdditionalWin.GetBool();
        CanVent = OptionCanVent.GetBool();
    }

    // ── ベント設定 ──────────────────────────────────────────────────
    public override void ApplyGameOptions(IGameOptions opt)
    {
        if (!CanVent) return;
        AURoleOptions.EngineerCooldown = 0f;
        AURoleOptions.EngineerInVentMaxTime = 0f;
    }

    public override bool CanClickUseVentButton => CanVent;

    // ── 仲間の認識 ─────────────────────────────────────────────────
    // 同じポーカーフェイスのプレイヤー同士のみ役職名を見せる
    public override void OverrideDisplayRoleNameAsSeer(
        PlayerControl seen, ref bool enabled, ref Color roleColor,
        ref string roleText, ref bool addon)
    {
        if (seen.PlayerId != Player.PlayerId && seen.Is(CustomRoles.PokerFace))
            enabled = true;
    }

    public override void OverrideDisplayRoleNameAsSeen(
        PlayerControl seer, ref bool enabled, ref Color roleColor,
        ref string roleText, ref bool addon)
    {
        if (seer.PlayerId != Player.PlayerId && seer.Is(CustomRoles.PokerFace))
            enabled = true;
    }

    // 仲間の名前に♦マークを表示（自分 → 他の仲間のみ）
    public override string GetMark(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false)
    {
        seen ??= seer;
        if (!Is(seer)) return "";
        if (seen.PlayerId == seer.PlayerId) return "";
        if (!seen.Is(CustomRoles.PokerFace)) return "";
        // 生存 → 金色♦、死亡 → グレー×
        return seen.IsAlive()
            ? $" <color={RoleInfo.RoleColorCode}>♦</color>"
            : " <color=#888888>×</color>";
    }

    // ── 勝利判定 ────────────────────────────────────────────────────
    public override void CheckWinner(GameOverReason reason)
    {
        if (!Player.IsAlive()) return;

        var allPF = AllPlayerControls.Where(pc => pc.Is(CustomRoles.PokerFace)).ToList();
        int groupSize = allPF.Count;

        // 3人未満は判定しない（設定ミス防止）
        if (groupSize < 3) return;

        // 生存しているポーカーフェイスが自分1人だけ → 勝利
        int aliveCount = allPF.Count(pc => pc.IsAlive());
        if (aliveCount != 1) return;

        if (AdditionalWin)
        {
            // 追加勝利: 既存の勝者に乗る
            CustomWinnerHolder.NeutralWinnerIds.Add(Player.PlayerId);
            CustomWinnerHolder.WinnerIds.Add(Player.PlayerId);
        }
        else
        {
            // 単独勝利（SoloWinOption の優先度で他の中立役職と比較）
            if (CustomWinnerHolder.ResetAndSetAndChWinner(
                CustomWinner.PokerFace, Player.PlayerId, true))
            {
                CustomWinnerHolder.NeutralWinnerIds.Add(Player.PlayerId);
                CustomWinnerHolder.WinnerIds.Add(Player.PlayerId);
            }
        }
    }

    // ── 表示 ────────────────────────────────────────────────────────
    // 生存している仲間の人数を右上に表示
    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        var allPF = AllPlayerControls.Where(pc => pc.Is(CustomRoles.PokerFace)).ToList();
        int alivePartners = allPF.Count(pc => pc.IsAlive() && pc.PlayerId != Player.PlayerId);
        return $"<color={RoleInfo.RoleColorCode}>({alivePartners})</color>";
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";

        var allPF = AllPlayerControls.Where(pc => pc.Is(CustomRoles.PokerFace)).ToList();
        int alivePartners = allPF.Count(pc => pc.IsAlive() && pc.PlayerId != Player.PlayerId);

        string size = isForHud ? "" : "<size=60%>";
        string color = RoleInfo.RoleColorCode;

        if (alivePartners == 0)
            return $"{size}<color={color}>仲間は全員死亡！このまま生き残れば勝利！</color>";

        return $"{size}<color={color}>生存している仲間: {alivePartners}人</color>";
    }
}