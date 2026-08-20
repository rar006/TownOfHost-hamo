using System;
using System.Collections.Generic;
using System.Linq;

using AmongUs.GameOptions;
using TownOfHost.Modules;
using TownOfHost.Patches;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using static TownOfHost.Translator;

namespace TownOfHost.Roles.Crewmate;

/// <summary>
/// 投票で追放される直前、かつ必要タスク数を完了している場合だけ
/// 通常追放を無効化して専用の逆転会議を行うクルー役職。
/// </summary>
public sealed class Reversal : RoleBase, IMeetingTimeAlterable
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Reversal),
            player => new Reversal(player),
            CustomRoles.Reversal,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            25300,
            SetupOptionItem,
            "rvs",
            "#ff69b4",
            (6, 11),
            from: From.TownOfHost_hamo
        );

    public Reversal(PlayerControl player) : base(RoleInfo, player)
    {
        CustomRoleManager.MarkOthers.Add(OtherMark);
    }

    public static OptionItem OptionRequiredTaskCount;
    public static OptionItem OptionMeetingSeconds;

    private enum OptionName
    {
        ReversalRequiredTaskCount,
        ReversalMeetingSeconds,
    }

    private static readonly HashSet<byte> PendingMeetingPlayerIds = new();
    private static readonly HashSet<byte> ActiveMeetingPlayerIds = new();
    private static readonly HashSet<byte> LockedSelectionPlayerIds = new();
    private static readonly Dictionary<byte, HashSet<byte>> SelectedTargetIds = new();

    private static void SetupOptionItem()
    {
        OptionRequiredTaskCount = IntegerOptionItem.Create(RoleInfo, 10, OptionName.ReversalRequiredTaskCount, new(0, 99, 1), 6, false);
        OptionMeetingSeconds = FloatOptionItem.Create(RoleInfo, 11, OptionName.ReversalMeetingSeconds, new(10f, 350f, 1f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    /// <summary>
    /// 逆転会議の発動に必要な完了タスク数を満たしているかを返す。
    /// 生存クルー人数は発動条件に含めない。
    /// </summary>
    public static bool MeetsCondition(PlayerControl player)
    {
        if (player == null) return false;
        var completed = player.GetPlayerTaskState()?.CompletedTasksCount ?? 0;
        return completed >= OptionRequiredTaskCount.GetInt();
    }

    public static int GetMeetingSeconds()
        => (int)OptionMeetingSeconds.GetFloat();

    public static bool IsInReversalMeeting(byte playerId)
        => ActiveMeetingPlayerIds.Contains(playerId);

    public static bool IsAnyReversalMeeting()
        => ActiveMeetingPlayerIds.Count > 0;

    public static bool IsSelectionLocked(byte playerId)
        => LockedSelectionPlayerIds.Contains(playerId);

    public static bool IsSelectedTarget(byte reversalPlayerId, byte targetPlayerId)
        => SelectedTargetIds.TryGetValue(reversalPlayerId, out var targets) && targets.Contains(targetPlayerId);

    public static void Init()
    {
        PendingMeetingPlayerIds.Clear();
        ActiveMeetingPlayerIds.Clear();
        LockedSelectionPlayerIds.Clear();
        SelectedTargetIds.Clear();
    }

    /// <summary>
    /// 実際に最多票で追放される対象だけを検出し、通常の追放を止める。
    /// 通常キル、同数票、ディクテーター等の強制追放では発動しない。
    /// </summary>
    public override bool VotingResults(ref NetworkedPlayerInfo Exiled, ref bool IsTie,
        Dictionary<byte, int> vote, byte[] mostVotedPlayers, bool ClearAndExile)
    {
        if (!AmongUsClient.Instance.AmHost) return false;

        // 専用会議では、通常投票の最多票による追放を発生させない。
        if (IsInReversalMeeting(Player.PlayerId))
        {
            Exiled = null;
            IsTie = false;
            return true;
        }

        // ClearAndExile はディクテーター等の強制追放。多数決による追放だけを対象にする。
        if (ClearAndExile || Exiled == null || IsTie) return false;
        if (Exiled.PlayerId != Player.PlayerId || !Player.IsAlive()) return false;
        if (PendingMeetingPlayerIds.Contains(Player.PlayerId)) return false;
        if (!MeetsCondition(Player)) return false;

        PendingMeetingPlayerIds.Add(Player.PlayerId);
        Exiled = null;
        IsTie = false;
        return true;
    }

    /// <summary>
    /// 対象選択中はリバーサル本人だけが候補をクリックでき、確定後は
    /// 天秤会議と同様に全員が指定済み候補にのみ投票できる。
    /// </summary>
    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)
    {
        if (!IsInReversalMeeting(Player.PlayerId)) return true;
        if (voter == null) return false;

        if (!IsSelectionLocked(Player.PlayerId))
            return voter.PlayerId == Player.PlayerId && votedForId < 15;

        return IsSelectedTarget(Player.PlayerId, votedForId);
    }

    /// <summary>
    /// 対象選択中のリバーサル本人のクリックを処理する。
    /// 同じ対象をもう一度選ぶと解除され、本人への投票で対象選択を確定する。
    /// </summary>
    public static void OnSelfVoteGuess(PlayerControl reversalPlayer, PlayerControl guessedTarget)
    {
        if (!AmongUsClient.Instance.AmHost || reversalPlayer == null) return;
        var reversalId = reversalPlayer.PlayerId;
        if (!IsInReversalMeeting(reversalId) || IsSelectionLocked(reversalId)) return;

        if (guessedTarget != null && guessedTarget.PlayerId == reversalId)
        {
            FinalizeTargetSelection(reversalPlayer);
            return;
        }

        if (guessedTarget == null || !guessedTarget.IsAlive()) return;

        var targets = GetOrCreateSelectedTargets(reversalId);
        if (!targets.Add(guessedTarget.PlayerId))
        {
            targets.Remove(guessedTarget.PlayerId);
            Utils.SendMessage(
                string.Format(GetString("ReversalTargetRemoved"), UtilsName.GetPlayerColor(guessedTarget, true), targets.Count),
                reversalId,
                GetString("ReversalMeeting"));
            return;
        }

        Utils.SendMessage(
            string.Format(GetString("ReversalTargetSelected"), UtilsName.GetPlayerColor(guessedTarget, true), targets.Count),
            reversalId,
            GetString("ReversalMeeting"));
    }

    /// <summary>
    /// 選択対象を固定する。対象が一人もいない場合は固定せず、選択を継続する。
    /// </summary>
    public static void FinalizeTargetSelection(PlayerControl reversalPlayer)
    {
        if (!AmongUsClient.Instance.AmHost || reversalPlayer == null) return;
        var reversalId = reversalPlayer.PlayerId;
        if (!IsInReversalMeeting(reversalId) || IsSelectionLocked(reversalId)) return;

        var targets = GetOrCreateSelectedTargets(reversalId);
        targets.RemoveWhere(id =>
        {
            var target = PlayerCatch.GetPlayerById(id);
            return target == null || !target.IsAlive();
        });

        if (targets.Count == 0)
        {
            Utils.SendMessage(GetString("ReversalTargetRequired"), reversalId, GetString("ReversalMeeting"));
            return;
        }

        LockedSelectionPlayerIds.Add(reversalId);
        Utils.SendMessage(
            string.Format(GetString("ReversalTargetLocked"), targets.Count),
            reversalId,
            GetString("ReversalMeeting"));
        UtilsNotifyRoles.NotifyRoles(ForceLoop: true, NoCache: true);
    }

    // 旧版のFixedUpdate呼び出しとの互換用。発動判定は VotingResults に一本化する。
    public static void CheckAndTrigger(PlayerControl player)
    {
    }

    // 旧版のFixedUpdate呼び出しとの互換用。会議時間は MeetingTimeManager で制御する。
    public static void Tick(float deltaTime)
    {
    }

    bool IMeetingTimeAlterable.RevertOnDie => false;

    int IMeetingTimeAlterable.CalculateMeetingTimeDelta()
    {
        if (!IsInReversalMeeting(Player.PlayerId)) return 0;
        return GetMeetingSeconds() - (int)Main.NormalOptions.DiscussionTime - (int)Main.NormalOptions.VotingTime;
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        // 専用会議が終了した時点で、指定対象の正誤を一括判定する。
        if (IsInReversalMeeting(Player.PlayerId))
        {
            ResolveReversalMeeting();
            return;
        }

        // 直前の通常会議で多数決追放を無効化した場合だけ、専用会議を開く。
        if (!PendingMeetingPlayerIds.Remove(Player.PlayerId)) return;
        if (!Player.IsAlive()) return;

        ActiveMeetingPlayerIds.Add(Player.PlayerId);
        LockedSelectionPlayerIds.Remove(Player.PlayerId);
        SelectedTargetIds[Player.PlayerId] = new();

        _ = new LateTask(() =>
        {
            if (!IsInReversalMeeting(Player.PlayerId) || !Player.IsAlive()) return;

            _ = new LateTask(() => Utils.AllPlayerKillFlash(), 1f, "Reversal.KillFlash", true);
            ReportDeadBodyPatch.ExReportDeadBody(Player, null, false, "ReversalMeeting", RoleInfo.RoleColorCode);
            Utils.SendMessage(GetString("ReversalMeetingAnnounce"), title: GetString("ReversalMeeting"));
            Utils.SendMessage(GetString("ReversalTargetGuide"), Player.PlayerId, GetString("ReversalMeeting"));
        }, 2f, "Reversal.Meeting", true);
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!isForMeeting || seer == null || seen == null) return "";
        if (seer.PlayerId != Player.PlayerId || seen.PlayerId != Player.PlayerId) return "";
        if (!IsInReversalMeeting(Player.PlayerId)) return "";

        var key = IsSelectionLocked(Player.PlayerId)
            ? "ReversalMeetingLockedLowerText"
            : "ReversalMeetingSelectLowerText";
        var message = $"<color={RoleInfo.RoleColorCode}>{GetString(key)}</color>";
        return isForHud ? message : $"<size=55%>{message}</size>";
    }

    public static string OtherMark(PlayerControl seer, PlayerControl seen, bool isForMeeting = false)
    {
        if (!isForMeeting || seen == null) return "";
        if (ActiveMeetingPlayerIds.Any(id => IsSelectionLocked(id) && IsSelectedTarget(id, seen.PlayerId)))
            return $"<color={RoleInfo.RoleColorCode}>Ω</color>";
        return "";
    }

    private static HashSet<byte> GetOrCreateSelectedTargets(byte reversalPlayerId)
    {
        if (!SelectedTargetIds.TryGetValue(reversalPlayerId, out var targets))
        {
            targets = new();
            SelectedTargetIds[reversalPlayerId] = targets;
        }
        return targets;
    }

    private void ResolveReversalMeeting()
    {
        var reversalId = Player.PlayerId;
        var targets = GetOrCreateSelectedTargets(reversalId)
            .Where(id => PlayerCatch.GetPlayerById(id)?.IsAlive() == true)
            .ToArray();

        var allTargetsAreImpostors = IsSelectionLocked(reversalId)
            && targets.Length > 0
            && targets.All(id => PlayerCatch.GetPlayerById(id).GetCustomRole().IsImpostor());

        if (allTargetsAreImpostors)
        {
            foreach (var targetId in targets)
                PlayerCatch.GetPlayerById(targetId)?.SetRealKiller(Player);

            MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.Vote, targets);
            if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Crewmate, reversalId))
                CustomWinnerHolder.WinnerIds.Add(reversalId);
        }
        else
        {
            Player.SetRealKiller(Player);
            MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.Suicide, reversalId);
        }

        ActiveMeetingPlayerIds.Remove(reversalId);
        LockedSelectionPlayerIds.Remove(reversalId);
        SelectedTargetIds.Remove(reversalId);

        _ = new LateTask(
            () => UtilsNotifyRoles.NotifyRoles(ForceLoop: true, NoCache: true),
            Main.LagTime,
            "Reversal.NotifyAfter");
    }
}
