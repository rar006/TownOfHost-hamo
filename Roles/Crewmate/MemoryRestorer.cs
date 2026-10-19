using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using TownOfHost.Roles.Neutral;
using static TownOfHost.Modules.SelfVoteManager;

namespace TownOfHost.Roles.Crewmate;

public sealed class MemoryRestorer : RoleBase, ISelfVoter
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(MemoryRestorer),
            player => new MemoryRestorer(player),
            CustomRoles.MemoryRestorer,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            87400,
            SetupOptionItem,
            "mr",
            "#0000CD",
            isNewRole: true
        );

    static OptionItem OptionUses;
    static OptionItem OptionNeedTasks;
    static OptionItem OptionTaskCount;
    static OptionItem OptionPerMeeting;
    enum OptionName { MemoryRestorerUses, MemoryRestorerNeedTasks, MemoryRestorerTaskCount, MemoryRestorerPerMeeting }

    static void SetupOptionItem()
    {
        OptionUses = IntegerOptionItem.Create(RoleInfo, 10, OptionName.MemoryRestorerUses, new(1, 99, 1), 1, false).SetValueFormat(OptionFormat.Times);
        OptionNeedTasks = BooleanOptionItem.Create(RoleInfo, 11, OptionName.MemoryRestorerNeedTasks, false, false);
        OptionTaskCount = IntegerOptionItem.Create(RoleInfo, 12, OptionName.MemoryRestorerTaskCount, new(1, 255, 1), 5, false, OptionNeedTasks).SetValueFormat(OptionFormat.Pieces);
        OptionPerMeeting = IntegerOptionItem.Create(RoleInfo, 13, OptionName.MemoryRestorerPerMeeting, new(1, 99, 1), 1, false).SetValueFormat(OptionFormat.Times);
    }

    public MemoryRestorer(PlayerControl player) : base(RoleInfo, player)
    {
        used = 0;
        usedThisMeeting = 0;
    }

    int used;
    int usedThisMeeting;

    public static bool IsTarget(PlayerControl pc)
    {
        if (pc == null || !pc.IsAlive()) return false;
        if (pc.GetCustomRole() is CustomRoles.Amnesiac or CustomRoles.PonkotuTeller or CustomRoles.SchrodingerCat or CustomRoles.BakeCat) return true;
        return pc.Is(CustomRoles.Amnesia) || pc.Is(CustomRoles.OneWolf);
    }

    bool CanUse => used < OptionUses.GetInt() && usedThisMeeting < OptionPerMeeting.GetInt()
        && (!OptionNeedTasks.GetBool() || (MyTaskState?.CompletedTasksCount ?? 0) >= OptionTaskCount.GetInt());

    public override void OnStartMeeting() => usedThisMeeting = 0;

    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (seer.PlayerId != Player.PlayerId || seen.PlayerId == seer.PlayerId || !IsTarget(seen)) return "";
        return Utils.ColorString(RoleInfo.RoleColor, "◎");
    }

    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)
    {
        if (!Canuseability() || voter.PlayerId != Player.PlayerId || !Player.IsAlive() || !CanUse) return true;
        if (CheckSelfVoteMode(Player, votedForId, out var status))
        {
            if (status is VoteStatus.Self)
                Utils.SendMessage(GetString("MemoryRestorerSelectMode"), Player.PlayerId);
            if (status is VoteStatus.Skip)
                Utils.SendMessage(GetString("VoteSkillFin"), Player.PlayerId);
            if (status is VoteStatus.Vote) Restore(votedForId);
            SetMode(Player, status is VoteStatus.Self);
            return false;
        }
        return true;
    }

    void Restore(byte targetId)
    {
        var target = PlayerCatch.GetPlayerById(targetId);
        if (!IsTarget(target)) { Utils.SendMessage(GetString("MemoryRestorerNotTarget"), Player.PlayerId); return; }
        used++;
        usedThisMeeting++;
        SendRPC();
        // 自分には相手の役職が分かる
        Utils.SendMessage(string.Format(GetString("MemoryRestorerResult"), target.Data?.PlayerName, UtilsRoleText.GetTrueRoleName(target.PlayerId, false)), Player.PlayerId);

        // 相手を覚醒させる
        if (target.Is(CustomRoles.Amnesia)) target.RpcReplaceSubRole(CustomRoles.Amnesia, true); // 能力が使えるようになる
        switch (target.GetRoleClass())
        {
            case SchrodingerCat cat: cat.RpcSetTeam(ISchrodingerCatOwner.TeamType.Crew); break; // クルーとして陣営変化
            case BakeCat bake: bake.RpcSetTeam(ISchrodingerCatOwner.TeamType.Crew); break;     // 自覚&クルーとして陣営変化
        }
        Utils.SendMessage(GetString("MemoryRestorerAwakened") + "\n" + string.Format(GetString("MemoryRestorerYourRole"), UtilsRoleText.GetTrueRoleName(target.PlayerId, false)), target.PlayerId);
        UtilsNotifyRoles.NotifyRoles();
    }

    public override string GetProgressText(bool comms = false, bool gamelog = false)
        => Utils.ColorString(CanUse ? RoleInfo.RoleColor : ModColors.NeutralGray, $"({OptionUses.GetInt() - used})");

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(used);
    }
    public override void ReceiveRPC(MessageReader reader) => used = reader.ReadInt32();
}
