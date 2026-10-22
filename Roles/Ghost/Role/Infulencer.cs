using System.Collections.Generic;
using AmongUs.GameOptions;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Ghost;
using static TownOfHost.Options;

namespace TownOfHost.Roles.Vanilla;

// TownOfHost-K の SpiritGuide(インフルエンサー)をそのまま移植。
// 死亡したクルー陣営(設定でマッドメイトも)に、守護天使と同じ幽霊役職の仕組みで付与する。
// バニラの自動配布(SpiritGuide の出現率)は使わない。
public static class Influencer
{
    static GhostRoleAssingData Data;
    private static readonly int Id = 25150;
    public static List<byte> playerIdList = new();
    public static OptionItem CoolDown;
    static OptionItem AssingMadmate;

    public static void SetupCustomOption()
    {
        SetupRoleOptions(Id, TabGroup.GhostRoles, CustomRoles.Influencer, fromtext: UtilsOption.GetFrom(From.AmongUs));
        Data = GhostRoleAssingData.Create(Id + 1, CustomRoles.Influencer, CustomRoleTypes.Crewmate, CustomRoleTypes.Madmate);
        CoolDown = FloatOptionItem.Create(Id + 2, "Cooldown", new(0f, 180f, 0.5f), 27.5f, TabGroup.GhostRoles, false)
            .SetValueFormat(OptionFormat.Seconds).SetParent(CustomRoleSpawnChances[CustomRoles.Influencer]).SetParentRole(CustomRoles.Influencer);
        AssingMadmate = BooleanOptionItem.Create(Id + 4, "AssgingMadmate", false, TabGroup.GhostRoles, false)
            .SetParent(CustomRoleSpawnChances[CustomRoles.Influencer]).SetParentRole(CustomRoles.Influencer);
    }

    public static void Init()
    {
        playerIdList = new();
        Data.SubRoleType = AssingMadmate.GetBool() ? CustomRoleTypes.Madmate : CustomRoleTypes.Crewmate;
    }

    public static void Add(byte playerId)
    {
        playerIdList.Add(playerId);
    }

    // バニラの SpiritGuide がそのまま配られた人(以前の仕組みの名残)を、インフルエンサーとして表示するか
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

    // バニラの SpiritGuide の出現率は0にしておく(配るのはMODの幽霊役職の仕組み)
    public static void ApplyOptions()
    {
        var options = Main.NormalOptions;
        if (options?.roleOptions == null) return;
        options.roleOptions.SetRoleRate(RoleTypes.SpiritGuide, 0, 0);
    }
}
