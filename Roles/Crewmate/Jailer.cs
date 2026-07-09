// ★ CustomRoles.Jailer を追加してください
// ★ CountTypes.Crew でスポーン設定

using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Modules;
using TownOfHost.Patches;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using UnityEngine;
using static TownOfHost.PlayerCatch;
using static TownOfHost.Translator;

namespace TownOfHost.Roles.Crewmate;

public sealed class Jailer : RoleBase, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Jailer),
            player => new Jailer(player),
            CustomRoles.Jailer,
            () => RoleTypes.Engineer,   // タスクモード基底: Engineer
            CustomRoleTypes.Crewmate,
            260300,
            SetupOptionItem,
            "jlr",
            "#4488cc",
            (2, 1),
            true,
            countType: CountTypes.Crew,
            from: From.TownOfHost_Pko
        );

    public Jailer(PlayerControl player)
        : base(RoleInfo, player, () => HasTask.True)
    {
        mode = JailerMode.Task;
        prisonLocationSet = false;
        prisonLocation = Vector2.zero;
        prisonerPlayerId = byte.MaxValue;
        hasPrisoner = false;
        imprisonSecondsLeft = 0f;
        imprisonTurnsLeft = 0;
        prisonObject = null;
        nowcool = OptionKillCooldown.GetFloat();
        LastCooltime = (int)nowcool;
        spawnCooldownStarted = false;
    }

    // ─── オプション ──────────────────────────────────────────────
    static OptionItem OptionKillCooldown;
    static OptionItem OptionImprisonType;
    static OptionItem OptionImprisonTurns;
    static OptionItem OptionImprisonSeconds;
    static OptionItem OptionContinueAfterMeeting;

    enum JailerMode { Task, SetLocation, SelectPrisoner }
    enum ImprisonmentType { Turns, Seconds }

    enum OptionName
    {
        JailerImprisonType,
        JailerImprisonTurns,
        JailerImprisonSeconds,
        JailerContinueAfterMeeting,
    }

    // ─── インスタンス状態 ─────────────────────────────────────────
    JailerMode mode;
    bool prisonLocationSet;
    Vector2 prisonLocation;
    byte prisonerPlayerId;
    bool hasPrisoner;
    float imprisonSecondsLeft;
    int imprisonTurnsLeft;
    PrisonNetObject prisonObject;
    float nowcool;
    int LastCooltime;
    bool spawnCooldownStarted;

    // ─── オプション値ヘルパー ─────────────────────────────────────
    static float KillCooldown => OptionKillCooldown.GetFloat();
    static ImprisonmentType ImpType => (ImprisonmentType)OptionImprisonType.GetValue();
    static int ImprisonTurns => OptionImprisonTurns.GetInt();
    static float ImprisonSeconds => OptionImprisonSeconds.GetFloat();
    static bool ContinueThroughMeeting => OptionContinueAfterMeeting.GetBool();

    static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown,
            new(2.5f, 60f, 2.5f), 25f, false)
            .SetValueFormat(OptionFormat.Seconds);

        OptionImprisonType = StringOptionItem.Create(RoleInfo, 11, OptionName.JailerImprisonType,
            new string[] { "Turns", "Seconds" }, 0, false);

        // Turns 専用
        OptionImprisonTurns = IntegerOptionItem.Create(RoleInfo, 12, OptionName.JailerImprisonTurns,
            new(1, 10, 1), 2, false)
            .SetValueFormat(OptionFormat.Rounds)
            .SetParent(CustomRoleSpawnChances[CustomRoles.Jailer]);

        // Seconds 専用
        OptionImprisonSeconds = FloatOptionItem.Create(RoleInfo, 13, OptionName.JailerImprisonSeconds,
            new(2.5f, 180f, 2.5f), 30f, false)
            .SetValueFormat(OptionFormat.Seconds)
            .SetParent(CustomRoleSpawnChances[CustomRoles.Jailer]);

        OptionContinueAfterMeeting = BooleanOptionItem.Create(RoleInfo, 14,
            OptionName.JailerContinueAfterMeeting, false, false)
            .SetParent(OptionImprisonSeconds);
    }

    // ─── ライフサイクル ───────────────────────────────────────────
    public override void Add()
    {
        nowcool = KillCooldown;
        LastCooltime = (int)nowcool;
        spawnCooldownStarted = false;
        PetActionManager.Register(Player.PlayerId, OnPetAction);
    }

    public override void OnDestroy()
    {
        PetActionManager.Unregister(Player.PlayerId);
        prisonObject?.Despawn();
        prisonObject = null;
    }

    // ─── IUsePhantomButton ───────────────────────────────────────
    bool IUsePhantomButton.IsPhantomRole => mode == JailerMode.SetLocation;
    bool IUsePhantomButton.IsresetAfterKill => false;
    bool IUsePhantomButton.SyncAbilityCooldownWithKillCooldown => false;

    // Phantom ボタン: 牢屋位置を設定
    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = false;
        ResetCooldown = false;
        if (mode != JailerMode.SetLocation || hasPrisoner) return;

        prisonLocation = Player.GetTruePosition();
        prisonLocationSet = true;

        // 仮の牢屋マーカーをスポーン
        prisonObject?.Despawn();
        prisonObject = new PrisonNetObject(prisonLocation, "?");

        // 囚人選択フェーズへ
        SwitchMode(JailerMode.SelectPrisoner);
        SendRpc();
        Utils.SendMessage(GetString("JailerLocationSet"), Player.PlayerId);
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    // ─── ApplyGameOptions ────────────────────────────────────────
    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(false);
        switch (mode)
        {
            case JailerMode.Task:
                AURoleOptions.EngineerCooldown = Mathf.Max(nowcool, 0.1f);
                AURoleOptions.EngineerInVentMaxTime = 0f;
                break;
            case JailerMode.SetLocation:
                AURoleOptions.PhantomCooldown = 0.1f; // 即座に押せる
                break;
            case JailerMode.SelectPrisoner:
                // Impostor desync → キルCDは別途 SetKillCooldown で管理
                break;
        }
    }

    public override bool CanClickUseVentButton => mode == JailerMode.Task;
    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
        => mode == JailerMode.Task && !hasPrisoner;

    public override RoleTypes? AfterMeetingRole
        => mode == JailerMode.SetLocation ? RoleTypes.Phantom : RoleTypes.Engineer;

    // ─── ペット: タスク ⇔ 能力モード切替（Sheriff方式）────────────
    void OnPetAction()
    {
        if (!Player.IsAlive()) return;
        if (hasPrisoner) return; // 拘禁中は切替不可

        if (mode == JailerMode.Task)
        {
            if (nowcool > 0f) return; // CDが切れるまで切替不可
            SwitchMode(JailerMode.SetLocation);
        }
        else
        {
            // 能力モードからキャンセル → タスクモードに戻す
            prisonLocationSet = false;
            prisonObject?.Despawn();
            prisonObject = null;
            SwitchMode(JailerMode.Task);
            // CDは引き継ぐ（ペナルティなし）
        }

        SendRpc();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    void SwitchMode(JailerMode newMode)
    {
        mode = newMode;
        ApplyModeDesync(newMode);
    }

    void ApplyModeDesync(JailerMode newMode)
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;

        // 自分のロールタイプを切り替え
        RoleTypes self = newMode switch
        {
            JailerMode.Task => RoleTypes.Engineer,
            JailerMode.SetLocation => RoleTypes.Phantom,
            JailerMode.SelectPrisoner => RoleTypes.Impostor,
            _ => RoleTypes.Engineer
        };
        Player.RpcSetRoleDesync(self, Player.GetClientId());

        // SelectPrisoner時: インポスターには Scientist に見せてキルボタンを隠す
        if (newMode == JailerMode.SelectPrisoner)
        {
            foreach (var pc in AllAlivePlayerControls)
            {
                if (pc.GetCustomRole().IsImpostor())
                    pc.RpcSetRoleDesync(RoleTypes.Scientist, Player.GetClientId());
            }
        }

        _ = new LateTask(() =>
        {
            if (!Player.IsAlive()) return;
            Player.RpcResetAbilityCooldown(Sync: true);
        }, 0.1f, "Jailer.Desync", true);
    }

    // ─── キルボタン: 囚人選択（SelectPrisoner mode）──────────────
    public override bool OnCheckMurderAsKiller(MurderInfo info)
    {
        if (mode != JailerMode.SelectPrisoner) return true;
        if (!prisonLocationSet || hasPrisoner) return true;

        (_, var target) = info.AttemptTuple;

        prisonerPlayerId = target.PlayerId;
        hasPrisoner = true;

        // 閉じ込め時間セット
        imprisonSecondsLeft = ImprisonSeconds;
        imprisonTurnsLeft = ImprisonTurns;

        // ターゲットを牢屋にワープ
        target.RpcSnapToForced(prisonLocation);

        // 牢屋表示更新
        prisonObject?.UpdateName(target.Data.PlayerName);

        // タスクモードに戻る（拘禁中は守備に回る）
        SwitchMode(JailerMode.Task);
        // ★ 閉じ込め終了までキルCDは一時停止（freedで0にリセット）
        nowcool = KillCooldown;
        LastCooltime = (int)nowcool;

        Utils.SendMessage(
            string.Format(GetString("JailerImprisoned"), target.Data.PlayerName),
            Player.PlayerId);
        Utils.SendMessage(GetString("JailerYouImprisoned"), target.PlayerId);

        SendRpc();
        UtilsNotifyRoles.NotifyRoles();

        return false; // 実際にはキルしない
    }

    // ─── OnFixedUpdate ────────────────────────────────────────────
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;

        if (!spawnCooldownStarted && Player.IsAlive()
            && !GameStates.Intro && GameStates.IsInTask && !GameStates.IsMeeting)
        {
            spawnCooldownStarted = true;
            Player.RpcResetAbilityCooldown(Sync: true);
        }

        // ── 囚人の位置強制 ────────────────────────────────────────
        if (hasPrisoner && GameStates.IsInTask)
        {
            var prisoner = GetPlayerById(prisonerPlayerId);
            if (prisoner == null || !prisoner.IsAlive())
            {
                FreePrisoner(); return;
            }
            // 動こうとしたら即戻す
            if (Vector2.Distance(prisoner.GetTruePosition(), prisonLocation) > 0.25f)
                prisoner.RpcSnapToForced(prisonLocation);
        }

        // ── 秒数モードタイマー ────────────────────────────────────
        if (hasPrisoner && ImpType == ImprisonmentType.Seconds && GameStates.IsInTask)
        {
            if (!GameStates.IsMeeting || ContinueThroughMeeting)
            {
                imprisonSecondsLeft -= Time.fixedDeltaTime;
                if (imprisonSecondsLeft <= 0f) { FreePrisoner(); return; }
            }
        }

        // ── タスクモード CDカウントダウン（Sheriff方式）─────────────
        if (mode == JailerMode.Task && !hasPrisoner && Player.IsAlive() && GameStates.IsInTask)
        {
            if (nowcool > 0) nowcool -= Time.fixedDeltaTime;
            else nowcool = 0;

            int now = (int)nowcool;
            if (now != LastCooltime)
            {
                LastCooltime = now;
                Player.MarkDirtySettings();
                _ = new LateTask(() =>
                {
                    if (Player.IsAlive()) Player.RpcResetAbilityCooldown(Sync: true);
                }, 0.1f, "Jailer.CDSync", true);
                if (player != PlayerControl.LocalPlayer)
                    UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: player);
            }
        }
    }

    // ─── 閉じ込め解除 ─────────────────────────────────────────────
    void FreePrisoner()
    {
        if (!hasPrisoner) return;

        var prisoner = GetPlayerById(prisonerPlayerId);
        if (prisoner != null && prisoner.IsAlive())
            Utils.SendMessage(GetString("JailerFreed"), prisoner.PlayerId);

        prisonObject?.Despawn();
        prisonObject = null;
        hasPrisoner = false;
        prisonerPlayerId = byte.MaxValue;
        prisonLocationSet = false;
        imprisonSecondsLeft = 0f;
        imprisonTurnsLeft = 0;

        // ★ 閉じ込め終了でキルCD即リセット
        nowcool = 0f;
        LastCooltime = 0;
        Player.MarkDirtySettings();
        _ = new LateTask(() =>
        {
            if (Player.IsAlive()) Player.RpcResetAbilityCooldown(Sync: true);
        }, 0.1f, "Jailer.FreeReset", true);

        Utils.SendMessage(GetString("JailerPrisonerFreed"), Player.PlayerId);
        SendRpc();
        UtilsNotifyRoles.NotifyRoles();
    }

    // ─── 会議系 ──────────────────────────────────────────────────
    public override void OnStartMeeting()
    {
        if (!hasPrisoner || ImpType != ImprisonmentType.Turns) return;
        imprisonTurnsLeft--;
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;

        if (hasPrisoner)
        {
            // ターンモード: ターン切れで解放
            if (ImpType == ImprisonmentType.Turns && imprisonTurnsLeft <= 0)
            {
                FreePrisoner();
                return;
            }

            // 会議後に囚人を牢屋に再スナップ
            _ = new LateTask(() =>
            {
                var prisoner = GetPlayerById(prisonerPlayerId);
                if (prisoner != null && prisoner.IsAlive())
                    prisoner.RpcSnapToForced(prisonLocation);
            }, 1.0f, "Jailer.PostMeetingSnap", true);
        }

        // desync 再適用
        _ = new LateTask(() => ApplyModeDesync(mode), 0.5f, "Jailer.AfterMeetDesync", true);
    }

    // ─── 表示 ────────────────────────────────────────────────────
    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        if (!Player.IsAlive()) return "";

        if (hasPrisoner)
        {
            var prisoner = GetPlayerById(prisonerPlayerId);
            string pn = prisoner?.Data?.PlayerName ?? "???";
            string t = ImpType == ImprisonmentType.Seconds
                ? $"{Mathf.CeilToInt(imprisonSecondsLeft)}s"
                : $"{imprisonTurnsLeft}T";
            return $"<color={RoleInfo.RoleColorCode}>🔒{pn}({t})</color>";
        }

        string modeStr = mode switch
        {
            JailerMode.Task => "<color=#f8cd46>[T]</color>",
            JailerMode.SetLocation => "<color=#ff8800>[📍]</color>",
            JailerMode.SelectPrisoner => "<color=#ff4444>[⛓]</color>",
            _ => "?"
        };
        return $"<color={RoleInfo.RoleColorCode}>({LastCooltime})</color>{modeStr}";
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";

        string sz = isForHud ? "" : "<size=60%>";
        string c = RoleInfo.RoleColorCode;

        if (hasPrisoner)
        {
            var prisoner = GetPlayerById(prisonerPlayerId);
            string pn = prisoner?.Data?.PlayerName ?? "???";
            string t = ImpType == ImprisonmentType.Seconds
                ? $"{Mathf.CeilToInt(imprisonSecondsLeft)}秒"
                : $"残り{imprisonTurnsLeft}ターン";
            return $"{sz}<color={c}>🔒 {pn} を拘禁中 ({t})</color>";
        }

        return mode switch
        {
            JailerMode.Task =>
                $"{sz}<color={c}>ペット → 能力モードへ" +
                (nowcool > 0 ? $" (CD: {LastCooltime}s)" : " <color=#00ff88>(準備完了)</color>") + "</color>",
            JailerMode.SetLocation =>
                $"{sz}<color=#ff8800>ファントムボタン → 牢屋の位置を設定\nペット → キャンセル</color>",
            JailerMode.SelectPrisoner =>
                $"{sz}<color=#ff4444>キルボタン → 閉じ込める相手を選択\nペット → キャンセル</color>",
            _ => ""
        };
    }

    // 囚人側のロワーテキスト（自分が囚われていることを表示）
    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (!hasPrisoner) return "";
        if (seen.PlayerId != prisonerPlayerId || seer.PlayerId != prisonerPlayerId) return "";
        return "<color=#4488cc>🔒 拘禁中 🔒</color>";
    }

    // ─── RPC ─────────────────────────────────────────────────────
    void SendRpc()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write((byte)mode);
        sender.Writer.Write(hasPrisoner);
        sender.Writer.Write(prisonerPlayerId);
        sender.Writer.Write(prisonLocationSet);
        sender.Writer.Write(prisonLocation.x);
        sender.Writer.Write(prisonLocation.y);
        sender.Writer.Write(imprisonSecondsLeft);
        sender.Writer.Write(imprisonTurnsLeft);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        mode = (JailerMode)reader.ReadByte();
        hasPrisoner = reader.ReadBoolean();
        prisonerPlayerId = reader.ReadByte();
        prisonLocationSet = reader.ReadBoolean();
        prisonLocation = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        imprisonSecondsLeft = reader.ReadSingle();
        imprisonTurnsLeft = reader.ReadInt32();
    }
}

