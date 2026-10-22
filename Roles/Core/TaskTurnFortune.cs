using System;
using System.Collections.Generic;
using AmongUs.GameOptions;
using UnityEngine;
using TownOfHost.Patches;

namespace TownOfHost.Roles.Core;

// 占い師系の「タスクターンで占う」機能(イビルテラーと同じ方法)。
// ペットを撫でるとタスクモード ⇔ 占いモードを切り替え、占いモードではキルボタンで相手を選び、
// 一定時間近くにいると役職が分かる(結果の出し方は各役職の占いと同じ)。
// ONの間はその役職をシェリフと同じ「自分にだけインポスターに見える」役職にする。
public sealed class TaskTurnFortune
{
    public sealed class Settings
    {
        public SimpleRoleInfo Info;
        public OptionItem Enable;
        public OptionItem NoMeeting;
        public OptionItem Time;
        public OptionItem Distance;
        public OptionItem Cooldown;
        public bool On => Enable?.GetBool() == true;
        public bool BlocksMeeting => On && NoMeeting?.GetBool() == true;
    }

    private static readonly List<Settings> allSettings = new();
    private static readonly Dictionary<byte, TaskTurnFortune> active = new();

    public static Settings CreateOptions(SimpleRoleInfo info, int idOffset)
    {
        var s = new Settings { Info = info };
        s.Enable = BooleanOptionItem.Create(info, idOffset, "TaskTurnFortune", false, false)
            .SetOptionName(() => "占い方法にタスクターンを追加(ペットでタスク/占いモード切替)");
        s.NoMeeting = BooleanOptionItem.Create(info, idOffset + 1, "TaskTurnFortuneNoMeeting", false, false, s.Enable)
            .SetOptionName(() => "会議では占えないようにする");
        s.Time = FloatOptionItem.Create(info, idOffset + 2, "TaskTurnFortuneTime", new(0.5f, 30f, 0.5f), 5f, false, s.Enable)
            .SetValueFormat(OptionFormat.Seconds)
            .SetOptionName(() => "占うのに近くにいる時間");
        s.Distance = FloatOptionItem.Create(info, idOffset + 3, "TaskTurnFortuneDistance", new(1f, 30f, 0.25f), 1.75f, false, s.Enable)
            .SetOptionName(() => "近くとみなす距離");
        s.Cooldown = FloatOptionItem.Create(info, idOffset + 4, "TaskTurnFortuneCooldown", new(0f, 180f, 2.5f), 20f, false, s.Enable)
            .SetValueFormat(OptionFormat.Seconds)
            .SetOptionName(() => "占いのクールタイム");
        s.Enable.RegisterUpdateValueEvent((_, _) => ApplyDesync(s));
        allSettings.Add(s);
        ApplyDesync(s);
        return s;
    }

    private static void ApplyDesync(Settings s)
    {
        if (s?.Info != null) s.Info.IsDesyncImpostor = s.On;
    }

    // 役職を配る前に呼ぶ(設定ファイルから読み込んだ値も反映する)
    public static void ApplyAll()
    {
        foreach (var s in allSettings) ApplyDesync(s);
    }

    // キルの判定より前に呼ぶ。占いモードでキルボタンを押した時はキルにせず、占いを始めて true を返す
    public static bool TryHandleKill(PlayerControl killer, PlayerControl target)
    {
        if (killer == null || !active.TryGetValue(killer.PlayerId, out var f) || f.role.Player != killer) return false;
        if (!f.On || f.TaskMode) return false;
        f.StartDivination(target);
        return true;
    }

    private readonly RoleBase role;
    private readonly Settings settings;
    private readonly Func<bool> canUse;
    private readonly Action<byte> onDivine;
    private byte targetId = byte.MaxValue;
    private float timer;

    public bool TaskMode { get; private set; } = true;
    public bool On => settings.On;
    public bool BlocksMeeting => settings.BlocksMeeting;
    public float Cooldown => settings.Cooldown.GetFloat();
    private PlayerControl Player => role.Player;

    public TaskTurnFortune(RoleBase role, Settings settings, Func<bool> canUse, Action<byte> onDivine)
    {
        this.role = role;
        this.settings = settings;
        this.canUse = canUse;
        this.onDivine = onDivine;
    }

    public void Add()
    {
        TaskMode = true;
        targetId = byte.MaxValue;
        if (!On || Player == null) return;
        active[Player.PlayerId] = this;
        PetActionManager.Register(Player.PlayerId, OnPet);
    }

