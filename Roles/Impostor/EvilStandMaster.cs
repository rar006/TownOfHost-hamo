<<<<<<< HEAD
/*using System.Linq;
=======
using System.Collections.Generic;
using System.Linq;
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using static TownOfHost.PlayerCatch;
using static TownOfHost.Translator;
<<<<<<< HEAD

namespace TownOfHost.Roles.Impostor;

public sealed class StandMaster : RoleBase, IImpostor, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(StandMaster),
            player => new StandMaster(player),
            CustomRoles.StandMaster,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            25100,
            SetUpOptionItem,
            "sm",
            OptionSort: (3, 15)
        );

    public StandMaster(PlayerControl player)
        : base(RoleInfo, player)
    {
        PhantomCooldown = OptionPhantomCooldown.GetFloat();
        KillCooldownReduction = OptionKillCooldownReduction.GetFloat();

        standId = byte.MaxValue;
        standOriginPos = Vector2.zero;
        isStandActive = false;
        standWasAlive = false;
        lastAliveCount = 15;
        lastStandKillTimer = 0f;
    }

    static OptionItem OptionPhantomCooldown;
    static float PhantomCooldown;
    static OptionItem OptionKillCooldownReduction;
    static float KillCooldownReduction;

    byte standId;
    Vector2 standOriginPos;
    bool isStandActive;
    bool standWasAlive;
    int lastAliveCount;
    float lastStandKillTimer;

    enum OptionName
    {
        StandMasterPhantomCooldown,
        StandMasterKillCooldownReduction,
    }

    static void SetUpOptionItem()
    {
        OptionPhantomCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.StandMasterPhantomCooldown, OptionBaseCoolTime, 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionKillCooldownReduction = FloatOptionItem.Create(RoleInfo, 11, OptionName.StandMasterKillCooldownReduction, new(0f, 60f, 0.5f), 5f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    public float CalculateKillCooldown() => 30f;
    public bool CanUseSabotageButton() => true;
    public bool CanUseImpostorVentButton() => true;
    bool IUsePhantomButton.IsresetAfterKill => false;
    bool IUsePhantomButton.IsPhantomRole => true;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = PhantomCooldown;
    }

    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = false;
        ResetCooldown = false;

        if (!Player.IsAlive()) return;
        if (isStandActive) return;

        var candidates = new System.Collections.Generic.List<PlayerControl>();
        foreach (var pc in AllAlivePlayerControls)
        {
            if (pc.PlayerId == Player.PlayerId) continue;
            if (!pc.GetCustomRole().IsImpostor()) continue;
            if (pc.MyPhysics.Animations.IsPlayingAnyLadderAnimation()) continue;
            if ((MapNames)Main.NormalOptions.MapId == MapNames.Airship
                && Vector2.Distance(pc.GetTruePosition(), new Vector2(7.76f, 8.56f)) <= 1.9f) continue;
            candidates.Add(pc);
        }

        if (candidates.Count == 0) return;

        var rand = IRandom.Instance;
        var stand = candidates[rand.Next(candidates.Count)];

        standId = stand.PlayerId;
        standOriginPos = stand.GetTruePosition();
        isStandActive = true;
        standWasAlive = true;
        lastAliveCount = AllAlivePlayerControls.Count();

        var warpPos = Player.GetTruePosition();
        warpPos.y += 0.47f;
        stand.NetTransform.RpcSnapTo(warpPos);

        float currentTimer = stand.killTimer;
        float newCooldown = Mathf.Max(0f, currentTimer - KillCooldownReduction);
        stand.killTimer = newCooldown;
        lastStandKillTimer = newCooldown;

        AURoleOptions.PhantomCooldown = PhantomCooldown;
        Player.RpcResetAbilityCooldown();

        SendRpcToggle(standId, standOriginPos, newCooldown);

        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true);
        Utils.SendMessage(GetString("StandMasterActivated"), Player.PlayerId);
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;

        int currentAliveCount = AllAlivePlayerControls.Count();
        bool someoneDied = currentAliveCount < lastAliveCount;
        lastAliveCount = currentAliveCount;

        if (!isStandActive) return;

        var stand = GetPlayerById(standId);
        if (stand == null)
        {
            ResetStand(returnToOrigin: false);
            return;
        }

        bool nowDead = PlayerState.GetByPlayerId(standId)?.IsDead ?? true;

        if (standWasAlive && nowDead)
        {
            stand.NetTransform.RpcSnapTo(standOriginPos);
            ResetStand(returnToOrigin: false);
            return;
        }

        standWasAlive = !nowDead;

        if (!nowDead)
        {
            float currentTimer = stand.killTimer;

            if (someoneDied || currentTimer > lastStandKillTimer + 2f)
            {
                ResetStand(returnToOrigin: true);
                return;
            }
            lastStandKillTimer = currentTimer;
        }
    }

    void ResetStand(bool returnToOrigin)
    {
        if (!isStandActive) return;

        if (returnToOrigin)
        {
            var stand = GetPlayerById(standId);
            if (stand != null && stand.IsAlive())
                stand.NetTransform.RpcSnapTo(standOriginPos);
        }

        isStandActive = false;
        standWasAlive = false;
        standId = byte.MaxValue;
        standOriginPos = Vector2.zero;

        SendRpcReturn();
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true);
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        if (isStandActive)
            ResetStand(returnToOrigin: true);
    }

    public override void OnStartMeeting()
    {
        if (isStandActive)
            ResetStand(returnToOrigin: true);
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!Player.IsAlive()) return;
        AURoleOptions.PhantomCooldown = PhantomCooldown;
        Player.RpcResetAbilityCooldown();
    }

    void SendRpcToggle(byte sId, Vector2 origin, float newTimer)
    {
        using var sender = CreateSender();
        sender.Writer.Write((byte)1);
        sender.Writer.Write(sId);
        sender.Writer.Write(origin.x);
        sender.Writer.Write(origin.y);
        sender.Writer.Write(newTimer);
    }

    void SendRpcReturn()
    {
        using var sender = CreateSender();
        sender.Writer.Write((byte)2);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        byte action = reader.ReadByte();
        if (action == 1)
        {
            standId = reader.ReadByte();
            float x = reader.ReadSingle();
            float y = reader.ReadSingle();
            standOriginPos = new Vector2(x, y);
            float newTimer = reader.ReadSingle();

            isStandActive = true;
            standWasAlive = true;
            lastAliveCount = AllAlivePlayerControls.Count();

            var s = GetPlayerById(standId);
            if (s != null) s.killTimer = newTimer;
        }
        else if (action == 2)
        {
            isStandActive = false;
            standWasAlive = false;
            standId = byte.MaxValue;
            standOriginPos = Vector2.zero;
        }
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seen.PlayerId != seer.PlayerId || isForMeeting || !Player.IsAlive()) return "";
        if (isStandActive)
        {
            var stand = GetPlayerById(standId);
            var name = stand != null ? stand.Data.PlayerName : "?";
            return $"{(isForHud ? "" : "<size=60%>")}<color=#cc0000>スタンド発動中: {name}</color>";
        }
        return $"{(isForHud ? "" : "<size=60%>")}<color=#cc0000>ファントムボタン → スタンド召喚</color>";
    }

    public override string GetAbilityButtonText() => GetString("StandMasterAbilityButtonText");
    public override bool OverrideAbilityButton(out string text)
    {
        text = "StandMaster_Ability";
        return true;
    }
}*/
=======
using static TownOfHost.Utils;

