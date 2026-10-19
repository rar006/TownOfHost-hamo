using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

public sealed class Catalyst : RoleBase, IKiller, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Catalyst),
            player => new Catalyst(player),
            CustomRoles.Catalyst,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            93400,
            SetupOptionItem,
            "cat",
            "#E60049",
            (3, 6),
            true,
            tab: TabGroup.Combinations,
            introSound: () => GetIntroSound(RoleTypes.Phantom),
            countType: CountTypes.Crew,
            assignInfo: new RoleAssignInfo(CustomRoles.Catalyst, CustomRoleTypes.Neutral)
            {
                AssignCountRule = new(1, 1, 1),
                AssignUnitRoles = [CustomRoles.Catalyst, CustomRoles.Conductor]
            },
            combination: CombinationRoles.CatalystandConductor,
            isNewRole: true
        );

    static OptionItem OptionResonanceCooldown;
    static OptionItem OptionCatalystCanKill;
    static OptionItem OptionCatalystKillCooldown;
    internal static OptionItem OptionCatalystToOpportunist;
    internal static OptionItem OptionDissonanceCooldown;
    internal static OptionItem OptionDissonanceDuration;
    internal static OptionItem OptionConductorCanKill;
    internal static OptionItem OptionConductorKillCooldown;
    internal static OptionItem OptionConductorToOpportunist;

    enum OptionName
    {
        CatalystResonanceCooldown, CatalystCanKill, CatalystToOpportunist,
        ConductorDissonanceCooldown, ConductorDissonanceDuration, ConductorCanKill, ConductorToOpportunist,
    }

    static void SetupOptionItem()
    {
        // カタリストの設定
        OptionResonanceCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.CatalystResonanceCooldown, new(2.5f, 100f, 2.5f), 30f, false).SetValueFormat(OptionFormat.Seconds);
        OptionCatalystCanKill = BooleanOptionItem.Create(RoleInfo, 11, OptionName.CatalystCanKill, false, false);
        OptionCatalystKillCooldown = FloatOptionItem.Create(RoleInfo, 12, GeneralOption.KillCooldown, new(2.5f, 100f, 2.5f), 30f, false, OptionCatalystCanKill).SetValueFormat(OptionFormat.Seconds);
        OptionCatalystToOpportunist = BooleanOptionItem.Create(RoleInfo, 13, OptionName.CatalystToOpportunist, false, false);
        // コンダクターの設定(コンビ役職なので、ここにまとめて置く)
        OptionDissonanceCooldown = FloatOptionItem.Create(RoleInfo, 14, OptionName.ConductorDissonanceCooldown, new(2.5f, 100f, 2.5f), 30f, false).SetValueFormat(OptionFormat.Seconds);
        OptionDissonanceDuration = FloatOptionItem.Create(RoleInfo, 15, OptionName.ConductorDissonanceDuration, new(2.5f, 60f, 2.5f), 10f, false).SetValueFormat(OptionFormat.Seconds);
        OptionConductorCanKill = BooleanOptionItem.Create(RoleInfo, 16, OptionName.ConductorCanKill, false, false);
        OptionConductorKillCooldown = FloatOptionItem.Create(RoleInfo, 17, GeneralOption.KillCooldown, new(2.5f, 100f, 2.5f), 30f, false, OptionConductorCanKill).SetValueFormat(OptionFormat.Seconds);
        OptionConductorToOpportunist = BooleanOptionItem.Create(RoleInfo, 18, OptionName.ConductorToOpportunist, false, false);
    }

    public Catalyst(PlayerControl player) : base(RoleInfo, player, () => HasTask.False) { }

    public static Catalyst Instance => CustomRoleManager.AllActiveRoles.Values.OfType<Catalyst>().FirstOrDefault();

    float disabledUntil = -1f;
    public bool IsDisabled => Time.time < disabledUntil;

    // キルボタンとファントムボタンのクールは共通(ファントムワンクリの仕組み)。キルボタンが使える時はキルクール
    public float CalculateKillCooldown() => OptionCatalystCanKill.GetBool() ? OptionCatalystKillCooldown.GetFloat() : OptionResonanceCooldown.GetFloat();
    public bool CanUseKillButton() => Player.IsAlive() && OptionCatalystCanKill.GetBool();
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;

    public void Disable(float seconds)
    {
        disabledUntil = Time.time + seconds;
        SendRPC();
        Utils.SendMessage(GetString("CatalystDisabled"), Player.PlayerId);
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    bool CanSee(PlayerControl other)
    {
        var a = Player.GetTruePosition();
        var b = other.GetTruePosition();
        var dist = Vector2.Distance(a, b);
        float radius;
        try { radius = ShipStatus.Instance.CalculateLightRadius(Player.Data); }
        catch { radius = 3f; }
        if (dist > radius) return false;
        return !PhysicsHelpers.AnyNonTriggersBetween(a, (b - a).normalized, dist, Constants.ShadowMask);
    }

    // ===== 共鳴(ファントムボタン) =====
    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        ResetCooldown = false;
        if (!Player.IsAlive()) return;
        var conductor = Conductor.Instance?.Player;
        if (conductor == null || !conductor.IsAlive()) return;
        if (IsDisabled) { Utils.SendMessage(GetString("CatalystDisabled"), Player.PlayerId); return; }
        if (CanSee(conductor)) { Utils.SendMessage(GetString("CatalystCantSeeConductor"), Player.PlayerId); return; }

        var range = NormalGameOptionsV12.KillDistances[Mathf.Clamp(Main.NormalOptions.KillDistance, 0, 2)];
        var center = conductor.GetTruePosition();
        var target = PlayerCatch.AllAlivePlayerControls
            .Where(p => p.PlayerId != Player.PlayerId && p.PlayerId != conductor.PlayerId && !p.inVent)
            .Select(p => (p, d: Vector2.Distance(center, p.GetTruePosition())))
            .Where(x => x.d <= range)
            .OrderBy(x => x.d)
            .Select(x => x.p)
            .FirstOrDefault();
        if (target == null) { Utils.SendMessage(GetString("CatalystNoTarget"), Player.PlayerId); return; }

        var pos = target.GetTruePosition();
        Logger.Info($"カタリスト: 共鳴で {target.Data?.GetLogPlayerName()} をキル", "Catalyst");
        CustomRoleManager.OnCheckMurder(Player, target, target, target, true, true, 10, CustomDeathReason.Kill);
        // キルワープ(キルが防がれた時はワープしない)
        _ = new LateTask(() =>
        {
            if (Player.IsAlive() && !target.IsAlive()) Player.RpcSnapToForced(pos);
        }, 0.2f, "Catalyst.Warp", true);
        AdjustKillCooldown = false;
        ResetCooldown = true;
    }

    public override string GetAbilityButtonText() => GetString("CatalystAbilityButton");

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || seer.PlayerId != Player.PlayerId || seen.PlayerId != Player.PlayerId || !IsDisabled) return "";
        return Utils.ColorString(ModColors.NeutralGray, GetString("CatalystDisabledLower"));
    }

    public override void OnFixedUpdate(PlayerControl player) => CatalystPair.CheckPartnerDead(Player, Conductor.Instance?.Player, OptionCatalystToOpportunist.GetBool());

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(IsDisabled ? disabledUntil - Time.time : -1f);
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        var left = reader.ReadSingle();
        disabledUntil = left > 0f ? Time.time + left : -1f;
    }
}

