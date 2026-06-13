using AmongUs.GameOptions;
using Hazel;
using HarmonyLib;
using System.Collections.Generic;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using UnityEngine;
using static TownOfHost.PlayerCatch;
using static TownOfHost.Translator;
using static TownOfHost.Utils;

namespace TownOfHost.Roles.Impostor;

public sealed class Swooper : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Swooper),
            player => new Swooper(player),
            CustomRoles.Swooper,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            326500,
            SetupOptionItem,
            "sw",
            OptionSort: (3, 10),
            from: From.TownOfHost_E
        );

    public Swooper(PlayerControl player) : base(RoleInfo, player)
    {
        cooldownTimer = 0f;
        durationTimer = 0f;
        isInvisible = false;
        lastVentId = -1;
        lastCoolDisplay = -1;
        lastDurDisplay = -1;
    }

    static OptionItem OptionCooldown;
    static OptionItem OptionDuration;
    static OptionItem OptionVentNormallyOnCooldown;

    static float Cooldown;
    static float Duration;
    static bool VentNormallyOnCooldown;

    enum OptionName
    {
        SwooperCooldown,
        SwooperDuration,
        SwooperVentNormallyOnCooldown,
    }

    float cooldownTimer;
    float durationTimer;
    bool isInvisible;
    int lastVentId;     // ★ デシンクブート時に使ったベントID（退出時に全クライアントにブート送信するため保持）
    int lastCoolDisplay;
    int lastDurDisplay;

    // 他役職からスウーパーの不可視状態を参照するための静的セット
    public static readonly HashSet<byte> InvisibleSwooperIds = new();

    static void SetupOptionItem()
    {
        OptionCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.SwooperCooldown,
            new(1f, 180f, 1f), 30f, false).SetValueFormat(OptionFormat.Seconds);
        OptionDuration = FloatOptionItem.Create(RoleInfo, 11, OptionName.SwooperDuration,
            new(1f, 60f, 1f), 15f, false).SetValueFormat(OptionFormat.Seconds);
        OptionVentNormallyOnCooldown = BooleanOptionItem.Create(RoleInfo, 12,
            OptionName.SwooperVentNormallyOnCooldown, true, false);
    }

    public float CalculateKillCooldown() => 30f;
    public bool CanUseKillButton() => Player.IsAlive();
    public bool CanUseSabotageButton() => true;
    public bool CanUseImpostorVentButton() => true;

    bool CanGoInvis => cooldownTimer <= 0f && !isInvisible;

    public override void Add()
    {
        Cooldown = OptionCooldown.GetFloat();
        Duration = OptionDuration.GetFloat();
        VentNormallyOnCooldown = OptionVentNormallyOnCooldown.GetBool();
        cooldownTimer = Cooldown;
        durationTimer = 0f;
        isInvisible = false;
        lastVentId = -1;
        lastCoolDisplay = -1;
        lastDurDisplay = -1;
        InvisibleSwooperIds.Remove(Player.PlayerId);
    }

    public override void OnDestroy()
    {
        InvisibleSwooperIds.Remove(Player.PlayerId);
    }

    public override bool CanClickUseVentButton => true;

    // ★ TOHE方式のデシンクブート:
    //    スウーパー自身のクライアントにのみ BootFromVent を送る。
    //    他クライアントはスウーパーをベント内（=不可視）のまま認識し続ける。
    private static void RpcBootFromVentDesync(PlayerPhysics physics, int ventId, PlayerControl target)
    {
        var writer = AmongUsClient.Instance.StartRpcImmediately(
            physics.NetId,
            (byte)RpcCalls.BootFromVent,
            SendOption.Reliable,
            target.GetClientId()
        );
        writer.WritePacked(ventId);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        if (!AmongUsClient.Instance.AmHost) return true;

        if (isInvisible)
        {
            // 透明中にベントに入った → 解除
            _ = new LateTask(() =>
            {
                if (!Player.IsAlive()) return;
                // 全クライアントに通常BootFromVentを送り、ベント内から「出現」させて可視化
                Player.MyPhysics?.RpcBootFromVent(lastVentId >= 0 ? lastVentId : ventId);
                ExitInvisible();
            }, 0.1f, "Swooper.ExitInvis", true);
            return true;
        }

        _ = new LateTask(() =>
        {
            if (!Player.IsAlive() || isInvisible) return;

            if (!CanGoInvis)
            {
                if (!VentNormallyOnCooldown)
                {
                    physics.RpcBootFromVent(ventId);
                    int cdSec = Mathf.CeilToInt(cooldownTimer);
                    SendMessage(
                        string.Format(GetString("SwooperInvisInCooldown"), cdSec),
                        Player.PlayerId);
                }
            }
            else
            {
                lastVentId = ventId;
                // ★ デシンクブート: スウーパー自身にのみ BootFromVent を送る
                //    → スウーパーは自分がベントを出たと認識して自由に動ける
                //    → 他クライアントはスウーパーがベント内にいる（不可視）と認識し続ける
                RpcBootFromVentDesync(physics, ventId, Player);
                EnterInvisible();
            }
        }, 0.8f, "Swooper.VentCheck", true);

        return true;
    }

    private void EnterInvisible()
    {
        if (!Player.IsAlive()) return;

        isInvisible = true;
        durationTimer = Duration;
        cooldownTimer = 0f;

        InvisibleSwooperIds.Add(Player.PlayerId);
        SendRpc();
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: Player);
        SendMessage(GetString("SwooperInvisState"), Player.PlayerId);
    }

    private void ExitInvisible()
    {
        if (!isInvisible) return;

        isInvisible = false;
        durationTimer = 0f;
        cooldownTimer = Cooldown;
        lastVentId = -1;

        InvisibleSwooperIds.Remove(Player.PlayerId);
        SendRpc();
        UtilsNotifyRoles.NotifyRoles();
        SendMessage(GetString("SwooperInvisStateOut"), Player.PlayerId);
    }

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        if (!Is(info.AttemptKiller) || info.IsSuicide) return;
        if (!isInvisible) return;

        info.DoKill = false;
        (var killer, var target) = info.AttemptTuple;

        RPC.PlaySoundRPC(killer.PlayerId, Sounds.KillSound);
        killer.RpcProtectedMurderPlayer(target);
        target.SetRealKiller(killer);
        target.RpcMurderPlayer(target);
        killer.ResetKillCooldown();
        killer.SetKillCooldown();
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInTask) return;
        if (!Player.IsAlive()) return;

        bool needSync = false;

        if (!isInvisible && cooldownTimer > 0f)
        {
            cooldownTimer = Mathf.Max(0f, cooldownTimer - Time.fixedDeltaTime);
            int now = Mathf.CeilToInt(cooldownTimer);
            if (now != lastCoolDisplay)
            {
                lastCoolDisplay = now;
                if (cooldownTimer <= 0f)
                    SendMessage(GetString("SwooperCanVent"), Player.PlayerId);
                needSync = true;
            }
        }

        if (isInvisible && durationTimer > 0f)
        {
            durationTimer = Mathf.Max(0f, durationTimer - Time.fixedDeltaTime);
            int now = Mathf.CeilToInt(durationTimer);
            if (now != lastDurDisplay)
            {
                lastDurDisplay = now;
                needSync = true;
            }
            if (durationTimer <= 0f)
            {
                // ★ 時間切れ: 全クライアントにBootFromVentを送り可視化してから解除
                if (lastVentId >= 0)
                    Player.MyPhysics?.RpcBootFromVent(lastVentId);
                ExitInvisible();
                return;
            }
        }

        if (needSync)
        {
            SendRpc();
            if (player != PlayerControl.LocalPlayer)
                UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: player);
        }
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (isInvisible)
        {
            // ★ 会議でベント内から通常の退出としてBootFromVentを全員に送る（可視化）
            if (lastVentId >= 0)
                Player.MyPhysics?.RpcBootFromVent(lastVentId);
            ExitInvisible();
        }
        cooldownTimer = 0f;
        SendRpc();
    }

    public override void OnStartMeeting()
    {
        InvisibleSwooperIds.Remove(Player.PlayerId);
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        // 会議中に不可視が解除されているので念のためクリア
        if (isInvisible) ExitInvisible();
        lastVentId = -1;
        cooldownTimer = Cooldown;
        lastCoolDisplay = -1;
        lastDurDisplay = -1;
        SendRpc();
    }

    void SendRpc()
    {
        using var sender = CreateSender();
        sender.Writer.Write(isInvisible);
        sender.Writer.Write(cooldownTimer);
        sender.Writer.Write(durationTimer);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        isInvisible = reader.ReadBoolean();
        cooldownTimer = reader.ReadSingle();
        durationTimer = reader.ReadSingle();

        // ★ デシンク方式では不可視化はベント状態に依存するが、
        //    インポスターへの視認防止のため Visible も制御する（バックアップ）
        if (!Player.AmOwner)
        {
            if (isInvisible)
                InvisibleSwooperIds.Add(Player.PlayerId);
            else
            {
                InvisibleSwooperIds.Remove(Player.PlayerId);
                Player.Visible = true;
            }
        }
        else
        {
            if (isInvisible) InvisibleSwooperIds.Add(Player.PlayerId);
            else InvisibleSwooperIds.Remove(Player.PlayerId);
        }
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive() || isForMeeting) return "";

        string size = isForHud ? "" : "<size=60%>";
        string color = RoleInfo.RoleColorCode;

        if (isInvisible)
        {
            int sec = Mathf.CeilToInt(durationTimer);
            return $"{size}<color={color}>【透明中】残り {sec}s で解除</color>";
        }
        if (cooldownTimer > 0f)
        {
            int sec = Mathf.CeilToInt(cooldownTimer);
            return $"{size}<color=#888888>クールダウン中 ({sec}s)</color>";
        }
        return $"{size}<color={color}>ベントに入ると透明化！</color>";
    }

    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        if (!Player.IsAlive()) return "";
        if (isInvisible)
            return $"<color={RoleInfo.RoleColorCode}>(透明: {Mathf.CeilToInt(durationTimer)}s)</color>";
        if (cooldownTimer > 0f)
            return $"<color=#888888>(CD: {Mathf.CeilToInt(cooldownTimer)}s)</color>";
        return $"<color={RoleInfo.RoleColorCode}>(透明OK)</color>";
    }
}

// ★ バックアップパッチ: インポスター視点でも確実に見えないようにする
[HarmonyPatch(typeof(PlayerControl), nameof(PlayerControl.FixedUpdate))]
public static class SwooperInvisibilityPatch
{
    static void Postfix(PlayerControl __instance)
    {
        if (__instance == null) return;
        if (!Swooper.InvisibleSwooperIds.Contains(__instance.PlayerId)) return;
        if (__instance.AmOwner) return;  // スウーパー本人は自分自身が見える
        if (__instance.Visible) __instance.Visible = false;
    }
}