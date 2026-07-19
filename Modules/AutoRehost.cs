using System;
using System.Collections.Generic;
using InnerNet;
using TMPro;

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
    public static bool IsPending => _pending;
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
        _stuckSince = 0f;
        _lastDiagnosticLogAt = 0f;
        _lastConfirmedInstanceId = 0;

        Logger.Info("Kicked while hosting online lobby. Will attempt to auto-rehost.", "AutoRehost");
        ScheduleTick(_seq, PollInterval);
        ScheduleDialogDismissTick(_seq);
    }

    // 「ルームから追い出されました」「設定を確認」など、立て直しの途中で
    // 割り込んでくる確認ダイアログを自動で閉じ続けるためのポーリング。
    // 特定のUI階層に依存せず、画面上のボタンを走査してラベルで判定する
    // (GMAutoPossess.cs の実装を踏襲)。
    private static readonly HashSet<string> DismissButtonLabels = new()
    {
        "OK", "ok", "確認",
    };
    private const float DialogDismissInterval = 0.15f;

    private static void ScheduleDialogDismissTick(int gen)
        => _ = new LateTask(() => DialogDismissTick(gen), DialogDismissInterval, "AutoRehost.DialogDismissTick", NoLog: true);

    private static void DialogDismissTick(int gen)
    {
        if (gen != _seq || !_pending) return;

        try
        {
            TryDismissConfirmationDialogs();
        }
        catch (Exception e)
        {
            Logger.Exception(e, "AutoRehost");
        }

        ScheduleDialogDismissTick(gen);
    }

    private static float _stuckSince;
    private const float StuckDiagnosticSeconds = 3f; // これだけダイアログを閉じられない状態が続いたら診断ログを出す
    private const float StuckDiagnosticRepeatInterval = 5f; // 診断ログを繰り返す間隔
    private static float _lastDiagnosticLogAt;

    // 直近でConfirmを呼んだCreateGameOptionsインスタンス(多重呼び出し防止用)。
    // 同じダイアログに何度もConfirm()を送ると不安定になりうるため、
    // インスタンスが変わった(=新しいダイアログが開いた)時だけ呼ぶ。
    private static int _lastConfirmedInstanceId;

    /// <summary>
    /// 「設定を確認」ダイアログの正体である CreateGameOptions を直接探し、
    /// アクティブならその場で Confirm() を呼ぶ。ボタン走査より確実。
    /// </summary>
    private static bool TryConfirmCreateGameOptions()
    {
        if (GameStates.IsInGame) return false; // ゲームプレイ中は絶対に触らない(誤操作防止)

        var cgo = UnityEngine.Object.FindObjectOfType<CreateGameOptions>();
        if (cgo == null || !cgo.isActiveAndEnabled) return false;

        var instanceId = cgo.GetInstanceID();
        if (instanceId == _lastConfirmedInstanceId) return false; // 同じダイアログには1回だけ

        try
        {
            // Confirm がビルドによって存在しない可能性があるため、直接呼び出しではなく
            // リフレクション経由にして、無ければ静かに諦める(コンパイルエラーを避ける)。
            var confirmMethod = typeof(CreateGameOptions).GetMethod("Confirm",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (confirmMethod == null)
            {
                Logger.Warn("Auto-rehost: CreateGameOptions.Confirm メソッドが見つかりません。", "AutoRehost");
                return false;
            }
            confirmMethod.Invoke(cgo, null);
            _lastConfirmedInstanceId = instanceId;
            Logger.Info("Auto-rehost: CreateGameOptions.Confirm() を直接呼び出しました。", "AutoRehost");
            return true;
        }
        catch (Exception e)
        {
            Logger.Exception(e, "AutoRehost.TryConfirmCreateGameOptions");
            return false;
        }
    }

    private static void TryDismissConfirmationDialogs()
    {
        // 最優先: 「設定を確認」ダイアログの正体は CreateGameOptions で、
        // ボタンを探してクリックをシミュレートするより、このインスタンスの
        // Confirm() を直接呼ぶ方がはるかに確実(ボタンの内部実装やUI階層に依存しない)。
        if (TryConfirmCreateGameOptions())
        {
            _stuckSince = 0f;
            _lastDiagnosticLogAt = 0f;
            return;
        }

        if (TryDismissAmongPassiveButtons() || TryDismissAmongUnityButtons() || TryDismissAmongSelectables())
        {
            _stuckSince = 0f;
            _lastDiagnosticLogAt = 0f;
            return;
        }

        var mm = MainMenuManagerCapture.Instance;
        var c = AmongUsClient.Instance;
        var atCleanMenu = mm != null && (c == null || !c.AmConnected);
        if (atCleanMenu) { _stuckSince = 0f; _lastDiagnosticLogAt = 0f; return; }

        if (_stuckSince == 0f) _stuckSince = UnityEngine.Time.realtimeSinceStartup;
        var stuckDuration = UnityEngine.Time.realtimeSinceStartup - _stuckSince;

        if (stuckDuration >= StuckDiagnosticSeconds)
        {
            // 原因調査用にボタン一覧を繰り返しログへ出す(1回きりだと機を逃す可能性があるため)
            if (_lastDiagnosticLogAt == 0f || UnityEngine.Time.realtimeSinceStartup - _lastDiagnosticLogAt >= StuckDiagnosticRepeatInterval)
            {
                _lastDiagnosticLogAt = UnityEngine.Time.realtimeSinceStartup;
                LogActiveButtonsForDiagnostics();
            }
        }
    }

    private static void LogActiveButtonsForDiagnostics()
    {
        try
        {
            Logger.Warn("Auto-rehost: 確認ダイアログを自動で閉じられていません。画面上のアクティブなボタン一覧:", "AutoRehost");
            foreach (var btn in UnityEngine.Object.FindObjectsOfType<PassiveButton>())
            {
                if (btn == null || !btn.gameObject.activeInHierarchy) continue;
                var label = GetButtonLabel(
                    btn.GetComponentInChildren<TextMeshPro>(),
                    btn.GetComponentInChildren<TMPro.TextMeshProUGUI>(),
                    btn.GetComponentInChildren<UnityEngine.UI.Text>());
                Logger.Warn($"  [PassiveButton] name=\"{btn.gameObject.name}\" label=\"{label}\"", "AutoRehost");
            }
            foreach (var btn in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>())
            {
                if (btn == null || !btn.gameObject.activeInHierarchy) continue;
                var label = GetButtonLabel(
                    btn.GetComponentInChildren<TextMeshPro>(),
                    btn.GetComponentInChildren<TMPro.TextMeshProUGUI>(),
                    btn.GetComponentInChildren<UnityEngine.UI.Text>());
                Logger.Warn($"  [UnityButton] name=\"{btn.gameObject.name}\" label=\"{label}\"", "AutoRehost");
            }
            foreach (var sel in UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Selectable>())
            {
                if (sel == null || !sel.gameObject.activeInHierarchy) continue;
                if (sel is UnityEngine.UI.Button || sel is PassiveButton) continue;
                var label = GetButtonLabel(
                    sel.GetComponentInChildren<TextMeshPro>(),
                    sel.GetComponentInChildren<TMPro.TextMeshProUGUI>(),
                    sel.GetComponentInChildren<UnityEngine.UI.Text>());
                Logger.Warn($"  [Selectable:{sel.GetType().Name}] name=\"{sel.gameObject.name}\" label=\"{label}\"", "AutoRehost");
            }
        }
        catch (Exception e)
        {
            Logger.Exception(e, "AutoRehost");
        }
    }

    private static bool TryDismissAmongPassiveButtons()
    {
        var buttons = UnityEngine.Object.FindObjectsOfType<PassiveButton>();
        foreach (var btn in buttons)
        {
            if (btn == null || !btn.gameObject.activeInHierarchy) continue;

            // 「ゲーム作成」ボタン自体は絶対に誤クリックしないよう明示的に除外する。
            // (ClickCreateGameButton側で意図したタイミングにのみ押す)
            if (btn.gameObject.name == "CreateGame") continue;

            // ゲームプレイ中は絶対に反応しないようにする(誤操作防止の最重要な安全策)。
            if (GameStates.IsInGame) continue;

            var label = GetButtonLabel(
                btn.GetComponentInChildren<TextMeshPro>(),
                btn.GetComponentInChildren<TMPro.TextMeshProUGUI>(),
                btn.GetComponentInChildren<UnityEngine.UI.Text>());
            if (string.IsNullOrEmpty(label)) continue;

            if (IsDismissLabel(label))
            {
                Logger.Info($"Auto-rehost: dismissing dialog button (PassiveButton) \"{label}\".", "AutoRehost");
                btn.OnClick?.Invoke();
                return true; // 1フレームにつき1つだけ処理し、重なったダイアログは次のポーリングで順次閉じる
            }
        }
        return false;
    }

    // 一部の確認ダイアログはPassiveButtonではなくUnity標準のButtonコンポーネントを
    // 使っている場合があるため、そちらも走査する。
    private static bool TryDismissAmongUnityButtons()
    {
        var buttons = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Button>();
        foreach (var btn in buttons)
        {
            if (btn == null || !btn.gameObject.activeInHierarchy) continue;
            if (btn.gameObject.name == "CreateGame") continue;
            if (GameStates.IsInGame) continue;

            var label = GetButtonLabel(
                btn.GetComponentInChildren<TextMeshPro>(),
                btn.GetComponentInChildren<TMPro.TextMeshProUGUI>(),
                btn.GetComponentInChildren<UnityEngine.UI.Text>());
            if (string.IsNullOrEmpty(label)) continue;

            if (IsDismissLabel(label))
            {
                Logger.Info($"Auto-rehost: dismissing dialog button (UnityButton) \"{label}\".", "AutoRehost");
                btn.onClick?.Invoke();
                return true;
            }
        }
        return false;
    }

    // PassiveButton/Buttonのどちらでもない、より汎用的なSelectable(クリック可能なUI全般)も
    // 走査対象にする。ラベルは自分の子だけでなく「兄弟要素」(同じ親を持つ別オブジェクト)の
    // テキストも見る。ダイアログによってはラベルがボタン自身の子ではなく、隣に配置された
    // 別オブジェクトとして存在することがあるため。
    private static bool TryDismissAmongSelectables()
    {
        var selectables = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Selectable>();
        foreach (var sel in selectables)
        {
            if (sel == null || !sel.gameObject.activeInHierarchy) continue;
            if (sel is UnityEngine.UI.Button || sel is PassiveButton) continue; // 既に別走査で見ている
            if (sel.gameObject.name == "CreateGame") continue;
            if (GameStates.IsInGame) continue;

            var label = GetButtonLabel(
                sel.GetComponentInChildren<TextMeshPro>(),
                sel.GetComponentInChildren<TMPro.TextMeshProUGUI>(),
                sel.GetComponentInChildren<UnityEngine.UI.Text>());

            // 自身の子から取れなければ、兄弟要素(親の他の子)からも探す
            if (string.IsNullOrEmpty(label) && sel.transform.parent != null)
            {
                var parent = sel.transform.parent;
                var tmpSibling = parent.GetComponentInChildren<TextMeshPro>();
                var tmpUguiSibling = parent.GetComponentInChildren<TMPro.TextMeshProUGUI>();
                var textSibling = parent.GetComponentInChildren<UnityEngine.UI.Text>();
                label = GetButtonLabel(tmpSibling, tmpUguiSibling, textSibling);
            }
            if (string.IsNullOrEmpty(label)) continue;

            if (IsDismissLabel(label))
            {
                Logger.Info($"Auto-rehost: dismissing dialog button (Selectable) \"{label}\".", "AutoRehost");
                UnityEngine.EventSystems.ExecuteEvents.Execute(
                    sel.gameObject,
                    new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current),
                    UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                return true;
            }
        }
        return false;
    }

    private static bool IsDismissLabel(string label)
    {
        foreach (var candidate in DismissButtonLabels)
        {
            if (string.Equals(label, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            if (label.Contains(candidate)) return true;
        }
        return false;
    }

    private static string GetButtonLabel(TextMeshPro tmpro, TMPro.TextMeshProUGUI tmproUgui, UnityEngine.UI.Text legacyText = null)
    {
        if (tmpro != null && !string.IsNullOrEmpty(tmpro.text)) return tmpro.text.Trim();
        if (tmproUgui != null && !string.IsNullOrEmpty(tmproUgui.text)) return tmproUgui.text.Trim();
        if (legacyText != null && !string.IsNullOrEmpty(legacyText.text)) return legacyText.text.Trim();
        return "";
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

    /// <summary>
    /// CreateGameOptions.Show の直後(=ゲーム作成画面が開いた直後)に呼ぶ。
    /// 「ゲーム作成」ボタン自体を自動でクリックし、ここまで来た立て直し処理を完結させる。
    /// ボタンが実際にクリック可能になるまで少し待ってから押す。
    /// </summary>
    public static void ClickCreateGameButton()
    {
        var gen = _seq;
        _ = new LateTask(() =>
        {
            if (gen != _seq || !_pending) return;
            try
            {
                var obj = UnityEngine.GameObject.Find("MainMenuManager/MainUI/AspectScaler/CreateGameScreen/ParentContent/Content/CreateGame");
                var button = obj?.GetComponent<PassiveButton>();
                if (button == null)
                {
                    Logger.Warn("Auto-rehost: CreateGame button not found.", "AutoRehost");
                    return;
                }
                button.OnClick.Invoke();
                Logger.Info("Auto-rehost: clicked CreateGame button.", "AutoRehost");
            }
            catch (Exception e)
            {
                Logger.Exception(e, "AutoRehost");
            }
        }, 0.5f, "AutoRehost.ClickCreateGame", NoLog: true);
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
