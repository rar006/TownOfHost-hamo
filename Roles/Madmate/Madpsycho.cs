using System.Linq;
using AmongUs.GameOptions;
using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Madmate;

public sealed class MadPsycho : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MadPsycho),
            player => new MadPsycho(player),
            CustomRoles.MadPsycho,
            () => OptionCanVent.GetBool() ? RoleTypes.Engineer : RoleTypes.Crewmate,
            CustomRoleTypes.Madmate,
            20200,
            SetupOptionItems,
            "mps",
            OptionSort: (2, 3)
        );

    public MadPsycho(PlayerControl player)
        : base(
            RoleInfo,
            player,
            () => HasTask.ForRecompute
        )
    {
    }

    private static OptionItem OptionCanVent;
    public static OptionItem OptionDeathReason;
    public static OptionItem OptionTaskTrigger;

    public bool CanPsycho()
    {
        return MyTaskState.HasCompletedEnoughCountOfTasks(OptionTaskTrigger.GetInt());
    }

    public static MadPsycho Instance { get; private set; }

    public override void Add()
    {
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
    enum OptionName
    {
        psychoDeathReason
    }
    private static void SetupOptionItems()
    {
        var cRolesString = deathReasons.Select(x => x.ToString()).ToArray();
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, 9, GeneralOption.CanVent, false, false);
        OptionDeathReason = StringOptionItem.Create(RoleInfo, 10, OptionName.psychoDeathReason, cRolesString, 1, false);
        OptionTaskTrigger = IntegerOptionItem.Create(RoleInfo, 11, GeneralOption.TaskTrigger, new(0, 99, 1), 1, false).SetValueFormat(OptionFormat.Pieces);
        OverrideTasksData.Create(RoleInfo, 20);
    }

    public static readonly CustomDeathReason[] deathReasons =
    {
        CustomDeathReason.Kill, CustomDeathReason.Counter
    };
    public new void OnCheckMurderAsTarget(MurderInfo info)
    {
        info.GuardPower = 1;
        Psycho(info.AttemptKiller, info.KillPower);
    }
    public void Psycho(PlayerControl killer, int power, bool meeting = false)
    {
        if (power >= 2) return;
        if (!CanPsycho()) return;
        if (meeting)
        {
            string send = "";
            foreach (var spl in PlayerCatch.AllPlayerControls.Where(pc => !pc.IsAlive()))
            {
                if (!spl.IsAlive())
                {
                    send = string.Format(GetString("Rpsych"), UtilsName.GetPlayerColor(Player, true), UtilsName.GetPlayerColor(killer, true));
                }
                else
                {
                    send = string.Format(GetString("RMeetingKill"), UtilsName.GetPlayerColor(Player, true), UtilsName.GetPlayerColor(Player, true));
                }
                Utils.SendMessage(send, spl.PlayerId, GetString("RMSKillTitle"));
            }
        }
        PlayerState.GetByPlayerId(killer.PlayerId).DeathReason = deathReasons[OptionDeathReason.GetValue()];
        CustomRoleManager.OnCheckMurder(Player, killer, Player, killer, Killpower: 10);
    }
}