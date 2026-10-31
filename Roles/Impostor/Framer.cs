using System.Collections.Generic;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

// フレーマー: キルボタンを1回押すと「姿の元」にする人を選び、ファントムボタンで近くの人をその姿に変える。
// 姿を変えられた本人には変わった姿を送らないので、本人は気づかない。ダブルクリックで普通にキルできる。
public sealed class Framer : RoleBase, IImpostor, IUsePhantomButton, IDoubleTrigger
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Framer),
            player => new Framer(player),
            CustomRoles.Framer,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Impostor,
            992500,
            SetupOptionItem,
            "frm",
            OptionSort: (6, 10),
            from: From.TownOfHost_hamo,
            isNewRole: true
        );

    public Framer(PlayerControl player)
    : base(RoleInfo, player)
    {
        killCooldown = OptionKillCooldown.GetFloat();
        abilityCooldown = OptionAbilityCooldown.GetFloat();
        duration = OptionDuration.GetFloat();
        maxCount = OptionMaxCount.GetInt();
        meetingNotice = OptionMeetingNotice.GetBool();
        impostorAware = OptionImpostorAware.GetBool();
        sourceId = byte.MaxValue;
        framed.Clear();
    }

    private static OptionItem OptionKillCooldown;
    private static OptionItem OptionAbilityCooldown;
    private static OptionItem OptionDuration;
    private static OptionItem OptionMaxCount;
    private static OptionItem OptionMeetingNotice;
    private static OptionItem OptionImpostorAware;
    enum OptionName
    {
        FramerDuration,
        FramerMeetingNotice,
        FramerImpostorAware,
    }
    private static float killCooldown;
    private static float abilityCooldown;
    private static float duration;
    private static int maxCount;
    private static bool meetingNotice;
    private static bool impostorAware;

    private int remaining;
    // 姿の元にする人
    private byte sourceId;
    // 姿を変えている人 → 元に戻す時刻
    private static readonly Dictionary<byte, float> framed = new();
    private bool usedSinceMeeting;

    private static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 1f), 35f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionAbilityCooldown = FloatOptionItem.Create(RoleInfo, 11, GeneralOption.Cooldown, new(0f, 255f, 1f), 40f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionDuration = FloatOptionItem.Create(RoleInfo, 12, OptionName.FramerDuration, new(0f, 999f, 1f), 15f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionMaxCount = IntegerOptionItem.Create(RoleInfo, 13, GeneralOption.OptionCount, new(0, 999, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
        OptionMeetingNotice = BooleanOptionItem.Create(RoleInfo, 14, OptionName.FramerMeetingNotice, true, false);
        OptionImpostorAware = BooleanOptionItem.Create(RoleInfo, 15, OptionName.FramerImpostorAware, true, false);
    }

    public float CalculateKillCooldown() => killCooldown;
    public override void ApplyGameOptions(IGameOptions opt) => AURoleOptions.PhantomCooldown = abilityCooldown;

    public override void Add()
    {
        Player.AddDoubleTrigger();
        remaining = maxCount;
    }

    public bool CheckAction => remaining > 0;

    public bool SingleAction(PlayerControl killer, PlayerControl target)
    {
        sourceId = target.PlayerId;
        SendRpc();
        Logger.Info($"{killer.GetNameWithRole().RemoveHtmlTags()} → {target.GetNameWithRole().RemoveHtmlTags()}：姿の元に選択", "Framer");
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: killer);
        return false;
    }

    public bool DoubleAction(PlayerControl killer, PlayerControl target) => true;

    public bool UseOneclickButton => remaining > 0;

    public void OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = true;
        ResetCooldown = false;
        if (!AmongUsClient.Instance.AmHost || remaining <= 0 || Camouflage.IsCamouflage) return;
        var source = PlayerCatch.GetPlayerById(sourceId);
        var target = Player.GetKillTarget(true);
        if (source == null || target == null || target == source || !target.IsAlive()) return;
        if (!Camouflage.PlayerSkins.TryGetValue(source.PlayerId, out var outfit) || outfit == null) return;

        ResetCooldown = true;
        remaining--;
        usedSinceMeeting = true;
        framed[target.PlayerId] = Time.time + duration;
        // インポスターが対象で、自覚できる設定なら本人にも見せる
        var targetSees = impostorAware && target.GetCustomRole().IsImpostor();
        SendOutfit(target, outfit, targetSees);
        SendRpc();
        Logger.Info($"{target.GetNameWithRole().RemoveHtmlTags()} を {source.GetNameWithRole().RemoveHtmlTags()} の姿に変えました(残り{remaining}回)", "Framer");
        UtilsNotifyRoles.NotifyRoles();
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || player != Player || framed.Count == 0) return;
        if (GameStates.Intro || GameStates.CalledMeeting) return;
        List<byte> ended = null;
        foreach (var (id, until) in framed)
            if (Time.time >= until) (ended ??= new()).Add(id);
        if (ended == null) return;
        foreach (var id in ended) Revert(id);
        UtilsNotifyRoles.NotifyRoles();
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target) => RevertAll();
    public override void OnDestroy() => RevertAll();

    private void RevertAll()
    {
        if (!AmongUsClient.Instance.AmHost || framed.Count == 0) return;
        foreach (var id in new List<byte>(framed.Keys)) Revert(id);
    }

    private static void Revert(byte id)
    {
        framed.Remove(id);
        var target = PlayerCatch.GetPlayerById(id);
        if (target == null || Camouflage.IsCamouflage) return;
        if (!Camouflage.PlayerSkins.TryGetValue(id, out var outfit) || outfit == null) return;
        SendOutfit(target, outfit, true);
    }

    // target の姿を outfit にする。includeTarget が false なら本人には送らない(本人の画面では元のまま)
    private static void SendOutfit(PlayerControl target, NetworkedPlayerInfo.PlayerOutfit outfit, bool includeTarget)
    {
        if (target == null || target.Data == null || target.Data.Disconnected) return;
        var sender = CustomRpcSender.Create(name: $"Framer.SendOutfit({target.Data.GetLogPlayerName()})");
        foreach (var seer in PlayerCatch.AllPlayerControls)
        {
            if (seer == null || (seer == target && !includeTarget)) continue;
            if (seer == PlayerControl.LocalPlayer)
            {
                target.SetColor(outfit.ColorId);
                target.SetHat(outfit.HatId, outfit.ColorId);
                target.SetSkin(outfit.SkinId, outfit.ColorId);
                target.SetVisor(outfit.VisorId, outfit.ColorId);
                continue;
            }
            var clientId = seer.GetClientId();
            if (clientId == -1) continue;
            sender.AutoStartRpc(target.NetId, (byte)RpcCalls.SetColor, clientId)
                .Write(target.Data.NetId)
                .Write((byte)outfit.ColorId)
                .EndRpc();
            sender.AutoStartRpc(target.NetId, (byte)RpcCalls.SetHatStr, clientId)
                .Write(outfit.HatId)
                .Write(target.GetNextRpcSequenceId(RpcCalls.SetHatStr))
                .EndRpc();
            sender.AutoStartRpc(target.NetId, (byte)RpcCalls.SetSkinStr, clientId)
                .Write(outfit.SkinId)
                .Write(target.GetNextRpcSequenceId(RpcCalls.SetSkinStr))
                .EndRpc();
            sender.AutoStartRpc(target.NetId, (byte)RpcCalls.SetVisorStr, clientId)
                .Write(outfit.VisorId)
                .Write(target.GetNextRpcSequenceId(RpcCalls.SetVisorStr))
                .EndRpc();
        }
        sender.SendMessage();
    }

    public override string MeetingAddMessage()
    {
        if (!meetingNotice || !usedSinceMeeting) return "";
        usedSinceMeeting = false;
        return Utils.ColorString(RoleInfo.RoleColor, Translator.GetString("FramerMeetingMessage"));
    }

    public override string GetMark(PlayerControl seer, PlayerControl seen, bool isForMeeting = false)
    {
        seen ??= seer;
        var mark = "";
        // 選んでいる姿の元は自分にだけ見える
        if (seer == Player && seen.PlayerId == sourceId) mark += Utils.ColorString(RoleInfo.RoleColor, "◎");
        // 姿を変えている人には、フレーマーとインポスターにだけ印を出す
        if (framed.ContainsKey(seen.PlayerId) && (seer == Player || seer.GetCustomRole().IsImpostor()))
            mark += Utils.ColorString(RoleInfo.RoleColor, "◇");
        return mark;
    }

    public override string GetProgressText(bool comms = false, bool GameLog = false)
        => Utils.ColorString(remaining > 0 ? RoleInfo.RoleColor : Color.gray, $"[{remaining}]");

    public override string GetAbilityButtonText() => Translator.GetString("FramerAbilityButtonText");

    private void SendRpc()
    {
        using var sender = CreateSender();
        sender.Writer.Write(remaining);
        sender.Writer.Write(sourceId);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        remaining = reader.ReadInt32();
        sourceId = reader.ReadByte();
    }
}
