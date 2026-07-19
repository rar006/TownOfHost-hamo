using AmongUs.GameOptions;
using Hazel;
using UnityEngine;

using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost.Roles.Neutral;

// ===== ヌル (Null) =====
// From: TownOfHost_hamo
// イントロ：1-1=0
// 陣営：ニュートラル / 置き換え：エンジニア(ベントのネイティブクールダウンゲージでタイマー進行を視覚化)
//
// 対称的な2段階の勝利条件を持つ第三陣営。
// フェーズ1: 誰か他プレイヤーが近くにいる状態が累計で設定秒数(既定100秒)続くと、フェーズ2へ移行。
// フェーズ2: 誰も近くにいない状態が累計で設定秒数(既定100秒)続くと、単独勝利。
//
// 実装メモ：
// 以前は各クライアントがTogetherTimer/AloneTimerをローカルで独立計算し、
// フェーズが切り替わる瞬間だけRPCで同期する方式だったため、
// 本人が非ホストの場合、ホスト側とタイマーの進み方がズレてしまい、
// 表示上「進んでいない」ように見える不具合があった。
// タイマーの計算・加算・フェーズ判定はホストのみが行い、
// 一定間隔でRPCにより全員へ最新値を配信、非ホストは受信した値を
// そのまま採用する方式(ホストが唯一の権威)にしている。
//
// 置き換え役職をエンジニアにし、ネイティブのベントクールダウンゲージ
// (AURoleOptions.EngineerCooldown)を「フェーズ達成までの残り時間」として
// 流用することで、タイマーの進み具合を視覚的にも分かりやすくしている。
// クールダウンは減少方向の値のため、(必要時間 - 経過時間)を割り当てている。
//
// 注意: ネイティブのクールダウンは一度値をセットすると、その後は
// ゲーム側で自動的に(現実時間で)減少し続ける。そのため条件を満たして
// いない間(誰も近くにいない/フェーズ2で誰かいる)にApplyGameOptionsで
// 値を送るのを止めるだけでは、ゲージは前回送った値から自動で減り続けて
// しまい、「近づいていないのにクールダウンが動く」ように見えてしまう。
// これを防ぐため、タイマーが実際に進んでいる間だけMarkDirtySettingsを
// 呼んでゲージを進行させ、進んでいない間は毎フレーム同じ残り時間を
// 送り直すことでネイティブ側の自動減少を打ち消し、ゲージを固定する。
public sealed class Null : RoleBase, IAdditionalWinner
{
    public static readonly SimpleRoleInfo RoleInfo =
        SimpleRoleInfo.Create(
            typeof(Null),
            player => new Null(player),
            CustomRoles.Null,
            () => RoleTypes.Engineer,
            CustomRoleTypes.Neutral,
            56500,
            SetupOptionItem,
            "nul",
            "#ffffff",
            (6, 5),
            introSound: () => GetIntroSound(RoleTypes.Crewmate),
            from: From.TownOfHost_hamo
        );

    public Null(PlayerControl player)
    : base(
        RoleInfo,
        player
    )
    {
        TogetherRange = OptionTogetherRange.GetFloat();
        AloneRange = OptionAloneRange.GetFloat();
        TogetherTimeNeeded = OptionTogetherTimeNeeded.GetFloat();
        AloneTimeNeeded = OptionAloneTimeNeeded.GetFloat();

        TogetherTimer = 0f;
        AloneTimer = 0f;
        Phase = 1;
    }

    public override void OnSpawn(bool initialState = false)
    {
        if (AmongUsClient.Instance.AmHost)
            RequestCooldownResync();
    }

    static OptionItem OptionTogetherRange;
    static OptionItem OptionAloneRange;
    static OptionItem OptionTogetherTimeNeeded;
    static OptionItem OptionAloneTimeNeeded;

    enum OptionName
    {
        NullTogetherRange,
        NullAloneRange,
        NullTogetherTimeNeeded,
        NullAloneTimeNeeded
    }

    private float TogetherRange;
    private float AloneRange;
    private float TogetherTimeNeeded;
    private float AloneTimeNeeded;

