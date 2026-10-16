using System;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

/// <summary>
/// クロックワーカー(インポスター)
/// ・名前の横にタイマーがあり、キルクールが明けた状態でキルをしないと減っていく(キルするとリセット)
/// ・タイマーが0になるとキル能力を失い、クルーメイトになってタスクを課される
/// ・課されたタスクを全部終えるとキル能力が戻る。そのたびにタイマーの初期値が増え、キルクールが減る(下限あり)
/// </summary>
public sealed class Clockworker : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Clockworker),
            player => new Clockworker(player),
            CustomRoles.Clockworker,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            92700,
            SetupOptionItem,
            "cw",
            "#FF1919",
            isNewRole: true
        );

    static OptionItem OptionTimer;
    static OptionItem OptionTimerIncrease;
    static OptionItem OptionKillCooldown;
    static OptionItem OptionKillCooldownDecrease;
    static OptionItem OptionMinKillCooldown;
    enum OptionName { ClockworkerTimer, ClockworkerTimerIncrease, ClockworkerKillCooldownDecrease, ClockworkerMinKillCooldown }

    static void SetupOptionItem()
    {
        OptionTimer = FloatOptionItem.Create(RoleInfo, 10, OptionName.ClockworkerTimer, new(10f, 240f, 1f), 30f, false).SetValueFormat(OptionFormat.Seconds);
        OptionTimerIncrease = FloatOptionItem.Create(RoleInfo, 11, OptionName.ClockworkerTimerIncrease, new(0.5f, 180f, 0.5f), 5f, false).SetValueFormat(OptionFormat.Seconds);
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 12, GeneralOption.KillCooldown, new(0.5f, 180f, 0.5f), 30f, false).SetValueFormat(OptionFormat.Seconds);
        OptionKillCooldownDecrease = FloatOptionItem.Create(RoleInfo, 13, OptionName.ClockworkerKillCooldownDecrease, new(0.5f, 180f, 0.5f), 5f, false).SetValueFormat(OptionFormat.Seconds);
        OptionMinKillCooldown = FloatOptionItem.Create(RoleInfo, 14, OptionName.ClockworkerMinKillCooldown, new(0.5f, 180f, 0.5f), 15f, false).SetValueFormat(OptionFormat.Seconds);
        // タイマーが0になると課されるタスク(コモン1 / ロング0 / ショート2)
        OverrideTasksData.Create(RoleInfo, 20, tasks: (true, 1, 0, 2));
    }

    public Clockworker(PlayerControl player) : base(RoleInfo, player)
    {
        timerInitial = OptionTimer.GetFloat();
        timer = timerInitial;
        killCooldown = OptionKillCooldown.GetFloat();
        inTaskMode = false;
    }

    float timerInitial;
    float timer;
    float killCooldown;
    bool inTaskMode;
    int tasksDoneInMode;
    float lastNotify;

    public bool InTaskMode => inTaskMode;

    public float CalculateKillCooldown() => killCooldown;
    public bool CanUseKillButton() => Player.IsAlive() && !inTaskMode;
    public bool CanUseImpostorVentButton() => !inTaskMode;
    public bool CanUseSabotageButton() => !inTaskMode;

    public override RoleTypes? AfterMeetingRole => inTaskMode ? RoleTypes.Crewmate : RoleTypes.Impostor;

    static int TasksNeeded
    {
        get
        {
            if (!OverrideTasksData.AllData.TryGetValue(CustomRoles.Clockworker, out var d)) return 3;
            return Math.Max(1, d.numCommonTasks.GetInt() + d.numLongTasks.GetInt() + d.numShortTasks.GetInt());
        }
    }

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (!info.DoKill || !info.CanKill) return;
        timer = timerInitial; // キルしたらタイマーが戻る
        SendRPC();
    }

    /// <summary>毎フレーム(ホスト・タスク中): キルクールが明けているのにキルしていなければタイマーを減らす</summary>
    public void Tick(float dt)
    {
        if (!Player.IsAlive() || inTaskMode) return;
        var since = CheckMurderPatch.TimeSinceLastKill.TryGetValue(Player.PlayerId, out var s) ? s : float.MaxValue;
        if (since < killCooldown) return;
        timer -= dt;
        if (Time.time - lastNotify >= 1f)
        {
            lastNotify = Time.time;
            UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
        }
        if (timer > 0f) return;
        EnterTaskMode();
    }

    void EnterTaskMode()
    {
        timer = 0f;
        inTaskMode = true;
        tasksDoneInMode = 0;
        Logger.Info("クロックワーカー: タイマー切れ → タスクモード", "Clockworker");
        Player.RpcSetRoleDesync(RoleTypes.Crewmate, Player.GetClientId());
        Player.Data.RpcSetTasks(Array.Empty<byte>()); // タスクを課す
        Player.SyncSettings();
        Utils.SendMessage(GetString("ClockworkerTimeUp"), Player.PlayerId);
        SendRPC();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    public override bool OnCompleteTask(uint taskid)
    {
        if (!AmongUsClient.Instance.AmHost || !inTaskMode) return true;
        tasksDoneInMode++;
        if (tasksDoneInMode < TasksNeeded) return true;
        // タスク完了 → キル能力が戻る(キルクールは明けた状態)
        inTaskMode = false;
        timerInitial += OptionTimerIncrease.GetFloat();
        timer = timerInitial;
        killCooldown = Mathf.Max(OptionMinKillCooldown.GetFloat(), killCooldown - OptionKillCooldownDecrease.GetFloat());
        Logger.Info($"クロックワーカー: タスク完了 → キル能力復活 (タイマー{timerInitial}s / キルク{killCooldown}s)", "Clockworker");
        Player.RpcSetRoleDesync(RoleTypes.Impostor, Player.GetClientId());
        Player.ResetKillCooldown();
        Player.SetKillCooldown(0.1f);
        Player.Data.RpcSetTasks(Array.Empty<byte>()); // タスクを再配布
        SendRPC();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
        return true;
    }

    public override string GetProgressText(bool comms = false, bool gamelog = false)
        => inTaskMode
            ? Utils.ColorString(ModColors.NeutralGray, $"({tasksDoneInMode}/{TasksNeeded})")
            : Utils.ColorString(RoleInfo.RoleColor, $"T{Mathf.CeilToInt(Mathf.Max(0f, timer))}");

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(inTaskMode);
        sender.Writer.Write(timer);
        sender.Writer.Write(killCooldown);
        sender.Writer.Write(tasksDoneInMode);
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        inTaskMode = reader.ReadBoolean();
        timer = reader.ReadSingle();
        killCooldown = reader.ReadSingle();
        tasksDoneInMode = reader.ReadInt32();
    }
}
