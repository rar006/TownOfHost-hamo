using System.Linq;
using AmongUs.GameOptions;
using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Neutral;

// ロミオとジュリエット: 第三陣営の2人1組。お互いの位置が矢印で分かり、2人だけの秘匿チャット(/rj)を使える。
// 片方が死んだ後、残った方が設定した死に方(キル・追放)で死ぬと2人で単独勝利する。
// 2人が同じタスクターンの間に死んだ時と、設定以外の死に方をした時は勝てない。
public sealed class Romeo : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Romeo),
            player => new Romeo(player),
            CustomRoles.Romeo,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            992700,
            SetupOptionItem,
            "rmo",
            "#e8475f",
            (3, 6),
            tab: TabGroup.Combinations,
            assignInfo: new RoleAssignInfo(CustomRoles.Romeo, CustomRoleTypes.Neutral)
            {
                AssignCountRule = new(1, 1, 1),
                AssignUnitRoles = [CustomRoles.Romeo, CustomRoles.Juliet]
            },
            combination: CombinationRoles.RomeoandJuliet,
            from: From.TownOfHost_hamo,
            isNewRole: true
        );

    public Romeo(PlayerControl player) : base(RoleInfo, player, () => HasTask.False) { }

    public static OptionItem OptionRomeoArrow;
    public static OptionItem OptionJulietArrow;
    public static OptionItem OptionCanUseChat;
    public static OptionItem OptionWinByKill;
    public static OptionItem OptionWinByExile;
    enum OptionName
    {
        RomeoArrow,
        JulietArrow,
        RomeoCanUseChat,
        RomeoWinDeathType,
        RomeoWinByKill,
        RomeoWinByExile,
    }

    private static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, 9, defo: 1);
        // ロミオから見たジュリエットの矢印 = 「ジュリエットの気配」
        OptionJulietArrow = BooleanOptionItem.Create(RoleInfo, 10, OptionName.JulietArrow, true, false);
        OptionRomeoArrow = BooleanOptionItem.Create(RoleInfo, 11, OptionName.RomeoArrow, true, false);
        OptionCanUseChat = BooleanOptionItem.Create(RoleInfo, 12, OptionName.RomeoCanUseChat, true, false);
        var deathType = BooleanOptionItem.Create(RoleInfo, 13, OptionName.RomeoWinDeathType, true, false);
        OptionWinByKill = BooleanOptionItem.Create(RoleInfo, 14, OptionName.RomeoWinByKill, true, false, deathType);
        OptionWinByExile = BooleanOptionItem.Create(RoleInfo, 15, OptionName.RomeoWinByExile, true, false, deathType);
    }

    public override void Add() => RomeoJuliet.OnAdd(Player, CustomRoles.Juliet, OptionJulietArrow.GetBool());
    public override void OnMurderPlayerAsTarget(MurderInfo info) => RomeoJuliet.OnKilled(Player);
    public override void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner) => RomeoJuliet.OnExiled(Player, exiled, ref DecidedWinner);
    public override void AfterMeetingTasks() => RomeoJuliet.AfterMeeting();
    public override void OnDead(PlayerControl player) => RomeoJuliet.OnDead(Player, player);
    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
        => RomeoJuliet.GetArrow(Player, seer, seen, isForMeeting, OptionJulietArrow.GetBool());
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
        => RomeoJuliet.GetMark(Player, seer, seen);
}

public sealed class Juliet : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Juliet),
            player => new Juliet(player),
            CustomRoles.Juliet,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            992800,
            null,
            "jlt",
            "#f48fb1",
            (3, 7),
            tab: TabGroup.Combinations,
            combination: CombinationRoles.RomeoandJuliet,
            from: From.TownOfHost_hamo,
            isNewRole: true
        );

    public Juliet(PlayerControl player) : base(RoleInfo, player, () => HasTask.False) { }

    public override void Add() => RomeoJuliet.OnAdd(Player, CustomRoles.Romeo, Romeo.OptionRomeoArrow.GetBool());
    public override void OnMurderPlayerAsTarget(MurderInfo info) => RomeoJuliet.OnKilled(Player);
    public override void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner) => RomeoJuliet.OnExiled(Player, exiled, ref DecidedWinner);
    public override void AfterMeetingTasks() => RomeoJuliet.AfterMeeting();
    public override void OnDead(PlayerControl player) => RomeoJuliet.OnDead(Player, player);
    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
        => RomeoJuliet.GetArrow(Player, seer, seen, isForMeeting, Romeo.OptionRomeoArrow.GetBool());
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
        => RomeoJuliet.GetMark(Player, seer, seen);
}

// ロミオとジュリエットで共通の処理
public static class RomeoJuliet
{
    // 今が何回目のタスクターンか(会議が終わるたびに増える)
    private static int turn;
    // 先に死んだ方の PlayerId と、死んだタスクターン
    private static byte firstDeadId = byte.MaxValue;
    private static int firstDeadTurn = -1;
    private static bool won;

