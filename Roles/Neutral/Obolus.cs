using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using UnityEngine;

namespace TownOfHost.Roles.Neutral;

public sealed class Obolus : RoleBase, ILNKiller, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Obolus),
            player => new Obolus(player),
            CustomRoles.Obolus,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Neutral,
            553000,
            SetupOptionItem,
            "ob",
            "#bd871a",
            (2, 5),
            true,
            from: From.NebulaontheShip,
            isNewRole: true
        );
    public Obolus(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        KillCooldown = OptionKillCooldown.GetFloat();
        CanVent = OptionCanVent.GetBool();
        KillCount = OptionCanKillCount.GetInt();
        WinFlag = false;
        Daycount = 1;
        NeedCount = OptionNeedKillCount.GetInt();
        KilledOnThisTurn = false;
    }
    static OptionItem OptionKillCooldown;
    static OptionItem OptionAddWin;
    public static OptionItem OptionCanVent;
    static OptionItem OptionKillLockTurn;
    static OptionItem OptionCanKillCount;
    static OptionItem OptionHasImpostorVision;
    static OptionItem OptionCanWinOtherKiller;
    static OptionItem OptionNeedKillCount;
    enum OptionName
    {
        CountKillerAddWin,
        ObolusKillLockTurn,
        WolfBoyShotLimit,
        ObolusCanWinOtherKiller,
        ObolusCanKill,
        ObolusNeedKillCont,
    }
    public static bool CanVent;
    private static float KillCooldown;
    int KillCount;
    bool WinFlag;
    int Daycount;
    int NeedCount;
    bool KilledOnThisTurn;
    bool IKiller.CanKill => !KilledOnThisTurn;

    // 追加: ターゲット役職ごとの「このキルで勝利可能」トグル
    public static Dictionary<CustomRoles, OptionItem> ExtraWinKillTargetOptions = new();

    private static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, 9, defo: 1);
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 20f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionKillLockTurn = IntegerOptionItem.Create(RoleInfo, 11, OptionName.ObolusKillLockTurn, new(0, 99, 1), 2, false)
            .SetValueFormat(OptionFormat.day);
        OptionCanKillCount = IntegerOptionItem.Create(RoleInfo, 12, OptionName.WolfBoyShotLimit, new(1, 14, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
        OptionNeedKillCount = IntegerOptionItem.Create(RoleInfo, 70, OptionName.ObolusNeedKillCont, new(1, 14, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
        OptionHasImpostorVision = BooleanOptionItem.Create(RoleInfo, 13, GeneralOption.ImpostorVision, true, false);
        OptionCanVent = BooleanOptionItem.Create(RoleInfo, 14, GeneralOption.CanVent, true, false);
        OptionAddWin = BooleanOptionItem.Create(RoleInfo, 15, OptionName.CountKillerAddWin, false, false);
        OptionCanWinOtherKiller = BooleanOptionItem.Create(RoleInfo, 16, OptionName.ObolusCanWinOtherKiller, false, false);
        SetUpExtraWinKillTargetOptions(17);
        RoleAddAddons.Create(RoleInfo, 71);
    }

    // 追加: 役職ごとの勝利対象トグルをまとめて生成
    private static void SetUpExtraWinKillTargetOptions(int idOffset)
    {
        foreach (var role in CustomRolesHelper.AllStandardRoles.Where(x => x.IsNeutralKiller() && x != CustomRoles.Obolus))
        {
            if (Event.CheckRole(role) is false) continue;

            var id = RoleInfo.ConfigId + idOffset;
            var roleName = UtilsRoleText.GetRoleName(role);
            var replacementDic = new Dictionary<string, string>
        {
            { "%role%", Utils.ColorString(UtilsRoleText.GetRoleColor(role), roleName) }
        };

            var item = BooleanOptionItem
                .Create(id, OptionName.ObolusCanKill + "%role%", false, RoleInfo.Tab, false)
                .SetParent(OptionCanWinOtherKiller)
                .SetParentRole(CustomRoles.Obolus);
            item.ReplacementDictionary = replacementDic;

            ExtraWinKillTargetOptions[role] = item;
            idOffset++;
        }
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(OptionHasImpostorVision.GetBool());
    }
    public float CalculateKillCooldown() => KillCooldown;
    public override void Add()
    {
        KillCount = OptionCanKillCount.GetInt();
        Daycount = 1;
        WinFlag = false;
    }
    private void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(KillCount);
        sender.Writer.Write(WinFlag);
        sender.Writer.Write(Daycount);
        sender.Writer.Write(NeedCount);
        sender.Writer.Write(KilledOnThisTurn);
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        KillCount = reader.ReadInt32();
        WinFlag = reader.ReadBoolean();
        Daycount = reader.ReadInt32();
        NeedCount = reader.ReadInt32();
        KilledOnThisTurn = reader.ReadBoolean();
    }
    public bool CanUseKillButton() => KillCount > 0 && Daycount > OptionKillLockTurn.GetInt() && !KilledOnThisTurn;
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => CanVent;
    public bool IstargetRole(CustomRoles role)
    {
        if (ExtraWinKillTargetOptions.TryGetValue(role, out var option) && option.GetBool() && OptionCanWinOtherKiller.GetBool())
        {
            return true;
        }
        return role.GetCustomRoleTypes() is CustomRoleTypes.Impostor;
    }

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (Daycount <= OptionKillLockTurn.GetInt())
        {
            info.DoKill = false;
            return;
        }
        if (KilledOnThisTurn is true)
        {
            info.DoKill = false;
            return;
        }
        (var killer, var target) = info.AttemptTuple;
        KilledOnThisTurn = true;
        --KillCount;
        SendRPC();
        if (IstargetRole(target.GetCustomRole()))
        {
            --NeedCount;
            if (NeedCount <= 0)
            {
                ForceSoloWin();
            }
        }
        _ = new LateTask(() =>
        {
            if (target.IsAlive())
            {
                //脳筋だけどこれで（）
                KilledOnThisTurn = false;
                SendRPC();
            }
        }, Main.LagTime, "", true);
    }
    private void ForceSoloWin()
    {
        WinFlag = true;

        if (OptionAddWin.GetBool()) return;
        if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.CountKiller, Player.PlayerId))
        {
            CustomWinnerHolder.WinnerIds.Add(Player.PlayerId);
            CustomWinnerHolder.NeutralWinnerIds.Add(Player.PlayerId);
        }
    }
    public override void AfterMeetingTasks()
    {
        ++Daycount;
        KilledOnThisTurn = false;
        SendRPC();
    }
    public override string GetProgressText(bool comms = false, bool gamelog = false) => Utils.ColorString(KillCount > 0 ? Color.yellow : Color.gray, $"({KillCount})");
    public bool CheckWin(ref CustomRoles winnerRole) => OptionAddWin.GetBool() && WinFlag;
}