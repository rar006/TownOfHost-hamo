using AmongUs.GameOptions;

using TownOfHost.Modules;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;
using static TownOfHost.Modules.SelfVoteManager;

namespace TownOfHost.Roles.Crewmate;

// ===== 大統領 (President) =====
// イントロ：異議あり!!
// 陣営：クルーメイト / 置き換え：クルーメイト
//
// 会議中に自投票して対象を決めておくと、一度だけ、投票結果が何であろうと
// その対象を吊ることができる。使用後は(設定次第で)名前の下に「大統領」と表示される。
// 会議終了時、対象を吊った場合は全員に「大統領〇〇によって△△が追放された。」と表示する。
public sealed class President : RoleBase, ISelfVoter
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(President),
            player => new President(player),
            CustomRoles.President,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            39200,
            SetupOptionItem,
            "pre",
            "#20b2aa",
            (1, 12),
            from: From.TownOfHost_hamo
        );

    public President(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
    }

    static OptionItem OptionShowUsedLabel;
    static OptionItem OptionShowBeforeUse;

    enum OptionName
    {
        PresidentShowUsedLabel,
        PresidentShowBeforeUse
    }

    static void SetupOptionItem()
    {
        OptionShowUsedLabel = BooleanOptionItem.Create(RoleInfo, 10, OptionName.PresidentShowUsedLabel, true, false);
        // ONにすると、能力を使う前(usedAbility=falseの間)からでも「大統領」の肩書き表示を出す。
        // (元々はOptionShowUsedLabelの通り、能力使用後にしか表示されない仕様だった)
        OptionShowBeforeUse = BooleanOptionItem.Create(RoleInfo, 11, OptionName.PresidentShowBeforeUse, false, false)
            .SetParent(OptionShowUsedLabel);
    }

    // 能力を既に使い切ったかどうか(「一度だけ」の制約)
    private bool usedAbility;

    // 能力使用時のアナウンスを、今の会議終了時ではなく次の会議開始時まで持ち越すための保留メッセージ。
    private string pendingExileAnnounce;

    bool ISelfVoter.CanUseVoted() => false;

    public override bool CheckVoteAsVoter(byte votedForId, PlayerControl voter)
    {
        if (usedAbility || !Canuseability()) return true;

        if (Is(voter))
        {
            if (CheckSelfVoteMode(Player, votedForId, out var status))
            {
                if (status is VoteStatus.Self)
                    Utils.SendMessage(string.Format(GetString("SkillMode"), GetString("Mode.President"), GetString("Vote.President")) + GetString("VoteSkillMode"), Player.PlayerId);
                if (status is VoteStatus.Skip)
                    Utils.SendMessage(GetString("VoteSkillFin"), Player.PlayerId);
                if (status is VoteStatus.Vote)
                {
                    ExecuteForceExile(votedForId);
                }
                SetMode(Player, status is VoteStatus.Self);
                return status is VoteStatus.Vote;
            }
        }

        return true;
    }

    public override (byte? votedForId, int? numVotes, bool doVote) ModifyVote(byte voterId, byte sourceVotedForId, bool isIntentional)
    {
        var (votedForId, numVotes, doVote) = base.ModifyVote(voterId, sourceVotedForId, isIntentional);
        var baseVote = (votedForId, numVotes, doVote);
        if (usedAbility || !isIntentional || !Canuseability() || voterId != Player.PlayerId
            || sourceVotedForId == Player.PlayerId || sourceVotedForId >= 253 || !Player.IsAlive())
        {
            return baseVote;
        }

        ExecuteForceExile(sourceVotedForId);
        return (votedForId, numVotes, false);
    }

    private void ExecuteForceExile(byte targetId)
    {
        var target = PlayerCatch.GetPlayerById(targetId);
        if (target == null) return;

        usedAbility = true;

        MeetingHudPatch.TryAddAfterMeetingDeathPlayers(CustomDeathReason.Vote, targetId);
        target.SetRealKiller(Player);
        MeetingVoteManager.Instance.ClearAndExile(Player.PlayerId, targetId);

        // 会議終了時ではなく、次の会議開始時に全員へ表示するため、ここでは保留するだけにする。
        pendingExileAnnounce = string.Format(GetString("President.ExileAnnounce"), UtilsName.GetPlayerColor(Player), UtilsName.GetPlayerColor(target));

        UtilsGameLog.AddGameLog("President", string.Format(GetString("President.log"), UtilsName.GetPlayerColor(Player), UtilsName.GetPlayerColor(target)));
    }

    // 次の会議が始まった瞬間、前回の能力使用結果を全員に知らせる。
    public override void OnStartMeeting()
    {
        if (pendingExileAnnounce == null) return;

        Utils.SendMessage(pendingExileAnnounce);
        pendingExileAnnounce = null;
    }

    // スター/君臨者(King)と同じ仕組みで、能力使用後は名前の下の役職名表示を
    // 「大統領」という金色の肩書きに上書きして、他プレイヤー全員に見えるようにする。
    // (RpcSetNamePrivate等でプレイヤー名の文字列自体を書き換える方式は、
    //  この仕組みと競合してうまく反映されなかったため、King/MadKaiserと
    //  同じOverrideDisplayRoleNameAsSeenのAPIに揃えた)
    public override void OverrideDisplayRoleNameAsSeen(PlayerControl seer, ref bool enabled, ref UnityEngine.Color roleColor, ref string roleText, ref bool addon)
    {
        seer ??= Player;
        if (seer == Player) return;
        if (!OptionShowUsedLabel.GetBool() || !Player.IsAlive()) return;
        // 通常は能力使用後(usedAbility=true)のみ表示するが、
        // OptionShowBeforeUseがONの場合は能力使用前でも表示する。
        if (!usedAbility && !OptionShowBeforeUse.GetBool()) return;

        enabled = true;
        addon = false;
        roleColor = StringHelper.CodeColor("#FFD700");
        roleText = GetString("President.UsedLabel");
    }

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;

        // 会議中、自分だけに見える「自投票モード中」の案内
        if (isForMeeting && Player.IsAlive() && !usedAbility && seer.PlayerId == seen.PlayerId && Canuseability())
        {
            var mes = $"<color={RoleInfo.RoleColorCode}>{GetString("SelfVoteRoleInfoMeg")}</color>";
            return isForHud ? mes : $"<size=40%>{mes}</size>";
        }

        return "";
    }
}