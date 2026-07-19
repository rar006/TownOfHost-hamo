using AmongUs.GameOptions;

using TownOfHost.Modules;
using TownOfHost.Roles.Core;

namespace TownOfHost.Roles.Crewmate;

// ===== ノーチラス (Nautilus) =====
// From: TownOfHost_hamo
// イントロ：深く、深くへ沈めてやる
// 陣営：クルーメイト / 置き換え：クルーメイト
//
// キルされた時、前回の会議で自分が投票したプレイヤーを道連れにする。
// ただし、前回の会議での投票先に自分が殺された場合は勝利できなくなる。
//
// 仕様メモ：
// ・「前回の会議で投票したプレイヤー」は、ノットヴォウター等で投票権が
//   奪われていても、実際に誰かへ投票していれば対象になる
//  (ノットヴォウターは票の集計に反映されないだけで、投票行為自体は
//   MeetingVoteManager側に記録されるため、追加の分岐は不要)
// ・投票先がその会議で既に死亡している(吊られた等)場合、道連れは発動しない
// ・投票先が追放されたことに起因して自分も同時に死亡する場合
//  (道連れ・後追い等)、自分は勝利できなくなる
public sealed class Nautilus : RoleBase
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Nautilus),
            player => new Nautilus(player),
            CustomRoles.Nautilus,
            () => RoleTypes.Crewmate,
            CustomRoleTypes.Crewmate,
            39100,
            SetupOptionItem,
            "naut",
            "#2700BD",
            (1, 11),
            introSound: () => GetIntroSound(RoleTypes.Crewmate),
            from: From.TownOfHost_hamo
        );

    public Nautilus(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
    }

    static OptionItem OptionTaskReplacement;

    enum OptionName
    {
        NautilusTaskReplacement
    }

    private static void SetupOptionItem()
    {
        // タスクの置き換え(OFF): 通常のクルーメイトタスクをそのまま持つ設定。
        // 将来的に専用タスクを実装する場合に備えたオプションで、現状は表示のみ。
        OptionTaskReplacement = BooleanOptionItem.Create(RoleInfo, 10, OptionName.NautilusTaskReplacement, false, false);
    }

    // このターン(直近の会議明けから次の会議まで)、既に道連れを発動したかどうか。
    // 同一ターン中の多重発動を防ぐ。
    private bool revengedThisTurn;

    public override void OnReportDeadBody(PlayerControl reporter, NetworkedPlayerInfo target)
    {
        // 会議が始まったら、次の会議明けの1ターン分として道連れフラグをリセットする。
        revengedThisTurn = false;
    }

    public override bool OnCheckMurderAsTarget(MurderInfo info)
    {
        if (!AmongUsClient.Instance.AmHost) return true;
        if (revengedThisTurn) return true;

        var killer = info.AttemptKiller;

        if (!Main.LastMeetingVotedFor.TryGetValue(Player.PlayerId, out var votedForId))
            return true; // 前回会議で投票記録が無い(初回の会議より前 等)なら何もしない

        // Skip/NoVote(実プレイヤーへの投票でない)は道連れ対象外
        if (votedForId == MeetingVoteManager.Skip || votedForId == MeetingVoteManager.NoVote)
            return true;

        var votedForPlayer = PlayerCatch.GetPlayerById(votedForId);
        // 投票先が既に死亡している(その会議で吊られた等)場合、このターンの道連れは発動しない
        if (votedForPlayer == null || !votedForPlayer.IsAlive())
            return true;

        // 投票先に自分自身が殺された場合：道連れは発動せず、代わりに自分が勝利できなくなる
        if (votedForPlayer.PlayerId == killer.PlayerId)
        {
            CustomWinnerHolder.CantWinPlayerIds.Add(Player.PlayerId);
            Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()}は前回投票した相手に殺されたため、勝利できなくなりました", "Nautilus");
            return true;
        }

        revengedThisTurn = true;
        var revengeTargetId = votedForPlayer.PlayerId;
        var selfId = Player.PlayerId;

        _ = new LateTask(() =>
        {
            var target = PlayerCatch.GetPlayerById(revengeTargetId);
            var self = PlayerCatch.GetPlayerById(selfId);
            if (target == null || self == null) return;
            if (!target.IsAlive()) return; // 遅延中に既に死亡していた場合は道連れしない

            CustomRoleManager.OnCheckMurder(
                self, target, self, target, true, false,
                deathReason: CustomDeathReason.Revenge);
            Logger.Info($"{self.GetNameWithRole().RemoveHtmlTags()}の道連れ先:{target.GetNameWithRole().RemoveHtmlTags()}", "Nautilus");
        }, 0.1f, "Nautilus.Revenge", true);

        return true;
    }

    /// <summary>
    /// 投票先が追放されたことに起因して自分も同時に死亡する場合
    /// (道連れ・後追い等)、勝利できなくなる。
    /// 会議終了直後、追放者の死亡処理と同じタイミングで各役職が
    /// TryAddAfterMeetingDeathPlayersに自分を追加するため、
    /// その結果を見て判定する。
    /// </summary>
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!Main.LastMeetingVotedFor.TryGetValue(Player.PlayerId, out var votedForId)) return;
        if (votedForId == MeetingVoteManager.Skip || votedForId == MeetingVoteManager.NoVote) return;

        if (!Main.AfterMeetingDeathPlayers.ContainsKey(Player.PlayerId)) return;
        if (!Main.AfterMeetingDeathPlayers.ContainsKey(votedForId)) return;

        // 自分と投票先の両方が「会議後の同時死亡待ち」リストに入っている
        // = 投票先の追放に起因して自分も同時に死ぬ(道連れ・後追い等)ケース
        if (!CustomWinnerHolder.CantWinPlayerIds.Contains(Player.PlayerId))
        {
            CustomWinnerHolder.CantWinPlayerIds.Add(Player.PlayerId);
            Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()}は投票先の追放に伴う同時死亡のため、勝利できなくなりました", "Nautilus");
        }
    }
}
