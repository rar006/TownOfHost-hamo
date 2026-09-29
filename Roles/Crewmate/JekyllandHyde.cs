using System.Collections.Generic;
using AmongUs.GameOptions;
using Hazel;
using Rewired;
using TownOfHost.Patches;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Roles.Crewmate;
using TownOfHost.Roles.Vanilla;
using UnityEngine;

namespace TownOfHost.Roles.Madmate;
public sealed class JekyllandHydeRole : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(JekyllandHydeRole),
            player => new JekyllandHydeRole(player),
            CustomRoles.JekyllandHyde,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            34800,
            SetupOptionItem,
            "jah",
            "#ffffff",
            (4, 2),
            introSound: () => GetIntroSound(RoleTypes.Shapeshifter),
            isNewRole: true,
            from: From.NebulaontheShip
        );

    public JekyllandHydeRole(PlayerControl player)
        : base(RoleInfo, player, () => HasTask.ForRecompute)
    {
        UsedKusuriId.Clear();
    }
    public override void Add()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        _ = new LateTask(() =>
        {
            int rnd = Random.Range(0, 2);
            if (rnd == 0)
            {
                Player.RpcSetCustomRole(CustomRoles.Jekyll, log: null);
            }
            else
            {
                Player.RpcSetCustomRole(CustomRoles.Hyde, log: null);
            }
        }, 0.2f, "", true);
    }
    public static OptionItem OptionAmmo;
    public static OptionItem OptionImpVision;
    public static OptionItem OptionKillCool;
    public static OptionItem OptionCanVent;
    public static OptionItem OptionCanVentMove;

    public static List<byte> UsedKusuriId = new();
    static void SetupOptionItem()
    {
        ObjectOptionitem.Create(RoleInfo, 7, "JekyllandHyde", true, null).SetOptionName(() => "Jekyll Setting").SetColor(Jekyll.RoleInfo.RoleColor);

        OverrideTasksData.Create(RoleInfo, 8, tasks: (true, 2, 2, 2));

        ObjectOptionitem.Create(RoleInfo, 14, "JekyllandHyde", true, null).SetOptionName(() => "Hyde Setting").SetColor(Hyde.RoleInfo.RoleColor);

        OptionAmmo = IntegerOptionItem.Create(RoleInfo, 15, OptionName.WolfBoyShotLimit, new(1, 15, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);

        OptionKillCool = FloatOptionItem.Create(RoleInfo, 16, GeneralOption.KillCooldown, OptionBaseCoolTime, 30f, false)
            .SetValueFormat(OptionFormat.Seconds);

        OptionImpVision = BooleanOptionItem.Create(RoleInfo, 17, GeneralOption.ImpostorVision, false, false);

        OptionCanVent = BooleanOptionItem.Create(RoleInfo, 18, GeneralOption.CanVent, false, false);
        OptionCanVentMove = BooleanOptionItem.Create(RoleInfo, 19, OptionName.MadmateCanMovedByVent, false, false, OptionCanVent);

        SatsumatoImo.HideRoleOptions(CustomRoles.Jekyll);
        SatsumatoImo.HideRoleOptions(CustomRoles.Hyde);
        SatsumatoImo.HideRoleOptions(CustomRoles.HydeImp);
    }

    enum OptionName
    {
        WolfBoyShotLimit,
        MadmateCanMovedByVent
    }
}

public sealed class Jekyll : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Jekyll),
            player => new Jekyll(player),
            CustomRoles.Jekyll,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            34900,
            SetupOptionItem,
            "jkl",
            "#8cffff",
            (8, 1),
            assignInfo: new RoleAssignInfo(CustomRoles.Jekyll, CustomRoleTypes.Crewmate)
            {
                IsInitiallyAssignableCallBack = () => false
            }
        );

    public Jekyll(PlayerControl player)
        : base(RoleInfo, player, () => HasTask.ForRecompute)
    {
        CanChangeHyde = false;
        PetActionManager.Register(Player.PlayerId, OnPetAction);
    }
    bool skipSwapForThisMeeting;
    bool CanChangeHyde = false;
    static void SetupOptionItem()
    {
        SatsumatoImo.HideRoleOptions(CustomRoles.Jekyll);
    }
    public override void Add()
    {
        PetActionManager.Register(Player.PlayerId, OnPetAction);
    }
    public override void OnDestroy()
    {
        PetActionManager.Unregister(Player.PlayerId);
    }

    public override void OnStartMeeting()
    {
        skipSwapForThisMeeting = SatsumatoImo.IsSpecialMeetingNoSwap();
    }
    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;
        if (skipSwapForThisMeeting)
        {
            skipSwapForThisMeeting = false;
            return;
        }
        skipSwapForThisMeeting = false;
        Player.RpcSetCustomRole(CustomRoles.Hyde, log: null);
    }
    void OnPetAction()
    {
        if (!IsTaskFinished || JekyllandHydeRole.UsedKusuriId.Contains(Player.PlayerId)) return;
        JekyllandHydeRole.UsedKusuriId.Add(Player.PlayerId);
        Player.RpcSetCustomRole(CustomRoles.Hyde, log: null);
    }
    public override bool OnCompleteTask(uint taskid)
    {
        if (IsTaskFinished)
        {
            CanChangeHyde = true;
            SendRPC();
        }
        return true;
    }

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(CanChangeHyde);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        CanChangeHyde = reader.ReadBoolean();
    }
}

