using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AmongUs.GameOptions;
using Il2CppSystem.Runtime.Remoting.Messaging;
using TownOfHost;
using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using UnityEngine;
using static Il2CppMono.Security.X509.X520;
using static TownOfHost.Modules.SelfVoteManager;

namespace TownOfHost.Roles.Impostor;

public sealed class Administer : RoleBase, IImpostor
{
    public static readonly SimpleRoleInfo RoleInfo =
         SimpleRoleInfo.Create(
            typeof(Administer),
            player => new Administer(player),
            CustomRoles.Administer,
            () => RoleTypes.Impostor,
            CustomRoleTypes.Impostor,
            77800,
            null,
            "Ads",
            OptionSort: (5, 3),
            isNewRole: true,
            from: From.TownOfHost_Y
        );
    public Administer(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        ismsgmode = false;
    }

    bool ismsgmode;

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        CreateMsg(true);
    }

    // ホスト
    public override string GetSuffix(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (!seer.AmOwner || !isForMeeting || !seen.IsAlive()) return string.Empty;

        // 最終場所の表示
        var room = PlayerState.GetByPlayerId(seen.PlayerId).LastRoom;
        var color = Color.green;
        var roomName = "";

        if (room == null)
        {
            roomName = GetString("FailToTrack");
            color = Color.gray;
        }
        else
        {
            roomName = GetString(room.RoomId.ToString());
        }
        return Utils.ColorString(color, $"@{roomName}");
    }
    // ホスト以外
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (seer.AmOwner || !isForMeeting || !seen.IsAlive()) return string.Empty;

        // 最終場所の表示
        var room = PlayerState.GetByPlayerId(seen.PlayerId).LastRoom;
        var color = Color.green;
        var roomName = "";

        if (room == null)
        {
            roomName = GetString("FailToTrack");
            color = Color.gray;
        }
        else
        {
            roomName = GetString(room.RoomId.ToString());
        }
        return Utils.ColorString(color, $"<size=80%>\n@{roomName}</size>");
    }
    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)
    {
        if (!Canuseability()) return true;

        if (Is(voter) && votedForId == Player.PlayerId && !ismsgmode)
        {
            ismsgmode = true;
            Utils.SendMessage(GetString("AdministerModeText1"), Player.PlayerId);
            return false;
        }
        if (votedForId == 253 && ismsgmode)
        {
            ismsgmode = false;
            CreateMsg();
            Utils.SendMessage(GetString("AdministerModeText2"), Player.PlayerId);
            return false;
        }
        return true;
    }
    void CreateMsg(bool Wait = false)
    {
        List<(PlayerControl pc, string roomName)> rooms = new();
        var sb = new StringBuilder();

        foreach (var pc in PlayerCatch.AllAlivePlayerControls)
        {
            var room = PlayerState.GetByPlayerId(pc.PlayerId).LastRoom;
            var roomName = (room != null) ? room.RoomId.ToString() : "FailToTrack";

            rooms.Add((pc, roomName));
        }

        // `FailToTrack`が最後に来るように
        rooms = rooms.OrderBy(room => room.roomName == "FailToTrack")
            .ThenBy(room => room.roomName).ToList();

        // SJISではアルファベットは1バイト，日本語は基本的に2バイト
        var longestNameByteCount = Main.AllPlayerNames.Values.Select(name => name.GetByteCount()).OrderByDescending(byteCount => byteCount).FirstOrDefault();
        //最大11.5emとする(★+日本語10文字分+半角空白)
        var pos = Math.Min(((float)longestNameByteCount / 2) + 2.5f /* ●+：*/ , 11.5f);

        foreach (var r in rooms)
        {
            var playerColor = Main.PlayerColors[r.pc.PlayerId];
            var playerName = r.pc.GetRealName().ApplyNameColorData(Player, r.pc, true);

            sb.Append("●".Color(playerColor)).Append(playerName).Append('：');
            sb.AppendFormat("<pos={0}em>", pos).Append(GetString(r.roomName)).Append("</pos>\n");
        }

        var message = sb.ToString();
        var title = GetString("AdministerMessage").Color(Color.green);
        if (Wait)
        {
            _ = new LateTask(() => Utils.SendMessage(message, Player.PlayerId, title), 3f, "Administer Message");
        }
        else
        {
            Utils.SendMessage(message, Player.PlayerId, title);
        }
    }
    public override void AfterMeetingTasks()
    {
        ismsgmode = false;
    }
}