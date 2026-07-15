using System.Linq;
<<<<<<< HEAD
=======
using System.Collections.Generic;
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

public sealed class LoversBreaker : RoleBase, ILNKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(LoversBreaker),
            player => new LoversBreaker(player),
            CustomRoles.LoversBreaker,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Neutral,
            52700,
            SetupOptionItem,
            "lb",
            "#ff66cc",
            (5, 4),
            true,
            from: From.SuperNewRoles
        );

    static OptionItem OptionKillCooldown;
    static OptionItem OptionRequiredLoversKills;
    static OptionItem OptionCanWinAtDeath;
<<<<<<< HEAD
=======
    static OptionItem OptionOnlyAssignWithLoversRole;
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56

    static float KillCooldown => OptionKillCooldown?.GetFloat() ?? 25f;
    static int RequiredLoversKills => OptionRequiredLoversKills?.GetInt() ?? 1;
    static bool CanWinAtDeath => OptionCanWinAtDeath?.GetBool() ?? false;
<<<<<<< HEAD
=======
    public static bool IsTanabataEventActive => Event.Tanabata;
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56

    enum OptionName
    {
        LoversBreakerRequiredLoversKills,
<<<<<<< HEAD
        LoversBreakerCanWinAtDeath
=======
        LoversBreakerCanWinAtDeath,
        LoversBreakerOnlyAssignWithLoversRole
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    }

    int loversKillCount;
    bool hadTargetLovers;

    public LoversBreaker(PlayerControl player)
        : base(RoleInfo, player)
    {
        loversKillCount = 0;
        hadTargetLovers = false;
    }

    static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, 10, defo: 1);
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 11, GeneralOption.KillCooldown, new(2.5f, 60f, 2.5f), 25f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionRequiredLoversKills = IntegerOptionItem.Create(RoleInfo, 12, OptionName.LoversBreakerRequiredLoversKills, new(1, 10, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
        OptionCanWinAtDeath = BooleanOptionItem.Create(RoleInfo, 13, OptionName.LoversBreakerCanWinAtDeath, false, false);
<<<<<<< HEAD
=======
        OptionOnlyAssignWithLoversRole = BooleanOptionItem.Create(RoleInfo, 14, OptionName.LoversBreakerOnlyAssignWithLoversRole, true, false);
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    }

    public override void Add()
    {
        RefreshTargetLoversState();
        SendRPC();
    }

    public float CalculateKillCooldown() => KillCooldown;
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (!Is(info.AttemptKiller) || info.IsSuicide) return;

        RefreshTargetLoversState();
        var target = info.AttemptTarget;

<<<<<<< HEAD
        if (IsBreakableLover(target)) return;
=======
        if (CanKillTarget(target)) return;
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56

        info.DoKill = false;
        PlayerState.GetByPlayerId(Player.PlayerId).DeathReason = CustomDeathReason.Misfire;
        info.AttemptKiller.RpcMurderPlayer(info.AttemptKiller);
    }

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (!Is(info.AttemptKiller) || info.IsSuicide) return;
<<<<<<< HEAD
        if (!IsBreakableLover(info.AttemptTarget)) return;
=======
        if (!CountsAsLoversKill(info.AttemptTarget)) return;
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56

        loversKillCount++;
        hadTargetLovers = true;
        SendRPC();
    }

    public override void CheckWinner(GameOverReason reason)
<<<<<<< HEAD
    {
        RefreshTargetLoversState();
        if (!CanWinAtDeath && !Player.IsAlive()) return;
        if (!CanSoloWinNow()) return;
=======
        => TryWinNow();

    public bool TryWinNow()
    {
        RefreshTargetLoversState();
        if (!CanWinAtDeath && !Player.IsAlive()) return false;
        if (!CanSoloWinNow()) return false;
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56

        if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.LoversBreaker, Player.PlayerId))
        {
            CustomWinnerHolder.NeutralWinnerIds.Add(Player.PlayerId);
<<<<<<< HEAD
        }
=======
            CustomWinnerHolder.WinnerIds.Add(Player.PlayerId);
            return true;
        }

        return false;
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    }

    public override string GetProgressText(bool comms = false, bool gamelog = false)
        => Utils.ColorString(RoleInfo.RoleColor, $"({loversKillCount}/{RequiredLoversKills})");

    bool CanSoloWinNow()
    {
        if (loversKillCount < RequiredLoversKills) return false;
        if (!hadTargetLovers) return false;
<<<<<<< HEAD
        return !PlayerCatch.AllAlivePlayerControls.Any(IsBreakableLover);
=======
        return !PlayerCatch.AllAlivePlayerControls.Any(IsVictoryTarget);
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    }

    void RefreshTargetLoversState()
    {
        if (hadTargetLovers) return;
<<<<<<< HEAD
        hadTargetLovers = PlayerCatch.AllPlayerControls.Any(IsBreakableLover);
=======
        hadTargetLovers = PlayerCatch.AllPlayerControls.Any(IsVictoryTarget);
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    }

    static bool IsBreakableLover(PlayerControl target)
        => target != null && target.IsLovers(checkonelover: false);

<<<<<<< HEAD
=======
    static bool IsTanabataCouple(PlayerControl target)
        => IsTanabataEventActive
            && target?.GetCustomRole() is CustomRoles.Vega or CustomRoles.Altair;

    static bool IsVictoryTarget(PlayerControl target)
        => IsBreakableLover(target) || IsTanabataCouple(target);

    static bool CanKillTarget(PlayerControl target)
        => IsVictoryTarget(target)
            || target?.Is(CustomRoles.Madonna) == true
            || target?.Is(CustomRoles.Cupid) == true;

    static bool CountsAsLoversKill(PlayerControl target)
    {
        if (target == null) return false;
        if (target.Is(CustomRoles.Cupid)) return false;
        if (target.Is(CustomRoles.Madonna)) return target.Is(CustomRoles.MadonnaLovers);
        return IsVictoryTarget(target);
    }

    public static bool ShouldRemoveFromAssignment(IEnumerable<CustomRoles> assignedRoles)
    {
        if (OptionOnlyAssignWithLoversRole?.GetBool() != true) return false;

        return !assignedRoles.Any(role =>
            role is CustomRoles.Lovers
                or CustomRoles.RedLovers
                or CustomRoles.YellowLovers
                or CustomRoles.BlueLovers
                or CustomRoles.GreenLovers
                or CustomRoles.WhiteLovers
                or CustomRoles.PurpleLovers
                or CustomRoles.Madonna
                or CustomRoles.Cupid
            || (IsTanabataEventActive && (role is CustomRoles.Vega or CustomRoles.Altair)));
    }

>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    void SendRPC()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        using var sender = CreateSender();
        sender.Writer.WritePacked((int)RPCType.SyncState);
        sender.Writer.Write(loversKillCount);
        sender.Writer.Write(hadTargetLovers);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        switch ((RPCType)reader.ReadPackedInt32())
        {
            case RPCType.SyncState:
                loversKillCount = reader.ReadInt32();
                hadTargetLovers = reader.ReadBoolean();
                break;
        }
    }

    enum RPCType
    {
        SyncState
    }
}
