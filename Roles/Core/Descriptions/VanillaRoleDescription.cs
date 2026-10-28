using AmongUs.GameOptions;
using TownOfHost.Roles.Vanilla;

namespace TownOfHost.Roles.Core.Descriptions;

/// <summary>
/// バニラ役職の説明文
/// </summary>
public class VanillaRoleDescription : RoleDescription
{
    public VanillaRoleDescription(SimpleRoleInfo roleInfo, RoleTypes vanillaRoleType) : base(roleInfo)
    {
        this.vanillaRoleType = vanillaRoleType;
    }
    private readonly RoleTypes vanillaRoleType;
    string PhantomDesc()
    {
        var nakama = Phantom.CanSeeNakama.GetBool() ? "\n" + Translator.GetString("PhantomDescNakama") : "";
        var kill = !Phantom.CanKillInvis.GetBool() ? "\n" + Translator.GetString("PhantomCantKill") : "";

        return nakama + kill;
    }
    public override string Blurb => DestroyableSingleton<RoleManager>.Instance.GetRole(vanillaRoleType).Blurb;
    public override string Description => vanillaRoleType is not RoleTypes.Impostor and not RoleTypes.Phantom ?
        DestroyableSingleton<RoleManager>.Instance.GetRole(vanillaRoleType).BlurbMed :
        vanillaRoleType is RoleTypes.Phantom ?
        DestroyableSingleton<RoleManager>.Instance.GetRole(vanillaRoleType).BlurbMed
        + PhantomDesc()
        :
        vanillaRoleType is RoleTypes.Impostor ?
        DestroyableSingleton<RoleManager>.Instance.GetRole(vanillaRoleType).Blurb :
        DestroyableSingleton<RoleManager>.Instance.GetRole(vanillaRoleType).BlurbLong
        ;
}