namespace TownOfHost.Roles.Impostor;

public sealed class EvilStandMaster : RoleBase, IImpostor, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(EvilStandMaster),
            player => new EvilStandMaster(player),
            CustomRoles.EvilStandMaster,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            327000,
            SetupOptionItem,
            "esm",
            OptionSort: (3, 11),
            from: From.TownOfHost_Pko
        );

    public EvilStandMaster(PlayerControl player) : base(RoleInfo, player) { }

    static OptionItem OptionWarpCooldown;
    static OptionItem OptionWarpStayDuration;
    static OptionItem OptionReduceTeammateKillCD;
    static OptionItem OptionTeammateKillCDReduce;
    static OptionItem OptionReduceOwnKillCD;
    static OptionItem OptionOwnKillCDReduce;

    static float WarpCooldown;
    static float WarpStayDuration;
    static bool ReduceTeammateKillCD;
    static float TeammateKillCDReduce;
    static bool ReduceOwnKillCD;
    static float OwnKillCDReduce;

    enum OptionName
    {
        EvilStandMasterWarpCooldown,
        EvilStandMasterWarpStayDuration,
        EvilStandMasterReduceTeammateKillCD,
        EvilStandMasterTeammateKillCDReduce,
        EvilStandMasterReduceOwnKillCD,
        EvilStandMasterOwnKillCDReduce,
    }

    static void SetupOptionItem()
    {
        OptionWarpCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.EvilStandMasterWarpCooldown,
            new(1f, 180f, 1f), 30f, false).SetValueFormat(OptionFormat.Seconds);

        OptionWarpStayDuration = FloatOptionItem.Create(RoleInfo, 11, OptionName.EvilStandMasterWarpStayDuration,
            new(0f, 30f, 0.5f), 5f, false).SetValueFormat(OptionFormat.Seconds);

        OptionReduceTeammateKillCD = BooleanOptionItem.Create(RoleInfo, 12,
            OptionName.EvilStandMasterReduceTeammateKillCD, true, false);
        OptionTeammateKillCDReduce = FloatOptionItem.Create(RoleInfo, 13,
            OptionName.EvilStandMasterTeammateKillCDReduce,
            new(0f, 60f, 0.5f), 10f, false, OptionReduceTeammateKillCD).SetValueFormat(OptionFormat.Seconds);

        OptionReduceOwnKillCD = BooleanOptionItem.Create(RoleInfo, 14,
            OptionName.EvilStandMasterReduceOwnKillCD, true, false);
        OptionOwnKillCDReduce = FloatOptionItem.Create(RoleInfo, 15,
            OptionName.EvilStandMasterOwnKillCDReduce,
            new(0f, 60f, 0.5f), 5f, false, OptionReduceOwnKillCD).SetValueFormat(OptionFormat.Seconds);
    }

    public override void Add()
    {
        WarpCooldown = OptionWarpCooldown.GetFloat();
        WarpStayDuration = OptionWarpStayDuration.GetFloat();
        ReduceTeammateKillCD = OptionReduceTeammateKillCD.GetBool();
        TeammateKillCDReduce = OptionTeammateKillCDReduce.GetFloat();
        ReduceOwnKillCD = OptionReduceOwnKillCD.GetBool();
        OwnKillCDReduce = OptionOwnKillCDReduce.GetFloat();
    }

    public float CalculateKillCooldown() => 30f;
    public bool CanUseKillButton() => Player.IsAlive();
    public bool CanUseSabotageButton() => true;
    public bool CanUseImpostorVentButton() => true;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.PhantomCooldown = WarpCooldown;
    }

    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = false;
        ResetCooldown = false;
        if (!Player.IsAlive()) return;

        var candidates = GetWarpCandidates();

        if (candidates.Count == 0)
        {
            if (ReduceOwnKillCD && OwnKillCDReduce > 0f)
            {
                float newCd = Mathf.Max(0.1f, Player.killTimer - OwnKillCDReduce);
                Player.SetKillCooldown(newCd);
                Logger.Info($"[EvilStandMaster] ワープ不可→自分のキルCD {OwnKillCDReduce}秒短縮", "EvilStandMaster");
            }
            Utils.SendMessage(GetString("EvilStandMasterNoTarget"), Player.PlayerId);
            return;
        }

        var target = candidates[IRandom.Instance.Next(candidates.Count)];
        var pos = Player.GetTruePosition();

        target.RpcSnapToForced(pos);
        Logger.Info($"[EvilStandMaster] {target.Data?.GetLogPlayerName()} を {pos} にワープ", "EvilStandMaster");

        if (WarpStayDuration > 0f)
        {
            var origSpeed = Main.AllPlayerSpeed.TryGetValue(target.PlayerId, out var s)
                ? s : Main.NormalOptions.PlayerSpeedMod;

            Main.AllPlayerSpeed[target.PlayerId] = Main.MinSpeed;
            target.MarkDirtySettings();

            _ = new LateTask(() =>
            {
                if (!target.IsAlive()) return;
                Main.AllPlayerSpeed[target.PlayerId] = origSpeed;
                target.MarkDirtySettings();
            }, WarpStayDuration, $"EvilStandMaster.Unfreeze.{target.PlayerId}", true);
        }

        if (ReduceTeammateKillCD && TeammateKillCDReduce > 0f)
        {
            float newCd = Mathf.Max(0.1f, target.killTimer - TeammateKillCDReduce);
            target.SetKillCooldown(newCd);
            Logger.Info($"[EvilStandMaster] {target.Data?.GetLogPlayerName()} のキルCD {TeammateKillCDReduce}秒短縮", "EvilStandMaster");
        }

        Utils.SendMessage(
            string.Format(GetString("EvilStandMasterWarped"), target.Data?.PlayerName ?? "???"),
            Player.PlayerId);
        UtilsNotifyRoles.NotifyRoles();
    }

    private List<PlayerControl> GetWarpCandidates()
    {
        return AllAlivePlayerControls
            .Where(pc =>
                pc.PlayerId != Player.PlayerId &&
                pc.GetCustomRole().IsImpostor() &&
                !pc.inVent &&
                !pc.inMovingPlat &&
                !pc.walkingToVent &&
                !pc.onLadder &&
                !pc.MyPhysics.Animations.IsPlayingEnterVentAnimation() &&
                !pc.MyPhysics.Animations.IsPlayingAnyLadderAnimation())
            .ToList();
    }

    bool IUsePhantomButton.IsresetAfterKill => false;
    bool IUsePhantomButton.UseOneclickButton => true;

    public override bool OverrideAbilityButton(out string text)
    {
        text = "EvilStandMaster_Warp";
        return true;
    }

    public override string GetAbilityButtonText() => GetString("EvilStandMasterButtonText");

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive() || isForMeeting) return "";

        var count = GetWarpCandidates().Count;
        string size = isForHud ? "" : "<size=60%>";
        string color = RoleInfo.RoleColorCode;

        return count > 0
            ? $"{size}<color={color}>ワープ対象: {count}人</color>"
            : $"{size}<color=#888888>ワープ対象なし（キルCD短縮待機中）</color>";
    }
}
>>>>>>> 980a20702729bba1cb2fbe62af4d17929491dd56