// ══════════════════════════════════════════════════════════════
//  牢屋 CNO（EHR ■スタイルで表現）
//  BeginnerImpostor 方式: RpcSetColor + RawSetColor + SetName
// ══════════════════════════════════════════════════════════════
public sealed class PrisonNetObject : CustomNetObject
{
    readonly Vector2 _pos;
    string _prisonerName;

    public PrisonNetObject(Vector2 position, string prisonerName)
    {
        _pos = position;
        _prisonerName = prisonerName;
        CreateNetObject(position);
    }

    protected override void OnCreated()
    {
        if (PlayerControl == null) return;

        var hostPC = PlayerControl.LocalPlayer;
        byte hColor = (byte)(hostPC?.Data?.DefaultOutfit.ColorId ?? 0);

        // ★ BeginnerImpostor 方式: RpcSetColor + 即時 RawSetColor
        PlayerControl.RpcSetColor(6); // Black
        if (hostPC != null) hostPC.RpcSetColor(hColor);
        PlayerControl.RawSetColor(6);

        // ボディを透明化
        try { PlayerControl.cosmetics.currentBodySprite.BodySprite.color = Color.clear; } catch { }
        PlayerControl.cosmetics.colorBlindText.color = Color.clear;

        SetName(BuildPrisonLabel(_prisonerName));
        SnapToPosition(_pos);

        var capturedPC = PlayerControl;
        _ = new LateTask(() =>
        {
            if (capturedPC != null) capturedPC.RawSetColor(6);
        }, 0.15f, "PrisonCNO.Color", true);
    }

    // 囚人名を更新（囚人確定時に呼ぶ）
    public void UpdateName(string prisonerName)
    {
        _prisonerName = prisonerName;
        SetName(BuildPrisonLabel(prisonerName));
    }

    // ★ EHR スタイルの牢屋ラベル
    static string BuildPrisonLabel(string prisoner)
    {
        const string bar = "<color=#777777>■</color>";
        const string wall = "<color=#777777>│</color>";
        string top = $"{bar}{bar}{bar}{bar}{bar}";
        string mid = $"{wall}<color=#ffffff>{prisoner}</color>{wall}";
        string bottom = $"{bar}{bar}{bar}{bar}{bar}";
        return $"<line-height=100%><size=130%>{top}</size>\n" +
               $"<size=100%><color=#ffdd88>🔒</color>{mid}<color=#ffdd88>🔒</color></size>\n" +
               $"<size=130%>{bottom}</size></line-height>";
    }

    // 牢屋は会議中も維持（デスポーンしない）
    public override void OnMeeting() { }
}