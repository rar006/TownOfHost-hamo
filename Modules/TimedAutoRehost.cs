using System;

namespace TownOfHost.Modules;

// ===== 「次のロビーで〇分たったら自動で立て直す」機能 =====
// 「設定試合数ごとに立て直す」(AutoRehost.TriggerPeriodicRehost)とは独立した、
// 時間ベースの立て直し機能。
//
// 流れ:
//   1. 試合終了後、通常通り(または自動戻り機能で)ロビー(GameStartManagerの待機画面)に戻る
//   2. ロビーの待機画面に入った瞬間(GameStartManager.Start)にタイマーを開始する
//   3. 設定した分数が経過した時点でまだロビー画面にいれば、自動立て直しをトリガーする
//   4. 途中で試合が始まった/部屋を離れた等でロビーを離れたらタイマーはキャンセルする
//
// ===== 「1回立て直したら以降は自動で止める」仕様 =====
// 一度立て直しが発動したら、そのプロセス(TownOfHostが起動している間)ではもう
// 自動では再度立て直さない。これは「立て直し後も新しいロビーで再度タイマーが動き、
// 何度も連続で立て直され続けてしまう」という報告を受けての仕様変更。
// 再度有効にしたい場合は、設定のON/OFFを一度切り替える(Resetを呼ぶ)ことで再度動作する。
public static class TimedAutoRehost
{
    private static int _seq; // 世代トークン。古いLateTaskを無効化する
    private static bool _hasTriggeredOnce; // 一度でも立て直しを実行したらtrueにし、以降は動作させない

    // 「このプロセスで一度でも試合が完了したか」を明示的に管理するフラグ。
    // 最初に部屋を作っただけの状態では、まだ「立て直す」べき試合が無いため、
    // このフラグがtrueになるまでは絶対に発動しない。
    private static bool _hasCompletedAnyGame = false;

    // ===== 前回の試合の人数(軽量に1回だけ記録) =====
    // 「最低人数を指定する」判定に使う。試合中/ロビー中に毎回GameData.PlayerCountを
    // 見に行くと(頻度によっては)負荷になりうるため、試合が終わったタイミングで
    // 1回だけ記録し、次のロビーではその値を使い回す(そのロビー内で何度参照しても
    // 追加の負荷が発生しない)。
    public static int PreviousGamePlayerCount { get; private set; } = -1;

    /// <summary>
    /// EndGameManager.ShowButtons の Postfix から呼ぶ。試合が1回完了したことを記録する。
    /// 併せて、今回の試合の人数を「前回の試合人数」として1回だけ記録する。
    /// </summary>
    public static void NotifyGameEnded()
    {
        _hasCompletedAnyGame = true;
        try
        {
            PreviousGamePlayerCount = GameData.Instance != null ? GameData.Instance.PlayerCount : -1;
        }
        catch
        {
            // 取得に失敗した場合は前回値を維持する(意図せず0人などにならないようにするため)
        }
    }

    /// <summary>
    /// GameStartManager.Start (ロビー待機画面が表示された瞬間) から呼ぶ。
    /// ホストかつ設定がONの場合、指定分数後に立て直しをトリガーするタイマーを(再)設定する。
    /// ただし、一度でも立て直しを実行済みの場合は何もしない(1回だけ動作する仕様のため)。
    /// また、このプロセスでまだ一度も試合が完了していない(=最初に部屋を作っただけ)場合も、
    /// 「立て直し」の対象になる試合がまだ無いため発動しない。
    /// </summary>
    public static void NotifyEnteredLobby()
    {
        _seq++; // 新しくロビーに入るたび、以前のタイマーは無効化する
        var gen = _seq;

        if (_hasTriggeredOnce) return; // 既に1回立て直し済みなら、以降は自動で動作させない
        if (!Options.OptionTimedAutoRehost.GetBool()) return;
        if (!_hasCompletedAnyGame) return;

        var c = AmongUsClient.Instance;
        if (c == null || !c.AmHost || !GameStates.IsOnlineGame) return;

        int minutes = Options.OptionTimedAutoRehostMinutes.GetInt();
        if (minutes <= 0) return;

        float delaySeconds = minutes * 60f;

        Logger.Info($"Timed auto-rehost: ロビーに入りました。{minutes}分後に自動で立て直します。", "TimedAutoRehost");

        _ = new LateTask(() =>
        {
            if (gen != _seq) return; // 既に別のロビー状態に遷移している場合は何もしない
            if (!Options.OptionTimedAutoRehost.GetBool()) return;
            if (_hasTriggeredOnce) return;

            var client = AmongUsClient.Instance;
            if (client == null || !client.AmHost || !GameStates.IsOnlineGame) return;

            // 試合中(ロビーではない)なら発動しない。試合終了後、次にロビーへ戻った時点で
            // NotifyEnteredLobbyから改めてタイマーが仕掛けられる。
            if (GameStates.IsInGame) return;

            Logger.Info($"Timed auto-rehost: {minutes}分経過したため自動で立て直します。以降は自動で立て直しません。", "TimedAutoRehost");
            _hasTriggeredOnce = true;
            AutoRehost.TriggerTimedRehost();
        }, delaySeconds, "TimedAutoRehost.Trigger", true);
    }

    /// <summary>
    /// 試合開始等でロビーを離れたタイミングで呼ぶ。既存のタイマーを無効化する。
    /// </summary>
    public static void NotifyLeftLobby()
    {
        _seq++;
    }

    /// <summary>
    /// 「1回立て直したら停止する」状態をリセットする。
    /// 設定のON/OFFを切り替えた際に呼ぶことで、再度有効化できるようにする。
    /// </summary>
    public static void ResetTriggeredState()
    {
        _hasTriggeredOnce = false;
    }
}
