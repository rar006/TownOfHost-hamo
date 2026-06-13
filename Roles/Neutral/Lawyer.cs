using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using static TownOfHost.PlayerCatch;
using static TownOfHost.Utils;
using static TownOfHost.Translator;

namespace TownOfHost.Roles.Neutral;

// ══════════════════════════════════════════════════════════════
// 弁護士 (TOHY移植)
// インポスター or キル可能ニュートラルを依頼人として擁護する。
// 依頼人が死亡/追放 → 追跡者へ転職。
// 依頼人の陣営が勝利した時: 生存→単独勝利 / 死亡→追加勝利。
// ══════════════════════════════════════════════════════════════
public sealed class Lawyer : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Lawyer),
            player => new Lawyer(player),
            CustomRoles.Lawyer,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            385200,
            SetupOptionItem,
            "lw",
            "#daa520",
            (4, 7),
            introSound: () => GetIntroSound(RoleTypes.Impostor),
            from: From.TownOfHost_Y
        );

    public Lawyer(PlayerControl player)
        : base(RoleInfo, player, () => HasTask.False)
    {
        targetPlayerId = byte.MaxValue;
        changedToPursuer = false;
        Lawyers.Add(this);
        CustomRoleManager.MarkOthers.Add(GetMarkOthers);
    }

    public override void OnDestroy()
    {
        Lawyers.Remove(this);
        CustomRoleManager.MarkOthers.Remove(GetMarkOthers);
    }

    // ─── オプション ──────────────────────────────────────
    static OptionItem OptionHasImpostorVision;
    static OptionItem OptionKnowTargetRole;
    static OptionItem OptionTargetKnows;
    static OptionItem OptionPursuerGuardNum;

    public static bool HasImpostorVision;
    public static bool KnowTargetRole;
    public static bool TargetKnows;
    public static int PursuerGuardNum;

    enum OptionName
    {
        LawyerKnowTargetRole,
        LawyerTargetKnows,
        LawyerPursuerGuardNum,
    }

    static void SetupOptionItem()
    {
        OptionHasImpostorVision = BooleanOptionItem.Create(RoleInfo, 10, GeneralOption.ImpostorVision, false, false);
        OptionKnowTargetRole = BooleanOptionItem.Create(RoleInfo, 11, OptionName.LawyerKnowTargetRole, false, false);
        OptionTargetKnows = BooleanOptionItem.Create(RoleInfo, 12, OptionName.LawyerTargetKnows, false, false);
        OptionPursuerGuardNum = IntegerOptionItem.Create(RoleInfo, 13, OptionName.LawyerPursuerGuardNum,
            new(0, 20, 1), 1, false).SetValueFormat(OptionFormat.Times);

        if (Options.CustomRoleSpawnChances?.TryGetValue(CustomRoles.Pursuer, out var sp) == true)
            sp.SetHidden(true);
        if (Options.CustomRoleCounts?.TryGetValue(CustomRoles.Pursuer, out var cp) == true)
            cp.SetHidden(true);
    }

    // ─── 内部状態 ─────────────────────────────────────────
    private static readonly HashSet<Lawyer> Lawyers = new();
    byte targetPlayerId;
    bool changedToPursuer;

    private PlayerControl Target
        => targetPlayerId == byte.MaxValue ? null : GetPlayerById(targetPlayerId);

    // ─── 初期化 ──────────────────────────────────────────
    public override void Add()
    {
        HasImpostorVision = OptionHasImpostorVision.GetBool();
        KnowTargetRole = OptionKnowTargetRole.GetBool();
        TargetKnows = OptionTargetKnows.GetBool();
        PursuerGuardNum = OptionPursuerGuardNum.GetInt();

        if (!AmongUsClient.Instance.AmHost) return;

        // 依頼人選出: インポスター or キル持ちニュートラル
        var candidates = AllPlayerControls
            .Where(pc => pc.PlayerId != Player.PlayerId
                      && (pc.GetCustomRole().IsImpostor()
                          || (pc.GetCustomRole().IsNeutral()
                              && (pc.GetRoleClass() is IKiller || pc.GetRoleClass() is ILNKiller))))
            .ToList();

        if (candidates.Count == 0) return;

        var chosen = candidates[IRandom.Instance.Next(candidates.Count)];
        targetPlayerId = chosen.PlayerId;
        SendRpc();

        Logger.Info($"[Lawyer] 依頼人: {chosen.GetNameWithRole().RemoveHtmlTags()}", "Lawyer");
    }

    public override void ApplyGameOptions(IGameOptions opt) => opt.SetVision(HasImpostorVision);

    // ─── 追放による転職 ──────────────────────────────────
    public override bool VotingResults(ref NetworkedPlayerInfo Exiled, ref bool IsTie,
        Dictionary<byte, int> vote, byte[] mostVotedPlayers, bool ClearAndExile)
    {
        if (!changedToPursuer && targetPlayerId != byte.MaxValue
            && Exiled != null && Exiled.PlayerId == targetPlayerId && Player.IsAlive())
        {
            changedToPursuer = true;
            _ = new LateTask(ChangeRole, 0.5f, "Lawyer.ChangeByExile", true);
        }
        return false;
    }

    // ─── タスク中の死亡検知 ──────────────────────────────
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (changedToPursuer || targetPlayerId == byte.MaxValue) return;
        if (!Player.IsAlive()) return;
        if (!GameStates.IsInTask) return;

        var target = Target;
        if (target != null && !target.IsAlive())
        {
            changedToPursuer = true;
            ChangeRole();
        }
    }

    // ─── 追跡者へ転職 ────────────────────────────────────
    private void ChangeRole()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        targetPlayerId = byte.MaxValue;
        SendRpc();

        if (!RoleSendList.Contains(Player.PlayerId))
            RoleSendList.Add(Player.PlayerId);

        Player.RpcSetCustomRole(CustomRoles.Pursuer, log: null);
        UtilsNotifyRoles.NotifyRoles();
    }

    // ─── 勝利判定（ゲーム終了時に呼び出す）─────────────────
    public static void EndGameCheck()
    {
        foreach (var lawyer in Lawyers.ToArray())
        {
            if (lawyer.targetPlayerId == byte.MaxValue) continue;
            var target = GetPlayerById(lawyer.targetPlayerId);
            if (target == null) continue;

            bool targetWins = CustomWinnerHolder.WinnerIds.Contains(target.PlayerId)
                           || CustomWinnerHolder.WinnerRoles.Contains(target.GetCustomRole());
            if (!targetWins) continue;

            if (lawyer.Player.IsAlive())
            {
                // 生存 → 勝利横取り（単独勝利）
                if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Lawyer, lawyer.Player.PlayerId, true))
                    CustomWinnerHolder.WinnerIds.Add(lawyer.Player.PlayerId);
            }
            else
            {
                // 死亡 → 追加勝利
                CustomWinnerHolder.WinnerIds.Add(lawyer.Player.PlayerId);
            }
        }
    }

    // ─── 表示 ─────────────────────────────────────────────
    public override void OverrideDisplayRoleNameAsSeer(PlayerControl seen,
        ref bool enabled, ref Color roleColor, ref string roleText, ref bool addon)
    {
        if (KnowTargetRole && targetPlayerId != byte.MaxValue && seen.PlayerId == targetPlayerId)
            enabled = true;
    }

    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (!Is(seer) || targetPlayerId == byte.MaxValue) return "";
        if (seen.PlayerId == targetPlayerId)
            return ColorString(RoleInfo.RoleColor, "§");
        return "";
    }

    public string GetMarkOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (!TargetKnows || targetPlayerId == byte.MaxValue || !Player.IsAlive()) return "";
        if (seer.PlayerId == targetPlayerId && seen.PlayerId == targetPlayerId)
            return ColorString(RoleInfo.RoleColor, "§");
        return "";
    }

    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        var target = Target;
        if (target == null) return "";
        return $"<color={RoleInfo.RoleColorCode}>({target.Data?.PlayerName ?? "???"})</color>";
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";
        if (targetPlayerId == byte.MaxValue) return "";
        var target = Target;
        if (target == null) return "";
        string size = isForHud ? "" : "<size=60%>";
        return $"{size}<color={RoleInfo.RoleColorCode}>依頼人: {target.Data?.PlayerName ?? "???"}  の勝利と共に勝つ</color>";
    }

    // ─── RPC ─────────────────────────────────────────────
    void SendRpc()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write(targetPlayerId);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        targetPlayerId = reader.ReadByte();
    }
}