    // 1 = 「誰かと一緒」フェーズ, 2 = 「誰もいない」フェーズ, 3 = 達成(勝利確定)
    public int Phase;
    private float TogetherTimer;
    private float AloneTimer;

    // 秒の桁が変わったタイミングだけ同期すれば十分なための直前値記録(RPC用)
    private int lastSyncedTogetherSecond = -1;
    private int lastSyncedAloneSecond = -1;

    private static void SetupOptionItem()
    {
        OptionTogetherRange = FloatOptionItem.Create(RoleInfo, 10, OptionName.NullTogetherRange, new(0.25f, 5.0f, 0.25f), 1.0f, false);
        OptionAloneRange = FloatOptionItem.Create(RoleInfo, 11, OptionName.NullAloneRange, new(0.25f, 5.0f, 0.25f), 2.0f, false);
        OptionTogetherTimeNeeded = FloatOptionItem.Create(RoleInfo, 12, OptionName.NullTogetherTimeNeeded, new(5f, 300f, 5f), 100f, false)
            .SetValueFormat(OptionFormat.Seconds);
        OptionAloneTimeNeeded = FloatOptionItem.Create(RoleInfo, 13, OptionName.NullAloneTimeNeeded, new(5f, 300f, 5f), 100f, false)
            .SetValueFormat(OptionFormat.Seconds);
    }

    public override void ApplyGameOptions(IGameOptions opt)
    {
        // ネイティブのクールダウンは一度値をセットすると、その後はApplyGameOptionsを
        // 呼ばなくてもゲーム側が自動的に(現実時間で)減少させ続ける仕様になっている。
        // そのため「条件を満たしている間は値を送らず自然に減らせておき、条件を満たさなく
        // なった瞬間にリセットして止める」という方式にする。
        // (以前は毎フレーム値を送り直していたが、それによって逆に毎フレームゲージが
        //  リセットされ続け、「近づいていないのに動いて見える」不具合の原因になっていた)
        float remaining = Phase switch
        {
            1 => TogetherTimeNeeded - TogetherTimer,
            2 => AloneTimeNeeded - AloneTimer,
            _ => 0f,
        };
        AURoleOptions.EngineerCooldown = Mathf.Max(remaining, 0.1f);
    }

