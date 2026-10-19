using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using static TownOfHost.Modules.SelfVoteManager;

namespace TownOfHost.Roles.Neutral;

public sealed class Lupin : RoleBase, IKiller, ISelfVoter
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Lupin),
            player => new Lupin(player),
            CustomRoles.Lupin,
            () => RoleTypes.Shapeshifter,
            CustomRoleTypes.Neutral,
            93200,
            SetupOptionItem,
            "lup",
            "#3b5ba5",
            isDesyncImpostor: true,
            countType: CountTypes.Crew,
            isNewRole: true
        );

    internal static OptionItem OptionAwakenTime;
    static OptionItem OptionShapeCooldown;
    static OptionItem OptionShapeDuration;
    internal static OptionItem OptionArseneCooldown;
    enum OptionName { LupinAwakenTime, LupinShapeCooldown, LupinShapeDuration, LupinArseneCooldown }

    static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, 9, defo: 35);
        OptionAwakenTime = FloatOptionItem.Create(RoleInfo, 10, OptionName.LupinAwakenTime, new(0f, 180f, 1f), 10f, false).SetValueFormat(OptionFormat.Seconds);
        OptionShapeCooldown = FloatOptionItem.Create(RoleInfo, 11, OptionName.LupinShapeCooldown, new(0f, 255f, 1f), 15f, false).SetValueFormat(OptionFormat.Seconds);
        OptionShapeDuration = FloatOptionItem.Create(RoleInfo, 12, OptionName.LupinShapeDuration, new(0f, 255f, 1f), 12f, false).SetValueFormat(OptionFormat.Seconds);
        OptionArseneCooldown = FloatOptionItem.Create(RoleInfo, 13, OptionName.LupinArseneCooldown, new(0f, 180f, 1f), 20f, false).SetValueFormat(OptionFormat.Seconds);
    }

    public Lupin(PlayerControl player) : base(RoleInfo, player, () => HasTask.False)
    {
        markedTarget = byte.MaxValue;
        arseneId = byte.MaxValue;
    }

    public static Lupin Instance => CustomRoleManager.AllActiveRoles.Values.OfType<Lupin>().FirstOrDefault();

    byte markedTarget;
    byte arseneId;
    float awakenUntil = -1f;
    bool killedThisAwaken;

    public bool IsAwakened => Time.time < awakenUntil;
    public bool CanUseKillButton() => Player.IsAlive() && IsAwakened && !killedThisAwaken;
    public bool CanUseSabotageButton() => false;
    public bool CanUseImpostorVentButton() => false;
    public float CalculateKillCooldown() => 0.1f;

    public override void ApplyGameOptions(IGameOptions opt)
    {
        AURoleOptions.ShapeshifterCooldown = OptionShapeCooldown.GetFloat();
        AURoleOptions.ShapeshifterDuration = OptionShapeDuration.GetFloat();
    }

    static bool CanBecomeArsene(CustomRoles role) => role is not (CustomRoles.Jester or CustomRoles.Terrorist or CustomRoles.Lupin);

    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)
    {
        if (!Canuseability() || voter.PlayerId != Player.PlayerId || !Player.IsAlive() || arseneId != byte.MaxValue) return true;
        if (CheckSelfVoteMode(Player, votedForId, out var status))
        {
            if (status is VoteStatus.Self) Utils.SendMessage(GetString("LupinSelectMode"), Player.PlayerId);
            if (status is VoteStatus.Skip) Utils.SendMessage(GetString("VoteSkillFin"), Player.PlayerId);
            if (status is VoteStatus.Vote)
            {
                var t = PlayerCatch.GetPlayerById(votedForId);
                if (t == null || !CanBecomeArsene(t.GetCustomRole())) Utils.SendMessage(GetString("LupinCantMark"), Player.PlayerId);
                else
                {
                    markedTarget = votedForId;
                    Utils.SendMessage(string.Format(GetString("LupinMarked"), t.Data?.PlayerName), Player.PlayerId);
                }
            }
            SetMode(Player, status is VoteStatus.Self);
            return false;
        }
        return true;
    }

    public void Tick()
    {
        if (markedTarget != byte.MaxValue && arseneId == byte.MaxValue)
        {
            var t = PlayerCatch.GetPlayerById(markedTarget);
            if (t != null && !t.IsAlive())
            {
                arseneId = markedTarget;
                markedTarget = byte.MaxValue;
                UserRoles2026.MakeGhost(t, CustomRoles.Arsene);
                Utils.SendMessage(GetString("ArseneBorn"), t.PlayerId);
                Utils.SendMessage(string.Format(GetString("LupinArseneBorn"), t.Data?.PlayerName), Player.PlayerId);
                SendRPC();
            }
        }
        if (awakenUntil > 0f && !IsAwakened)
        {
            awakenUntil = -1f;
            SendRPC();
            UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
        }
    }

    public void Awaken()
    {
        if (!Player.IsAlive()) return;
        awakenUntil = Time.time + OptionAwakenTime.GetFloat();
        killedThisAwaken = false;
        Player.SetKillCooldown(0.1f);
        SendRPC();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    public void OnMurderPlayerAsKiller(MurderInfo info)
    {
        if (!info.DoKill || !info.CanKill) return;
        killedThisAwaken = true;
        SendRPC();
    }

    public override bool OnCheckMurderAsTarget(MurderInfo info) => !IsAwakened || info.IsSuicide;

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || !IsAwakened || seen.PlayerId != Player.PlayerId) return "";
        return Utils.ColorString(RoleInfo.RoleColor, GetString("LupinAwakened"));
    }

    public bool IsArsene(byte id) => arseneId == id;

    public static bool CheckWin(ref GameOverReason reason)
    {
        var lupin = Instance;
        if (lupin == null || !lupin.Player.IsAlive()) return false;
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Crewmate) return false;
        if (reason == GameOverReason.CrewmatesByTask) return false; // タスク勝ちはキラーが残っているので奪わない
        if (!CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Lupin, lupin.Player.PlayerId)) return false;
        CustomWinnerHolder.NeutralWinnerIds.Add(lupin.Player.PlayerId);
        if (lupin.arseneId != byte.MaxValue)
        {
            CustomWinnerHolder.WinnerIds.Add(lupin.arseneId);
            CustomWinnerHolder.AdditionalWinnerRoles.Add(CustomRoles.Arsene);
        }
        reason = GameOverReason.ImpostorsByKill;
        return true;
    }

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(arseneId);
        sender.Writer.Write(IsAwakened ? awakenUntil - Time.time : -1f);
        sender.Writer.Write(killedThisAwaken);
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        arseneId = reader.ReadByte();
        var left = reader.ReadSingle();
        awakenUntil = left > 0f ? Time.time + left : -1f;
        killedThisAwaken = reader.ReadBoolean();
    }
}

public static class Arsene
{
    public static void UseAbility(PlayerControl pc, PlayerControl target)
    {
        if (pc == null || !pc.Is(CustomRoles.Arsene)) return;
        pc.RpcResetAbilityCooldown();
        var lupin = Lupin.Instance;
        if (lupin == null || target == null || target.PlayerId != lupin.Player.PlayerId || lupin.IsAwakened) return;
        lupin.Awaken();
        Logger.Info($"アルセーヌ {pc.Data?.GetLogPlayerName()} がルパンを覚醒させた", "Arsene");
    }
}
