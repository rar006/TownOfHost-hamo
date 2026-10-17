using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Roles.Crewmate;

namespace TownOfHost.Roles.Madmate;

/// <summary>
/// マッドセンダー(マッドメイト)
/// 会議で自投票 → 相手に投票すると、次に打ったチャットが相手にだけ表示される(誰から届いたかは分からない)。
/// 設定でインポスターが分かる。
/// </summary>
public sealed class MadSender : RoleBase, IKillFlashSeeable, IDeathReasonSeeable, ISelfVoter
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MadSender),
            player => new MadSender(player),
            CustomRoles.MadSender,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            93550,
            SetupOptionItem,
            "msd",
            "#ff1919",
            introSound: () => GetIntroSound(RoleTypes.Impostor),
            isNewRole: true
        );

    static OptionItem OptionUses;
    static OptionItem OptionCanSeeImpostor;
    enum OptionName { SenderUses, MadSenderCanSeeImpostor }

    static void SetupOptionItem()
    {
        OptionUses = IntegerOptionItem.Create(RoleInfo, 10, OptionName.SenderUses, new(0, 999, 1), 5, false).SetValueFormat(OptionFormat.Times);
        OptionCanSeeImpostor = BooleanOptionItem.Create(RoleInfo, 11, OptionName.MadSenderCanSeeImpostor, false, false);
    }

    public MadSender(PlayerControl player) : base(RoleInfo, player, () => HasTask.ForRecompute)
    {
        state = new();
        canSeeKillFlash = Options.MadmateCanSeeKillFlash.GetBool();
        canSeeDeathReason = Options.MadmateCanSeeDeathReason.GetBool();
    }
    static bool canSeeKillFlash;
    static bool canSeeDeathReason;
    public bool? CheckKillFlash(MurderInfo info) => canSeeKillFlash;
    public bool? CheckSeeDeathReason(PlayerControl seen) => canSeeDeathReason;

    readonly SenderState state;

    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)
        => state.CheckVote(Player, votedForId, voter, OptionUses.GetInt());

    public bool TryIntercept(string text) => state.TryIntercept(Player, text, OptionUses.GetInt());

    public override void OnStartMeeting() => state.Reset();

    /// <summary>設定でインポスターが分かる</summary>
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (!OptionCanSeeImpostor.GetBool() || seer.PlayerId != Player.PlayerId || seen.PlayerId == seer.PlayerId) return "";
        var role = seen.GetCustomRole();
        return role.IsImpostor() || role is CustomRoles.Egoist ? "<#ff1919>★</color>" : "";
    }

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
