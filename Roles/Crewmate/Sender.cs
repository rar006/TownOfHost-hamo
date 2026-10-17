using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using static TownOfHost.Modules.SelfVoteManager;

namespace TownOfHost.Roles.Crewmate;

/// <summary>
/// センダー(クルーメイト)
/// 会議で自投票 → 相手に投票すると、次に打ったチャットが相手にだけ表示される(誰から届いたかは分からない)。
/// </summary>
public sealed class Sender : RoleBase, ISelfVoter
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Sender),
            player => new Sender(player),
            CustomRoles.Sender,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            93500,
            SetupOptionItem,
            "snd",
            "#7FB3D5",
            isNewRole: true
        );

    static OptionItem OptionUses;
    enum OptionName { SenderUses }

    static void SetupOptionItem()
    {
        OptionUses = IntegerOptionItem.Create(RoleInfo, 10, OptionName.SenderUses, new(0, 999, 1), 5, false).SetValueFormat(OptionFormat.Times);
    }

    public Sender(PlayerControl player) : base(RoleInfo, player) { state = new(); }

    readonly SenderState state;

    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)
        => state.CheckVote(Player, votedForId, voter, OptionUses.GetInt());

    /// <summary>チャットを横取りして相手にだけ送る(ホスト)。送ったら true</summary>
    public bool TryIntercept(string text) => state.TryIntercept(Player, text, OptionUses.GetInt());

    public override void OnStartMeeting() => state.Reset();

    public override string GetProgressText(bool comms = false, bool gamelog = false)
        => Utils.ColorString(state.Left(OptionUses.GetInt()) > 0 ? RoleInfo.RoleColor : ModColors.NeutralGray, $"({state.Left(OptionUses.GetInt())})");

    public override void ReceiveRPC(MessageReader reader) { state.Used = reader.ReadInt32(); state.HasPending = reader.ReadBoolean(); }
    public void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(state.Used);
        sender.Writer.Write(state.HasPending);
    }
    public bool HasPending => state.HasPending;
    public void ClearPendingLocal() => state.HasPending = false;
}

/// <summary>センダーとマッドセンダーの共通処理</summary>
public sealed class SenderState
{
    public int Used;
    byte pendingTarget = byte.MaxValue;

    /// <summary>次のチャットを相手に送る待ち状態か(本人のMODにも同期する)</summary>
    public bool HasPending;

    public int Left(int max) => System.Math.Max(0, max - Used);
    public void Reset() { pendingTarget = byte.MaxValue; HasPending = false; }

    static void Sync(PlayerControl me)
    {
        (me.GetRoleClass() as Sender)?.SendRPC();
        (me.GetRoleClass() as Madmate.MadSender)?.SendRPC();
    }

    public bool CheckVote(PlayerControl me, byte votedForId, PlayerControl voter, int max)
    {
        if (!Canuseability() || voter.PlayerId != me.PlayerId || !me.IsAlive() || Left(max) <= 0) return true;
        if (!CheckSelfVoteMode(me, votedForId, out var status)) return true;
        if (status is VoteStatus.Self) Utils.SendMessage(GetString("SenderSelectMode"), me.PlayerId);
        if (status is VoteStatus.Skip) { pendingTarget = byte.MaxValue; HasPending = false; Sync(me); Utils.SendMessage(GetString("VoteSkillFin"), me.PlayerId); }
        if (status is VoteStatus.Vote)
        {
            var t = PlayerCatch.GetPlayerById(votedForId);
            if (t == null || !t.IsAlive() || t.PlayerId == me.PlayerId) Utils.SendMessage(GetString("SenderCantTarget"), me.PlayerId);
            else
            {
                pendingTarget = votedForId;
                HasPending = true;
                Sync(me);
                Utils.SendMessage(string.Format(GetString("SenderReady"), t.Data?.PlayerName), me.PlayerId);
            }
        }
        SetMode(me, status is VoteStatus.Self);
        return false;
    }

    public bool TryIntercept(PlayerControl me, string text, int max)
    {
        if (pendingTarget == byte.MaxValue || !GameStates.IsMeeting || !me.IsAlive() || string.IsNullOrWhiteSpace(text)) return false;
        if (text.TrimStart().StartsWith("/")) return false; // コマンドはそのまま
        var target = PlayerCatch.GetPlayerById(pendingTarget);
        pendingTarget = byte.MaxValue;
        HasPending = false;
        if (target == null || !target.IsAlive() || Left(max) <= 0) { Sync(me); return false; }
        Used++;
        Sync(me);
        Utils.SendMessage(text, target.PlayerId, Utils.ColorString(UnityEngine.Color.cyan, GetString("SenderFrom")));
        Utils.SendMessage(string.Format(GetString("SenderSent"), target.Data?.PlayerName, Left(max)), me.PlayerId);
        Logger.Info($"センダー: {me.Data?.GetLogPlayerName()} → {target.Data?.GetLogPlayerName()} に秘匿を送信", "Sender");
        return true;
    }

    static string GetString(string key) => Translator.GetString(key);
}