    public static void OnAdd(PlayerControl self, CustomRoles partnerRole, bool arrow)
    {
        turn = 0;
        firstDeadId = byte.MaxValue;
        firstDeadTurn = -1;
        won = false;
        if (!arrow) return;
        var partner = GetPartner(self);
        if (partner != null) TargetArrow.Add(self.PlayerId, partner.PlayerId);
    }

    public static PlayerControl GetPartner(PlayerControl self)
    {
        if (self == null) return null;
        var want = self.Is(CustomRoles.Romeo) ? CustomRoles.Juliet : CustomRoles.Romeo;
        return PlayerCatch.AllPlayerControls.FirstOrDefault(pc => pc != null && pc.Is(want));
    }

    public static bool IsPair(PlayerControl pc) => pc != null && (pc.Is(CustomRoles.Romeo) || pc.Is(CustomRoles.Juliet));

    public static void AfterMeeting() => turn++;

    // 死んだ(死に方に関わらず)。先に死んだ方を覚える
    public static void OnDead(PlayerControl self, PlayerControl dead)
    {
        if (dead != self || firstDeadId != byte.MaxValue) return;
        var partner = GetPartner(self);
        if (partner != null && !partner.IsAlive()) return; // 相方が先に死んでいる(この人は2人目)
        firstDeadId = self.PlayerId;
        firstDeadTurn = turn;
        UtilsNotifyRoles.NotifyRoles();
    }

    // 残った方がキルされた
    public static void OnKilled(PlayerControl self)
    {
        if (!AmongUsClient.Instance.AmHost || won || !Romeo.OptionWinByKill.GetBool()) return;
        if (!CanWinNow(self, sameTurnCheck: true)) return;
        Win(self);
    }

    // 残った方が追放された
    public static void OnExiled(PlayerControl self, NetworkedPlayerInfo exiled, ref bool decidedWinner)
    {
        if (!AmongUsClient.Instance.AmHost || won || exiled == null || exiled.PlayerId != self.PlayerId) return;
        if (!Romeo.OptionWinByExile.GetBool()) return;
        if (!CanWinNow(self, sameTurnCheck: false)) return;
        Win(self);
        decidedWinner = true;
    }

    private static bool CanWinNow(PlayerControl self, bool sameTurnCheck)
    {
        var partner = GetPartner(self);
        if (partner == null || partner.IsAlive()) return false;
        if (firstDeadId != partner.PlayerId) return false;
        // 同じタスクターンの間に2人とも死んだ時は勝てない
        if (sameTurnCheck && firstDeadTurn == turn) return false;
        return true;
    }

    private static void Win(PlayerControl self)
    {
        won = true;
        var partner = GetPartner(self);
        if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Romeo, self.PlayerId, true))
        {
            CustomWinnerHolder.NeutralWinnerIds.Add(self.PlayerId);
            if (partner != null)
            {
                CustomWinnerHolder.WinnerIds.Add(partner.PlayerId);
                CustomWinnerHolder.NeutralWinnerIds.Add(partner.PlayerId);
            }
        }
        Logger.Info($"ロミオとジュリエットが勝利: {self.GetNameWithRole().RemoveHtmlTags()}", "RomeoJuliet");
    }

    public static string GetArrow(PlayerControl self, PlayerControl seer, PlayerControl seen, bool isForMeeting, bool arrow)
    {
        seen ??= seer;
        if (!arrow || isForMeeting || seer != self || seen != self) return "";
        var partner = GetPartner(self);
        // どちらかが死んだら矢印は消える
        if (partner == null || !self.IsAlive() || !partner.IsAlive()) return "";
        return Utils.ColorString(UtilsRoleText.GetRoleColor(partner.GetCustomRole()), TargetArrow.GetArrows(self, partner.PlayerId));
    }

    public static string GetMark(PlayerControl self, PlayerControl seer, PlayerControl seen)
    {
        seen ??= seer;
        if (seer != self || seen == self) return "";
        var partner = GetPartner(self);
        if (partner == null || seen != partner) return "";
        return Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.Romeo), "♡");
    }

    // 秘匿チャット(/rj 本文)。送れた時は true
    public static bool TrySendChat(PlayerControl player, string[] args)
    {
        if (!AmongUsClient.Instance.AmHost || player == null || !GameStates.InGame) return false;
        if (!IsPair(player) || !player.IsAlive() || Romeo.OptionCanUseChat?.GetBool() != true) return false;
        var send = string.Join(" ", args.Where(a => !a.StartsWith("/")));
        if (string.IsNullOrWhiteSpace(send)) return false;
        Logger.Info($"{player.Data.GetLogPlayerName()} : {send}", "RomeoJulietChat");
        var color = UtilsRoleText.GetRoleColor(CustomRoles.Romeo);
        var title = Utils.ColorString(color, $"♡{player.GetPlayerColor()}♡");
        foreach (var pc in PlayerCatch.AllPlayerControls)
        {
            if (pc == null) continue;
            // 2人と、死んでいる人(幽霊)に見せる
            if (!IsPair(pc) && pc.IsAlive()) continue;
            if (pc != PlayerControl.LocalPlayer && pc.GetClientId() == -1) continue;
            Utils.SendMessage(send.Mark(color), pc.PlayerId, title);
        }
        return true;
    }
}
