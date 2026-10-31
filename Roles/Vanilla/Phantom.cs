using System;
using System.Linq;
using AmongUs.GameOptions;
using HarmonyLib;
using Hazel;
using TownOfHost.Patches;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Roles.Impostor;
using UnityEngine;

namespace TownOfHost.Roles.Vanilla;

public sealed class Phantom : RoleBase, IImpostor, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.CreateForVanilla(
            typeof(Phantom),
            player => new Phantom(player),
            RoleTypes.Phantom,
            SetUpCustomOption,
            from: From.AmongUs
        );
    public Phantom(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        Duration = PhantomDuration.GetFloat();
        NowInvisible = false;
        DurationTimer = null;
    }
    public static OptionItem PhantomCooldown;
    public static OptionItem PhantomDuration;
    public static OptionItem CanKillInvis;
    public static OptionItem CanSeeNakama;
    float Duration;
    float? DurationTimer;
    public bool NowInvisible;

    public bool UseOneclickButton => CanKillInvis.GetBool();
    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        if (NowInvisible)
        {
            ExitInvis();
        }
        return !NowInvisible;
    }
    enum OptionName
    {
        PhantomCanKillInvis,
        PhantomCanSeeNakama
    }

    public static void SetUpCustomOption()
    {
        PhantomCooldown = FloatOptionItem.Create(RoleInfo, 10, "PhantomCooldown", new(0f, 180f, 2.5f), 15f, false)
        .SetValueFormat(OptionFormat.Seconds);
        PhantomDuration = FloatOptionItem.Create(RoleInfo, 11, "PhantomDuration", new(0f, 180f, 2.5f), 5f, false)
                .SetValueFormat(OptionFormat.Seconds).SetZeroNotation(OptionZeroNotation.Infinity);
        CanSeeNakama = BooleanOptionItem.Create(RoleInfo, 12, OptionName.PhantomCanSeeNakama, false, false);
        CanKillInvis = BooleanOptionItem.Create(RoleInfo, 13, OptionName.PhantomCanKillInvis, true, false);
    }
    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = PhantomCooldown.GetFloat();
        AURoleOptions.PhantomDuration = PhantomDuration.GetFloat();
    }
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (DurationTimer != null)
        {
            DurationTimer -= Time.fixedDeltaTime;
            if (DurationTimer <= 0f)
            {
                ExitInvis();
            }
        }
    }

    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        if (!NowInvisible) return string.Empty;

        var durat = Math.Truncate((decimal)DurationTimer);

        return $"{durat}";
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
        => NowInvisible ? Utils.ColorString(RoleInfo.RoleColor, GetString("PhantomInvisText")) : string.Empty;

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(NowInvisible);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        NowInvisible = reader.ReadBoolean();
    }
    private static void RpcEnterVentDesync(PlayerPhysics physics, int ventId, PlayerControl seer)
    {
        var og = physics.myPlayer.transform.position;
        var vent = ShipStatus.Instance.AllVents.FirstOrDefault(v => v.Id == ventId);

        Vector2 ventPos = vent?.transform.position ?? physics.myPlayer.transform.position;

        physics.myPlayer.RpcSnapToDesync(seer, ventPos);
        var writer = AmongUsClient.Instance.StartRpcImmediately(
            physics.NetId,
            (byte)RpcCalls.EnterVent,
            SendOption.Reliable,
            seer.GetClientId()
        );
        writer.WritePacked(ventId);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }
    void IKiller.OnCheckMurderAsKiller(MurderInfo info)
    {
        if (!CanKillInvis.GetBool() && NowInvisible)
        {
            info.DoKill = false;
        }
    }

    private static void RpcBootFromVentDesync(PlayerPhysics physics, int ventId, PlayerControl seer)
    {
        var og = physics.myPlayer.transform.position;
        var vent = ShipStatus.Instance.AllVents.FirstOrDefault(v => v.Id == ventId);
        physics.myPlayer.RpcSnapToDesync(seer, vent?.transform.position ?? physics.myPlayer.transform.position);

        var writer = AmongUsClient.Instance.StartRpcImmediately(
            physics.NetId,
            (byte)RpcCalls.BootFromVent,
            SendOption.Reliable,
            seer.GetClientId()
        );
        writer.WritePacked(ventId);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = true;
        ResetCooldown = true;

        if (NowInvisible)
        {
            ResetCooldown = false;
            return;
        }

        Swooper.InvisibleSwooperIds.Add(Player.PlayerId);
        DurationTimer = Duration;

        AURoleOptions.PhantomDuration = NowInvisible ? PhantomCooldown.GetFloat() : PhantomDuration.GetFloat();
        NowInvisible = !NowInvisible;

        if (!AmongUsClient.Instance.AmHost || !NowInvisible) return;

        SendRPC();

        if (!CanKillInvis.GetBool())
        {
            //バニラだと解除できなさそう。チェックも入れてるしこのままで良き
            /*var writer = CustomRpcSender.Create("Phantom OneClick", SendOption.None);
            writer.StartMessage(Player.GetClientId());

            writer.StartRpc(Player.NetId, (byte)RpcCalls.StartVanish)
                .EndRpc();
            writer.StartRpc(Player.NetId, (byte)RpcCalls.StartAppear)
                .Write(true)
                .EndRpc();*/
        }


        var physics = Player.MyPhysics;
        foreach (var p in PlayerCatch.AllAlivePlayerControls)
        {
            //MOD入れてるならスウーパーのパッチ側で消す。
            if (Is(p) || p.IsModClient()) continue;

            var isikiller = p.GetRoleClass() != null && p.GetRoleClass() is IKiller or ILNKiller or IUsePhantomButton;

            //インポ置き換えじゃなくて、モード切替系でもない
            //インポ置き換え前提のインターフェースも持ってない
            if (p.GetCustomRole().GetRoleTypes() is not
                RoleTypes.Impostor and not
                RoleTypes.Phantom and not
                RoleTypes.Shapeshifter and not
                RoleTypes.Viper &&

                p.GetCustomRole() is not
                CustomRoles.Sheriff and not CustomRoles.SwitchSheriff
                and not CustomRoles.Milkman and not CustomRoles.NiceLogger
                and not CustomRoles.SheriffHadouHo and not CustomRoles.Jailer
                and not CustomRoles.Walkure and not CustomRoles.Amnesiac
                and not CustomRoles.DarkSheriff &&
                !isikiller)
                continue;

            if (p.Is(CustomRoleTypes.Impostor) && CanSeeNakama.GetBool()) continue;

            var farthestVent = ShipStatus.Instance.AllVents
                .OrderByDescending(v => Vector2.Distance(p.GetTruePosition(), v.transform.position))
                .FirstOrDefault();

            if (farthestVent == null)
            {
                var vent = ShipStatus.Instance.AllVents
                    .FirstOrDefault();
                Vector2 vpos = farthestVent?.transform.position ?? Vector2.zero;
                vpos.x += 1.01f;
                vpos.y += 0.3f;

                Player.RpcSnapToDesync(p, vpos);
                RpcEnterVentDesync(physics, vent?.Id ?? 1, Player);
                continue;
            }

            Vector2 pos = farthestVent?.transform.position ?? Vector2.zero;
            pos.x += 1.01f;
            pos.y += 0.3f;

            Player.RpcSnapToDesync(p, pos);
            RpcEnterVentDesync(physics, farthestVent?.Id ?? 1, p);
        }
    }

    void ExitInvis()
    {
        NowInvisible = false;
        Swooper.InvisibleSwooperIds.Remove(Player.PlayerId);

        if (!AmongUsClient.Instance.AmHost) return;

        SendRPC();

        var physics = Player.MyPhysics;
        /*var writer = CustomRpcSender.Create("Phantom OneClick", SendOption.None);
        writer.StartMessage(Player.GetClientId());

        writer.StartRpc(Player.NetId, (byte)RpcCalls.StartVanish)
            .EndRpc();
        writer.StartRpc(Player.NetId, (byte)RpcCalls.StartAppear)
            .Write(true)
            .EndRpc();*/

        foreach (var p in PlayerCatch.AllAlivePlayerControls)
        {
            if (Is(p) || p.IsModClient()) continue;

            var isikiller = p.GetRoleClass() != null && p.GetRoleClass() is IKiller or ILNKiller or IUsePhantomButton;

            //インポ置き換えじゃなくて、モード切替系でもない
            //インポ置き換え前提のインターフェースも持ってない
            if (p.GetCustomRole().GetRoleTypes() is not
                RoleTypes.Impostor and not
                RoleTypes.Phantom and not
                RoleTypes.Shapeshifter and not
                RoleTypes.Viper &&

                p.GetCustomRole() is not
                CustomRoles.Sheriff and not CustomRoles.SwitchSheriff
                and not CustomRoles.Milkman and not CustomRoles.NiceLogger
                and not CustomRoles.SheriffHadouHo and not CustomRoles.Jailer
                and not CustomRoles.Walkure and not CustomRoles.Amnesiac
                and not CustomRoles.DarkSheriff &&
                !isikiller)
                continue;

            if (p.Is(CustomRoleTypes.Impostor) && CanSeeNakama.GetBool()) continue;

            var farthestVent = ShipStatus.Instance.AllVents
                .OrderByDescending(v => Vector2.Distance(p.GetTruePosition(), v.transform.position))
                .FirstOrDefault();

            if (farthestVent == null)
            {
                var vent = ShipStatus.Instance.AllVents
                    .FirstOrDefault();

                RpcBootFromVentDesync(physics, vent?.Id ?? 1, Player);
                continue;
            }

            RpcBootFromVentDesync(physics, farthestVent?.Id ?? 1, p);

            _ = new LateTask(() =>
            {
                if (Player == null || !Player.IsAlive() || p == null || !p.IsAlive()) return;
                Player.RpcSnapToDesync(p, Player.transform.position);
            }, 0.5f, "PhantomReturnPos");

        }
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckVanish))]
class CheckVanishPatch
{
    public static bool Prefix(PlayerControl __instance)
    {
        if (!AmongUsClient.Instance.AmHost || __instance.AmOwner) return true;
        if (!__instance.IsAlive() || __instance.GetRoleClass() is not Phantom pb) return true;

        AbilityButtonDoClickPatch.HandlePhantomClick(__instance, pb, pb);
        return false;
    }
}

[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.CheckAppear))]
class CheckAppearPatch
{
    public static bool Prefix(PlayerControl __instance, bool shouldAnimate)
    {
        if (!AmongUsClient.Instance.AmHost || __instance.AmOwner) return true;
        if (__instance.GetRoleClass() is not Phantom pb) return true;

        if (pb.NowInvisible)
        {
            AbilityButtonDoClickPatch.HandlePhantomClick(__instance, pb, pb);
        }
        return false;
    }
}

[HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.HandleRpc))]
class InvisiblePhantomVentPatch
{
    public static bool Prefix(PlayerPhysics __instance, byte callId)
    {
        if (!AmongUsClient.Instance.AmHost) return true;
        if ((RpcCalls)callId is not (RpcCalls.EnterVent or RpcCalls.ExitVent)) return true;

        var pc = __instance.myPlayer;
        if (pc.GetRoleClass() is Phantom ph && ph.NowInvisible)
        {
            return false;
        }
        return true;
    }
}