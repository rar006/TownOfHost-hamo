using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using UnityEngine;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Impostor;

public sealed class Abductor : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Abductor),
            player => new Abductor(player),
            CustomRoles.Abductor,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            92800,
            SetupOptionItem,
            "abd",
            "#FF1919",
            isNewRole: true
        );

    static OptionItem OptionKillCooldown;
    static OptionItem OptionDragTime;
    static OptionItem OptionStayTime;
    enum OptionName { AbductorDragTime, AbductorStayTime }

    static void SetupOptionItem()
    {
        OptionKillCooldown = FloatOptionItem.Create(RoleInfo, 10, GeneralOption.KillCooldown, new(0f, 180f, 1f), 35f, false).SetValueFormat(OptionFormat.Seconds);
        OptionDragTime = FloatOptionItem.Create(RoleInfo, 11, OptionName.AbductorDragTime, new(0f, 255f, 1f), 8f, false).SetValueFormat(OptionFormat.Seconds);
        OptionStayTime = FloatOptionItem.Create(RoleInfo, 12, OptionName.AbductorStayTime, new(0f, 255f, 1f), 8f, false).SetValueFormat(OptionFormat.Seconds);
    }

    public Abductor(PlayerControl player) : base(RoleInfo, player) { }

    PlayerControl victim;
    SystemTypes targetRoom;
    float dragTime;
    float stayTime;
    int snapCounter;

    public float CalculateKillCooldown() => OptionKillCooldown.GetFloat();

    public void OnCheckMurderAsKiller(MurderInfo info)
    {
        var target = info.AttemptTarget;
        if (victim == null)
        {
            if (info.CheckHasGuard()) { info.IsGuard = false; return; }
            info.DoKill = false;
            Grab(target);
            return;
        }
        // 2回目: 通常キル(引きずり始めてから設定秒数が経つまではキルできない)
        if (dragTime < OptionDragTime.GetFloat())
        {
            info.DoKill = false;
            Utils.SendMessage(string.Format(GetString("AbductorWait"), Mathf.CeilToInt(OptionDragTime.GetFloat() - dragTime)), Player.PlayerId);
            return;
        }
        var v = victim;
        Release();
        if (target != v)
        {
            info.DoKill = false;
            Player.RpcMurderPlayer(v);
        }
    }

    void Grab(PlayerControl target)
    {
        victim = target;
        dragTime = 0f;
        BootFromVent(target);
        stayTime = 0f;
        var rooms = ShipStatus.Instance.AllRooms.Where(r => r?.RoomId is not null and not SystemTypes.Hallway).Select(r => r.RoomId).Distinct().ToList();
        targetRoom = rooms.Count > 0 ? rooms[IRandom.Instance.Next(rooms.Count)] : SystemTypes.Hallway;
        CheckMurderPatch.TimeSinceLastKill[Player.PlayerId] = 0f;
        target.GetPlayerState().CanMove = false;
        target.MarkDirtySettings();
        Player.SetKillCooldown(0.1f);
        SendRPC();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    // ベントの中(ベントの上)にいる相手は瞬間移動で動かせず、引きずれないまま時間が過ぎていた。一番近いベントから出す
    static void BootFromVent(PlayerControl target)
    {
        if (target == null || !target.inVent || ShipStatus.Instance == null) return;
        var pos = target.transform.position;
        var vent = ShipStatus.Instance.AllVents.OrderBy(v => (v.transform.position - pos).sqrMagnitude).FirstOrDefault();
        if (vent == null) return;
        target.MyPhysics.RpcBootFromVent(vent.Id);
    }
    void Release()
    {
        if (victim != null)
        {
            victim.GetPlayerState().CanMove = true;
            victim.MarkDirtySettings();
        }
        victim = null;
        SendRPC();
        UtilsNotifyRoles.NotifyRoles(SpecifySeer: Player);
    }

    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost || !GameStates.IsInTask || victim == null) return;
        if (!Player.IsAlive() || !victim.IsAlive()) { Release(); return; }
        dragTime += Time.fixedDeltaTime;
        // 指定された部屋に、設定秒数いる → 死体を残さずにキル
        if (Player.GetPlainShipRoom()?.RoomId == targetRoom)
        {
            stayTime += Time.fixedDeltaTime;
            if (stayTime >= OptionStayTime.GetFloat()) { VanishKill(); return; }
        }
        else stayTime = 0f;
        // 相手を引きずる(ベントの中にいると動かせないので、先にベントから出す)
        if (victim.inVent && snapCounter % 15 == 0) BootFromVent(victim);
        if (++snapCounter % 3 == 0 && !victim.MyPhysics.Animations.IsPlayingAnyLadderAnimation())
            victim.RpcSnapToForced(Player.transform.position, SendOption.None);
    }

    void VanishKill()
    {
        var v = victim;
        Release();
        var state = PlayerState.GetByPlayerId(v.PlayerId);
        state.DeathReason = CustomDeathReason.Kill;
        v.SetRealKiller(Player);
        v.RpcExileV2(); // 死体を残さない
        state.SetDead();
        Player.ResetKillCooldown();
        Player.SetKillCooldown();
        UtilsGameLog.AddGameLog("Kill", string.Format(GetString("AbductorLog"), UtilsName.GetPlayerColor(v, true), UtilsName.GetPlayerColor(Player, true)));
    }

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target) => Release();

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (isForMeeting || victim == null || seer.PlayerId != Player.PlayerId || seen.PlayerId != Player.PlayerId) return "";
        return Utils.ColorString(RoleInfo.RoleColor, string.Format(GetString("AbductorRoom"), GetString(targetRoom.ToString())));
    }

    void SendRPC()
    {
        using var sender = CreateSender();
        sender.Writer.Write(victim?.PlayerId ?? byte.MaxValue);
        sender.Writer.Write((byte)targetRoom);
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        var id = reader.ReadByte();
        victim = id == byte.MaxValue ? null : PlayerCatch.GetPlayerById(id);
        targetRoom = (SystemTypes)reader.ReadByte();
    }
}
