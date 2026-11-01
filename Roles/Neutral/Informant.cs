using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

public sealed class Informant : RoleBase, IKiller, IUsePhantomButton, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Informant),
            player => new Informant(player),
            CustomRoles.Informant,
            () => RoleTypes.Phantom,
            CustomRoleTypes.Neutral,
            97500,
            SetupOptionItem,
            "inf",
            "#C7C7C7",
            (6, 5),
            from: From.TownOfHost_hamo,
            isNewRole: true
        );

    public Informant(PlayerControl player)
    : base(RoleInfo, player, () => HasTask.False)
    {
        cooldown = OptionCooldown.GetFloat();
        notifyTarget = OptionNotifyTarget.GetBool();
        revealTarget = OptionRevealTarget.GetBool();
        needCount = OptionNeedCount.GetInt();
        soloWin = OptionSoloWin.GetBool();
    }

    static OptionItem OptionCooldown;
    static OptionItem OptionNotifyTarget;
    static OptionItem OptionRevealTarget;
    static OptionItem OptionNeedCount;
    static OptionItem OptionSoloWin;
    enum OptionName { InformantCooldown, InformantNotifyTarget, InformantRevealTarget, InformantNeedCount, InformantSoloWin }

    static float cooldown;
    static bool notifyTarget;
    static bool revealTarget;
    static int needCount;
    static bool soloWin;

    byte pickedId = byte.MaxValue;
    readonly List<(byte Target, byte Receiver)> pending = new();
    readonly HashSet<byte> informed = new();

    static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, 9, defo: 15);
        OptionCooldown = FloatOptionItem.Create(RoleInfo, 10, OptionName.InformantCooldown, new(0.5f, 180f, 0.5f), 35f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionNotifyTarget = BooleanOptionItem.Create(RoleInfo, 11, OptionName.InformantNotifyTarget, true, false);
        OptionRevealTarget = BooleanOptionItem.Create(RoleInfo, 12, OptionName.InformantRevealTarget, false, false);
        OptionNeedCount = IntegerOptionItem.Create(RoleInfo, 13, OptionName.InformantNeedCount, new(1, 15, 1), 3, false)
            .SetValueFormat(OptionFormat.Players);
        OptionSoloWin = BooleanOptionItem.Create(RoleInfo, 14, OptionName.InformantSoloWin, false, false);
    }

    public bool CanKill => false;
    public bool IsKiller => false;
    public bool CanUseKillButton() => false;
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;
    public float CalculateKillCooldown() => 0f;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        opt.SetVision(false);
        AURoleOptions.PhantomCooldown = cooldown;
    }

    bool IUsePhantomButton.IsPhantomRole => true;
    bool IUsePhantomButton.IsresetAfterKill => false;
    public override string GetAbilityButtonText() => GetString("InformantButton");

    static float KillRange => NormalGameOptionsV12.KillDistances[Mathf.Clamp(Main.NormalOptions.KillDistance, 0, 2)] + 0.2f;

    PlayerControl NearestTarget()
    {
        var from = Player.GetTruePosition();
        return PlayerCatch.AllAlivePlayerControls
            .Where(pc => pc != null && pc != Player && pc.IsAlive() && !pc.inVent && pc.PlayerId != pickedId)
            .Where(pc => Vector2.Distance(from, pc.GetTruePosition()) <= KillRange)
            .OrderBy(pc => Vector2.Distance(from, pc.GetTruePosition()))
            .FirstOrDefault();
    }

    void IUsePhantomButton.OnClick(ref bool AdjustKillCooldown, ref bool? ResetCooldown)
    {
        AdjustKillCooldown = true;
        ResetCooldown = false;
        if (!AmongUsClient.Instance.AmHost || !Player.IsAlive()) return;
        var target = NearestTarget();
        if (target == null) return;

        var picked = PlayerCatch.GetPlayerById(pickedId);
        if (picked == null || !picked.IsAlive())
        {
            pickedId = target.PlayerId;
            Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} が {target.GetNameWithRole().RemoveHtmlTags()} を密告の対象に選びました", "Informant");
        }
        else
        {
            pending.RemoveAll(p => p.Target == pickedId && p.Receiver == target.PlayerId);
            pending.Add((pickedId, target.PlayerId));
            Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} が {picked.GetNameWithRole().RemoveHtmlTags()} の役職を {target.GetNameWithRole().RemoveHtmlTags()} に密告しました", "Informant");
            pickedId = byte.MaxValue;
            ResetCooldown = true;
        }
        SendRPC();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    static CustomRoles ShownRole(PlayerControl target)
    {
        var role = target.GetCustomRole();
        return role is CustomRoles.Arcana ? CustomRoles.Crewmate : role;
    }

    public override void OnStartMeeting()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        pickedId = byte.MaxValue;
        if (pending.Count == 0) return;
        var title = Utils.ColorString(RoleInfo.RoleColor, GetString("InformantTitle"));
        foreach (var (targetId, receiverId) in pending)
        {
            var target = PlayerCatch.GetPlayerById(targetId);
            var receiver = PlayerCatch.GetPlayerById(receiverId);
            if (target == null || !target.IsAlive())
            {
                Logger.Info($"密告の対象 ID{targetId} が会議の前に死んだので、密告は不発です", "Informant");
                continue;
            }
            informed.Add(targetId);
            if (notifyTarget) Send(targetId, GetString("Informant.Informed"), title);
            if (receiver == null) continue;
            var roleText = UtilsRoleText.GetRoleColorAndtext(ShownRole(target));
            var text = revealTarget
                ? string.Format(GetString("Informant.ReceivedWithName"), UtilsName.GetPlayerColor(target, true), roleText)
                : string.Format(GetString("Informant.Received"), roleText);
            Send(receiverId, text, title);
        }
        pending.Clear();
        SendRPC();
    }

    static void Send(byte to, string text, string title)
    {
        _ = new LateTask(() => Utils.SendMessage(text, to, title), 4f, "Informant.Send", true);
        MeetingHudPatch.StartPatch.meetingsends.Add((to, text, title));
    }

    int AliveInformedCount => informed.Count(id => PlayerCatch.GetPlayerById(id)?.IsAlive() == true);

    public bool CheckWin(ref CustomRoles winnerRole)
    {
        if (!Player.IsAlive() || AliveInformedCount < needCount) return false;
        winnerRole = CustomRoles.Informant;
        if (soloWin) CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Informant, Player.PlayerId, true);
        return true;
    }

    public override string GetProgressText(bool comms = false, bool gamelog = false)
        => Utils.ColorString(AliveInformedCount >= needCount ? RoleInfo.RoleColor : Color.gray, $"({AliveInformedCount}/{needCount})");

    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (!Is(seer) || seen == seer) return "";
        if (seen.PlayerId == pickedId) return Utils.ColorString(RoleInfo.RoleColor, "◎");
        return informed.Contains(seen.PlayerId) ? Utils.ColorString(RoleInfo.RoleColor, "◇") : "";
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || !Is(seer) || seen != seer || !Player.IsAlive()) return "";
        var picked = PlayerCatch.GetPlayerById(pickedId);
        return Utils.ColorString(RoleInfo.RoleColor, picked != null && picked.IsAlive()
            ? string.Format(GetString("InformantPickReceiver"), UtilsName.GetPlayerColor(picked, true))
            : GetString("InformantPickTarget"));
    }

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(pickedId);
        sender.Writer.Write(informed.Count);
        foreach (var id in informed) sender.Writer.Write(id);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        pickedId = reader.ReadByte();
        informed.Clear();
        var count = reader.ReadInt32();
        for (var i = 0; i < count; i++) informed.Add(reader.ReadByte());
    }
}
