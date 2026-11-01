using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using UnityEngine;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

// Ported from SuperNewRoles (GPL-3.0)

public sealed class Pusher : RoleBase, IImpostor, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Pusher),
            player => new Pusher(player),
            CustomRoles.Pusher,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            97100,
            SetupOptionItem,
            "psh",
            "#FF1919",
            from: From.SuperNewRoles,
            isNewRole: true
        );

    public Pusher(PlayerControl player) : base(RoleInfo, player)
    {
        killCooldown = OptionKillCooldown.GetFloat();
        pushCooldown = OptionPushCooldown.GetFloat();
    }

    static OptionItem OptionKillCooldown;
    static OptionItem OptionPushCooldown;
    enum OptionName { PusherCooldown }

    static float killCooldown;
    static float pushCooldown;

    static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 0.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionPushCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.PusherCooldown, new(0f, 180f, 2.5f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    enum PushTarget { Right, Left, Down, Up }

    static readonly Dictionary<MapNames, (Vector2 Position, float Radius, Vector2 PushPosition, PushTarget Push)[]> PushPositions = new()
    {
        [MapNames.Airship] = new[]
        {
            (new Vector2(5.8f, 8.95f), 1f, new Vector2(5.8132f, 8.8f), PushTarget.Right),
            (new Vector2(10f, 9f), 1f, new Vector2(9.7064f, 8.8f), PushTarget.Left),
            (new Vector2(26.85f, 0.5f), 1f, new Vector2(26.6653f, 0.45f), PushTarget.Right),
            (new Vector2(28.15f, -1.5f), 1f, new Vector2(28.1792f, -1.65f), PushTarget.Left),
            (new Vector2(-13.84f, -16.3494f), 1.15f, new Vector2(-13.89f, -16.5f), PushTarget.Down),
            (new Vector2(7.3f, -15.6f), 2.2f, new Vector2(7.05f, -14.672f), PushTarget.Down),
            (new Vector2(10.3f, -16.7f), 1f, new Vector2(10.27f, -16.3995f), PushTarget.Down),
            (new Vector2(10.2f, -15.1f), 1f, new Vector2(10.59f, -15.1f), PushTarget.Right),
        },
        [MapNames.Skeld] = new[]
        {
            (new Vector2(11.93f, -12.14f), 1.75f, new Vector2(10.3f, -12.24f), PushTarget.Right),
            (new Vector2(7.7f, -10.8f), 1f, new Vector2(8.7f, -11.21f), PushTarget.Left),
            (new Vector2(7.8f, -13.4f), 0.75f, new Vector2(8.3f, -13.05f), PushTarget.Left),
            (new Vector2(0.2f, -17.1f), 0.55f, new Vector2(0.2f, -17.1f), PushTarget.Down),
            (new Vector2(10.9f, 0.7f), 1.25f, new Vector2(9.98f, 0.95f), PushTarget.Right),
            (new Vector2(7.5f, 2.78f), 0.9f, new Vector2(8.2f, 2.4f), PushTarget.Left),
            (new Vector2(8.08f, -0.17f), 0.85f, new Vector2(8.2f, 0.78f), PushTarget.Down),
        },
        [MapNames.Polus] = new[]
        {
            (new Vector2(24.2f, -3f), 0.8f, new Vector2(24.2f, -3f), PushTarget.Left),
            (new Vector2(4.79f, -4.07f), 0.8f, new Vector2(4.79f, -4.07f), PushTarget.Right),
            (new Vector2(33.97f, -4.85f), 1.5f, new Vector2(34.5f, -5.18f), PushTarget.Up),
            (new Vector2(32.33f, -14.32f), 1.4f, new Vector2(31.5f, -13.9f), PushTarget.Right),
            (new Vector2(32.11f, -15.75f), 0.75f, new Vector2(32.68f, -15.728f), PushTarget.Right),
            (new Vector2(31.55f, -16.9f), 0.75f, new Vector2(31.17f, -16.95f), PushTarget.Down),
        },
        [MapNames.Fungle] = new[]
        {
            (new Vector2(-23.81f, -7.9f), 1.2f, new Vector2(-23.26f, -7.12f), PushTarget.Left),
            (new Vector2(16.5f, 13.49f), 1.3f, new Vector2(16.8f, 13.6f), PushTarget.Left),
            (new Vector2(6.8f, -0.14f), 0.75f, new Vector2(6.8f, -0.14f), PushTarget.Left),
            (new Vector2(5.85f, 1.19f), 0.75f, new Vector2(5.85f, 1.19f), PushTarget.Left),
        },
    };

    public float CalculateKillCooldown() => killCooldown;
    public override void ApplyGameOptions(IGameOptions opt) => AURoleOptions.PhantomCooldown = pushCooldown;

    bool IUsePhantomButton.IsPhantomRole => true;
    public override string GetAbilityButtonText() => GetString("PusherButton");
    bool IUsePhantomButton.IsresetAfterKill => true;

    static float KillRange => NormalGameOptionsV12.KillDistances[Mathf.Clamp(Main.NormalOptions.KillDistance, 0, 2)] + 0.2f;

    const float CheckInterval = 0.25f;
    bool canPush;
    float nextCheckAt;

    IEnumerable<PlayerControl> PushCandidates()
    {
        var from = Player.GetTruePosition();
        return PlayerCatch.AllAlivePlayerControls
            .Where(pc => pc != null && pc != Player && pc.IsAlive() && !pc.inVent && !Player.IsTeammate(pc))
            .Where(pc => Vector2.Distance(from, pc.GetTruePosition()) <= KillRange)
            .OrderBy(pc => Vector2.Distance(from, pc.GetTruePosition()));
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || player != Player || Time.time < nextCheckAt) return;
        nextCheckAt = Time.time + CheckInterval;
        var map = (MapNames)Main.NormalOptions.MapId;
        var now = Player.IsAlive() && GameStates.IsInTask && !GameStates.IsMeeting && !Player.inVent
            && PushCandidates().Any(pc => TryGetPushSpot(map, pc, out _) || GetTopLadder(pc) != null);
        if (now == canPush) return;
        canPush = now;
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = false;
        ResetCooldown = false;
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;
        var map = (MapNames)Main.NormalOptions.MapId;
        foreach (var target in PushCandidates())
        {
            if (TryGetPushSpot(map, target, out var pushPos))
            {
                ResetCooldown = true;
                PushOffCliff(target, pushPos);
                return;
            }
            var ladder = GetTopLadder(target);
            if (ladder != null)
            {
                ResetCooldown = true;
                PushOffLadder(target, ladder);
                return;
            }
        }
        AdjustKillCooldown = true;
    }

    static bool TryGetPushSpot(MapNames map, PlayerControl target, out Vector2 pushPos)
    {
        pushPos = default;
        if (!PushPositions.TryGetValue(map, out var spots)) return false;
        var pos = (Vector2)target.transform.position;
        foreach (var spot in spots)
        {
            if (Vector2.Distance(spot.Position, pos) > spot.Radius) continue;
            pushPos = spot.PushPosition;
            return true;
        }
        return false;
    }

    static Ladder GetTopLadder(PlayerControl target)
    {
        var ladders = ShipStatus.Instance?.Ladders;
        if (ladders == null) return null;
        return ladders.FirstOrDefault(l => l != null && l.IsTop && l.Destination != null
            && Vector2.Distance(l.transform.position, target.transform.position) <= l.UsableDistance);
    }

    void PushOffCliff(PlayerControl target, Vector2 pushPos)
    {
        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} が {target.GetNameWithRole().RemoveHtmlTags()} を突き落としました", "Pusher");
        TownOfHost.Modules.ClientEffects.Sound(TownOfHost.Modules.ClientEffects.SoundKind.Swing, target.GetTruePosition()); TownOfHost.Modules.ClientEffects.MuteTeleport();
        target.RpcSnapToForced(pushPos, Hazel.SendOption.Reliable);
        _ = new LateTask(() =>
        {
            if (!target.IsAlive() || !Player.IsAlive() || !GameStates.IsInTask) return;
            DeadBodyEraser.KillWithoutBody(Player, target, CustomDeathReason.Fall);
            Player.SetKillCooldown();
        }, 0.15f, "Pusher.Cliff", true);
    }

    void PushOffLadder(PlayerControl target, Ladder ladder)
    {
        Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} が {target.GetNameWithRole().RemoveHtmlTags()} を梯子から突き落としました", "Pusher");
        var bottom = (Vector2)ladder.Destination.transform.position + new Vector2(0f, -0.2f);
        TownOfHost.Modules.ClientEffects.Sound(TownOfHost.Modules.ClientEffects.SoundKind.Swing, target.GetTruePosition()); TownOfHost.Modules.ClientEffects.MuteTeleport();
        target.RpcSnapToForced(bottom, Hazel.SendOption.Reliable);
        _ = new LateTask(() =>
        {
            if (!target.IsAlive() || !Player.IsAlive() || !GameStates.IsInTask) return;
            DeadBodyEraser.KillWithoutBody(Player, target, CustomDeathReason.Fall);
            Player.SetKillCooldown();
        }, 0.15f, "Pusher.Ladder", true);
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || seer.PlayerId != Player.PlayerId || seen.PlayerId != Player.PlayerId || !Player.IsAlive()) return "";
        if (!PushPositions.ContainsKey((MapNames)Main.NormalOptions.MapId) && (ShipStatus.Instance?.Ladders?.Length ?? 0) == 0)
            return Utils.ColorString(Palette.DisabledGrey, GetString("PusherNoSpot"));
        return canPush ? Utils.ColorString(RoleInfo.RoleColor, "◯") : "";
    }
}
