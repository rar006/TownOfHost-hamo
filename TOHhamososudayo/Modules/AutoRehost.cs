using System;
using InnerNet;

namespace TownOfHost.Modules;

// ===== 配信中の急な切断(kick)対策: 部屋の自動立て直し =====
// オンラインロビーのホスト中に公式サーバーから切断された場合、MainMenuへ戻った後
// 自動で「ロビーを作成(MainMenuManager.OpenCreateGame)」を実行し、部屋を立て直す。
// リージョンやゲームモードの設定はゲームクライアント側の状態としてそのまま保持されるため、
// こちら側で明示的に再適用する処理は行わない。
//
// 大まかな流れ:
//   1. OnGameJoined (Postfix) でホスト状態のラッチを更新する
//      (切断処理の途中で AmHost が先に false へ倒れることがあるため、
//       "綺麗な瞬間" である OnGameJoined の値を信用する)
//   2. OnDisconnected (Postfix) で「オンラインホストだった」かつ
//      「自分から抜けたのではない」切断を検知したら Start() する
//   3. MainMenu に戻り、一定時間 (SettleSeconds) 状態が安定するのを待ってから
//      OpenCreateGame() を呼び出す
//   4. 新しい部屋に入れたかどうかは OnGameJoined (GameId が変わったか) で判定する
//   5. 一定時間たっても入れなければ再試行し、MaxAttempts 回失敗したら諦める
public static class AutoRehost
{
    private const float PollInterval = 0.5f;         // MainMenuの状態を確認する間隔
    private const float SettleSeconds = 1f;           // MainMenuに戻ってから安定を待つ時間
    private const float AttemptTimeoutSeconds = 20f;  // OpenCreateGame後、成功しなければ再試行するまでの時間
    private const int MaxAttempts = 5;

    private static bool _pending;
    private static int _attempts;
    private static int _seq; // 世代トークン。古いLateTaskを無効化する
    private static float _cleanSince;
    private static int _oldGameId;
    private static bool _hostingOnlineLatch;

    /// <summary>AmongUsClient.OnGameJoined の Postfix から呼ぶ</summary>
    public static void NotifyGameJoined()
    {
        var c = AmongUsClient.Instance;
        if (c == null) return;

        // ホスト状態のラッチ更新 (綺麗な瞬間に記録しておき、切断時の判定に使う)
        _hostingOnlineLatch = c.AmHost && GameStates.IsOnlineGame;

        if (!_pending) return;
        if (!(c.AmHost && GameStates.IsOnlineGame)) return;
        if (c.GameId == _oldGameId) return; // 同じ部屋への再接続は成功とみなさない

        Success();
    }

    /// <summary>AmongUsClient.OnDisconnected の Postfix から呼ぶ</summary>
    public static void NotifyDisconnected()
    {
        if (!Main.AutoRehost.Value) return;
        if (_pending) return;

        var c = AmongUsClient.Instance;
        var wasHostingOnline = _hostingOnlineLatch || (c != null && c.AmHost && GameStates.IsOnlineGame);
        if (!wasHostingOnline) return;

        // 自分から部屋を抜けた/破棄した場合は対象外
        var reason = c?.LastDisconnectReason ?? DisconnectReasons.Unknown;
        if (reason is DisconnectReasons.ExitGame or DisconnectReasons.Destroy) return;

        Start(c);
    }

    private static void Start(AmongUsClient c)
    {
        _pending = true;
        _attempts = 0;
        _seq++;
        _cleanSince = 0f;
        _oldGameId = c?.GameId ?? 0;

        Logger.Info("Kicked while hosting online lobby. Will attempt to auto-rehost.", "AutoRehost");
        ScheduleTick(_seq, PollInterval);
    }

    private static void ScheduleTick(int gen, float delay)
        => _ = new LateTask(() => Tick(gen), delay, "AutoRehost.Tick", NoLog: true);

    private static void Tick(int gen)
    {
        if (gen != _seq || !_pending) return;

        var mm = MainMenuManagerCapture.Instance;
        var c = AmongUsClient.Instance;
        var atCleanMenu = mm != null && (c == null || !c.AmConnected);

        if (!atCleanMenu)
        {
            _cleanSince = 0f;
            ScheduleTick(gen, PollInterval);
            return;
        }

        if (_cleanSince == 0f)
            _cleanSince = UnityEngine.Time.realtimeSinceStartup;

        if (UnityEngine.Time.realtimeSinceStartup - _cleanSince < SettleSeconds)
        {
            ScheduleTick(gen, PollInterval);
            return;
        }

        _attempts++;
        Logger.Info($"Auto-rehost: attempt {_attempts}/{MaxAttempts}", "AutoRehost");
        try
        {
            mm.OpenCreateGame();
        }
        catch (Exception e)
        {
            Logger.Exception(e, "AutoRehost");
        }

        if (_attempts >= MaxAttempts)
        {
            ScheduleGiveUpCheck(gen);
            return;
        }

        ScheduleRetryCheck(gen);
    }

    private static void ScheduleRetryCheck(int gen)
    {
        _ = new LateTask(() =>
        {
            if (gen != _seq || !_pending) return;
            // まだ新しい部屋に入れていなければ、次の試行へ
            _cleanSince = 0f;
            Tick(gen);
        }, AttemptTimeoutSeconds, "AutoRehost.RetryCheck", NoLog: true);
    }

    private static void ScheduleGiveUpCheck(int gen)
    {
        _ = new LateTask(() =>
        {
            if (gen != _seq || !_pending) return;
            GiveUp();
        }, AttemptTimeoutSeconds, "AutoRehost.GiveUpCheck", NoLog: true);
    }

    private static void Success()
    {
        Logger.Info($"Auto-rehost succeeded (attempt {_attempts}, gameId={AmongUsClient.Instance?.GameId}).", "AutoRehost");
        _pending = false;
        _seq++;
    }

    private static void GiveUp()
    {
        Logger.Warn($"Auto-rehost gave up after {MaxAttempts} attempts.", "AutoRehost");
        _pending = false;
        _seq++;
    }
}

/// <summary>
/// MainMenuManagerのインスタンスを保持するだけの小さなクラス。
/// (MainMenuManagerは他のシングルトンと違いstatic Instanceを持たないため、
/// Start時のHarmonyパッチで拾っておく)
/// </summary>
public static class MainMenuManagerCapture
{
    public static MainMenuManager Instance;
}
