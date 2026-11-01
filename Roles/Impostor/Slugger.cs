using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

// Ported from SuperNewRoles (GPL-3.0)

public sealed class Slugger : RoleBase, IImpostor, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Slugger),
            player => new Slugger(player),
            CustomRoles.Slugger,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            97000,
            SetupOptionItem,
            "slg",
            "#FF1919",
            from: From.SuperNewRoles,
            isNewRole: true
        );

    public Slugger(PlayerControl player) : base(RoleInfo, player)
    {
        killCooldown = OptionKillCooldown.GetFloat();
        abilityCooldown = OptionAbilityCooldown.GetFloat();
        chargeTime = OptionChargeTime.GetFloat();
        swingDelay = OptionSwingDelay.GetFloat();
        multiKill = OptionMultiKill.GetBool();
        syncKillCooldown = OptionSyncKillCooldown.GetBool();
        canKillWhileCharging = OptionCanKillWhileCharging.GetBool();
        cantMoveWhileCharging = OptionCantMoveWhileCharging.GetBool();
    }

    static OptionItem OptionKillCooldown;
    static OptionItem OptionAbilityCooldown;
    static OptionItem OptionChargeTime;
    static OptionItem OptionSwingDelay;
    static OptionItem OptionMultiKill;
    static OptionItem OptionSyncKillCooldown;
    static OptionItem OptionCanKillWhileCharging;
    static OptionItem OptionCantMoveWhileCharging;
    enum OptionName { SluggerAbilityCooldown, SluggerChargeTime, SluggerSwingDelay, SluggerIsMultiKill, SluggerIsSyncKillCoolTime, SluggerCanKillWhileCharging, SluggerCantMoveWhileCharging }

    static float killCooldown;
    static float abilityCooldown;
    static float chargeTime;
    static float swingDelay;
    static bool multiKill;
    static bool syncKillCooldown;
    static bool canKillWhileCharging;
    static bool cantMoveWhileCharging;

    const float KillRadius = 1.5f;
    const float MaxHoldTime = 10f;
    const float FollowInterval = 0.2f;
    const float DummySpawnDelay = 4f;

    bool charging;
    bool waiting;
    float chargeUntil;
    float swingAt;
    int lastShownSec = -1;
    float savedSpeed = -1f;
    float nextFollowAt;

    TextDummy fan;
    float fanSpawnAt = -1f;

    static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionAbilityCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.SluggerAbilityCooldown, new(2.5f, 60f, 2.5f), 20f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionChargeTime = FloatOptionItem.Create(RoleInfo, 12, OptionName.SluggerChargeTime, new(0.5f, 10f, 0.5f), 3f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionSwingDelay = FloatOptionItem.Create(RoleInfo, 16, OptionName.SluggerSwingDelay, new(0f, 5f, 0.25f), 0.5f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMultiKill = BooleanOptionItem.Create(RoleInfo, 13, OptionName.SluggerIsMultiKill, false, false);
        OptionSyncKillCooldown = BooleanOptionItem.Create(RoleInfo, 14, OptionName.SluggerIsSyncKillCoolTime, false, false);
        OptionCanKillWhileCharging = BooleanOptionItem.Create(RoleInfo, 15, OptionName.SluggerCanKillWhileCharging, false, false);
        OptionCantMoveWhileCharging = BooleanOptionItem.Create(RoleInfo, 17, OptionName.SluggerCantMoveWhileCharging, false, false);
    }

    public float CalculateKillCooldown() => killCooldown;
    public override void ApplyGameOptions(IGameOptions opt) => AURoleOptions.PhantomCooldown = abilityCooldown;

    bool IUsePhantomButton.IsPhantomRole => true;
    public override string GetAbilityButtonText() => GetString(charging && Time.time >= chargeUntil ? "SluggerSwingButton" : "SluggerButton");
    bool IUsePhantomButton.IsresetAfterKill => syncKillCooldown;

    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = true;
        ResetCooldown = false;
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive() || waiting) return;
        if (charging)
        {
            if (Time.time < chargeUntil) return;
            StartWait();
            return;
        }
        charging = true;
        chargeUntil = Time.time + chargeTime;
        ClientEffects.Charge(Player, chargeTime, new Color(1f, 0.85f, 0.3f));
        lastShownSec = -1;
        nextFollowAt = 0f;
        if (cantMoveWhileCharging) SetSpeed(Main.MinSpeed);
        ShowFan(false);
        SendRPC();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    void StartWait()
    {
        charging = false;
        waiting = true;
        swingAt = Time.time + swingDelay;
        ShowFan(false);
        SendRPC();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
        if (swingDelay <= 0f) Swing();
    }

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (info.IsFakeSuicide) return;
        if ((charging || waiting) && !canKillWhileCharging) info.DoKill = false;
    }

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (!syncKillCooldown || info.IsFakeSuicide) return;
        if (charging || waiting) Cancel();
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || player != Player) return;
        if (!Player.IsAlive())
        {
            if (charging || waiting) Cancel();
            RemoveFan();
            return;
        }
        if (GameStates.IsMeeting || GameStates.CalledMeeting)
        {
            if (charging || waiting) Cancel();
            return;
        }
        if (fan == null && GameStates.IsInTask)
        {
            if (fanSpawnAt < 0f) fanSpawnAt = Time.time + DummySpawnDelay;
            if (Time.time >= fanSpawnAt) fan = new TextDummy();
        }
        if (!charging && !waiting) return;
        if ((charging || waiting) && Time.time >= nextFollowAt)
        {
            nextFollowAt = Time.time + FollowInterval;
            fan?.MoveTo(FanPoint());
        }
        if (waiting)
        {
            if (Time.time >= swingAt) Swing();
            return;
        }
        if (Time.time >= chargeUntil + MaxHoldTime) { StartWait(); return; }
        var sec = Mathf.CeilToInt(chargeUntil - Time.time);
        if (sec != lastShownSec)
        {
            lastShownSec = sec;
            UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
        }
    }

    Vector2 Facing => Player.cosmetics.FlipX ? Vector2.left : Vector2.right;
    bool fanForward;
    Vector2 FanPoint() => fanForward
        ? Player.GetTruePosition() + Facing * 0.85f + new Vector2(0f, 0.35f)
        : Player.GetTruePosition() + Facing * 0.45f + new Vector2(0f, 0.95f);

    const string FanPaper = "#FFFFFF";
    const string FanFold = "#C9BC98";
    const string FanTape = "#D7263D";
    const string FanCell = "■";
    const string FanPrefix = "<size=40%><line-height=1em><mspace=1em><alpha=#00>";

    static readonly (int Start, int Length, char Kind)[][] FanUp =
    {
        new[] { (0, 7, 'F') },
        new[] { (0, 7, 'F') },
        new[] { (0, 7, 'F') },
        new[] { (1, 5, 'F') },
        new[] { (1, 5, 'F') },
        new[] { (2, 3, 'F') },
        new[] { (2, 3, 'T') },
        new[] { (2, 3, 'T') },
        new[] { (2, 3, 'T') },
    };
    const int FanUpWidth = 7;

    static readonly (int Start, int Length, char Kind)[][] FanForward =
    {
        new[] { (6, 3, 'F') },
        new[] { (4, 5, 'F') },
        new[] { (0, 3, 'T'), (3, 6, 'F') },
        new[] { (0, 3, 'T'), (3, 6, 'F') },
        new[] { (0, 3, 'T'), (3, 6, 'F') },
        new[] { (4, 5, 'F') },
        new[] { (6, 3, 'F') },
    };
    const int FanForwardWidth = 9;

    string FanArt(bool forward)
    {
        var rows = forward ? FanForward : FanUp;
        var width = forward ? FanForwardWidth : FanUpWidth;
        var fold = forward ? "▀" : "▌";
        var mirror = forward && Player.cosmetics.FlipX;
        var sb = new System.Text.StringBuilder(FanPrefix);
        for (var r = 0; r < rows.Length; r++)
        {
            var segments = rows[r]
                .Select(s => mirror ? (Start: width - s.Start - s.Length, s.Length, s.Kind) : s)
                .OrderBy(s => s.Start)
                .ToArray();
            var x = 0;
            foreach (var (start, length, kind) in segments)
            {
                sb.Append(string.Concat(Enumerable.Repeat(FanCell, start - x)));
                if (kind == 'T')
                    sb.Append($"<alpha=#FF><mark={FanTape}FF><alpha=#00>").Append(string.Concat(Enumerable.Repeat(FanCell, length))).Append("</mark>");
                else
                    sb.Append($"<alpha=#FF><mark={FanPaper}FF><color={FanFold}>").Append(string.Concat(Enumerable.Repeat(fold, length))).Append("</color></mark><alpha=#00>");
                x = start + length;
            }
            sb.Append(string.Concat(Enumerable.Repeat(FanCell, width - x)));
            if (r < rows.Length - 1) sb.Append('\n');
        }
        return sb.Append("</size>").ToString();
    }

    void ShowFan(bool forward)
    {
        fanForward = forward;
        if (fan == null) return;
        fan.Show(FanArt(forward));
        fan.MoveTo(FanPoint());
    }

    void Swing()
    {
        charging = false;
        waiting = false;
        ClientEffects.ChargeStop(Player);
        ClientEffects.Sound(ClientEffects.SoundKind.Swing, Player.GetTruePosition());
        RestoreSpeed();
        SendRPC();
        ShowFan(true);
        _ = new LateTask(() => fan?.Park(), 0.4f, "Slugger.ParkFan", true);

        var targets = new List<PlayerControl>();
        var from = Player.GetTruePosition();
        var facing = Facing;
        var candidates = PlayerCatch.AllAlivePlayerControls
            .Where(pc => pc != null && pc != Player && pc.IsAlive() && !pc.inVent)
            .Where(pc => !Player.IsTeammate(pc) || SuddenDeathMode.NowSuddenDeathMode)
            .Where(pc => Vector2.Distance(from, pc.GetTruePosition()) <= KillRadius)
            .Where(pc => Vector2.Dot(pc.GetTruePosition() - from, facing) >= -0.2f)
            .Where(pc => !PhysicsHelpers.AnyNonTriggersBetween(from, (pc.GetTruePosition() - from).normalized, Vector2.Distance(from, pc.GetTruePosition()), Constants.ShipAndObjectsMask))
            .OrderBy(pc => Vector2.Distance(from, pc.GetTruePosition()))
            .ToList();
        if (multiKill) targets.AddRange(candidates);
        else if (candidates.Count > 0) targets.Add(candidates[0]);

        foreach (var target in targets)
            KnockAndKill(target, facing);
        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} がハリセンを振って {targets.Count} 人に当てました", "Slugger");

        Player.RpcResetAbilityCooldown();
        if (syncKillCooldown) Player.SetKillCooldown();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    const float KnockDistance = 1.6f;
    const int KnockSteps = 5;
    const float KnockStepTime = 0.06f;

    void KnockAndKill(PlayerControl target, Vector2 dir)
    {
        var start = (Vector2)target.transform.position;
        var dist = KnockDistance;
        var hit = Physics2D.Raycast(target.GetTruePosition(), dir, KnockDistance + 0.3f, Constants.ShipOnlyMask);
        if (hit.collider != null) dist = Mathf.Max(0f, hit.distance - 0.3f);
        ClientEffects.SluggerHit(target.GetTruePosition(), dir, dist);
        for (var i = 1; i <= KnockSteps; i++)
        {
            var t = (float)i / KnockSteps;
            var pos = start + dir * (dist * (1f - (1f - t) * (1f - t)));
            _ = new LateTask(() =>
            {
                if (target.IsAlive() && GameStates.IsInTask) target.RpcSnapToForced(pos);
            }, KnockStepTime * i, "Slugger.Knock", true);
        }
        _ = new LateTask(() =>
        {
            if (!target.IsAlive() || !Player.IsAlive() || !GameStates.IsInTask) return;
            DeadBodyEraser.KillWithoutBody(Player, target, CustomDeathReason.Hit);
        }, KnockStepTime * KnockSteps + 0.1f, "Slugger.KnockKill", true);
    }

    void Cancel()
    {
        if (!charging && !waiting) return;
        charging = false;
        waiting = false;
        ClientEffects.ChargeStop(Player);
        RestoreSpeed();
        fan?.Park();
        SendRPC();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    void SetSpeed(float speed)
    {
        if (savedSpeed < 0f) savedSpeed = Main.AllPlayerSpeed[Player.PlayerId];
        Main.AllPlayerSpeed[Player.PlayerId] = speed;
        Player.MarkDirtySettings();
    }

    void RestoreSpeed()
    {
        if (savedSpeed < 0f) return;
        Main.AllPlayerSpeed[Player.PlayerId] = savedSpeed;
        savedSpeed = -1f;
        Player.MarkDirtySettings();
    }

    void RemoveFan()
    {
        fan?.Remove();
        fan = null;
        fanSpawnAt = -1f;
    }

    public override void OnDestroy() => RemoveFan();

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        Cancel();
        RemoveFan();
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || seer.PlayerId != Player.PlayerId || seen.PlayerId != Player.PlayerId) return "";
        if (waiting) return Utils.ColorString(RoleInfo.RoleColor, GetString("SluggerWindup"));
        if (!charging) return "";
        var left = Mathf.CeilToInt(chargeUntil - Time.time);
        return Utils.ColorString(RoleInfo.RoleColor, left > 0
            ? string.Format(GetString("SluggerCharging"), left)
            : GetString("SluggerCharged"));
    }

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(charging);
        sender.Writer.Write(waiting);
        sender.Writer.Write(Mathf.Max(0f, chargeUntil - Time.time));
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        charging = reader.ReadBoolean();
        waiting = reader.ReadBoolean();
        chargeUntil = Time.time + reader.ReadSingle();
    }
}
