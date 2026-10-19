using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using static TownOfHost.Modules.SelfVoteManager;

namespace TownOfHost.Roles.Crewmate;

public sealed class Fugitive : RoleBase, ISelfVoter
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Fugitive),
            player => new Fugitive(player),
            CustomRoles.Fugitive,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            87300,
            SetupOptionItem,
            "fug",
            "#D9D9D9"
        );

    static OptionItem OptionSenseKill;
    enum OptionName { FugitiveSenseKill }
    static void SetupOptionItem()
    {
        OptionSenseKill = BooleanOptionItem.Create(RoleInfo, 10, OptionName.FugitiveSenseKill, false, false);
    }

    public Fugitive(PlayerControl player) : base(RoleInfo, player)
    {
        pendingTarget = byte.MaxValue;
        fleeTarget = byte.MaxValue;
    }

    byte pendingTarget;
    byte fleeTarget;
    bool fleeToKiller;
    bool attackedWhileFleeing;

    public bool IsFleeing => fleeTarget != byte.MaxValue;

    static bool CanFleeTo(PlayerControl target)
        => target != null && target.IsAlive() && target.GetCustomRole() is not (CustomRoles.King or CustomRoles.Walkure or CustomRoles.Autocrat);

    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)
    {
        if (!Canuseability() || voter.PlayerId != Player.PlayerId || !Player.IsAlive()) return true;
        if (CheckSelfVoteMode(Player, votedForId, out var status))
        {
            if (status is VoteStatus.Self)
                Utils.SendMessage(GetString("FugitiveSelectMode"), Player.PlayerId);
            if (status is VoteStatus.Skip)
                Utils.SendMessage(GetString("VoteSkillFin"), Player.PlayerId);
            if (status is VoteStatus.Vote)
            {
                var target = PlayerCatch.GetPlayerById(votedForId);
                if (!CanFleeTo(target)) Utils.SendMessage(GetString("FugitiveCantFlee"), Player.PlayerId);
                else
                {
                    pendingTarget = votedForId;
                    Utils.SendMessage(string.Format(GetString("FugitiveSelected"), target.Data?.PlayerName), Player.PlayerId);
                }
            }
            SetMode(Player, status is VoteStatus.Self);
            return false;
        }
        return true;
    }

    public override void OnStartMeeting()
    {
        // 逃亡は会議が始まると解除される
        if (!AmongUsClient.Instance.AmHost) return;
        if (IsFleeing && fleeToKiller && Player.IsAlive())
        {
            // キル可能な人外に逃亡していた → この会議で死亡
            MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.Kill, Player.PlayerId);
        }
        if (IsFleeing && attackedWhileFleeing && OptionSenseKill.GetBool())
            _ = new LateTask(() => Utils.SendMessage(GetString("FugitiveSensedKill"), Player.PlayerId), 3f, "Fugitive.Sense", true);
        fleeTarget = byte.MaxValue;
        fleeToKiller = false;
        attackedWhileFleeing = false;
        pendingTarget = byte.MaxValue;
        SendRPC();
    }

    public override void OnExileWrapUp(NetworkedPlayerInfo exiled, ref bool DecidedWinner)
    {
        if (!AmongUsClient.Instance.AmHost || pendingTarget == byte.MaxValue || !Player.IsAlive()) return;
        var target = PlayerCatch.GetPlayerById(pendingTarget);
        if ((exiled != null && exiled.PlayerId == pendingTarget) || !CanFleeTo(target))
        {
            pendingTarget = byte.MaxValue;
            return;
        }
        fleeTarget = pendingTarget;
        pendingTarget = byte.MaxValue;
        var role = target.GetCustomRole();
        fleeToKiller = !role.IsCrewmate() && !role.IsMadmate() && (target.GetRoleClass() is IKiller k ? k.CanUseKillButton() : role.IsImpostor());
        SendRPC();
    }

    public override bool OnCheckMurderAsTarget(MurderInfo info)
    {
        if (!IsFleeing) return true;
        if (info.AttemptKiller != null && info.AttemptKiller.Is(CustomRoles.EarnestWolf)) return true;
        attackedWhileFleeing = true;
        info.AttemptKiller?.SetKillCooldown();
        return false;
    }

    public void CheckFleeTargetDead()
    {
        if (!IsFleeing || !Player.IsAlive()) return;
        var target = PlayerCatch.GetPlayerById(fleeTarget);
        if (target != null && target.IsAlive()) return;
        fleeTarget = byte.MaxValue;
        var state = PlayerState.GetByPlayerId(Player.PlayerId);
        if (state != null) state.DeathReason = CustomDeathReason.FollowingSuicide;
        Player.RpcMurderPlayerV2(Player);
        SendRPC();
    }

    public override string GetProgressText(bool comms = false, bool gamelog = false)
        => IsFleeing ? Utils.ColorString(RoleInfo.RoleColor, "⇒") : "";

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(fleeTarget);
    }
    public override void ReceiveRPC(MessageReader reader) => fleeTarget = reader.ReadByte();
}
