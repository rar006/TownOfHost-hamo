using AmongUs.GameOptions;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Madmate;

/// <summary>
/// マッドインビジブル: 存在が希薄なマッドメイト。
/// ベントが使える。会議で投票しても票が入らない。生存人数にカウントされず、(設定で)キルの対象にもならない。
/// 追放されれば死亡する。設定でゲッサーにも撃たれない。
/// </summary>
public sealed class MadInvisible : RoleBase, IKillFlashSeeable, IDeathReasonSeeable
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MadInvisible),
            player => new MadInvisible(player),
            CustomRoles.MadInvisible,
            () => OptionCanVent.GetBool() ? RoleTypes.Engineer : RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            92900,
            SetupOptionItem,
            "minv",
            "#FFFFFF",
            countType: CountTypes.None, // 生存人数にカウントされない
            introSound: () => GetIntroSound(RoleTypes.Impostor),
            isNewRole: true
        );

    static OptionItem OptionCantBeKilled;
    static OptionItem OptionCanVent;
    static OptionItem OptionCantBeGuessed;
    enum OptionName { MadInvisibleCantBeKilled, MadInvisibleCantBeGuessed }

    static void SetupOptionItem()
    {
        OptionCantBeKilled = BooleanOptionItem.Create(RoleInfo, 10, OptionName.MadInvisibleCantBeKilled, true, false);
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, 11, GeneralOption.CanVent, true, false);
        OptionCantBeGuessed = BooleanOptionItem.Create(RoleInfo, 12, OptionName.MadInvisibleCantBeGuessed, true, false);
    }

    public MadInvisible(PlayerControl player) : base(RoleInfo, player, () => HasTask.ForRecompute)
    {
        canSeeKillFlash = Options.MadmateCanSeeKillFlash.GetBool();
        canSeeDeathReason = Options.MadmateCanSeeDeathReason.GetBool();
    }
    static bool canSeeKillFlash;
    static bool canSeeDeathReason;
    public bool? CheckKillFlash(MurderInfo info) => canSeeKillFlash;
    public bool? CheckSeeDeathReason(PlayerControl seen) => canSeeDeathReason;

    /// <summary>投票しても票が入らない</summary>
    public override (byte? votedForId, int? numVotes, bool doVote) ModifyVote(byte voterId, byte sourceVotedForId, bool isIntentional)
    {
        if (voterId != Player.PlayerId) return (null, null, true);
        return (null, null, false);
    }

    /// <summary>キルの対象にならない</summary>
    public override bool OnCheckMurderAsTarget(MurderInfo info) => !OptionCantBeKilled.GetBool() || info.IsSuicide;

    /// <summary>ゲッサーに撃たれない(null で無効)</summary>
    public override bool? CheckGuess(PlayerControl killer) => OptionCantBeGuessed.GetBool() ? null : true;
}