    public void OnDestroy()
    {
        if (Player == null) return;
        if (active.TryGetValue(Player.PlayerId, out var f) && f == this) active.Remove(Player.PlayerId);
        if (On) PetActionManager.Unregister(Player.PlayerId);
    }

    private void OnPet()
    {
        if (!AmongUsClient.Instance.AmHost || !On || Player == null || !Player.IsAlive() || GameStates.IsMeeting) return;
        SetMode(!TaskMode);
    }

    public void SetMode(bool taskMode)
    {
        TaskMode = taskMode;
        targetId = byte.MaxValue;
        if (!AmongUsClient.Instance.AmHost || Player == null) return;
        var clientId = Player.GetClientId();
        if (Player.IsAlive() && clientId != -1)
        {
            foreach (var pc in PlayerCatch.AllAlivePlayerControls)
            {
                var r = pc.GetCustomRole();
                // 占いモードの間は、インポスターの仲間表示(赤い名前)が出ないよう科学者に見せる(シェリフと同じ)
                if (r.IsImpostor()) SetRoleForClient(pc, taskMode ? r.GetRoleTypes() : RoleTypes.Scientist, clientId);
                if (pc == Player) SetRoleForClient(pc, taskMode ? RoleTypes.Crewmate : RoleTypes.Impostor, clientId);
            }
        }
        if (!taskMode) Player.SetKillCooldown(Cooldown, delay: true);
        UpdateLocalHud();
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: Player);
    }

    private void SetRoleForClient(PlayerControl target, RoleTypes rt, int clientId)
    {
        if (target == PlayerControl.LocalPlayer && Player == PlayerControl.LocalPlayer)
        {
            RoleManager.Instance.SetRole(PlayerControl.LocalPlayer, rt);
            return;
        }
        target.RpcSetRoleDesync(rt, clientId);
    }

    private void UpdateLocalHud()
    {
        if (Player != PlayerControl.LocalPlayer || !HudManager.InstanceExists) return;
        var hud = HudManager.Instance;
        hud.KillButton.ToggleVisible(CanUseKillButton());
        hud.ImpostorVentButton.ToggleVisible(false);
        hud.SabotageButton.ToggleVisible(false);
    }

    public bool CanUseKillButton()
        => On && !TaskMode && Player != null && Player.IsAlive() && targetId == byte.MaxValue && canUse();

    private void StartDivination(PlayerControl target)
    {
        if (target == null || !target.IsAlive() || target == Player) return;
        if (targetId != byte.MaxValue || !canUse()) return;
        targetId = target.PlayerId;
        timer = 0f;
        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} がタスクターンの占いを開始: {target.GetNameWithRole().RemoveHtmlTags()}", "TaskTurnFortune");
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    public void Cancel()
    {
        if (targetId == byte.MaxValue) return;
        targetId = byte.MaxValue;
        timer = 0f;
        if (Player != null) UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    public void OnFixedUpdate()
    {
        if (!AmongUsClient.Instance.AmHost || !On || targetId == byte.MaxValue || !GameStates.IsInTask) return;
        var target = PlayerCatch.GetPlayerById(targetId);
        if (Player == null || !Player.IsAlive() || target == null || !target.IsAlive())
        {
            Cancel();
            return;
        }
        if (Vector2.Distance(Player.transform.position, target.transform.position) > settings.Distance.GetFloat())
        {
            // 離れたら失敗(イビルテラーと同じ)
            Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} のタスクターンの占いが離れたため失敗", "TaskTurnFortune");
            Cancel();
            Player.SetKillCooldown(Cooldown, delay: true);
            return;
        }
        timer += Time.fixedDeltaTime;
        if (timer < settings.Time.GetFloat()) return;
        var id = targetId;
        targetId = byte.MaxValue;
        timer = 0f;
        onDivine?.Invoke(id);
        Player.SetKillCooldown(Cooldown, delay: true);
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player, ForceLoop: true);
    }

    // 会議が始まったら、占い途中のものは取り消し
    public void OnReportDeadBody() => Cancel();

    // 会議のあとはタスクモードから(会議でバニラの役職に戻るため)
    public void AfterMeetingTasks()
    {
        TaskMode = true;
        targetId = byte.MaxValue;
    }

    public string GetMark(PlayerControl seer, PlayerControl seen, bool isForMeeting)
    {
        if (isForMeeting || seer != Player || seen == null) return "";
        return seen.PlayerId == targetId ? "<color=#ff1919>◆</color>" : "";
    }

    public string ModeText()
        => !On || GameStates.CalledMeeting ? "" : Utils.ColorString(Color.yellow, TaskMode ? " [タスク]" : " [占い]");
}
