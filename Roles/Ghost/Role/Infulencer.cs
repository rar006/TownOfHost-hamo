using AmongUs.GameOptions;
using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Vanilla;

public sealed class Influencer : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.CreateForVanilla(
            typeof(Influencer),
            player => new Influencer(player),
            RoleTypes.SpiritGuide,
            SetUpCustomOption,
            "#8cffff",
            assignInfo: new RoleAssignInfo(CustomRoles.Influencer, CustomRoleTypes.Crewmate)
            {
                IsInitiallyAssignableCallBack = () => false
            },
            from: From.AmongUs,
            tab: TabGroup.GhostRoles
        );

    private static OptionItem messageCooldown;

    public Influencer(PlayerControl player) : base(RoleInfo, player) { }

    public static bool IsDisplayedAsInfluencer(PlayerControl player)
    {
        if (player == null) return false;
        var state = PlayerState.GetByPlayerId(player.PlayerId);
        if (state == null) return false;
        if (player.Data?.Role?.Role == RoleTypes.SpiritGuide)
            state.WasInfluencer = true;
        return state.WasInfluencer && state.GhostRole == CustomRoles.NotAssigned &&
            (state.IsDead || player.Data?.IsDead == true ||
            player.Data?.Role?.Role == RoleTypes.SpiritGuide);
    }

    private static void SetUpCustomOption()
    {
        messageCooldown = FloatOptionItem.Create(RoleInfo, 2, "InfluencerMessageCooldown", new(0f, 180f, 5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        Options.CustomRoleSpawnChances[CustomRoles.Influencer].RegisterUpdateValueEvent((_, _) => ApplyOptions());
        Options.CustomRoleCounts[CustomRoles.Influencer].RegisterUpdateValueEvent((_, _) => ApplyOptions());
        messageCooldown.RegisterUpdateValueEvent((_, _) => ApplyOptions());
    }

    public static void ApplyOptions()
    {
        if (GameOptionsManager.Instance == null || messageCooldown == null ||
            Options.CustomRoleSpawnChances == null || Options.CustomRoleCounts == null) return;

        var options = Main.NormalOptions;
        if (options?.roleOptions == null) return;
        var chance = Options.GetRoleChance(CustomRoles.Influencer);
        options.roleOptions.SetRoleRate(RoleTypes.SpiritGuide,
            chance > 0 ? Options.CustomRoleCounts[CustomRoles.Influencer].GetInt() : 0, chance);
        if (options.roleOptions.TryGetRoleOptions(RoleTypes.SpiritGuide, out SpiritGuideRoleOptionsV12 roleOptions))
            roleOptions.SpiritGuideCooldownSeconds = messageCooldown.GetFloat();
    }
}
