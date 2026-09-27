using System.Linq;
using AmongUs.GameOptions;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

public sealed class Survivor : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Survivor),
            player => new Survivor(player),
            CustomRoles.Survivor,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            77300,
            SetupOptionItem,
            "Sur",
            OptionSort: (7, 7),
            from: From.SuperNewRoles,
            isNewRole: true
        );
    public Survivor(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
    }
    static OptionItem OptionKillCoolDown;
    private static void SetupOptionItem()
    {
        OptionKillCoolDown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, OptionBaseCoolTime, 20f, false)
                .SetValueFormat(OptionFormat.Seconds);
    }
    public float CalculateKillCooldown() => OptionKillCoolDown.GetFloat();
    public override void CheckWinner(GameOverReason reason)
    {
        if (!Player.IsAlive())
        {
            CustomWinnerHolder.CantWinPlayerIds.Add(Player.PlayerId);
            CustomWinnerHolder.WinnerIds.Remove(Player.PlayerId);
        }
    }
}