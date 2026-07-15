using AmongUs.GameOptions;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

public sealed class Minimalist : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Minimalist),
            player => new Minimalist(player),
            CustomRoles.Minimalist,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            5600,
            SetupOptionItem,
            "mml",
            OptionSort: (2, 11),
            from: From.SuperNewRoles
        );

    public Minimalist(PlayerControl player) : base(RoleInfo, player)
    {
        KillCooldown = OptionKillCooldown.GetFloat();
<<<<<<< HEAD
    }

    static OptionItem OptionKillCooldown;
    static float KillCooldown;
=======
        CanVent = OptionCanVent.GetBool();
        CanSabotage = OptionCanSabotage.GetBool();
    }

    static OptionItem OptionKillCooldown;
    static OptionItem OptionCanVent;
    static OptionItem OptionCanSabotage;

    static float KillCooldown;
    static bool CanVent;
    static bool CanSabotage;

    enum OptionName
    {
        MinimalistCanVent,
        MinimalistCanSabotage,
    }
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56

    static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown,
            new(0f, 60f, 0.5f), 10f, false).SetValueFormat(OptionFormat.Seconds);
<<<<<<< HEAD
    }

    public float CalculateKillCooldown() => KillCooldown;
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;

    public override bool CanClickUseVentButton => false;
    public override bool OnEnterVent(PlayerPhysics physics, int ventId) => false;
=======
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, 11, OptionName.MinimalistCanVent, false, false);
        OptionCanSabotage = BooleanOptionItem.Create(RoleInfo, 12, OptionName.MinimalistCanSabotage, false, false);
    }

    public float CalculateKillCooldown() => KillCooldown;
    public bool CanUseSabotageButton() => CanSabotage;
    public bool CanUseImpostorVentButton() => CanVent;

    public override bool CanClickUseVentButton => CanVent;
    public override bool OnEnterVent(PlayerPhysics physics, int ventId) => CanVent;
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
}