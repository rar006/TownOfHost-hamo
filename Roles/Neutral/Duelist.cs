using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using Hazel;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using static TownOfHost.PlayerCatch;
using static TownOfHost.Utils;
using static TownOfHost.Translator;

namespace TownOfHost.Roles.Neutral;

// ══════════════════════════════════════════════════════════════
// 決闘者 (TOHY移植)
// 設定ターン内に宿敵を決める（投票は全視点で見えない）。
// 宿敵が死亡し自分が生存 → 追加勝利。
// 設定ターン内に決定しないと自爆（マドンナ方式）。
// ══════════════════════════════════════════════════════════════
public sealed class Duelist : RoleBase, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Duelist),
            player => new Duelist(player),
            CustomRoles.Duelist,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            485300,
            SetupOptionItem,
            "dl",
            "#ff6347",
            (4, 9),
            from: From.TownOfHost_Y
        );

    public Duelist(PlayerControl player)
        : base(RoleInfo, player, () => HasTask.True)
    {
        archenemyPlayerId = byte.MaxValue;
        hasChosenArchenemy = false;

        Duelists.Add(this);
        CustomRoleManager.MarkOthers.Add(GetMarkOthers);
    }

    public override void OnDestroy()
    {
        Duelists.Remove(this);
        CustomRoleManager.MarkOthers.Remove(GetMarkOthers);
    }

    // ─── オプション ───────────────────────────────────────
    static OptionItem OptionMeetingLimit;
    public static int MeetingLimit;

    enum OptionName { DuelistMeetingLimit }

    static void SetupOptionItem()
    {
        // ★ マドンナの limit と同じ考え方: 何ターン目まで宿敵を決められるか
        OptionMeetingLimit = IntegerOptionItem.Create(
            RoleInfo, 10, OptionName.DuelistMeetingLimit,
            new(1, 10, 1), 1, false)
            .SetValueFormat(OptionFormat.day);
    }

    // ─── 内部状態 ─────────────────────────────────────────
    public static readonly HashSet<Duelist> Duelists = new();
    public byte archenemyPlayerId;
    bool hasChosenArchenemy;

    private PlayerControl Archenemy
        => archenemyPlayerId == byte.MaxValue ? null : GetPlayerById(archenemyPlayerId);

    public override void Add()
    {
        MeetingLimit = OptionMeetingLimit.GetInt();
    }

    // ─── 投票インターセプト（宿敵未決定の間は常に有効） ─────
    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)
    {
        if (!Is(voter) || !Player.IsAlive()) return true;
        if (hasChosenArchenemy) return true; // 決定済みなら通常投票

        // スキップ or 自投票 → 今ターンはパス（ターン消費は AfterMeetingTasks で判定）
        if (votedForId == Player.PlayerId || votedForId >= 253)
        {
            int left = MeetingLimit - UtilsGameLog.day;
            SendMessage(
                $"<color={RoleInfo.RoleColorCode}>宿敵を決めませんでした。残り {left} ターン</color>",
                Player.PlayerId);
            return false; // 投票を隠す
        }

        // 有効な相手を宿敵に設定
        var target = GetPlayerById(votedForId);
        if (target == null || !target.IsAlive()) return true;

        archenemyPlayerId = votedForId;
        hasChosenArchenemy = true;
        SendRpc();

        if (!RoleSendList.Contains(target.PlayerId))
            RoleSendList.Add(target.PlayerId);

        target.RpcSetCustomRole(CustomRoles.Archenemy, log: null);

        _ = new LateTask(() =>
        {
            if (target.GetRoleClass() is Archenemy ae)
                ae.SetDuelist(Player.PlayerId);
        }, 0.1f, "Duelist.SetArchenemy", true);

        SendMessage(
            $"<color={RoleInfo.RoleColorCode}>{target.Data.PlayerName} を宿敵に指定しました！\n相手が死亡すれば追加勝利！</color>",
            Player.PlayerId);

        UtilsNotifyRoles.NotifyRoles();
        return false; // 投票を隠す
    }

    // ─── ★ マドンナ方式: ターン超過で自爆 ────────────────
    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (hasChosenArchenemy) return;    // 決定済みならスキップ
        if (!Player.IsAlive()) return;

        // Madonna: limit <= UtilsGameLog.day で自爆
        if (MeetingLimit > UtilsGameLog.day) return;

        _ = new LateTask(() =>
        {
            if (!Player.IsAlive()) return;
            var state = PlayerState.GetByPlayerId(Player.PlayerId);
            if (state != null) state.DeathReason = CustomDeathReason.Suicide;
            Player.SetRealKiller(Player);
            Player.RpcMurderPlayerV2(Player);
            UtilsGameLog.AddGameLog("Duelist",
                $"{UtilsName.GetPlayerColor(Player)} は期限内に宿敵を決めず自爆した");
        }, 0.1f, "Duelist.Suicide", true);
    }

    // ─── 追加勝利: 生存 かつ 宿敵が死亡 ─────────────────
    bool IAdditionalWinner.CheckWin(ref CustomRoles winnerRole)
    {
        var ae = Archenemy;
        return Player.IsAlive() && ae != null && !ae.IsAlive();
    }

    // ─── 表示 ─────────────────────────────────────────────
    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (!Is(seer) || archenemyPlayerId == byte.MaxValue) return "";
        if (seen.PlayerId == archenemyPlayerId)
            return ColorString(RoleInfo.RoleColor, "χ");
        return "";
    }

    public string GetMarkOthers(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (archenemyPlayerId == byte.MaxValue || !Player.IsAlive()) return "";
        if (seer.PlayerId == archenemyPlayerId && seen.PlayerId == Player.PlayerId)
            return ColorString(RoleInfo.RoleColor, "χ");
        return "";
    }

    // ★ マドンナ風: 未決定なら (day/limit) 表示
    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        if (!hasChosenArchenemy)
        {
            bool danger = UtilsGameLog.day >= MeetingLimit;
            string c = danger ? "#ff1919" : RoleInfo.RoleColorCode;
            return $"<color={c}>({UtilsGameLog.day}/{MeetingLimit})</color>";
        }
        if (archenemyPlayerId == byte.MaxValue)
            return $"<color=#888888>(自爆)</color>";

        var ae = Archenemy;
        bool aeDead = ae == null || !ae.IsAlive();
        return aeDead
            ? $"<color={RoleInfo.RoleColorCode}>(宿敵†✓)</color>"
            : $"<color={RoleInfo.RoleColorCode}>(宿敵♦)</color>";
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";

        string size = isForHud ? "" : "<size=60%>";

        if (!hasChosenArchenemy && isForMeeting)
        {
            int left = MeetingLimit - UtilsGameLog.day;
            string warn = left <= 0
                ? "<color=#ff1919>！期限切れ・次ターン自爆！</color>"
                : $"残り{left}ターン";
            return $"{size}<color={RoleInfo.RoleColorCode}>誰かに投票 → 宿敵を設定 ({warn})\nスキップ or 自投票 → 今ターンはパス</color>";
        }

        if (archenemyPlayerId == byte.MaxValue) return "";

        var ae = Archenemy;
        string aeName = ae?.Data?.PlayerName ?? "???";
        bool aeDead = ae == null || !ae.IsAlive();
        string status = aeDead ? "<color=#00ff88>†</color>" : "<color=#ff4444>生</color>";
        return $"{size}<color={RoleInfo.RoleColorCode}>宿敵: {aeName} {status}</color>";
    }

    // ─── RPC ─────────────────────────────────────────────
    void SendRpc()
    {
        using var sender = CreateSender();
        sender.Writer.Write(archenemyPlayerId);
        sender.Writer.Write(hasChosenArchenemy);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        archenemyPlayerId = reader.ReadByte();
        hasChosenArchenemy = reader.ReadBoolean();
    }
}