    // 「進行中→停止」に切り替わった瞬間にホストが呼ぶ。
    // RpcResetAbilityCooldownはホスト専用のAPIで、ホストが呼ぶことで
    // 対象クライアント(非ホストの場合も含む)に正しくクールダウンリセットが伝わる。
    private void RequestCooldownResync()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        Player.MarkDirtySettings();
        Player.RpcResetAbilityCooldown(log: false, Sync: true);
    }

    private void SendRPC()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        using var sender = CreateSender();
        sender.Writer.Write((byte)Phase);
        sender.Writer.Write(TogetherTimer);
        sender.Writer.Write(AloneTimer);
    }
    public override void ReceiveRPC(MessageReader reader)
    {
        Phase = reader.ReadByte();
        TogetherTimer = reader.ReadSingle();
        AloneTimer = reader.ReadSingle();
    }

    // タイマーの計算・フェーズ判定はホストのみが行う。非ホストはRPCで受け取った値をそのまま表示する。
    public override void OnFixedUpdate(PlayerControl player)
    {
        if (!Player.IsAlive()) return;
        if (Phase == 3) return;
        if (!AmongUsClient.Instance.AmHost) return;
        if (!GameStates.IsInTask) return;

        bool someoneNearby = false;
        bool phaseChanged = false;

        if (Phase == 1)
        {
            foreach (var pc in PlayerCatch.AllPlayerControls)
            {
                if (pc == null || pc == Player || !pc.IsAlive()) continue;
                if (Vector2.Distance(Player.transform.position, pc.transform.position) <= TogetherRange)
                {
                    someoneNearby = true;
                    break;
                }
            }

            _isAdvancing = someoneNearby;
            if (someoneNearby)
            {
                TogetherTimer += Time.fixedDeltaTime;
                if (TogetherTimer >= TogetherTimeNeeded)
                {
                    Phase = 2;
                    phaseChanged = true;
                    _isAdvancing = false;
                }
            }
        }
        else if (Phase == 2)
        {
            foreach (var pc in PlayerCatch.AllPlayerControls)
            {
                if (pc == null || pc == Player || !pc.IsAlive()) continue;
                if (Vector2.Distance(Player.transform.position, pc.transform.position) <= AloneRange)
                {
                    someoneNearby = true;
                    break;
                }
            }

            _isAdvancing = !someoneNearby;
            if (!someoneNearby)
            {
                AloneTimer += Time.fixedDeltaTime;
                if (AloneTimer >= AloneTimeNeeded)
                {
                    Phase = 3;
                    phaseChanged = true;
                    _isAdvancing = false;

                    // 条件達成: 見た目の役職を通常のクルーメイトに変更する。
                    Player.RpcSetRoleDesync(RoleTypes.Crewmate, Player.GetClientId());
                    Logger.Info($"{Player.GetNameWithRole().RemoveHtmlTags()} は条件を達成し、クルーメイトになりました", "Null");
                }
            }
        }

        // RPC同期(他クライアントのGetLowerTextやログ表示用)は秒の桁が変わった時か、
        // フェーズが切り替わった時だけで十分。
        if (phaseChanged)
        {
            lastSyncedTogetherSecond = Mathf.FloorToInt(TogetherTimer);
            lastSyncedAloneSecond = Mathf.FloorToInt(AloneTimer);
            SendRPC();
        }
        else
        {
            int togetherSecond = Mathf.FloorToInt(TogetherTimer);
            int aloneSecond = Mathf.FloorToInt(AloneTimer);
            if (togetherSecond != lastSyncedTogetherSecond || aloneSecond != lastSyncedAloneSecond)
            {
                lastSyncedTogetherSecond = togetherSecond;
                lastSyncedAloneSecond = aloneSecond;
                SendRPC();
            }
        }

        // 「進行中でない(=タイマーが増えていない)」間は、ネイティブのクールダウンが
        // 現実時間で勝手に減り続けてしまうため、定期的にresyncしてゲージを固定する。
        // (以前は「進行中→停止」に切り替わった瞬間だけresyncしていたが、そもそも
        //  一度も進行しないまま停止が続くケース(誰も近くに来ないまま試合が進む等)では
        //  resyncのトリガー自体が発生せず、「誰もいないのにゲージが減る」不具合の原因になっていた)
        // 毎フレームRPCを送ると負荷が大きいため、一定間隔(StoppedResyncInterval)で間引く。
        if (!_isAdvancing)
        {
            _stoppedResyncTimer += Time.fixedDeltaTime;
            if (_stoppedResyncTimer >= StoppedResyncInterval)
            {
                _stoppedResyncTimer = 0f;
                RequestCooldownResync();
            }
        }
        else
        {
            _stoppedResyncTimer = 0f;
        }
    }

    // 現在タイマーが進行中(誰かと一緒/一人きりの条件を満たしている)かどうか。
    private bool _isAdvancing;
    // 停止中のクールダウンresyncを間引くためのタイマー
    private float _stoppedResyncTimer;
    private const float StoppedResyncInterval = 0.5f;

    public override string GetLowerText(PlayerControl seer, PlayerControl seen = null, bool isForMeeting = false, bool isForHud = false)
    {
        seen ??= seer;
        if (!Is(seer) || !Is(seen)) return "";
        if (isForMeeting) return "";

        string text = Phase switch
        {
            1 => $"{GetString("NullPhase1Text")}: {(int)TogetherTimer}/{(int)TogetherTimeNeeded}",
            2 => $"{GetString("NullPhase2Text")}: {(int)AloneTimer}/{(int)AloneTimeNeeded}",
            _ => GetString("NullPhase3Text"),
        };
        return Utils.ColorString(RoleInfo.RoleColor, text);
    }

    // フェーズ3(達成)に到達していれば勝利
    public bool CheckWin(ref CustomRoles winnerRole)
    {
        if (Player?.IsAlive() != true) return false;
        if (Phase != 3) return false;

        winnerRole = CustomRoles.Null;
        if (CustomWinnerHolder.WinnerTeam != CustomWinner.Null)
            CustomWinnerHolder.ResetAndSetAndChWinner(CustomWinner.Null, Player.PlayerId, true);

        return true;
    }
}