// ══════════════════════════════════════════════════════════════
// 追跡者 (弁護士の転職後ロール)
// 依頼人が死亡/追放後に弁護士から変化する。
// キルをガードし、生存していれば追加勝利。
// ══════════════════════════════════════════════════════════════
public sealed class Pursuer : RoleBase, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Pursuer),
            player => new Pursuer(player),
            CustomRoles.Pursuer,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            85100,
            SetupOptionItem,
            "pur",
            "#daa520",
            (4, 7),
            introSound: () => GetIntroSound(RoleTypes.Impostor),
            from: From.TownOfHost_Pko
        );

    public Pursuer(PlayerControl player) : base(RoleInfo, player, () => HasTask.False)
    {
        // Lawyer から転職した時点での静的設定を引き継ぐ
        guardCount = Lawyer.PursuerGuardNum;
        hasImpostorVision = Lawyer.HasImpostorVision;
    }

    static void SetupOptionItem()
    {
        // 弁護士オプション側に内包されるため個別設定なし
        if (Options.CustomRoleSpawnChances?.TryGetValue(CustomRoles.Pursuer, out var sp) == true)
            sp.SetHidden(true);
        if (Options.CustomRoleCounts?.TryGetValue(CustomRoles.Pursuer, out var cp) == true)
            cp.SetHidden(true);
    }

    int guardCount;
    bool hasImpostorVision;

    public override void ApplyGameOptions(IGameOptions opt) => opt.SetVision(hasImpostorVision);

    // ─── キルガード ──────────────────────────────────────
    public override bool OnCheckMurderAsTarget(MurderInfo info)
    {
        if (guardCount <= 0) return true;

        (var killer, var target) = info.AttemptTuple;
        if (killer.GetCustomRole() == CustomRoles.Tairou) return true; // 直接キルはガード不可

        info.DoKill = false;
        guardCount--;

        killer.RpcProtectedMurderPlayer(target);
        target.RpcProtectedMurderPlayer(target);
        killer.ResetKillCooldown();

        SendRpc();
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: target);
        return true;
    }

    // ─── 追加勝利: 生存していれば勝つ ────────────────────
    bool IAdditionalWinner.CheckWin(ref CustomRoles winnerRole) => Player.IsAlive();

    public override string GetProgressText(bool comms = false, bool GameLog = false)
        => ColorString(guardCount > 0 ? Color.yellow : Color.gray, $"〔{guardCount}〕");

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";
        string size = isForHud ? "" : "<size=60%>";
        return $"{size}<color={RoleInfo.RoleColorCode}>ガード残り {guardCount} 回  生存で追加勝利</color>";
    }

    // ─── RPC ─────────────────────────────────────────────
    void SendRpc()
    {
        using var sender = CreateSender();
        sender.Writer.Write(guardCount);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        guardCount = reader.ReadInt32();
    }
}