public sealed class Hyde : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Hyde),
            player => new Hyde(player),
            CustomRoles.Hyde,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Madmate,
            70660,
            SetupOptionItem,
            "hyd",
            "#ff1919",
            (8, 2),
            true,
            assignInfo: new RoleAssignInfo(CustomRoles.Hyde, CustomRoleTypes.Madmate)
            {
                IsInitiallyAssignableCallBack = () => false
            },
            introSound: () => GetIntroSound(RoleTypes.Impostor)
        );

    public Hyde(PlayerControl player)
        : base(RoleInfo, player, () => HasTask.ForRecompute)
    {
        KillCooldown = JekyllandHydeRole.OptionKillCool.GetFloat();
        CanVent = JekyllandHydeRole.OptionCanVent.GetBool();
        CanMove = JekyllandHydeRole.OptionCanVentMove.GetBool();
        KillCount = JekyllandHydeRole.OptionAmmo.GetInt();
    }
    bool skipSwapForThisMeeting;
    static float KillCooldown;
    static bool CanVent;
    static bool CanMove;
    int KillCount;
    public float CalculateKillCooldown() => KillCooldown;
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => CanVent;
    public override bool CanVentMoving(PlayerPhysics physics, int ventId) => CanMove;
    bool IKiller.CanUseKillButton() => CanUseKillButton();

    static void SetupOptionItem()
    {
        SatsumatoImo.HideRoleOptions(CustomRoles.Hyde);
    }

    public override void OnStartMeeting()
    {
        skipSwapForThisMeeting = SatsumatoImo.IsSpecialMeetingNoSwap();
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;
        if (skipSwapForThisMeeting)
        {
            skipSwapForThisMeeting = false;
            return;
        }
        skipSwapForThisMeeting = false;
        Player.RpcSetCustomRole(CustomRoles.Jekyll, log: null);
    }
    bool CanUseKillButton() => KillCount > 0;
    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (CanUseKillButton())
        {
            info.DoKill = false;
            return;
        }
        --KillCount;
        SendRPC();
        var (killer, target) = info.AttemptTuple;
        if (target.Is(CustomRoleTypes.Impostor))
        {
            Player.RpcSetCustomRole(CustomRoles.HydeImp, log: null);
        }
    }
    public override string GetProgressText(bool comms = false, bool gamelog = false) => Utils.ColorString(CanUseKillButton() ? Color.yellow : Color.gray, $"({KillCount})");
    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(KillCount);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        KillCount = reader.ReadInt32();
    }
}

public sealed class HydeImp : RoleBase, IKiller
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(HydeImp),
            player => new HydeImp(player),
            CustomRoles.HydeImp,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            70670,
            SetupOptionItem,
            "hyd",
            "#ff1919",
            (8, 2),
            assignInfo: new RoleAssignInfo(CustomRoles.HydeImp, CustomRoleTypes.Impostor)
            {
                IsInitiallyAssignableCallBack = () => false
            },
            introSound: () => GetIntroSound(RoleTypes.Impostor)
        );

    public HydeImp(PlayerControl player)
        : base(RoleInfo, player, () => HasTask.ForRecompute)
    {
        KillCooldown = JekyllandHydeRole.OptionKillCool.GetFloat();
    }
    static float KillCooldown;
    public float CalculateKillCooldown() => KillCooldown;
    public bool CanUseSabotageButton() => true;
    public bool CanUseImpostorVentButton() => true;
    static void SetupOptionItem()
    {
        SatsumatoImo.HideRoleOptions(CustomRoles.HydeImp);
    }
}
