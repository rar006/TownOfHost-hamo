using System.Collections.Generic;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

/// <summary>
/// パリティ(第三陣営)
/// その会議で自分に入った合計票数で能力が変わる(0票は偶数)。
/// ・奇数: キルできるが勝利できない
/// ・偶数: キルできないが、生存して試合が終わると単独勝利(設定で追加勝利)
/// 設定で自投票を数えるか、リアクターを直せるかを選べる。
/// </summary>
public sealed class Parity : RoleBase, IKiller, ISystemTypeUpdateHook
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Parity),
            player => new Parity(player),
            CustomRoles.Parity,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Neutral,
            93600,
            SetupOptionItem,
            "par",
            "#9B59B6",
            isDesyncImpostor: true,
            countType: CountTypes.Crew,
            isNewRole: true
        );

    static OptionItem OptionKillCooldown;
    static OptionItem OptionCountSelfVote;
    static OptionItem OptionAdditionalWin;
    static OptionItem OptionCanFixReactor;
    enum OptionName { ParityCountSelfVote, ParityAdditionalWin, ParityCanFixReactor }

    static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, 9, defo: 10);
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 1f), 35f, false).SetValueFormat(OptionFormat.Seconds);
        OptionCountSelfVote = BooleanOptionItem.Create(RoleInfo, 11, OptionName.ParityCountSelfVote, true, false);
        OptionAdditionalWin = BooleanOptionItem.Create(RoleInfo, 12, OptionName.ParityAdditionalWin, false, false);
        OptionCanFixReactor = BooleanOptionItem.Create(RoleInfo, 13, OptionName.ParityCanFixReactor, false, false);
    }

    public Parity(PlayerControl player) : base(RoleInfo, player, () => HasTask.False) { }

    /// <summary>最後の会議で入った票数(0は偶数)</summary>
    int votes;
    public bool IsOdd => votes % 2 == 1;

    public float CalculateKillCooldown() => OptionKillCooldown.GetFloat();
    public bool CanUseKillButton() => Player.IsAlive() && IsOdd;
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;

    public bool UpdateReactorSystem(ReactorSystemType reactorSystem, byte amount) => OptionCanFixReactor.GetBool() || !Player.IsAlive();

    /// <summary>投票の集計時に、自分に入った票数を覚える(何度か呼ばれても同じ結果になる)</summary>
    public override bool VotingResults(ref NetworkedPlayerInfo Exiled, ref bool IsTie, Dictionary<byte, int> vote, byte[] mostVotedPlayers, bool ClearAndExile)
    {
        var n = vote != null && vote.TryGetValue(Player.PlayerId, out var c) ? c : 0;
        if (!OptionCountSelfVote.GetBool()
            && MeetingVoteManager.Instance?.AllVotes != null
            && MeetingVoteManager.Instance.AllVotes.TryGetValue(Player.PlayerId, out var mine)
            && mine.VotedFor == Player.PlayerId)
            n -= mine.NumVotes;
        votes = System.Math.Max(0, n);
        SendRPC();
        return false;
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        Player.ResetKillCooldown();
        Player.MarkDirtySettings();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    public override string GetProgressText(bool comms = false, bool gamelog = false)
        => Utils.ColorString(IsOdd ? Palette.ImpostorRed : RoleInfo.RoleColor, $"({GetString(IsOdd ? "ParityOdd" : "ParityEven")}{votes})");

    /// <summary>勝利判定: 生存していて偶数なら単独勝利(設定で追加勝利)</summary>
    public static void CheckWin()
    {
        foreach (var pc in PlayerCatch.AllPlayerControls)
        {
            if (pc.GetRoleClass() is not Parity p || !pc.IsAlive() || p.IsOdd) continue;
            if (OptionAdditionalWin.GetBool())
            {
                if (!CustomWinnerHolder.WinnerIds.Contains(pc.PlayerId))
                {
                    CustomWinnerHolder.WinnerIds.Add(pc.PlayerId);
                    CustomWinnerHolder.AdditionalWinnerRoles.Add(CustomRoles.Parity);
                }
                continue;
            }
            if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Parity, pc.PlayerId))
                CustomWinnerHolder.NeutralWinnerIds.Add(pc.PlayerId);
        }
    }

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(votes);
    }
    public override void ReceiveRPC(MessageReader reader) => votes = reader.ReadInt32();
}
