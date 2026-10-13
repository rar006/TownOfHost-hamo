using System.Collections.Generic;

using AmongUs.GameOptions;
using Hazel;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using static TownOfHost.Modules.SelfVoteManager;

namespace TownOfHost.Roles.Crewmate;

// ===== コレクシス (Corexis) =====
// カラーコード: #06C755
// イントロ：君たちの場所を共有しよう
// 陣営：クルーメイト / 置き換え：クルーメイト
//
// 会議中、自投票の後に2人へ順番に投票することで、その2人を「シェア状態」にする。
// シェアされた本人は誰かとシェアされたことだけ通知され、誰とかは分からない。
// 設定ターン数が経った会議の開始前(会議前)にどの部屋にいたかをお互いに通知する。
// 一度シェアした組み合わせは再度シェア対象にできない。
// シェア発動ターン数中は能力を使用できない。
// 遺言：死亡時、シェア中の対象がいれば、会議でその対象たちに「誰がコレクシスだったか」を通知する。
public sealed class Corexis : RoleBase, ISelfVoter
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Corexis),
            player => new Corexis(player),
            CustomRoles.Corexis,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            96100,
            SetupOptionItem,
            "Cor",
            "#06C755",
            (5, 1),
            from: From.TownOfHost_hamo
        );

    public Corexis(PlayerControl player) : base(RoleInfo, player)
    {
        abilityUseCount = OptionAbilityUseCount.GetInt();
        shareTurns = OptionShareTurns.GetInt();
        canKnowBeforeConnect = OptionCanKnowBeforeConnect.GetBool();
        canShareSamePlayerTwice = OptionCanShareSamePlayerTwice.GetBool();
        hasWill = OptionHasWill.GetBool();

        remainingUses = abilityUseCount;
        selectionState = SelectionState.None;
        firstTarget = byte.MaxValue;
        turnsSinceLastShare = int.MaxValue;
        pendingShares = new List<SharePair>();
        activeShares = new List<SharePair>();
        pastSharedPairs = new HashSet<(byte, byte)>();
    }

    static OptionItem OptionAbilityUseCount;
    static OptionItem OptionShareTurns;
    static OptionItem OptionCanKnowBeforeConnect;
    static OptionItem OptionCanShareSamePlayerTwice;
    static OptionItem OptionHasWill;

    enum OptionName
    {
        CorexisAbilityUseCount,
        CorexisShareTurns,
        CorexisCanKnowBeforeConnect,
        CorexisCanShareSamePlayerTwice,
        CorexisHasWill,
    }

    private enum SelectionState
    {
        None,
        WaitingFirstTarget,
        WaitingSecondTarget,
    }

    private readonly struct SharePair
    {
        public readonly byte PlayerA;
        public readonly byte PlayerB;
        public SharePair(byte a, byte b) { PlayerA = a; PlayerB = b; }
    }

    private readonly int abilityUseCount;
    private readonly int shareTurns;
    private readonly bool canKnowBeforeConnect;
    private readonly bool canShareSamePlayerTwice;
    private readonly bool hasWill;

    private int remainingUses;
    private SelectionState selectionState;
    private byte firstTarget;
    // シェア発動までの残りターン数(発動待ちのシェアがない間はint.MaxValue)
    private int turnsSinceLastShare;
    // 発動待ち(まだ通知していない)のシェアペア
    private readonly List<SharePair> pendingShares;
    // 現在有効なシェアペア(通知済み、まだ解除されていない)
    private readonly List<SharePair> activeShares;
    // 過去にシェア済みだったペアの記録(重複シェア防止用)
    private readonly HashSet<(byte, byte)> pastSharedPairs;

    static void SetupOptionItem()
    {
        OptionAbilityUseCount = IntegerOptionItem.Create(RoleInfo, 10, OptionName.CorexisAbilityUseCount,
            new(0, 99, 1), 3, false)
            .SetValueFormat(OptionFormat.Times);
        OptionShareTurns = IntegerOptionItem.Create(RoleInfo, 11, OptionName.CorexisShareTurns,
            new(0, 99, 1), 1, false)
            .SetValueFormat(OptionFormat.Times);
        OptionCanKnowBeforeConnect = BooleanOptionItem.Create(RoleInfo, 12, OptionName.CorexisCanKnowBeforeConnect, false, false);
        OptionCanShareSamePlayerTwice = BooleanOptionItem.Create(RoleInfo, 13, OptionName.CorexisCanShareSamePlayerTwice, false, false);
        OptionHasWill = BooleanOptionItem.Create(RoleInfo, 14, OptionName.CorexisHasWill, true, false);
    }

    bool ISelfVoter.CanUseVoted() => Player.IsAlive() && remainingUses > 0 && turnsSinceLastShare >= shareTurns;

    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)
    {
        if (!Is(voter) || !Canuseability()) return true;
        if (!Player.IsAlive() || remainingUses <= 0) return true;
        // シェア発動待ちのターン中は能力を使用できない。
        if (turnsSinceLastShare < shareTurns) return true;

        if (CheckSelfVoteMode(Player, votedForId, out var status))
        {
            if (status is VoteStatus.Self)
            {
                selectionState = SelectionState.WaitingFirstTarget;
                firstTarget = byte.MaxValue;
                Utils.SendMessage(GetString("Corexis.SelectFirstTarget"), Player.PlayerId);
                SetMode(Player, true);
                return false;
            }

            if (status is VoteStatus.Skip)
            {
                selectionState = SelectionState.None;
                firstTarget = byte.MaxValue;
                Utils.SendMessage(GetString("Corexis.Cancelled"), Player.PlayerId);
                SetMode(Player, false);
                return false;
            }

            if (status is VoteStatus.Vote)
            {
                if (votedForId == Player.PlayerId || votedForId >= 253)
                {
                    selectionState = SelectionState.None;
                    firstTarget = byte.MaxValue;
                    Utils.SendMessage(GetString("Corexis.InvalidTarget"), Player.PlayerId);
                    SetMode(Player, false);
                    return false;
                }

                if (selectionState == SelectionState.WaitingFirstTarget)
                {
                    firstTarget = votedForId;
                    selectionState = SelectionState.WaitingSecondTarget;
                    Utils.SendMessage(GetString("Corexis.SelectSecondTarget"), Player.PlayerId);
                    // まだ2人目を選ぶ必要があるため、モードを維持したまま自投票判定を続ける。
                    SetMode(Player, true);
                    return false;
                }

                if (selectionState == SelectionState.WaitingSecondTarget)
                {
                    var secondTarget = votedForId;
                    selectionState = SelectionState.None;

                    if (secondTarget == firstTarget && !canShareSamePlayerTwice)
                    {
                        Utils.SendMessage(GetString("Corexis.SameTargetError"), Player.PlayerId);
                        firstTarget = byte.MaxValue;
                        SetMode(Player, false);
                        return false;
                    }

                    var pair = MakePair(firstTarget, secondTarget);
                    if (!canShareSamePlayerTwice && pastSharedPairs.Contains(pair))
                    {
                        Utils.SendMessage(GetString("Corexis.AlreadySharedError"), Player.PlayerId);
                        firstTarget = byte.MaxValue;
                        SetMode(Player, false);
                        return false;
                    }

                    DoShare(firstTarget, secondTarget);
                    firstTarget = byte.MaxValue;
                    SetMode(Player, false);
                    return false;
                }
            }
        }

        return true;
    }

    private static (byte, byte) MakePair(byte a, byte b) => a < b ? (a, b) : (b, a);

    private void DoShare(byte a, byte b)
    {
        remainingUses--;
        turnsSinceLastShare = 0;
        pendingShares.Add(new SharePair(a, b));
        pastSharedPairs.Add(MakePair(a, b));

        Utils.SendMessage(GetString("Corexis.ShareRegistered"), Player.PlayerId);

        if (canKnowBeforeConnect)
        {
            var playerA = a.GetPlayerControl();
            var playerB = b.GetPlayerControl();
            if (playerA != null) Utils.SendMessage(GetString("Corexis.YouWereShared"), a);
            if (playerB != null) Utils.SendMessage(GetString("Corexis.YouWereShared"), b);
        }

        UtilsGameLog.AddGameLog("Corexis",
            $"{UtilsName.GetPlayerColor(Player)}が{UtilsName.GetPlayerColor(a.GetPlayerControl())}と{UtilsName.GetPlayerColor(b.GetPlayerControl())}をシェアした");
    }

    public override void OnStartMeeting()
    {
        selectionState = SelectionState.None;
        firstTarget = byte.MaxValue;

        // 遺言(死亡時に発動待ちだった通知)があれば、この会議で流す。
        if (willTriggered && willTargets != null)
        {
            willTriggered = false;
            foreach (var id in willTargets)
            {
                var pc = id.GetPlayerControl();
                if (pc == null || !pc.IsAlive()) continue;
                Utils.SendMessage(string.Format(GetString("Corexis.WillNotify"), UtilsName.GetPlayerColor(Player)), id);
            }
        }

        // 発動待ちのシェアがある場合、ターン数のカウントを進める。
        if (turnsSinceLastShare < shareTurns)
        {
            turnsSinceLastShare++;
        }

        // 【重要・不具合修正】以前は、シェア発動待ち(位置通知がまだの状態)の間に
        // コレクシスが死亡すると、pendingSharesをまるごと破棄していたため、
        // シェア自体が「無かったこと」になり、遺言(誰がコレクシスだったか通知)の
        // 対象にもならなくなってしまっていた。
        // 発動待ちの間の死亡では、位置の相互通知(NotifySharedLocations)は行わない
        // ものの、シェア自体は成立したものとして扱い、遺言の対象には含める。
        if (!Player.IsAlive() && pendingShares.Count > 0)
        {
            activeShares.AddRange(pendingShares);
            pendingShares.Clear();
        }

        if (turnsSinceLastShare >= shareTurns && pendingShares.Count > 0)
        {
            foreach (var pair in pendingShares)
            {
                NotifySharedLocations(pair);
                activeShares.Add(pair);
            }
            pendingShares.Clear();
        }
    }

    private void NotifySharedLocations(SharePair pair)
    {
        var playerA = pair.PlayerA.GetPlayerControl();
        var playerB = pair.PlayerB.GetPlayerControl();
        if (playerA == null || playerB == null) return;

        NotifyOneSide(playerA, playerB);
        NotifyOneSide(playerB, playerA);
    }

    private void NotifyOneSide(PlayerControl self, PlayerControl partner)
    {
        if (self == null || !self.IsAlive()) return;

        if (partner == null || !partner.IsAlive())
        {
            Utils.SendMessage(GetString("Corexis.PartnerNotInRoom"), self.PlayerId);
            return;
        }

        var psr = partner.GetPlainShipRoom();
        if (psr == null)
        {
            Utils.SendMessage(GetString("Corexis.PartnerNotInRoom"), self.PlayerId);
        }
        else
        {
            var roomName = GetString($"{psr.RoomId}");
            Utils.SendMessage(string.Format(GetString("Corexis.ShareNotify"), roomName), self.PlayerId);
        }
    }

    public override void OnDead(PlayerControl player)
    {
        if (!hasWill || !Is(player)) return;

        // 【重要・不具合修正】以前はactiveShares(既に位置通知が済んだシェア)のみを
        // 遺言の対象にしていたが、これだと「シェアを発動した直後、位置通知が
        // 行われる前(pendingShares段階)に死亡した」場合に遺言が一切発動しなかった。
        // まだ通知前のpendingSharesも遺言の対象に含める。
        if (activeShares.Count == 0 && pendingShares.Count == 0) return;

        var targets = new HashSet<byte>();
        foreach (var pair in activeShares)
        {
            targets.Add(pair.PlayerA);
            targets.Add(pair.PlayerB);
        }
        foreach (var pair in pendingShares)
        {
            targets.Add(pair.PlayerA);
            targets.Add(pair.PlayerB);
        }
        willTargets = targets;
        willTriggered = true;
    }

    private HashSet<byte> willTargets;
    private bool willTriggered;

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!isForMeeting || !Is(seer) || seer.PlayerId != seen.PlayerId || !Player.IsAlive()) return "";

        var prefix = isForHud ? "" : "<size=60%>";
        var c = RoleInfo.RoleColorCode;

        if (remainingUses <= 0)
            return $"{prefix}<color={c}>シェア能力: 使用回数なし</color>";

        if (turnsSinceLastShare < shareTurns)
            return $"{prefix}<color={c}>シェア発動まで: {shareTurns - turnsSinceLastShare}ターン</color>";

        return selectionState switch
        {
            SelectionState.WaitingFirstTarget => $"{prefix}<color={c}>1人目を選択中...(残り{remainingUses}回)</color>",
            SelectionState.WaitingSecondTarget => $"{prefix}<color={c}>2人目を選択中...(残り{remainingUses}回)</color>",
            _ => $"{prefix}<color={c}>会議自投票→シェア開始(残り{remainingUses}回)</color>",
        };
    }

    public static System.Collections.Generic.Dictionary<int, Achievement> achievements = new();
    [Attributes.PluginModuleInitializer]
    public static void Load()
    {
        var n1 = new Achievement(RoleInfo, 0, 1, 0, 1);
        achievements.Add(0, n1);
    }
}
