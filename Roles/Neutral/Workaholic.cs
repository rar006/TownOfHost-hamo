using AmongUs.GameOptions;
using System.Linq;
<<<<<<< HEAD

=======
using UnityEngine;

using TownOfHost;
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Neutral;

public sealed class Workaholic : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Workaholic),
            player => new Workaholic(player),
            CustomRoles.Workaholic,
            () => OptionCanVent.GetBool() ? RoleTypes.Engineer : RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            55400,
            SetupOptionItem,
            "wh",
            "#008b8b",
            (5, 3),
            from: From.TownOfHost_Y,
            introSound: () => ShipStatus.Instance.CommonTasks.Where(task => task.TaskType == TaskTypes.FixWiring).FirstOrDefault().MinigamePrefab.OpenSound
        );
<<<<<<< HEAD
    public Workaholic(PlayerControl player)
    : base(
        RoleInfo,
        player,
        () => HasTask.ForRecompute
    )
    {
        ventCooldown = OptionVentCooldown.GetFloat();
        CanWinAtDeath = OptionWinatDeath.GetBool();
    }
    private static OptionItem OptionCanVent;
    private static OptionItem OptionVentCooldown;
    private static OptionItem OptionWinatDeath;
    enum OptionName { WorkaholicCanWinAtDeath }
    private static bool CanWinAtDeath;
    private static float ventCooldown;
=======

    public Workaholic(PlayerControl player)
        : base(
            RoleInfo,
            player,
            () => HasTask.ForRecompute
        )
    {
        ventCooldown = OptionVentCooldown.GetFloat();
        CanWinAtDeath = OptionWinatDeath.GetBool();
        revealIfIdle = OptionRevealIfIdle.GetBool();
        revealDelay = OptionRevealDelay.GetFloat();
        InitializeRevealState();
    }

    private static OptionItem OptionCanVent;
    private static OptionItem OptionVentCooldown;
    private static OptionItem OptionWinatDeath;
    private static OptionItem OptionRevealIfIdle;
    private static OptionItem OptionRevealDelay;

    private static bool CanWinAtDeath;
    private static float ventCooldown;

    private readonly bool revealIfIdle;
    private readonly float revealDelay;
    private bool revealed;
    private float revealTimer;
    private int lastShownSecond;

    enum OptionName
    {
        WorkaholicCanWinAtDeath,
        WorkaholicRevealIfIdle,
        WorkaholicRevealDelay,
    }

>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    private static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, 10, defo: 1);
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, 11, GeneralOption.CanVent, false, false);
        OptionVentCooldown = FloatOptionItem.Create(RoleInfo, 12, GeneralOption.Cooldown, new(0f, 180f, 2.5f), 0f, false, OptionCanVent)
                .SetValueFormat(OptionFormat.Seconds);
        OptionWinatDeath = BooleanOptionItem.Create(RoleInfo, 13, OptionName.WorkaholicCanWinAtDeath, false, false);
<<<<<<< HEAD

        OverrideTasksData.Create(RoleInfo, 20);
    }
=======
        OptionRevealIfIdle = BooleanOptionItem.Create(RoleInfo, 14, OptionName.WorkaholicRevealIfIdle, false, false);
        OptionRevealDelay = FloatOptionItem.Create(RoleInfo, 15, OptionName.WorkaholicRevealDelay, new(5f, 60f, 5f), 20f, false, OptionRevealIfIdle)
                .SetValueFormat(OptionFormat.Seconds);

        OverrideTasksData.Create(RoleInfo, 20);
    }

    private void InitializeRevealState()
    {
        if (revealIfIdle)
        {
            revealTimer = revealDelay;
            revealed = false;
            lastShownSecond = Mathf.CeilToInt(revealTimer);
        }
        else
        {
            revealTimer = 0f;
            revealed = true;
            lastShownSecond = -1;
        }
    }

    public override void OnSpawn(bool initialState = false)
    {
        InitializeRevealState();
    }

>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.EngineerCooldown = ventCooldown;
        AURoleOptions.EngineerInVentMaxTime = 0;
    }
<<<<<<< HEAD
    public override bool OnCompleteTask(uint taskid)
    {
=======

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!revealIfIdle || revealed || !MyState.HasSpawned) return;

        revealTimer = Mathf.Max(0f, revealTimer - Time.fixedDeltaTime);
        var currentSecond = Mathf.CeilToInt(revealTimer);

        if (revealTimer <= 0f)
        {
            revealed = true;
            if (lastShownSecond != 0)
            {
                lastShownSecond = 0;
                UtilsNotifyRoles.NotifyRoles(NoCache: true);
            }
            return;
        }

        if (currentSecond != lastShownSecond)
        {
            lastShownSecond = currentSecond;
            UtilsNotifyRoles.NotifyRoles(NoCache: true);
        }
    }

    public override bool OnCompleteTask(uint taskid)
    {
        if (revealIfIdle)
        {
            InitializeRevealState();
            UtilsNotifyRoles.NotifyRoles(NoCache: true);
        }

>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
        if (IsTaskFinished && (CanWinAtDeath || Player.IsAlive()))
        {
            if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Workaholic, Player.PlayerId, true))
            {
                CustomWinnerHolder.NeutralWinnerIds.Add(Player.PlayerId);
            }
        }
        return true;
    }
<<<<<<< HEAD
    public override void OverrideProgressTextAsSeen(PlayerControl seer, ref bool enabled, ref string text)
    {
        seer ??= Player; //自視点/GMは変更なし
        if (!Player.IsAlive() && !CanWinAtDeath)
        {
            text = "";
        }

        if (Is(seer) || seer.Is(CustomRoles.GM)) return;
        text = $"(?/{MyTaskState.AllTasksCount})";
    }
=======

    public override void OverrideDisplayRoleNameAsSeen(PlayerControl seer, ref bool enabled, ref UnityEngine.Color roleColor, ref string roleText, ref bool addon)
    {
        if (!revealIfIdle) return;
        if (seer == null || seer == Player || seer.Is(CustomRoles.GM)) return;
        if (!revealed) enabled = false;
    }

    public override void OverrideProgressTextAsSeen(PlayerControl seer, ref bool enabled, ref string text)
    {
        seer ??= Player; // self/GM no change
        if (!Player.IsAlive() && !CanWinAtDeath)
        {
            text = "";
            return;
        }

        if (Is(seer) || seer.Is(CustomRoles.GM)) return;
        if (revealIfIdle && !revealed)
        {
            text = "";
            return;
        }
        text = $"(?/{MyTaskState.AllTasksCount})";
    }

    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        if (!revealIfIdle) return "";
        return revealed ? "\u30bf\u30b9\u30af\u3092\u3057\u308d!!" : $"({Mathf.CeilToInt(revealTimer)}s)";
    }
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
}
