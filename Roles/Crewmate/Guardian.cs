using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;

using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using static TownOfHost.Modules.SelfVoteManager;

namespace TownOfHost.Roles.Crewmate;

// ===== 守護者 (Guardian) =====
// イントロ：仲間は俺が守護る
// 陣営：クルーメイト
//
// 会議で自投票することで「守護モード」に切り替わる。守護モード中に投票することで、
// 対象を守ることができる(設定タスク数以上完了している必要あり)。
// 守っている間、対象がキルされそうになると、対象の代わりに守護者自身が死亡する。
// 守護者自身がキルされた場合は何も起こらない。
// 守護は指定ターン数(会議数)が経過すると自動的に解除される。
public sealed class Guardian : RoleBase, ISelfVoter
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Guardian),
            player => new Guardian(player),
            CustomRoles.Guardian,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            39400,
            SetupOptionItem,
            "grd",
            "#67E9FF",
            (1, 14),
            from: From.TownOfHost_hamo
        );

    public Guardian(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
    }

    static OptionItem OptionGuardDurationMeetings;
    static OptionItem OptionMaxSimultaneousGuards;
    static OptionItem OptionMaxLifetimeGuards;
    static OptionItem OptionRequiredTaskCount;

    enum OptionName
    {
        GuardianGuardDurationMeetings,
        GuardianMaxSimultaneousGuards,
        GuardianMaxLifetimeGuards,
        GuardianRequiredTaskCount,
    }

    static void SetupOptionItem()
    {
        OptionGuardDurationMeetings = IntegerOptionItem.Create(RoleInfo, 10, OptionName.GuardianGuardDurationMeetings,
            new(1, 10, 1), 2, false)
            .SetValueFormat(OptionFormat.Times);
        OptionMaxSimultaneousGuards = IntegerOptionItem.Create(RoleInfo, 11, OptionName.GuardianMaxSimultaneousGuards,
            new(1, 14, 1), 1, false)
            .SetValueFormat(OptionFormat.Players);
        OptionMaxLifetimeGuards = IntegerOptionItem.Create(RoleInfo, 12, OptionName.GuardianMaxLifetimeGuards,
            new(1, 14, 1), 3, false)
            .SetValueFormat(OptionFormat.Players);
        OptionRequiredTaskCount = IntegerOptionItem.Create(RoleInfo, 13, OptionName.GuardianRequiredTaskCount,
            new(0, 8, 1), 3, false)
            .SetValueFormat(OptionFormat.Pieces);
    }

    // 現在守護モード中かどうか
    private bool isGuardMode;
    // 現在守っている対象と、残り会議数のペア
    private readonly Dictionary<byte, int> guardedTargets = new();
    // これまでに守った(生涯)対象の数(重複カウントしない、対象ごとに1回)
    private readonly HashSet<byte> lifetimeGuardedTargets = new();

    bool ISelfVoter.CanUseVoted() => false;

    private bool HasEnoughTasks()
        => OptionRequiredTaskCount.GetInt() <= 0
        || MyTaskState.HasCompletedEnoughCountOfTasks(OptionRequiredTaskCount.GetInt());

    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)
    {
        if (!Canuseability()) return true;
        if (!Is(voter)) return true;

        // 自投票 => 守護モードのON/OFF切り替え
        if (CheckSelfVoteMode(Player, votedForId, out var status))
        {
            if (status is VoteStatus.Self)
            {
                isGuardMode = !isGuardMode;
                Utils.SendMessage(
                    string.Format(GetString("SkillMode"), GetString("Mode.Guardian"), GetString("Vote.Guardian"))
                    + GetString("VoteSkillMode"),
                    Player.PlayerId);
            }
            if (status is VoteStatus.Skip)
                Utils.SendMessage(GetString("VoteSkillFin"), Player.PlayerId);

            if (status is VoteStatus.Vote)
            {
                TryGuard(votedForId);
            }

            SetMode(Player, status is VoteStatus.Self);
            return false; // 守護者の投票自体は結果に影響させない
        }

        return true;
    }

    private void TryGuard(byte targetId)
    {
        if (targetId == Player.PlayerId)
        {
            Utils.SendMessage(GetString("Guardian.CannotGuardSelf"), Player.PlayerId);
            return;
        }

        var target = PlayerCatch.GetPlayerById(targetId);
        if (target == null || !target.IsAlive())
        {
            Utils.SendMessage(GetString("VoteSkillFin"), Player.PlayerId);
            return;
        }

        if (guardedTargets.ContainsKey(targetId))
        {
            Utils.SendMessage(GetString("Guardian.AlreadyGuarding"), Player.PlayerId);
            return;
        }

        if (guardedTargets.Count >= OptionMaxSimultaneousGuards.GetInt())
        {
            Utils.SendMessage(GetString("Guardian.SimultaneousLimitReached"), Player.PlayerId);
            return;
        }

        if (!lifetimeGuardedTargets.Contains(targetId)
            && lifetimeGuardedTargets.Count >= OptionMaxLifetimeGuards.GetInt())
        {
            Utils.SendMessage(GetString("Guardian.LifetimeLimitReached"), Player.PlayerId);
            return;
        }

        if (!HasEnoughTasks())
        {
            Utils.SendMessage(GetString("Guardian.NotEnoughTasks"), Player.PlayerId);
            return;
        }

        guardedTargets[targetId] = OptionGuardDurationMeetings.GetInt();
        lifetimeGuardedTargets.Add(targetId);

        Utils.SendMessage(
            string.Format(GetString("Guardian.StartGuarding"), UtilsName.GetPlayerColor(target, true)),
            Player.PlayerId);
        // 守った対象には即座に通知する
        Utils.SendMessage(GetString("Guardian.YouAreGuarded"), targetId);

        UtilsGameLog.AddGameLog("Guardian",
            $"{UtilsName.GetPlayerColor(Player)}が{UtilsName.GetPlayerColor(target)}を守護し始めた");
    }

    public override void AfterMeetingTasks()
    {
        if (!AmongUsClient.Instance.AmHost) return;

        // 会議終了ごとに残りターン数を減らし、0になったら守護を解除する
        var expired = new List<byte>();
        var keys = guardedTargets.Keys.ToList();
        foreach (var id in keys)
        {
            guardedTargets[id]--;
            if (guardedTargets[id] <= 0)
                expired.Add(id);
        }
        foreach (var id in expired)
        {
            guardedTargets.Remove(id);
            var p = PlayerCatch.GetPlayerById(id);
            if (p != null)
                Utils.SendMessage(GetString("Guardian.GuardExpired"), id);
        }

        isGuardMode = false;
    }

    // 守護者自身がキルされた場合は何も起こらない(特別扱いしない)。
    public override bool OnCheckMurderAsTarget(MurderInfo info) => true;

    /// <summary>
    /// 守護者が守っている対象がキルされようとしていないかを、キル判定の外側からチェックするための
    /// ヘルパー。CustomRoleManager側のキル処理から、被害者がこの守護者に守られているかを
    /// 判定するために使う。
    /// </summary>
    public bool IsGuarding(byte targetId) => guardedTargets.ContainsKey(targetId);

    /// <summary>
    /// 守っていた対象の代わりに守護者自身を死亡させ、守護を解除する。
    /// </summary>
    public void Sacrifice(PlayerControl target)
    {
        if (target == null) return;
        guardedTargets.Remove(target.PlayerId);

        Utils.SendMessage(string.Format(GetString("Guardian.SacrificeAnnounce"),
            UtilsName.GetPlayerColor(Player, true),
            UtilsName.GetPlayerColor(target, true)));

        UtilsGameLog.AddGameLog("Guardian",
            $"{UtilsName.GetPlayerColor(Player)}が{UtilsName.GetPlayerColor(target)}の身代わりになって死亡した");

        if (Player.IsAlive())
            Player.RpcMurderPlayer(Player);
    }

    /// <summary>
    /// 全生存プレイヤーの中から、指定した対象を守っている守護者を探す。
    /// キル判定処理から使うための静的ヘルパー。
    /// </summary>
    public static Guardian FindGuardianFor(byte targetId)
    {
        foreach (var pc in PlayerCatch.AllAlivePlayerControls)
        {
            if (!pc.Is(CustomRoles.Guardian)) continue;
            if (pc.GetRoleClass() is Guardian g && g.IsGuarding(targetId))
                return g;
        }
        return null;
    }
}