public sealed class Conductor : RoleBase, IKiller, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Conductor),
            player => new Conductor(player),
            CustomRoles.Conductor,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            93450,
            null,
            "cdt",
            "#00E5FF",
            (3, 7),
            true,
            tab: TabGroup.Combinations,
            introSound: () => GetIntroSound(RoleTypes.Phantom),
            countType: CountTypes.Crew,
            combination: CombinationRoles.CatalystandConductor,
            isNewRole: true
        );

    public Conductor(PlayerControl player) : base(RoleInfo, player, () => HasTask.False) { }

    public static Conductor Instance => CustomRoleManager.AllActiveRoles.Values.OfType<Conductor>().FirstOrDefault();

    public float CalculateKillCooldown() => Catalyst.OptionConductorCanKill.GetBool() ? Catalyst.OptionConductorKillCooldown.GetFloat() : Catalyst.OptionDissonanceCooldown.GetFloat();
    public bool CanUseKillButton() => Player.IsAlive() && Catalyst.OptionConductorCanKill.GetBool();
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;

    // ===== 不協和音(ファントムボタン) =====
    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        ResetCooldown = false;
        if (!Player.IsAlive()) return;
        var catalyst = Catalyst.Instance;
        if (catalyst?.Player == null || !catalyst.Player.IsAlive()) return;
        catalyst.Disable(Catalyst.OptionDissonanceDuration.GetFloat());
        Utils.SendMessage(string.Format(GetString("ConductorUsed"), Catalyst.OptionDissonanceDuration.GetFloat()), Player.PlayerId);
        AdjustKillCooldown = false;
        ResetCooldown = true;
    }

    public override string GetAbilityButtonText() => GetString("ConductorAbilityButton");

    public override void OnFixedUpdate(PlayerControl player) => CatalystPair.CheckPartnerDead(Player, Catalyst.Instance?.Player, Catalyst.OptionConductorToOpportunist.GetBool());
}

public static class CatalystPair
{
    static bool IsMember(PlayerControl p) => p != null && (p.Is(CustomRoles.Catalyst) || p.Is(CustomRoles.Conductor));

    public static void CheckPartnerDead(PlayerControl me, PlayerControl partner, bool toOpportunist)
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsInTask || me == null || !me.IsAlive()) return;
        if (partner != null && partner.IsAlive() && partner.Data?.Disconnected != true) return;
        if (toOpportunist)
        {
            Logger.Info($"{me.Data?.GetLogPlayerName()}: 相方が死亡したのでオポチュニストになる", "CatalystPair");
            me.RpcSetCustomRole(CustomRoles.Opportunist, true, true);
            return;
        }
        Logger.Info($"{me.Data?.GetLogPlayerName()}: 相方が死亡したので後追い", "CatalystPair");
        var state = PlayerState.GetByPlayerId(me.PlayerId);
        if (state != null) state.DeathReason = CustomDeathReason.FollowingSuicide;
        me.RpcMurderPlayerV2(me);
    }

    public static bool CheckInstantWin(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        var alive = PlayerCatch.AllAlivePlayerControls.ToList();
        var team = alive.Where(IsMember).ToList();
        if (team.Count == 0) return false;
        var others = alive.Where(p => !IsMember(p)).ToList();
        if (others.Count > team.Count) return false;
        if (others.Any(p => p.GetCountTypes() is not CountTypes.Crew || (p.GetRoleClass() is IKiller k && k.CanUseKillButton()))) return false;
        if (!CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Catalyst, team[0].PlayerId)) return false;
        foreach (var p in PlayerCatch.AllPlayerControls.Where(IsMember))
        {
            CustomWinnerHolder.WinnerIds.Add(p.PlayerId);
            CustomWinnerHolder.NeutralWinnerIds.Add(p.PlayerId);
        }
        return true;
    }
}
