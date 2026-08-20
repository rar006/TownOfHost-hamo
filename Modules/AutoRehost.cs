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



    /// <summary>MainMenuManager.OpenCreateGame の Postfix から呼ぶ(部屋作成を開始した瞬間)。

    /// OnGameJoinedの成功を待たずにフラグを立てることで、

    /// 「部屋作成の途中(設定確認画面など)で切断された」場合も立て直し対象にする。</summary>

    public static void NotifyCreatingGame()

    {

        _hostingOnlineLatch = true;

    }



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



        // ゲーム開始中/試合中に切断された場合は、自動立て直しを行わない

        // (進行中のゲームを強制的に終わらせてしまうため)。

        // ※IsLobbyは「現在接続中かどうか」も条件に含むため、切断直後は常にfalseになってしまい使えない。

        //   接続状態に関係なく試合中かどうかを保持しているGameStates.InGameを使う。

        if (GameStates.InGame) return;



        // 自分から部屋を抜けた/破棄した場合は対象外

        var reason = c?.LastDisconnectReason ?? DisconnectReasons.Unknown;

        if (reason is DisconnectReasons.ExitGame or DisconnectReasons.Destroy) return;



        Start(c);

    }



    // ===== クラッシュ復帰(外部ウォッチドッグ連携)用 =====

    // 外部ウォッチドッグ(TOHhamoWatchdog.ps1)がAmong Usをプロセスごと再起動した時、

    // デスクトップの TOHhamo_Logs フォルダに再起動マーカーファイルを作成しておくことで、

    // MOD側がそれを検知し「起動直後に自動で部屋を立てる」ことができる。

    // (通常のNotifyDisconnectedは「切断イベント」が前提のため、プロセスの新規起動には反応しない)

    private static bool _checkedRelaunchMarker;



    /// <summary>

    /// タイトル画面(MainMenu)に到達したタイミングで呼ぶ。

    /// 再起動マーカーファイルが存在すれば、それを消費して自動で部屋を立てる処理を開始する。

    /// </summary>

    public static void CheckRelaunchMarker()

    {

        if (_checkedRelaunchMarker) return; // 1セッションにつき1回だけ確認する

        _checkedRelaunchMarker = true;



        // 通常の自動再ホスト、またはWindowsのクラッシュ自動再起動で作られた

        // restart_request.flagを消費して起動直後にロビーを作成する。

        // クラッシュ自動再起動は設定画面から常時有効化へ変更済みのため、旧設定値には依存しない。

        if (!Main.AutoRehost.Value && !WatchdogLauncher.IsSupported) return;

        if (_pending) return;



        try

        {

            var dir = System.IO.Path.Combine(

                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "TOHhamo_Logs");

            var markerPath = System.IO.Path.Combine(dir, "restart_request.flag");

            if (!System.IO.File.Exists(markerPath)) return;



            // マーカーが古すぎる(5分以上前)場合は無視する(誤って残っていた場合の暴走防止)

            var age = (DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(markerPath)).TotalSeconds;

            System.IO.File.Delete(markerPath);

            if (age > 300) return;



            Logger.Info("Relaunch marker detected. Will attempt to auto-host after crash recovery.", "AutoRehost");

            Start(AmongUsClient.Instance);

        }

        catch (Exception e)

        {

            Logger.Error($"CheckRelaunchMarkerの処理に失敗: {e.Message}", "AutoRehost");

        }

    }



    // ===== 定期的な自動立て直し(N試合ごと) =====

    // 「クラッシュ/切断からの復帰」ではなく、能動的に「設定試合数ごとに

    // 部屋を一度畳んで新しく立て直す」機能。長時間の連戦でロビーが不安定になる

    // (メモリリーク・部屋情報の肥大化等)のを定期的にリセットする目的。

    //

    // EndGameManager.ShowButtons(試合結果画面)のタイミングで判定し、条件を満たせば

    // 通常の「NextGame(同じ部屋でもう一度)」の代わりにこちらを呼ぶ。

    public static void TriggerPeriodicRehost()

    {

        if (_pending) return;



        var c = AmongUsClient.Instance;

        if (c == null || !c.AmHost) return;



        // 「N試合ごと」の周期性を保つため、こちらの立て直しだけは試合数を0に戻す。

        TriggerRehostNow(c,

            $"Periodic auto-rehost triggered (every {Options.OptionPeriodicAutoRehostInterval.GetInt()} games).",

            resetGameCount: true);

    }



    /// <summary>

    /// 部屋はそのまま維持しつつ、蓄積したログ・キャッシュ類だけをクリアして軽量化する。

    /// 「立て直し(部屋の離脱→再作成)」とは完全に独立した、別の機能。

    /// </summary>

    public static void TriggerPeriodicDataReset()

    {

        var c = AmongUsClient.Instance;

        if (c == null || !c.AmHost) return;



        ResetAccumulatedData();



        Logger.Info(

            $"Periodic data reset triggered (every {Options.OptionPeriodicDataResetInterval.GetInt()} games). Room is kept, only accumulated data was cleared.",

            "AutoRehost");

    }



    /// <summary>

    /// 蓄積した試合ログ・デバッグキャッシュ等をクリアしてメモリを軽量化する。

    /// 実績(Achievement)データ等の永続データには触れない。

    /// </summary>

    private static void ResetAccumulatedData()

    {

        try

        {

            Logger.disableList.Clear();

            Logger.sendToGameList.Clear();



            UtilsGameLog.LastLog.Clear();

            UtilsGameLog.LastLogRole.Clear();

            UtilsGameLog.LastLogPro.Clear();

            UtilsGameLog.LastLogSubRole.Clear();

            UtilsGameLog.LastLogLoveRole.Clear();

            UtilsGameLog.GameLog.Clear();



            // 明示的にGCを走らせ、解放したメモリを実際にOSへ返しやすくする。

            GC.Collect();

            GC.WaitForPendingFinalizers();



            Logger.Info("Periodic auto-rehost: cleared accumulated logs/caches.", "AutoRehost");

        }

        catch (Exception e)

        {

            Logger.Exception(e, "AutoRehost");

        }

    }



    /// <summary>

    /// 「次のロビーに戻ってから〇分経過した」場合に呼ばれる、時間ベースの立て直しトリガー。

    /// TimedAutoRehost クラスから呼ばれる。

    /// OBS表示や配信ログで使われている通算試合数(Main.GameCount)には触れない

    /// (この立て直しは「N試合ごと」の周期とは無関係なため、リセットすると

    ///  OBS/ログ上の試合数が意図せず0に戻ってしまう)。

    /// </summary>

    public static void TriggerTimedRehost()

    {

        if (_pending) return;



        var c = AmongUsClient.Instance;

        if (c == null || !c.AmHost) return;



        TriggerRehostNow(c,

            $"Timed auto-rehost triggered (after {Options.OptionTimedAutoRehostMinutes.GetInt()} minutes in lobby).",

            resetGameCount: false);

    }



    /// <summary>

    /// 実際に「部屋を離脱して立て直す」処理の本体。TriggerPeriodicRehost/TriggerTimedRehost共通。

    /// </summary>

    /// <param name="resetGameCount">

    /// trueの場合のみ Main.GameCount (通算試合数。OBS表示やログの試合数にも使われる)を0に戻す。

    /// 「N試合ごと」の周期立て直し専用のフラグで、時間ベースの立て直しでは必ずfalseにすること。

    /// </param>

    private static void TriggerRehostNow(AmongUsClient c, string logMessage, bool resetGameCount)

    {

        Logger.Info(logMessage, "AutoRehost");



        if (resetGameCount)

        {

            // 新しい部屋になるので、次の部屋では試合数カウントを0からやり直す。

            // (これをしないと、立て直し後も通算試合数のままカウントされ続けてしまい、

            //  「N試合ごと」の周期がずれていく)

            Main.GameCount = 0;

        }



        // 能動的な立て直しは「自分から離脱する」ため、NotifyDisconnectedの除外条件

        // (ExitGame/Destroyは対象外)を経由せず、直接Startを呼ぶ。

        Start(c);



        try

        {

            c.ExitGame(DisconnectReasons.ExitGame);

        }

        catch (Exception e)

        {

            Logger.Error($"自動立て直しのためのExitGameに失敗: {e.Message}", "AutoRehost");

        }

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

