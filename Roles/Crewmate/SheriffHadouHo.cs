/*using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Modules;
using TownOfHost.Patches;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using UnityEngine;

namespace TownOfHost.Roles.Crewmate;

public sealed class SheriffHadouHo : RoleBase, IUsePhantomButton
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(SheriffHadouHo),
            player => new SheriffHadouHo(player),
            CustomRoles.SheriffHadouHo,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Crewmate,
            160200,
            SetupOptionItem,
            "shh",
            "#f8cd46",
            (2, 0),
            true,
            countType: CountTypes.Crew
        );

    public SheriffHadouHo(PlayerControl player)
        : base(RoleInfo, player, () => HasTask.False)
    {
        Cooldown = OptionCooldown.GetFloat();
        ChargeTime = OptionChargeTime.GetFloat();
        ShotLimit = OptionShotLimit.GetInt();
        SelfDestructOnMiss = OptionSelfDestructOnMiss.GetBool();
        BeamColorModeValue = OptionBeamColorMode.GetValue();

        IsCharging = false;
        chargeTimer = 0f;
        PlayerSpeed = 0f;
        ShowBeamMark = false;
        HasHitEvil = false;
        IsDead = false;
        IsFiring = false;
        _prevCharging = false;
        _prevBeamMark = false;
        BeamFacingLeft = false;
        PlayerColor = 0;

        beamMode = false;
        nowcool = Cooldown;
        LastCooltime = (int)Cooldown;

        CustomRoleManager.LowerOthers.Add(GetLowerTextOthers);
    }

    static OptionItem OptionCooldown;
    static float Cooldown;
    static OptionItem OptionChargeTime;
    static float ChargeTime;
    static OptionItem OptionShotLimit;
    static OptionItem OptionSelfDestructOnMiss;
    static bool SelfDestructOnMiss;
    static OptionItem OptionBeamColorMode;
    static int BeamColorModeValue;

    int ShotLimit;
    public bool IsCharging;
    float chargeTimer;
    float PlayerSpeed;
    public bool ShowBeamMark;
    bool HasHitEvil;
    bool IsDead;
    bool IsFiring;
    bool spawnCooldownStarted;
    bool _prevCharging;
    bool _prevBeamMark;
    bool BeamFacingLeft;
    int PlayerColor;

    // ★ モード管理
    bool beamMode;
    float nowcool;
    int LastCooltime;

    enum BeamColorMode { Cyan, Yellow }

    enum OptionName
    {
        SheriffHadouHoChargeTime,
        SheriffHadouHoShotLimit,
        SheriffHadouHoSelfDestruct,
        SheriffHadouHoBeamColorMode,
    }

    static void SetupOptionItem()
    {
        OptionCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.Cooldown, OptionBaseCoolTime, 30f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionChargeTime = FloatOptionItem.Create(RoleInfo, 11, OptionName.SheriffHadouHoChargeTime,
            new(0.5f, 10f, 0.5f), 3f, false).SetValueFormat(OptionFormat.Seconds);
        OptionShotLimit = IntegerOptionItem.Create(RoleInfo, 12, OptionName.SheriffHadouHoShotLimit,
            new(1, 15, 1), 3, false).SetValueFormat(OptionFormat.Times);
        OptionSelfDestructOnMiss = BooleanOptionItem.Create(RoleInfo, 13, OptionName.SheriffHadouHoSelfDestruct, false, false);
        OptionBeamColorMode = StringOptionItem.Create(RoleInfo, 14, OptionName.SheriffHadouHoBeamColorMode,
            new string[] { "Cyan", "Yellow" }, 1, false);
    }

    public override void Add()
    {
        PlayerSpeed = Main.AllPlayerSpeed[Player.PlayerId];
        PlayerColor = Player.Data.DefaultOutfit.ColorId;
        BeamColorModeValue = OptionBeamColorMode.GetValue();
        spawnCooldownStarted = false;
        beamMode = false;
        nowcool = Cooldown;
        LastCooltime = (int)Cooldown;
        PetActionManager.Register(Player.PlayerId, OnPetUsed);
    }

    public override void OnDestroy()
    {
        PetActionManager.Unregister(Player.PlayerId);
        CustomRoleManager.LowerOthers.Remove(GetLowerTextOthers);
    }

    // ★ タスクモード: ベントCDにクールタイム表示
    // ★ 波動砲モード: ファントムボタンでビーム
    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(false);
        if (!beamMode)
        {
            AURoleOptions.EngineerCooldown = Mathf.Max(nowcool, 0.1f);
            AURoleOptions.EngineerInVentMaxTime = 0f;
        }
        else
        {
            AURoleOptions.PhantomCooldown = Cooldown;
        }
    }

    public override bool CanClickUseVentButton => !beamMode;
    public override bool OnEnterVent(PlayerPhysics physics, int ventId)
    {
        if (IsCharging || ShowBeamMark) return false;
        return !beamMode;
    }

    // ★ ファントムボタンは波動砲モード時のみ有効
    bool IUsePhantomButton.IsPhantomRole => beamMode && !IsFiring;
    bool IUsePhantomButton.IsresetAfterKill => false;
    public override bool CanUseAbilityButton() => beamMode && !IsFiring && ShotLimit > 0;

    // ★ AfterMeetingRole: 波動砲モードはPhantom、タスクモードはEngineer
    public override RoleTypes? AfterMeetingRole
        => (beamMode && ShotLimit > 0) ? RoleTypes.Phantom : RoleTypes.Engineer;

    // ★ ペット撫で → モード切り替え
    void OnPetUsed()
    {
        if (!Player.IsAlive()) return;
        if (ShotLimit <= 0) return;
        if (IsFiring) return;

        beamMode = !beamMode;
        ApplyModeDesync(beamMode);
        SendRpc();
        UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: Player);
    }

    // ★ シェリフと同じデシンク：波動砲モード時はインポスターからScientistに見える
    void ApplyModeDesync(bool toBeamMode)
    {
        if (!Player.IsAlive()) return;
        foreach (var pc in PlayerCatch.AllAlivePlayerControls)
        {
            var role = pc.GetCustomRole();
            if (role.IsImpostor())
                pc.RpcSetRoleDesync(
                    toBeamMode ? RoleTypes.Scientist : role.GetRoleTypes(),
                    Player.GetClientId());
            if (Is(pc))
                pc.RpcSetRoleDesync(
                    toBeamMode ? RoleTypes.Phantom : RoleTypes.Engineer,
                    Player.GetClientId());
        }

        if (!toBeamMode)
        {
            Player.MarkDirtySettings();
            _ = new LateTask(() =>
            {
                if (Player.IsAlive() && !beamMode)
                    Player.RpcResetAbilityCooldown(Sync: true);
            }, 0.1f, "SheriffHadouHo.EngineerReset", true);
        }
        else
        {
            _ = new LateTask(() =>
            {
                if (Player.IsAlive() && beamMode)
                {
                    AURoleOptions.PhantomCooldown = Cooldown;
                    Player.RpcResetAbilityCooldown(Sync: true);
                }
            }, 0.1f, "SheriffHadouHo.PhantomReset", true);
        }
    }

    // ★ ファントムボタン → チャージ開始
    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = false;
        ResetCooldown = false;

        if (IsFiring || ShowBeamMark || !Player.IsAlive() || IsCharging || !beamMode) return;
        if (ShotLimit <= 0) return;

        IsFiring = true;
        IsCharging = true;
        chargeTimer = 0f;
        Main.AllPlayerSpeed[Player.PlayerId] = Main.MinSpeed;
        Player.MarkDirtySettings();
        Utils.AllPlayerKillFlash();
        Player.SyncSettings();
        _prevCharging = true;
        _prevBeamMark = false;

        UtilsNotifyRoles.NotifyRoles(ForceLoop: true);
        SendRpc();
    }

    void SetRoleTextHeight(bool beaming)
    {
        var t = Player.cosmetics.nameText.transform.Find("RoleText");
        if (t == null) return;
        var rt = t.GetComponent<TMPro.TextMeshPro>();
        if (rt == null) return;
        if (beaming) { rt.text = "<alpha=#00>　</alpha>"; t.SetLocalY(0.35f); }
        else { rt.enabled = true; t.SetLocalY(0.35f); }
    }

    void ResetBeamState()
    {
        IsCharging = false;
        ShowBeamMark = false;
        IsFiring = false;
        _prevCharging = false;
        _prevBeamMark = false;
        Main.AllPlayerSpeed[Player.PlayerId] = PlayerSpeed;
        Player.MarkDirtySettings();
        SetRoleTextHeight(false);
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;

        if (!spawnCooldownStarted && Player.IsAlive() && !GameStates.Intro && GameStates.IsInTask && !GameStates.IsMeeting)
        {
            spawnCooldownStarted = true;
            Player.RpcResetAbilityCooldown(Sync: true);
        }

        if (MeetingHud.Instance != null)
        {
            if (IsCharging || ShowBeamMark || IsFiring)
            {
                ResetBeamState();
                UtilsNotifyRoles.NotifyRoles();
                SendRpc();
            }
            return;
        }

        if (!Player.IsAlive() && (IsCharging || ShowBeamMark))
        {
            ResetBeamState();
            Player.RpcSetColor((byte)PlayerColor);
            UtilsNotifyRoles.NotifyRoles();
            SendRpc();
            return;
        }

        // ★ タスクモード中：ベントCDを秒ごとに更新
        if (!beamMode && !IsFiring && Player.IsAlive() && GameStates.IsInTask)
        {
            if (nowcool > 0) nowcool -= Time.fixedDeltaTime;
            else nowcool = 0;

            var now = (int)nowcool;
            if (now != LastCooltime)
            {
                LastCooltime = now;
                Player.MarkDirtySettings();
                _ = new LateTask(() =>
                {
                    if (Player.IsAlive() && !beamMode)
                        Player.RpcResetAbilityCooldown(Sync: true);
                }, 0.1f, "SheriffHadouHo.VentCDSync", true);
                if (player != PlayerControl.LocalPlayer)
                    UtilsNotifyRoles.NotifyRoles(OnlyMeName: true, SpecifySeer: player);
            }
        }

        bool changed = (IsCharging != _prevCharging) || (ShowBeamMark != _prevBeamMark);
        if (changed)
        {
            _prevCharging = IsCharging;
            _prevBeamMark = ShowBeamMark;
            UtilsNotifyRoles.NotifyRoles(ForceLoop: true);
        }

        if (ShowBeamMark && Player.IsAlive()) ApplyBeamHit();
        if (IsCharging) { chargeTimer += Time.fixedDeltaTime; if (chargeTimer >= ChargeTime) FireBeam(); }
    }

    void FireBeam()
    {
        if (IsDead || !Player.IsAlive()) return;
        BeamFacingLeft = Player.cosmetics.FlipX;
        SendBeamDirection(BeamFacingLeft);
        Utils.AllPlayerKillFlash();
        IsCharging = false; chargeTimer = 0f; HasHitEvil = false; ShowBeamMark = true;
        SetRoleTextHeight(true);
        _prevCharging = false; _prevBeamMark = true;
        UtilsNotifyRoles.NotifyRoles(ForceLoop: true);
        SendRpc();
        ApplyBeamHit();

        _ = new LateTask(() =>
        {
            ShowBeamMark = false; _prevBeamMark = false;
            SetRoleTextHeight(false);

            // ★ 人外を1人も命中させなかった場合は自爆
            bool suicide = !HasHitEvil && SelfDestructOnMiss;

            UtilsNotifyRoles.NotifyRoles(ForceLoop: true);
            SendRpc();

            if (!Player.IsAlive()) { IsFiring = false; return; }

            if (suicide)
            {
                Player.RpcSetColor((byte)PlayerColor);
                Main.AllPlayerSpeed[Player.PlayerId] = PlayerSpeed;
                Player.MarkDirtySettings();
                PlayerState.GetByPlayerId(Player.PlayerId).DeathReason = CustomDeathReason.Misfire;
                Player.RpcMurderPlayerV2(Player);
                IsFiring = false;
                return;
            }

            Player.RpcSetColor((byte)PlayerColor);
            Main.AllPlayerSpeed[Player.PlayerId] = PlayerSpeed;
            Player.MarkDirtySettings();

            _ = new LateTask(() =>
            {
                if (!Player.IsAlive()) { IsFiring = false; return; }
                AURoleOptions.PhantomCooldown = Cooldown;
                Player.RpcResetAbilityCooldown(Sync: true);
                UtilsNotifyRoles.NotifyRoles(OnlyMeName: true);
                _ = new LateTask(() => { IsFiring = false; }, 0.3f, "SHHResetFiring", true);
            }, 0.2f, "SHHResetCooldown", true);
        }, 3f);
    }

    void ApplyBeamHit()
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;
        var myPos = Player.GetTruePosition();
        Vector2 dir = BeamFacingLeft ? Vector2.left : Vector2.right;
        foreach (var target in PlayerCatch.AllAlivePlayerControls)
        {
            if (target.PlayerId == Player.PlayerId) continue;
            var toTarget = target.GetTruePosition() - myPos;
            float dot = Vector2.Dot(toTarget, dir);
            if (dot <= 0) continue;
            if ((toTarget - dir * dot).magnitude > 1.3f) continue;

            bool isEvil = Sheriff.CanBeKilledBy(target);
            CustomRoleManager.OnCheckMurder(Player, target, target, target, true, true, 1, CustomDeathReason.Hit);
            if (isEvil) HasHitEvil = true;
        }
        ShotLimit--;
        SendRpc();
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        ResetBeamState();
        UtilsNotifyRoles.NotifyRoles(ForceLoop: true);
        SendRpc();
    }

    public override void OnStartMeeting()
    {
        ResetBeamState();
        if (beamMode)
        {
            beamMode = false;
            SendRpc();
        }
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;
        nowcool = Cooldown;
        LastCooltime = (int)Cooldown;
        ApplyModeDesync(beamMode);
    }

    public override bool GetTemporaryName(ref string name, ref bool NoMarker, bool isForMeeting, PlayerControl seer, PlayerControl seen = null)
    {
        seen ??= seer;
        if (!Player.IsAlive() || isForMeeting) return false;
        string myColor = "#" + ColorUtility.ToHtmlStringRGB(Palette.PlayerColors[Player.Data.DefaultOutfit.ColorId]);

        if (IsCharging && seen.PlayerId == Player.PlayerId)
        {
            bool fl = seer.PlayerId == Player.PlayerId ? Player.cosmetics.FlipX : BeamFacingLeft;
            string bigStar = $"<size=800%><color={myColor}>★</color></size>";
            string blank = "　　　";
            name = "<line-height=1200%>\n" + (fl ? bigStar + blank : blank + bigStar) + "</line-height>";
            NoMarker = true; return true;
        }

        if (seen == seer && Is(seer) && !seer.IsModClient() && (IsCharging || ShowBeamMark))
        {
            if (ShowBeamMark && seen.PlayerId == Player.PlayerId) { BuildBeamName(ref name, myColor, false); NoMarker = true; return true; }
            return false;
        }

        if (ShowBeamMark && seen.PlayerId == Player.PlayerId)
        {
            SetRoleTextHeight(true);
            BuildBeamName(ref name, myColor, true);
            NoMarker = true; return true;
        }
        return false;
    }

    void BuildBeamName(ref string name, string myColor, bool wider)
    {
        SetRoleTextHeight(true);
        bool fl = BeamFacingLeft;
        string star = $"<voffset=0.35em><size=800%><color={myColor}>★</color></size></voffset>";
        string beam = BuildBeamBlock();
        string blank = "<size=1200%>　</size>";
        string sB = fl ? star + blank : blank + star;
        string longBeam = fl ? beam + beam + sB : sB + beam + beam;
        string hugeBlank = "<alpha=#00>" + new string('　', 10) + "</alpha>";
        string ls = wider ? "<line-height=5300%>\n" : "<line-height=4300%>\n";
        string ss = "<size=5000%>", se = "</size></line-height>";
        name = fl
            ? ls + $"{ss}{longBeam}{se}{ss}{hugeBlank}{se}"
            : ls + $"{ss}{hugeBlank}{se}{ss}{longBeam}{se}";
    }

    string BuildBeamBlock()
    {
        if ((BeamColorMode)BeamColorModeValue == BeamColorMode.Yellow)
            return "<color=#f8cd46>━</color><color=#f8cd46>━</color><color=#f8cd46>━</color><color=#f8cd46>━</color><color=#f8cd46>━</color><color=#f8cd46>━</color><color=#f8cd46>━</color>";
        return "<color=#00CFFF>━</color><color=#00CFFF>━</color><color=#00CFFF>━</color><color=#00CFFF>━</color><color=#00CFFF>━</color><color=#00CFFF>━</color><color=#00CFFF>━</color>";
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seen.PlayerId != seer.PlayerId || isForMeeting || !Player.IsAlive()) return "";

        string size = isForHud ? "" : "<size=60%>";

        if (ShotLimit <= 0) return $"{size}<color=#888888>弾切れ</color>";
        if (!beamMode) return $"{size}<color=#f8cd46>ペット → 波動砲モードへ (CD: {LastCooltime}s)</color>";
        if (!IsCharging) return $"{size}<color=#ff0000>ファントムボタン → チャージ発射</color>";
        return $"{size}<color=#ff0000>チャージ中... {(ChargeTime - chargeTimer):F1}s</color>";
    }

    public string GetLowerTextOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (seen != seer || isForMeeting || !Player.IsAlive()) return "";
        if (IsCharging && seer.PlayerId != Player.PlayerId) return $"\n<color=#ff0000>チャージ中... {(int)(ChargeTime - chargeTimer)}s</color>";
        if (ShowBeamMark && seer.PlayerId != Player.PlayerId) return "\n<color=#ff0000>ビーム中</color>";
        return "";
    }

    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        if (!Player.IsAlive()) return "";
        string mode = beamMode ? "<color=#ff4444>[波動砲]</color>" : "<color=#f8cd46>[Task]</color>";
        return $"{Utils.ColorString(ShotLimit > 0 ? Color.yellow : Color.gray, $"({ShotLimit})")}{mode}";
    }

    public override string GetAbilityButtonText() => "発射";
    public override bool OverrideAbilityButton(out string text)
    {
        text = "SheriffHadouHo_Fire";
        return true;
    }

    void SendRpc()
    {
        using var sender = CreateSender();
        sender.Writer.Write(IsCharging);
        sender.Writer.Write(ShowBeamMark);
        sender.Writer.Write(ShotLimit);
        sender.Writer.Write(beamMode);
    }

    void SendBeamDirection(bool left)
    {
        using var sender = CreateSender();
        sender.Writer.Write((byte)1);
        sender.Writer.Write(left);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        if (reader.Length - reader.Position == 2)
        { reader.ReadByte(); BeamFacingLeft = reader.ReadBoolean(); return; }
        IsCharging = reader.ReadBoolean();
        ShowBeamMark = reader.ReadBoolean();
        ShotLimit = reader.ReadInt32();
        beamMode = reader.ReadBoolean();
    }
}*/