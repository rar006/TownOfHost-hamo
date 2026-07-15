<<<<<<< HEAD
/*using System.Collections.Generic;
=======
using System.Collections.Generic;
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using UnityEngine;

namespace TownOfHost.Roles.Impostor;

public sealed class Rocket : RoleBase, IImpostor, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Rocket),
            player => new Rocket(player),
            CustomRoles.Rocket,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            26350,
            SetupOptionItem,
            "rkt",
            OptionSort: (3, 14),
            from: From.SuperNewRoles
        );

    public Rocket(PlayerControl player) : base(RoleInfo, player)
    {
        InitialGrabCooldown = OptionInitialGrabCooldown.GetFloat();
        SubsequentGrabCooldown = OptionSubsequentGrabCooldown.GetFloat();
        LaunchCooldown = OptionLaunchCooldown.GetFloat();

        GrabbedPlayers = new();
        launchPending = false;
        killCDOverride = InitialGrabCooldown;

        CustomRoleManager.LowerOthers.Add(GetLowerTextOthers);
    }

    static OptionItem OptionInitialGrabCooldown; static float InitialGrabCooldown;
    static OptionItem OptionSubsequentGrabCooldown; static float SubsequentGrabCooldown;
    static OptionItem OptionLaunchCooldown; static float LaunchCooldown;

    enum OptionName
    {
        RocketInitialGrabCooldown,
        RocketSubsequentGrabCooldown,
        RocketLaunchCooldown,
    }

    public readonly List<PlayerControl> GrabbedPlayers;
<<<<<<< HEAD

    /// <summary>
    /// CheckMurderでキルをブロックするために使用するグローバルセット。
    /// CustomNetObject.DummyKillPatchから参照される。
    /// </summary>
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    public static readonly HashSet<byte> GrabbedPlayerIds = new();

    bool launchPending;
    float killCDOverride;
    int snapFrame = 0;

    static readonly Dictionary<byte, (string msg, float expireTime)> LaunchNotifications = new();

    static void SetupOptionItem()
    {
        OptionInitialGrabCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.RocketInitialGrabCooldown,
            new(2.5f, 60f, 2.5f), 15f, false).SetValueFormat(OptionFormat.Seconds);
        OptionSubsequentGrabCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.RocketSubsequentGrabCooldown,
            new(0f, 60f, 2.5f), 5f, false).SetValueFormat(OptionFormat.Seconds);
        OptionLaunchCooldown = FloatOptionItem.Create(RoleInfo, 12, OptionName.RocketLaunchCooldown,
            new(0f, 60f, 2.5f), 10f, false).SetValueFormat(OptionFormat.Seconds);
    }

    public float CalculateKillCooldown() => killCDOverride;
    public bool CanUseSabotageButton() => true;
    public bool CanUseImpostorVentButton() => true;
    public override bool CanClickUseVentButton => true;
    public override bool CanUseAbilityButton() => GrabbedPlayers.Count > 0;

    bool IUsePhantomButton.IsPhantomRole => true;
    bool IUsePhantomButton.IsresetAfterKill => false;

    [Attributes.GameModuleInitializer]
    public static void Init() => GrabbedPlayerIds.Clear();

    public override void Add()
    {
        killCDOverride = InitialGrabCooldown;
        Main.AllPlayerKillCooldown[Player.PlayerId] = killCDOverride;
    }

    public override void ApplyGameOptions(IGameOptions opt)
        => AURoleOptions.PhantomCooldown = LaunchCooldown;

<<<<<<< HEAD
    // ─── キルボタン → 掴む ───────────────────────────────────────
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        var target = info.AttemptTarget;
        if (target == null) { info.DoKill = false; return; }
        if (GrabbedPlayers.Contains(target)) { info.DoKill = false; return; }

        info.DoKill = false;
<<<<<<< HEAD
        if (!AmongUsClient.Instance.AmHost) return;
        GrabPlayer(target);
=======

        if (AmongUsClient.Instance.AmHost)
        {
            GrabPlayer(target);
        }
        else
        {
            using var sender = CreateSender();
            sender.Writer.Write((byte)0);
            sender.Writer.Write(target.PlayerId);
        }
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    }

    void GrabPlayer(PlayerControl target)
    {
        GrabbedPlayers.Add(target);
<<<<<<< HEAD
=======
        GrabbedPlayerIds.Add(target.PlayerId);
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56

        PlayerState.GetByPlayerId(target.PlayerId).CanMove = false;
        target.MarkDirtySettings();

<<<<<<< HEAD
        // ★ ホストはIsDead一時フラグでキルターゲットから外す
        // ★ 非ホストはSetRole偽装RPCを送る
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
        if (Player.AmOwner)
            target.Data.IsDead = true;
        else
            SetAppearsAsImpostorForRocket(target, true);

        killCDOverride = SubsequentGrabCooldown;
        Main.AllPlayerKillCooldown[Player.PlayerId] = killCDOverride;
        var grabState = PlayerState.GetByPlayerId(Player.PlayerId);
        if (grabState != null) grabState.Is10secKillButton = false;
        Player.SetKillCooldown(killCDOverride, force: false);
        Player.KillFlash();

<<<<<<< HEAD
        SendRpc();
=======
        SendSyncRpc();
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
        UtilsNotifyRoles.NotifyRoles();
        Logger.Info($"{Player.Data.GetLogPlayerName()} が {target.Data.GetLogPlayerName()} を掴んだ", "Rocket");
    }

<<<<<<< HEAD
    // ★ 非ホストのロケットプレイヤーの視点でのみターゲットをインポスターに見せる
    void SetAppearsAsImpostorForRocket(PlayerControl target, bool asImpostor)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Player.AmOwner) return; // ★ ホストには送らない
=======
    void SetAppearsAsImpostorForRocket(PlayerControl target, bool asImpostor)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (Player.AmOwner) return;
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56

        var fakeRole = asImpostor ? RoleTypes.Impostor : target.Data.RoleType;
        var writer = AmongUsClient.Instance.StartRpcImmediately(
            target.NetId, (byte)RpcCalls.SetRole, SendOption.Reliable, Player.OwnerId);
        writer.Write((ushort)fakeRole);
        writer.Write(true);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    void ReleasePlayer(PlayerControl target)
    {
        if (!GrabbedPlayers.Remove(target)) return;
<<<<<<< HEAD
=======
        GrabbedPlayerIds.Remove(target.PlayerId);
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
        PlayerState.GetByPlayerId(target.PlayerId).CanMove = true;
        target.MarkDirtySettings();
        if (Player.AmOwner)
            target.Data.IsDead = false;
        else
            SetAppearsAsImpostorForRocket(target, false);
    }

    void ReleaseAll()
    {
        foreach (var p in GrabbedPlayers.ToArray())
        {
            PlayerState.GetByPlayerId(p.PlayerId).CanMove = true;
            p.MarkDirtySettings();
            if (Player.AmOwner)
                p.Data.IsDead = false;
            else
                SetAppearsAsImpostorForRocket(p, false);
        }
        GrabbedPlayers.Clear();
<<<<<<< HEAD
    }

    // ─── ファントムボタン → 打ち上げ ─────────────────────────────
=======
        GrabbedPlayerIds.Clear();
    }

>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        if (GrabbedPlayers.Count == 0)
        {
            AdjustKillCooldown = true;
            ResetCooldown = false;
            return;
        }

        AdjustKillCooldown = false;
        ResetCooldown = false;

        if (!Player.IsAlive()) return;

<<<<<<< HEAD
=======
        if (AmongUsClient.Instance.AmHost)
        {
            ExecuteLaunch();
        }
        else
        {
            using var sender = CreateSender();
            sender.Writer.Write((byte)1);
        }
    }

    void ExecuteLaunch()
    {
        if (!Player.IsAlive()) return;

>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
        LaunchAll(Player.GetTruePosition());

        killCDOverride = InitialGrabCooldown;
        Main.AllPlayerKillCooldown[Player.PlayerId] = killCDOverride;
        var launchState = PlayerState.GetByPlayerId(Player.PlayerId);
        if (launchState != null) launchState.Is10secKillButton = false;
        Player.SetKillCooldown(killCDOverride, force: false);

        _ = new LateTask(() =>
        {
            if (!Player.IsAlive()) return;
            AURoleOptions.PhantomCooldown = LaunchCooldown;
            Player.RpcResetAbilityCooldown();
        }, 0.1f, "Rocket.ResetCD", true);

<<<<<<< HEAD
        SendRpc();
=======
        SendSyncRpc();
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    }

    void LaunchAll(Vector2 launchPos)
    {
        if (!AmongUsClient.Instance.AmHost) return;

        var targets = GrabbedPlayers.ToArray();
<<<<<<< HEAD
        ReleaseAll(); // ★ GrabbedPlayerIds も全削除
=======
        ReleaseAll();
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
        UtilsNotifyRoles.NotifyRoles();

        for (int i = 0; i < targets.Length; i++)
        {
            var target = targets[i];
            if (target == null || !target.IsAlive()) continue;

            float xOffset = (i - (targets.Length - 1) / 2f) * 0.4f;
            Vector2 spawnPos = launchPos + new Vector2(xOffset, 0f);

            PlayerState.GetByPlayerId(target.PlayerId).DeathReason = CustomDeathReason.Launch;
            target.RpcExileV3();
            PlayerState.GetByPlayerId(target.PlayerId).SetDead();

            UtilsGameLog.AddGameLog("Rocket",
                $"{UtilsName.GetPlayerColor(Player)}が{UtilsName.GetPlayerColor(target)}を打ち上げた");

<<<<<<< HEAD
            // ★ 0.6秒ずつずらしてスポーン（IsSpawning競合防止）
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
            var capturedTarget = target;
            var capturedPos = spawnPos;
            int idx = i;
            _ = new LateTask(() =>
            {
                _ = new RocketFlyDummy(capturedTarget, capturedPos, 1.5f);
            }, idx * 0.6f, $"Rocket.SpawnDummy.{idx}", true);

            NotifyLaunchToNearby(launchPos);
        }

        UtilsNotifyRoles.NotifyRoles();
    }

    void NotifyLaunchToNearby(Vector2 launchPos)
    {
        string msg = "\n<color=#ff6600>🚀 誰かが打ち上げられた！</color>";
        float expireTime = Time.realtimeSinceStartup + 2f;

        foreach (var pc in PlayerCatch.AllAlivePlayerControls)
        {
            if (pc.PlayerId == Player.PlayerId) continue;
            pc.KillFlash();
            LaunchNotifications[pc.PlayerId] = (msg, expireTime);
        }

        UtilsNotifyRoles.NotifyRoles();
    }

    public string GetLowerTextOthers(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seen != seer || isForMeeting) return "";

        if (LaunchNotifications.TryGetValue(seer.PlayerId, out var entry))
        {
            if (Time.realtimeSinceStartup < entry.expireTime)
                return entry.msg;
            LaunchNotifications.Remove(seer.PlayerId);
        }
        return "";
    }

<<<<<<< HEAD
    // ─── FixedUpdate ─────────────────────────────────────────────
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (LaunchNotifications.Count > 0)
        {
            bool anyExpired = false;
            foreach (var key in LaunchNotifications.Keys.ToArray())
            {
                if (Time.realtimeSinceStartup >= LaunchNotifications[key].expireTime)
                {
                    LaunchNotifications.Remove(key);
                    anyExpired = true;
                }
            }
            if (anyExpired) UtilsNotifyRoles.NotifyRoles();
        }

        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInTask) return;
        if (GrabbedPlayers.Count == 0) return;
<<<<<<< HEAD
        if (!Player.IsAlive()) { ReleaseAll(); SendRpc(); return; }

        foreach (var p in GrabbedPlayers.ToArray())
            if (p == null || !p.IsAlive()) ReleasePlayer(p);
=======
        if (!Player.IsAlive()) { ReleaseAll(); SendSyncRpc(); return; }

        bool removed = false;
        foreach (var p in GrabbedPlayers.ToArray())
        {
            if (p == null || !p.IsAlive())
            {
                ReleasePlayer(p);
                removed = true;
            }
        }
        if (removed) SendSyncRpc();
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56

        snapFrame++;
        if (snapFrame % 3 != 0) return;

        var myPos = Player.GetTruePosition();
        foreach (var grabbed in GrabbedPlayers.ToArray())
        {
            if (grabbed == null || !grabbed.IsAlive()) continue;
            if (grabbed.MyPhysics.Animations.IsPlayingAnyLadderAnimation()) continue;
            grabbed.NetTransform.SnapTo(myPos);
            ushort sid = (ushort)(grabbed.NetTransform.lastSequenceId + 2U);
            var writer = AmongUsClient.Instance.StartRpcImmediately(
                grabbed.NetTransform.NetId, (byte)RpcCalls.SnapTo, SendOption.Reliable);
            NetHelpers.WriteVector2(myPos, writer);
            writer.Write(sid);
            AmongUsClient.Instance.FinishRpcImmediately(writer);
        }
    }

<<<<<<< HEAD
    // ─── 会議関連 ─────────────────────────────────────────────────
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        RocketFlyDummy.DespawnAll();

        if (GrabbedPlayers.Count > 0) launchPending = true;

        foreach (var p in GrabbedPlayers.ToArray())
        {
            PlayerState.GetByPlayerId(p.PlayerId).CanMove = true;
            p.MarkDirtySettings();
<<<<<<< HEAD
            // ★ 会議中はSetRole偽装を解除（ホストはData.IsDead不使用なので不要）
            if (!Player.AmOwner)
                SetAppearsAsImpostorForRocket(p, false);
        }
=======
            if (!Player.AmOwner)
                SetAppearsAsImpostorForRocket(p, false);
        }
        SendSyncRpc();
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    }

    public override void OnStartMeeting()
    {
        launchPending = false;
        RocketFlyDummy.DespawnAll();
        foreach (var p in GrabbedPlayers.ToArray())
        {
            if (!Player.AmOwner)
                SetAppearsAsImpostorForRocket(p, false);
        }
<<<<<<< HEAD
=======
        SendSyncRpc();
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;
<<<<<<< HEAD
        if (!Player.IsAlive()) { ReleaseAll(); SendRpc(); return; }
=======
        if (!Player.IsAlive()) { ReleaseAll(); SendSyncRpc(); return; }
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56

        if (launchPending && GrabbedPlayers.Count > 0)
        {
            var pos = Player.GetTruePosition();
            _ = new LateTask(() =>
            {
                LaunchAll(pos);
                killCDOverride = InitialGrabCooldown;
                Main.AllPlayerKillCooldown[Player.PlayerId] = killCDOverride;
                var afterMeetingState = PlayerState.GetByPlayerId(Player.PlayerId);
                if (afterMeetingState != null) afterMeetingState.Is10secKillButton = false;
                Player.SetKillCooldown(killCDOverride, force: false);
<<<<<<< HEAD
                SendRpc();
=======
                SendSyncRpc();
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
            }, 1.5f, "Rocket.LaunchAfterMeeting", true);
        }
        else
        {
            foreach (var p in GrabbedPlayers.ToArray())
            {
                PlayerState.GetByPlayerId(p.PlayerId).CanMove = false;
                p.MarkDirtySettings();
                if (!Player.AmOwner)
                    SetAppearsAsImpostorForRocket(p, true);
<<<<<<< HEAD
                // ★ ホストは GrabbedPlayerIds に入ったままなのでCheckMurderがブロックされる
            }
=======
            }
            SendSyncRpc();
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
        }
        launchPending = false;

        _ = new LateTask(() =>
        {
            if (!Player.IsAlive()) return;
            AURoleOptions.PhantomCooldown = LaunchCooldown;
            Player.RpcResetAbilityCooldown();
        }, 0.3f, "Rocket.AfterMeeting.CD", true);
    }

<<<<<<< HEAD
    // ─── テキスト ─────────────────────────────────────────────────
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (!Is(seer)) return "";
        if (GrabbedPlayers.Contains(seen)) return "<color=#ff6600>🚀</color>";
        return "";
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";
        if (isForMeeting) return "";
        string size = isForHud ? "" : "<size=60%>";
        string color = RoleInfo.RoleColorCode;
        int count = GrabbedPlayers.Count;
        if (count == 0)
            return $"{size}<color={color}>キルボタン → 掴む | ファントム → 打ち上げ</color>";
        return $"{size}<color={color}>掴み中: {count}人 | ファントムボタン → 打ち上げ！</color>";
    }

    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        if (!Player.IsAlive()) return "";
        int count = GrabbedPlayers.Count;
        if (count == 0) return "";
        return $"<color=#ff6600>({count}人)</color>";
    }

<<<<<<< HEAD
    // ─── RPC ─────────────────────────────────────────────────────
    void SendRpc()
    {
        using var sender = CreateSender();
=======
    void SendSyncRpc()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write((byte)2);
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
        sender.Writer.Write(GrabbedPlayers.Count);
        foreach (var p in GrabbedPlayers)
            sender.Writer.Write(p?.PlayerId ?? byte.MaxValue);
        sender.Writer.Write(launchPending);
        sender.Writer.Write(killCDOverride);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
<<<<<<< HEAD
        int count = reader.ReadInt32();
        // ★ 受信時もGrabbedPlayerIdsを同期
        foreach (var old in GrabbedPlayers) GrabbedPlayerIds.Remove(old.PlayerId);
        GrabbedPlayers.Clear();

        for (int i = 0; i < count; i++)
        {
            var id = reader.ReadByte();
            var pc = PlayerCatch.GetPlayerById(id);
            if (pc != null)
            {
                GrabbedPlayers.Add(pc);
                GrabbedPlayerIds.Add(id);
            }
        }
        launchPending = reader.ReadBoolean();
        killCDOverride = reader.ReadSingle();
=======
        byte rpcType = reader.ReadByte();
        if (rpcType == 0)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            byte targetId = reader.ReadByte();
            var target = PlayerCatch.GetPlayerById(targetId);
            if (target != null && !GrabbedPlayers.Contains(target))
            {
                GrabPlayer(target);
            }
        }
        else if (rpcType == 1)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            ExecuteLaunch();
        }
        else if (rpcType == 2)
        {
            int count = reader.ReadInt32();
            foreach (var old in GrabbedPlayers) GrabbedPlayerIds.Remove(old.PlayerId);
            GrabbedPlayers.Clear();

            for (int i = 0; i < count; i++)
            {
                var id = reader.ReadByte();
                var pc = PlayerCatch.GetPlayerById(id);
                if (pc != null)
                {
                    GrabbedPlayers.Add(pc);
                    GrabbedPlayerIds.Add(id);
                }
            }
            launchPending = reader.ReadBoolean();
            killCDOverride = reader.ReadSingle();
        }
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
    }

    public override string GetAbilityButtonText() =>
        GrabbedPlayers.Count > 0 ? "打ち上げ" : "掴む";

    public override bool OverrideAbilityButton(out string text)
    {
        text = GrabbedPlayers.Count > 0 ? "Rocket_Launch" : "Rocket_Grab";
        return true;
    }
}

<<<<<<< HEAD
// ─── 打ち上げアニメーションダミー ────────────────────────────────────
=======
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
public class RocketFlyDummy : CustomNetObject
{
    private static readonly List<RocketFlyDummy> ActiveDummies = new();

    private readonly Vector2 startPos;
    private readonly float duration;
    private bool isDone = false;

    private const float TotalRiseY = 20f;
    private const float StepInterval = 0.02f;

    private int currentStep = 0;
    private int totalSteps;
    private float yPerStep;
    private int rpcCounter = 0;

    public RocketFlyDummy(PlayerControl source, Vector2 spawnPos, float duration)
    {
        this.startPos = spawnPos;
        this.duration = duration;

        CreateNetObject(spawnPos);

        _ = new LateTask(() =>
        {
            if (PlayerControl == null) return;

            var outfit = source.Data?.DefaultOutfit;
            SetAppearance(
                outfit?.ColorId ?? 0,
                outfit?.SkinId ?? "",
                outfit?.HatId ?? "",
                outfit?.PetId ?? "",
                outfit?.VisorId ?? "");

            SetName("");
            StartRise();
            ActiveDummies.Add(this);
        }, 0.2f, $"RocketFly.Init.{Id}", true);
    }

    private void StartRise()
    {
        totalSteps = Mathf.Max(1, Mathf.RoundToInt(duration / StepInterval));
        yPerStep = TotalRiseY / totalSteps;
        currentStep = 0;
        rpcCounter = 0;
        DoStep();
    }

    private void DoStep()
    {
        if (isDone || PlayerControl == null) return;

        currentStep++;
        Vector2 newPos = startPos + new Vector2(0f, yPerStep * currentStep);

        SnapToPosition(newPos);

        rpcCounter++;
        if (rpcCounter >= 2)
        {
            rpcCounter = 0;
            try
            {
                ushort sid = (ushort)(PlayerControl.NetTransform.lastSequenceId + 1U);
                var writer = AmongUsClient.Instance.StartRpcImmediately(
                    PlayerControl.NetTransform.NetId, (byte)RpcCalls.SnapTo, SendOption.Reliable);
                NetHelpers.WriteVector2(newPos, writer);
                writer.Write(sid);
                AmongUsClient.Instance.FinishRpcImmediately(writer);
            }
            catch { }
        }

        if (currentStep >= totalSteps)
            FinishAndDespawn();
        else
            _ = new LateTask(DoStep, StepInterval, $"RocketFly.Step.{Id}.{currentStep}", true);
    }

    private void FinishAndDespawn()
    {
        if (isDone) return;
        isDone = true;
        ActiveDummies.Remove(this);
        try { Despawn(); } catch { }
    }

    public void RequestDespawn()
    {
        if (isDone) return;
        isDone = true;
        ActiveDummies.Remove(this);
        try { Despawn(); } catch { }
    }

    public override void OnMeeting() => RequestDespawn();

    public static void DespawnAll()
    {
        foreach (var d in ActiveDummies.ToArray())
            d.RequestDespawn();
        ActiveDummies.Clear();
    }
<<<<<<< HEAD
}
*/
=======
}
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
