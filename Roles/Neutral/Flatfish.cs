using AmongUs.GameOptions;
using UnityEngine;
using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Neutral;

public sealed class Flatfish : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Flatfish),
            player => new Flatfish(player),
            CustomRoles.Flatfish,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            93100,
            SetupOptionItem,
            "ff",
            "#C39143",
            countType: CountTypes.Crew,
            isNewRole: true
        );

    static OptionItem OptionMoveTime;
    static OptionItem OptionSuicide;
    enum OptionName { FlatfishMoveTime, FlatfishSuicide }

    static void SetupOptionItem()
    {
        SoloWinOption.Create(RoleInfo, 9, defo: 15);
        OptionMoveTime = FloatOptionItem.Create(RoleInfo, 10, OptionName.FlatfishMoveTime, new(0f, 30f, 0.5f), 5f, false).SetValueFormat(OptionFormat.Seconds);
        OptionSuicide = BooleanOptionItem.Create(RoleInfo, 11, OptionName.FlatfishSuicide, false, false);
    }

    public Flatfish(PlayerControl player) : base(RoleInfo, player, () => HasTask.False) { }

    float movedTime;
    Vector2 lastPos;
    bool posInit;
    bool exposedThisTurn;

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || !player.IsAlive() || !GameStates.IsInTask || GameStates.CalledMeeting) return;
        var pos = player.GetTruePosition();
        if (!posInit) { lastPos = pos; posInit = true; return; }
        var moved = Vector2.Distance(pos, lastPos) >= 0.01f;
        lastPos = pos;
        if (!moved) return;
        movedTime += Time.fixedDeltaTime;
        if (movedTime < OptionMoveTime.GetFloat()) return;
        if (OptionSuicide.GetBool())
        {
            PlayerState.GetByPlayerId(player.PlayerId).DeathReason = CustomDeathReason.Suicide;
            player.RpcMurderPlayerV2(player);
            return;
        }
        exposedThisTurn = true;
    }

    public override string MeetingAddMessage()
    {
        if (!exposedThisTurn || !Player.IsAlive()) return "";
        return string.Format(GetString("FlatfishExposed"), UtilsName.GetPlayerColor(Player, true));
    }

    public override void AfterMeetingTasks()
    {
        movedTime = 0f;
        posInit = false;
        exposedThisTurn = false;
    }

    public override string GetProgressText(bool comms = false, bool gamelog = false)
    {
        var left = Mathf.Max(0f, OptionMoveTime.GetFloat() - movedTime);
        return Utils.ColorString(exposedThisTurn ? Color.red : RoleInfo.RoleColor, $"({left:0.0}s)");
    }

    public static bool CheckWin(ref GameOverReason reason)
    {
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Crewmate) return false;
        foreach (var pc in PlayerCatch.AllPlayerControls)
        {
            if (pc.GetRoleClass() is not Flatfish || !pc.IsAlive()) continue;
            if (CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Flatfish, pc.PlayerId))
            {
                CustomWinnerHolder.NeutralWinnerIds.Add(pc.PlayerId);
                return true;
            }
        }
        return false;
    }
}