// ══════════════════════════════════════════════════════════════
// 宿敵 (決闘者によって指定される)
// 決闘者が死亡し自分が生存 → 追加勝利。
// ══════════════════════════════════════════════════════════════
public sealed class Archenemy : RoleBase, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Archenemy),
            player => new Archenemy(player),
            CustomRoles.Archenemy,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Neutral,
            85290,
            SetupOptionItem,
            "ae",
            "#ff6347",
            (4, 9),
            countType: CountTypes.OutOfGame,
            from: From.TownOfHost_Pko
        );

    public Archenemy(PlayerControl player) : base(RoleInfo, player, () => HasTask.False)
    {
        duelistPlayerId = byte.MaxValue;
    }

    static void SetupOptionItem()
    {
        if (Options.CustomRoleSpawnChances?.TryGetValue(CustomRoles.Archenemy, out var sp) == true)
            sp.SetHidden(true);
        if (Options.CustomRoleCounts?.TryGetValue(CustomRoles.Archenemy, out var cp) == true)
            cp.SetHidden(true);
    }

    public byte duelistPlayerId;

    public void SetDuelist(byte id)
    {
        duelistPlayerId = id;
        SendRpc();
    }

    private PlayerControl Duelist
        => duelistPlayerId == byte.MaxValue ? null : GetPlayerById(duelistPlayerId);

    bool IAdditionalWinner.CheckWin(ref CustomRoles winnerRole)
    {
        var dl = Duelist;
        return Player.IsAlive() && dl != null && !dl.IsAlive();
    }

    public override string GetMark(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false)
    {
        seen ??= seer;
        if (!Is(seer) || duelistPlayerId == byte.MaxValue) return "";
        if (seen.PlayerId == duelistPlayerId)
            return ColorString(RoleInfo.RoleColor, "χ");
        return "";
    }

    public override string GetProgressText(bool comms = false, bool GameLog = false)
    {
        var dl = Duelist;
        if (dl == null) return "";
        bool dlDead = !dl.IsAlive();
        return dlDead
            ? $"<color={RoleInfo.RoleColorCode}>(決闘者†✓)</color>"
            : $"<color={RoleInfo.RoleColorCode}>(決闘者♦)</color>";
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null,
        bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";
        if (duelistPlayerId == byte.MaxValue) return "";

        var dl = Duelist;
        string dlName = dl?.Data?.PlayerName ?? "???";
        bool dlDead = dl == null || !dl.IsAlive();
        string status = dlDead ? "<color=#00ff88>†</color>" : "<color=#ff4444>生</color>";
        string size = isForHud ? "" : "<size=60%>";
        return $"{size}<color={RoleInfo.RoleColorCode}>決闘者: {dlName} {status}  決闘者が死ねば追加勝利</color>";
    }

    void SendRpc()
    {
        using var sender = CreateSender();
        sender.Writer.Write(duelistPlayerId);
    }

    public override void ReceiveRPC(MessageReader reader)
    {
        duelistPlayerId = reader.ReadByte();
    }
}