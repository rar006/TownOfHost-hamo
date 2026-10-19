using AmongUs.GameOptions;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Madmate;

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

    public override (byte? votedForId, int? numVotes, bool doVote) ModifyVote(byte voterId, byte sourceVotedForId, bool isIntentional)
    {
        if (voterId != Player.PlayerId) return (null, null, true);
        return (null, null, false);
    }

    public override bool OnCheckMurderAsTarget(MurderInfo info) => !OptionCantBeKilled.GetBool() || info.IsSuicide;

    public override bool? CheckGuess(PlayerControl killer) => OptionCantBeGuessed.GetBool() ? null : true;
}
