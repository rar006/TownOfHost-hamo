using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

/// <summary>
/// 🎃ダオシー(第三陣営・限定役職)
/// ・ペットボタンで「通報モード」と「能力モード」を切り替える
/// ・能力モードで死体を通報(1回押し)すると、その死体の人をキョンシー(守護天使)にする。素早く2回押すと通常通報
///   キョンシーにした人の死体は通報できない
/// ・自分以外の全員が白色に見え(名前も見えない)、見分けがつかない
/// ・死体が出ると自分にだけキルフラッシュ(設定で死体への矢印)
/// ・キョンシーの「霊視」を受けている人だけキルできる(それ以外をキルしようとすると不発でキルクールを消費)
/// 勝利条件: キョンシーとダオシーの数が生存数を超え、他が無害な役職だけになったら単独勝利
/// </summary>
public sealed class Daoshi : RoleBase, IKiller, IKillFlashSeeable
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Daoshi),
            player => new Daoshi(player),
            CustomRoles.Daoshi,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Neutral,
            93300,
            SetupOptionItem,
            "dao",
            "#7986CB",
            isDesyncImpostor: true,
            countType: CountTypes.Crew,
            isNewRole: true
        );

    static OptionItem OptionKillCooldown;
    static OptionItem OptionMaxKyonshi;
    static OptionItem OptionKillFlash;
    static OptionItem OptionBodyArrow;
    static OptionItem OptionNotify;
    internal static OptionItem OptionKyonshiCooldown;
    internal static OptionItem OptionSightTime;
    enum OptionName { DaoshiMaxKyonshi, DaoshiKillFlash, DaoshiBodyArrow, DaoshiNotify, KyonshiCooldown, KyonshiSightTime }

    static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, 9, defo: 1);
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 1f), 40f, false).SetValueFormat(OptionFormat.Seconds);
        OptionMaxKyonshi = IntegerOptionItem.Create(RoleInfo, 11, OptionName.DaoshiMaxKyonshi, new(0, 15, 1), 3, false).SetValueFormat(OptionFormat.Players);
        OptionKillFlash = BooleanOptionItem.Create(RoleInfo, 12, OptionName.DaoshiKillFlash, true, false);
        OptionBodyArrow = BooleanOptionItem.Create(RoleInfo, 13, OptionName.DaoshiBodyArrow, false, false);
        OptionNotify = BooleanOptionItem.Create(RoleInfo, 14, OptionName.DaoshiNotify, true, false);
        OptionKyonshiCooldown = FloatOptionItem.Create(RoleInfo, 15, OptionName.KyonshiCooldown, new(0f, 180f, 1f), 25f, false).SetValueFormat(OptionFormat.Seconds);
        OptionSightTime = FloatOptionItem.Create(RoleInfo, 16, OptionName.KyonshiSightTime, new(0f, 999f, 1f), 3f, false).SetValueFormat(OptionFormat.Seconds);
    }

    public Daoshi(PlayerControl player) : base(RoleInfo, player, () => HasTask.False) { }

    public static Daoshi Instance => CustomRoleManager.AllActiveRoles.Values.OfType<Daoshi>().FirstOrDefault();

    const byte WhiteColor = 7;
    bool abilityMode;
    float lastReportAttempt = -10f;
    byte lastReportTarget = byte.MaxValue;
    readonly HashSet<byte> kyonshi = new();
    /// <summary>霊視されている人 → 見える期限</summary>
    readonly Dictionary<byte, float> sight = new();
    readonly List<Vector3> bodyArrows = new();

    public float CalculateKillCooldown() => OptionKillCooldown.GetFloat();
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;
    public bool? CheckKillFlash(MurderInfo info) => OptionKillFlash.GetBool();

    public override void Add()
    {
        if (AmongUsClient.Instance.AmHost) TownOfHost.Patches.PetActionManager.Register(Player.PlayerId, ToggleMode);
    }

    void ToggleMode()
    {
        abilityMode = !abilityMode;
        SendRPC();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    // ===== 白色の視界(自分にだけ、他の全員を白色に見せる) =====
    void SendColor(PlayerControl target, byte color)
    {
        if (!AmongUsClient.Instance.AmHost || target == null || target.PlayerId == Player.PlayerId) return;
        var cid = Player.GetClientId();
        if (cid == -1) return;
        if (Player.AmOwner) return; // ホスト本人の場合は見た目の変更をしない(他の人の画面に影響するため)
        var writer = AmongUsClient.Instance.StartRpcImmediately(target.NetId, (byte)RpcCalls.SetColor, SendOption.Reliable, cid);
        writer.Write(target.Data.NetId);
        writer.Write(color);
        AmongUsClient.Instance.FinishRpcImmediately(writer);
    }

    void ApplyWhiteView()
    {
        foreach (var pc in PlayerCatch.AllPlayerControls)
            if (pc != null && pc.PlayerId != Player.PlayerId && !sight.ContainsKey(pc.PlayerId)) SendColor(pc, WhiteColor);
    }

    public override void StartGameTasks() => ApplyWhiteView();
    public override void AfterMeetingTasks() { sight.Clear(); ApplyWhiteView(); }
    public override void OnStartMeeting()
    {
        // 会議中は本来の色に戻す
        RestoreColors();
        foreach (var p in bodyArrows) GetArrow.Remove(Player.PlayerId, p);
        bodyArrows.Clear();
    }

    /// <summary>自分の画面の色を本来の色に戻す</summary>
    void RestoreColors()
    {
        foreach (var pc in PlayerCatch.AllPlayerControls)
            if (pc != null && pc.Data != null) SendColor(pc, (byte)pc.Data.DefaultOutfit.ColorId);
    }

    /// <summary>試合終了時: 白いままロビーに戻らないように色を戻す</summary>
    public override void OnDestroy()
    {
        try
        {
            if (Player != null && AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost && AmongUsClient.Instance.AmConnected) RestoreColors();
        }
        catch (System.Exception e) { Logger.Warn($"Daoshi.OnDestroy: {e.Message}", "Daoshi"); }
    }

    /// <summary>名前も見えない(霊視中の人は見える)</summary>
    public override bool GetTemporaryName(ref string name, ref bool NoMarker, bool isForMeeting, PlayerControl seer, PlayerControl seen = null)
    {
        seen ??= seer;
        if (isForMeeting || seer.PlayerId != Player.PlayerId || seen.PlayerId == Player.PlayerId || sight.ContainsKey(seen.PlayerId)) return false;
        name = "<size=0>.</size>";
        NoMarker = true;
        return true;
    }

    // ===== キョンシーの霊視 =====
    public void AddSight(byte targetId)
    {
        var target = PlayerCatch.GetPlayerById(targetId);
        if (target == null || !target.IsAlive()) return;
        sight[targetId] = Time.time + OptionSightTime.GetFloat();
        if (target.Data != null) SendColor(target, (byte)target.Data.DefaultOutfit.ColorId);
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    public void Tick()
    {
        if (sight.Count == 0) return;
        foreach (var (id, until) in sight.ToList())
        {
            if (Time.time < until) continue;
            sight.Remove(id);
            SendColor(PlayerCatch.GetPlayerById(id), WhiteColor);
            UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
        }
    }

    /// <summary>霊視されていない人はキルできない(不発・キルクールは消費)</summary>
    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        var target = info.AttemptTarget;
        if (target != null && sight.ContainsKey(target.PlayerId)) return;
        info.DoKill = false;
        Player.SetKillCooldown();
    }

    // ===== 通報: 能力モードならキョンシーにする =====
    public override bool CancelReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target, ref DontReportreson reason)
    {
        if (reporter == null || reporter.PlayerId != Player.PlayerId || target == null) return false;
        // キョンシーにした人の死体は通報できない
        if (kyonshi.Contains(target.PlayerId)) { reason = DontReportreson.Other; return true; }
        if (!abilityMode) return false;
        // 素早く2回押すと通常通報
        if (lastReportTarget == target.PlayerId && Time.time - lastReportAttempt < 0.6f) return false;
        lastReportAttempt = Time.time;
        lastReportTarget = target.PlayerId;
        if (kyonshi.Count >= OptionMaxKyonshi.GetInt()) { reason = DontReportreson.Other; return true; }
        var pc = PlayerCatch.GetPlayerById(target.PlayerId);
        _ = new LateTask(() =>
        {
            // 2回目の通報が来ていなければキョンシーにする
            if (pc == null || GameStates.CalledMeeting || kyonshi.Contains(pc.PlayerId) || Time.time - lastReportAttempt < 0.55f) return;
            kyonshi.Add(pc.PlayerId);
            UserRoles2026.MakeGhost(pc, CustomRoles.Kyonshi);
            Utils.SendMessage(GetString("KyonshiBorn"), pc.PlayerId);
            SendRPC();
            UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
        }, 0.6f, "Daoshi.MakeKyonshi", true);
        reason = DontReportreson.Other;
        return true;
    }

    // ===== 死体が出たら矢印 =====
    public void OnSomeoneDied(PlayerControl dead)
    {
        if (!OptionBodyArrow.GetBool() || dead == null || !Player.IsAlive()) return;
        var p = dead.transform.position;
        bodyArrows.Add(p);
        GetArrow.Add(Player.PlayerId, p);
    }

    public override string MeetingAddMessage()
        => OptionNotify.GetBool() && Player.IsAlive() ? GetString("DaoshiExists") : "";

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || seer.PlayerId != Player.PlayerId || seen.PlayerId != Player.PlayerId) return "";
        var mode = Utils.ColorString(RoleInfo.RoleColor, GetString(abilityMode ? "DaoshiModeAbility" : "DaoshiModeReport"));
        var arrows = bodyArrows.Count > 0 ? " " + GetArrow.GetArrows(Player, bodyArrows.ToArray()) : "";
        return mode + arrows;
    }

    public override string GetProgressText(bool comms = false, bool gamelog = false)
        => Utils.ColorString(RoleInfo.RoleColor, $"({kyonshi.Count}/{OptionMaxKyonshi.GetInt()})");

    public bool IsKyonshiOf(byte id) => kyonshi.Contains(id);

    /// <summary>単独勝利: キョンシー+ダオシーの数が他の生存数を超え、他が無害(キル陣営なし)</summary>
    public static bool CheckInstantWin(out GameOverReason reason)
    {
        reason = GameOverReason.ImpostorsByKill;
        var d = Instance;
        if (d == null || !d.Player.IsAlive()) return false;
        var others = PlayerCatch.AllAlivePlayerControls.Where(p => p.PlayerId != d.Player.PlayerId).ToList();
        if (d.kyonshi.Count + 1 <= others.Count) return false;
        if (others.Any(p => p.GetCountTypes() is not CountTypes.Crew || (p.GetRoleClass() is IKiller k && k.CanUseKillButton()))) return false;
        if (!CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Daoshi, d.Player.PlayerId)) return false;
        CustomWinnerHolder.NeutralWinnerIds.Add(d.Player.PlayerId);
        foreach (var id in d.kyonshi) { CustomWinnerHolder.WinnerIds.Add(id); }
        CustomWinnerHolder.AdditionalWinnerRoles.Add(CustomRoles.Kyonshi);
        return true;
    }

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(abilityMode);
        sender.Writer.Write(kyonshi.Count);
        foreach (var id in kyonshi) sender.Writer.Write(id);
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        abilityMode = reader.ReadBoolean();
        kyonshi.Clear();
        var n = reader.ReadInt32();
        for (int i = 0; i < n; i++) kyonshi.Add(reader.ReadByte());
    }
}

/// <summary>
/// キョンシー(ダオシーが作るゴースト役職・守護天使ボタン)
/// 対象に守護天使ボタンを押すと、設定秒数ダオシーがその人を視認できる(霊視)。
/// ダオシー生存時はダオシーの勝利で追加勝利。ダオシー死亡時は元の役職の勝利条件に戻る。
/// この役職になるとクルーのタスク勝利に含まれない。
/// </summary>
public static class Kyonshi
{
    public static void UseAbility(PlayerControl pc, PlayerControl target)
    {
        if (pc == null || !pc.Is(CustomRoles.Kyonshi)) return;
        pc.RpcResetAbilityCooldown();
        var d = Daoshi.Instance;
        if (d == null || target == null) return;
        d.AddSight(target.PlayerId);
        Logger.Info($"キョンシー {pc.Data?.GetLogPlayerName()} が {target.Data?.GetLogPlayerName()} を霊視", "Kyonshi");
    }
}
