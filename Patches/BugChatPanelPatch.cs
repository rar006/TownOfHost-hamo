using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;

using TownOfHost.Modules;
using TownOfHost.Templates;

namespace TownOfHost.Patches
{
    // ===== BUG専用チャットパネル =====
    // 要望により、Among Us標準のロビー内チャットとは別に、バグ報告チケットとやり取りする
    // 専用のチャットスペースを用意している。指定したチケット1件分の会話を表示・送信する。
    // メインメニュー・ロビーどちらのBUGボタンからも(ウィザードのチケット一覧経由で)開ける。
    //
    // 見た目の仕様:
    //   - 自分(game)のメッセージは右寄せ、Discord側は左寄せで表示し、吹き出し風に見分けやすくする。
    //   - Enterキーでも送信できる(TextBoxTMP.OnEnterを利用)。
    //   - 自分が入力中の間だけ「入力中…」の表示を出す(自分視点のみ・送信 or フォーカスが外れたら消える)。
    //   - 「更新」ボタンで新着メッセージを手動取得できる。
    public static class BugChatPanel
    {
        private static bool _isOpen = false;
        private static string _currentTicketId = "";
        private static GameObject _panelRoot;
        private static TextMeshPro _historyText;
        private static TextMeshPro _typingIndicatorText;
        private static SimpleTextBox _inputBox;
        private static SimpleButton _sendButton;
        private static SimpleButton _attachButton;
        private static readonly List<string> _pendingAttachments = new(); // 次の送信で一緒に送る画像・動画(最大3つ)
        private static bool _sending;
        private static SimpleButton _reloadButton;
        private static SimpleButton _scrollUpButton;
        private static SimpleButton _scrollDownButton;

        private static float _pollTimer = 0f;
        private const float PollIntervalSeconds = 12f; // 要望により短め(10〜15秒)のポーリング間隔

        // ===== スクロール =====
        // 直近10件だけを表示する方式だと、メッセージが増えると古い分が見えなくなっていた。
        // 全メッセージを保持しつつ、1ページに表示する件数を決めてスクロールオフセットで
        // 表示範囲をずらせるようにする(▲/▼ボタンでページ送り)。
        private const int MessagesPerPage = 8;
        private static int _scrollOffset = 0; // 0 = 最新側。値が大きいほど過去に遡る。

        // パネルの大きさ。画面中央に表示するようになったため、以前より少し大きくして見やすくした。
        // バグ報告一覧パネルと同じサイズにし、背後のロビーUI(ホスト名・開始ボタン等)を
        // 完全に覆い隠せるようにしている(以前は少し小さく、下端がはみ出て見えていた)。
        private static readonly Vector2 PanelSize = new(5.0f, 5.0f);

        public static void Open(Transform parent, string ticketId)
        {
            // 要望により、バグ報告フォームが開いていたら専用チャットを開いたタイミングで閉じる。
            BugReportWizard.Close();

            if (!EnsurePanel(parent)) return;

            // 要望により、画面のどこでも常に中央に表示されるようにする。
            var camera = Camera.main;
            if (camera != null && parent != null)
            {
                var worldPos = AspectPosition.ComputeWorldPosition(camera, AspectPosition.EdgeAlignments.Center, Vector3.zero);
                worldPos.z = camera.transform.position.z + 5f;
                var centerLocalPosition = parent.InverseTransformPoint(worldPos);
                centerLocalPosition.z = -20f;
                _panelRoot.transform.localPosition = centerLocalPosition;
            }

            _currentTicketId = ticketId;
            BugReportSystem.MarkAsRead(ticketId); // 開いたら未読バッジを消す

            _isOpen = true;
            _scrollOffset = 0; // 開いた時は常に最新メッセージが見える状態にする
            _panelRoot.SetActive(true);
            SetTypingIndicator(false);
            RefreshHistory();
            _pollTimer = PollIntervalSeconds; // 開いた瞬間にも1回取得する
        }

        /// <summary>専用チャットを表示中か(ロビーの「ホスト：」「開始」を隠すのに使う)</summary>
        public static bool IsShowing => _isOpen && _panelRoot != null && !_panelRoot.IsDestroyedOrNull() && _panelRoot.activeSelf;

        public static void Close()
        {
            _isOpen = false;
            SetTypingIndicator(false);
            if (_panelRoot != null && !_panelRoot.IsDestroyedOrNull())
                _panelRoot.SetActive(false);
        }

        /// <summary>指定チケットのチャットが今まさに表示中なら閉じる(修正完了で削除された時などに使う)。</summary>
        public static void CloseIfShowing(string ticketId)
        {
            if (_isOpen && _currentTicketId == ticketId)
            {
                Close();
            }
        }

        /// <summary>ロビー/メインメニューにいる間、パネルが開いていれば定期的にDiscord側の新着を取得する。</summary>
        public static void UpdateTick(float deltaTime)
        {
            if (!_isOpen) return;

            _pollTimer += deltaTime;
            if (_pollTimer < PollIntervalSeconds) return;
            _pollTimer = 0f;

            _ = PollAndRefreshAsync();
        }

        private static async System.Threading.Tasks.Task PollAndRefreshAsync()
        {
            await BugReportSystem.PollAllTicketsAsync().ConfigureAwait(false);
            BugReportSystem.MarkAsRead(_currentTicketId); // 表示中のチケットの未読はすぐ消す
            RefreshHistory();
        }

        /// <summary>「更新」ボタン用: 手動で今すぐ新着メッセージを取得する。</summary>
        private static void ManualReload()
        {
            _pollTimer = 0f; // 自動ポーリングのタイマーもリセットしておく
            _ = PollAndRefreshAsync();
        }

        // 入力中表示は廃止。
        private static void SetTypingIndicator(bool visible) { }

        private static void RefreshHistory()
        {
            if (_historyText == null) return;

            var ticket = BugReportSystem.GetTicket(_currentTicketId);
            if (ticket == null)
            {
                _historyText.text = "<color=#aaaaaa>このチケットは見つかりませんでした。</color>";
                _inputBox?.SetActive(false);
                _sendButton?.Button.gameObject.SetActive(false);
                _attachButton?.Button.gameObject.SetActive(false);
                UpdateScrollButtonsInteractable(0, 0);
                return;
            }

            _inputBox?.SetActive(true);
            _sendButton?.Button.gameObject.SetActive(true);
            _attachButton?.Button.gameObject.SetActive(!Main.IsAndroid()); // Androidではファイル選択が使えないので出さない

            var all = ticket.Messages;
            var totalCount = all.Count;

            // _scrollOffset(0=最新)から MessagesPerPage 件分を切り出す。
            // 範囲外にならないようクランプしておく。
            var maxOffset = Math.Max(0, totalCount - MessagesPerPage);
            if (_scrollOffset > maxOffset) _scrollOffset = maxOffset;
            if (_scrollOffset < 0) _scrollOffset = 0;

            var skipFromEnd = _scrollOffset;
            var takeCount = Math.Min(MessagesPerPage, totalCount - skipFromEnd);
            var startIndex = Math.Max(0, totalCount - skipFromEnd - takeCount);
            var page = takeCount > 0 ? all.Skip(startIndex).Take(takeCount).ToList() : new System.Collections.Generic.List<BugReportSystem.TicketMessage>();

            var sb = new StringBuilder();

            if (totalCount == 0)
            {
                sb.Append("<color=#aaaaaa>まだメッセージはありません。</color>");
            }
            else
            {
                // 過去にまだ隠れているメッセージがあることが分かるようにヒントを出す。
                if (startIndex > 0)
                {
                    sb.Append("<align=center><color=#777777>▲ 上にまだ古いメッセージがあります ▲</color></align>\n\n");
                }

                foreach (var msg in page)
                {
                    // 修正完了などのシステムメッセージは、専用チャット内では短く表示する。
                    if (msg.Resolved)
                    {
                        sb.Append("<align=center><color=#4caf50>― 修正完了 ―</color></align>\n");
                        continue;
                    }

                    // 送信失敗などのお知らせ(自分の画面だけ): 中央に表示
                    if (msg.From == "system")
                    {
                        sb.Append($"<align=center><size=85%>{msg.Text}</size></align>\n\n");
                        continue;
                    }

                    var isDiscord = msg.From == "discord";
                    // 要望により、自分(game)は右寄せ、Discord側は左寄せにして見分けやすくする。
                    if (isDiscord)
                    {
                        // 運営(Discord)側: 左寄せ・ピンクの名前に「運営」と時刻
                        sb.Append($"<align=left><size=85%><color=#ff5c8a><b>{msg.Author}</b></color> <color=#b0a4c4>運営 {Time(msg.At)}</color></size>\n<color=#f2ecfa>{msg.Text}</color></align>\n\n");
                    }
                    else
                    {
                        // 自分側: 右寄せ・水色の名前と時刻
                        sb.Append($"<align=right><size=85%><color=#b0a4c4>{Time(msg.At)}</color> <color=#7fd8ff><b>{msg.Author}</b></color></size>\n<color=#f2ecfa>{msg.Text}</color></align>\n\n");
                    }
                }

                if (skipFromEnd > 0)
                {
                    sb.Append("<align=center><color=#777777>▼ 下に新しいメッセージがあります ▼</color></align>\n");
                }
            }
            _historyText.text = sb.ToString();

            UpdateScrollButtonsInteractable(_scrollOffset, maxOffset);
        }

        private static string Time(long at)
        {
            try { return at > 0 ? System.DateTimeOffset.FromUnixTimeMilliseconds(at).ToLocalTime().ToString("HH:mm") : ""; }
            catch { return ""; }
        }

        /// <summary>▲(古い方へ)/▼(新しい方へ)ボタンの見た目を、これ以上動かせない端では薄く表示する。</summary>
        private static void UpdateScrollButtonsInteractable(int offset, int maxOffset)
        {
            if (_scrollUpButton != null)
            {
                var canScrollUp = offset < maxOffset;
                SetButtonEnabled(_scrollUpButton, canScrollUp);
            }
            if (_scrollDownButton != null)
            {
                var canScrollDown = offset > 0;
                SetButtonEnabled(_scrollDownButton, canScrollDown);
            }
        }

        private static void SetButtonEnabled(SimpleButton button, bool enabled)
        {
            button.Button.enabled = enabled;
            var color = button.NormalSprite.color;
            color.a = enabled ? 1f : 0.35f;
            button.NormalSprite.color = color;
        }

        /// <summary>▲ボタン: より古いメッセージへスクロールする。</summary>
        private static void ScrollOlder()
        {
            _scrollOffset += MessagesPerPage;
            RefreshHistory();
        }

        /// <summary>▼ボタン: より新しいメッセージへスクロールする。</summary>
        private static void ScrollNewer()
        {
            _scrollOffset = Math.Max(0, _scrollOffset - MessagesPerPage);
            RefreshHistory();
        }

        private static bool EnsurePanel(Transform parent)
        {
            if (_panelRoot != null && !_panelRoot.IsDestroyedOrNull())
            {
                _panelRoot.transform.SetParent(parent, false);
                return true;
            }

            try
            {
                _panelRoot = new GameObject("BugChatPanel");
                _panelRoot.transform.SetParent(parent, false);
                _panelRoot.transform.localPosition = new Vector3(0f, 0f, -20f);

                var bgTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                bgTexture.SetPixel(0, 0, Color.white);
                bgTexture.Apply();
                var bgSprite = Sprite.Create(bgTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

                var bgRenderer = _panelRoot.AddComponent<SpriteRenderer>();
                bgRenderer.sprite = bgSprite;
                bgRenderer.drawMode = SpriteDrawMode.Sliced;
                bgRenderer.size = PanelSize;
                bgRenderer.color = new Color(0f, 0f, 0f, 0.9f);
                bgRenderer.sortingOrder = 950; // 要望により最前面に来るよう大きめの値にしている

                // 要望により、こちらのパネルにもピンクの縁を付ける(BugReportUIPatchと同じ処理を流用)。
                BugReportUIPatch.CreatePinkBorder(_panelRoot.transform, PanelSize, 949);

                // ↓ここから、パネルの上端・下端(半分の高さ = 2.3)を超えないように配置している。

                var title = TMPTemplate.Create(
                    name: "BugChatTitle",
                    text: "BUG専用チャット",
                    color: Color.white,
                    fontSize: 1.9f,
                    alignment: TextAlignmentOptions.Top,
                    setActive: true,
                    parent: _panelRoot.transform);
                SetTmpPosition(title, new Vector3(0.3f, 2.0f, -1f), new Vector2(3.4f, 0.4f));
                SetSortingOrder(title, 960);

                var closeButton = new SimpleButton(
                    parent: _panelRoot.transform,
                    name: "BugChatCloseButton",
                    localPosition: new Vector3(-2.15f, 2.0f, -1f), // 要望により左上
                    normalColor: new Color32(120, 30, 30, 255),
                    hoverColor: new Color32(160, 40, 40, 255),
                    action: () => Close(),
                    label: "X");
                closeButton.Scale = new Vector2(0.4f, 0.4f);
                closeButton.FontSize = 1.4f;

                _reloadButton = new SimpleButton(
                    parent: _panelRoot.transform,
                    name: "BugChatReloadButton",
                    localPosition: new Vector3(-1.55f, 2.0f, -1f), // 要望により左上
                    normalColor: new Color32(60, 90, 150, 230),
                    hoverColor: new Color32(80, 110, 180, 230),
                    action: () => ManualReload(),
                    label: "更新");
                _reloadButton.Scale = new Vector2(0.65f, 0.4f);
                _reloadButton.FontSize = 1.1f;

                _historyText = TMPTemplate.Create(
                    name: "BugChatHistory",
                    text: "",
                    color: Color.white,
                    fontSize: 1.15f,
                    alignment: TextAlignmentOptions.TopLeft,
                    setActive: true,
                    parent: _panelRoot.transform);
                // 上端(タイトル下)〜下端(入力欄上)の範囲に収まるよう高さを調整。
                // 右側に▲/▼スクロールボタンを置くため、少し幅を狭めている。
                SetTmpPosition(_historyText, new Vector3(-0.25f, 0.2f, -1f), new Vector2(4.1f, 2.9f)); // 更新ボタン等と重ならないよう少し下げる
                _historyText.enableWordWrapping = true; // 要望により、長いメッセージは自動で折り返す
                _historyText.overflowMode = TextOverflowModes.Truncate;
                _historyText.lineSpacing = 4f; // 行間を少し広げて見やすくする
                SetSortingOrder(_historyText, 961);

                // ===== 履歴スクロールボタン(▲古い方へ / ▼新しい方へ) =====
                // 要望により、メッセージ数が増えても過去の履歴を遡って見られるようにする。
                _scrollUpButton = new SimpleButton(
                    parent: _panelRoot.transform,
                    name: "BugChatScrollUpButton",
                    localPosition: new Vector3(2.15f, 1.35f, -1f),
                    normalColor: new Color32(70, 70, 70, 230),
                    hoverColor: new Color32(100, 100, 100, 230),
                    action: () => ScrollOlder(),
                    label: "▲");
                _scrollUpButton.Scale = new Vector2(0.4f, 0.35f);
                _scrollUpButton.FontSize = 1.2f;

                _scrollDownButton = new SimpleButton(
                    parent: _panelRoot.transform,
                    name: "BugChatScrollDownButton",
                    localPosition: new Vector3(2.15f, 0.9f, -1f),
                    normalColor: new Color32(70, 70, 70, 230),
                    hoverColor: new Color32(100, 100, 100, 230),
                    action: () => ScrollNewer(),
                    label: "▼");
                _scrollDownButton.Scale = new Vector2(0.4f, 0.35f);
                _scrollDownButton.FontSize = 1.2f;

                _inputBox = new SimpleTextBox(
                    parent: _panelRoot.transform,
                    name: "BugChatInputBox",
                    localPosition: new Vector3(-2.25f, -2.2f, -1f),
                    width: 3.1f,
                    height: 0.7f,
                    characterLimit: 200,
                    placeholder: "メッセージを入力…",
                    multiline: true,
                    onFocusGained: null,
                    onFocusLost: null,
                    onEnter: () => SendCurrentInput()); // 要望により、Enterキーでも送信できるようにする

                _sendButton = new SimpleButton(
                    parent: _panelRoot.transform,
                    name: "BugChatSendButton",
                    localPosition: new Vector3(1.7f, -2.2f, -1f),
                    normalColor: new Color32(60, 150, 60, 230),
                    hoverColor: new Color32(80, 180, 80, 230),
                    action: () => SendCurrentInput(),
                    label: "送信");
                _sendButton.Scale = new Vector2(1.1f, 0.45f);
                _sendButton.FontSize = 1.3f;

                // 画像・動画の添付ボタン(送信ボタンの上)。Ctrl+クリックで3つまで選べる
                _attachButton = new SimpleButton(
                    parent: _panelRoot.transform,
                    name: "BugChatAttachButton",
                    localPosition: new Vector3(1.7f, -1.65f, -1f),
                    normalColor: new Color32(60, 60, 60, 230),
                    hoverColor: new Color32(90, 90, 90, 230),
                    action: () => OnAttachClicked(),
                    label: "添付");
                _attachButton.Scale = new Vector2(1.1f, 0.38f);
                _attachButton.FontSize = 1.2f;

                BugUiTheme.Apply(_panelRoot, PanelSize, title);
                _panelRoot.SetActive(false);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"BUGチャットパネルの生成に失敗しました: {ex}", "BugChatPanel");
                if (_panelRoot != null) UnityEngine.Object.Destroy(_panelRoot);
                _panelRoot = null;
                return false;
            }
        }

        /// <summary>
        /// TMPTemplateで複製したTextMeshProの表示位置を確実に指定するヘルパー。
        /// 以前はrectTransform.anchoredPosition3Dを使っていたが、パネルの親にRectTransformが
        /// 無いため、x座標を0以外にした要素だけ計算が不安定になり、想定と全く違う位置
        /// (パネルの外側)に表示される不具合が起きていた。localPositionを使うことで解消する。
        /// </summary>
        private static void SetTmpPosition(TextMeshPro tmp, Vector3 position, Vector2 sizeDelta)
        {
            var rt = tmp.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.localPosition = position;
            rt.sizeDelta = sizeDelta;
        }

        private static void SetSortingOrder(TextMeshPro tmp, int order)
        {
            var renderer = tmp.GetComponent<Renderer>();
            if (renderer != null) renderer.sortingOrder = order;
        }

        private static void OnAttachClicked()
        {
            if (Main.IsAndroid()) return;
            // 選び直すと入れ替わる。4つ以上選んだ時は最初の3つだけ(ダイアログ側で切り捨て済み)
            Win32FileDialog.ShowOpenImageOrVideoDialogAsync((paths, truncated) =>
            {
                _pendingAttachments.Clear();
                _pendingAttachments.AddRange(paths);
                UpdateAttachLabel();
            });
        }

        private static void UpdateAttachLabel()
        {
            if (_attachButton?.Label == null) return;
            _attachButton.Label.text = _pendingAttachments.Count == 0 ? "添付" : $"✓{_pendingAttachments.Count}件";
        }

        private static void SendCurrentInput()
        {
            if (_sending) return;
            var text = _inputBox?.Text?.Trim();
            var files = new List<string>(_pendingAttachments);
            // 文字が無くても、画像だけで送れる
            if (string.IsNullOrEmpty(text) && files.Count == 0) return;

            _inputBox?.SetText("");
            _pendingAttachments.Clear();
            UpdateAttachLabel();
            SetTypingIndicator(false);
            _ = SendAsync(text, files);
        }

        private static async System.Threading.Tasks.Task SendAsync(string text, List<string> files)
        {
            _sending = true;
            try
            {
                var playerName = PlayerControl.LocalPlayer != null
                    ? PlayerControl.LocalPlayer.GetNameWithRole().RemoveHtmlTags()
                    : "プレイヤー";
                var ticketId = _currentTicketId;

                if (!string.IsNullOrEmpty(text))
                {
                    var error = await BugReportSystem.ReplyAsync(ticketId, text, playerName).ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(error)) AddLocalNotice(ticketId, error);
                }
                if (files.Count > 0)
                {
                    var (ok, message) = await BugReportSystem.UploadAttachmentsAsync(ticketId, files, playerName).ConfigureAwait(false);
                    if (!ok) AddLocalNotice(ticketId, message);
                }
            }
            finally
            {
                _sending = false;
            }
            MainThreadDispatcher.Enqueue(() => RefreshHistory());
            // 要望により、送信結果のシステムメッセージはゲーム内チャットには出さない
            // (エラー時のみ、専用チャット内の履歴に表示する)。
        }

        /// <summary>送信に失敗した時など、自分の画面の履歴にだけ出すお知らせ(サーバーには送らない)</summary>
        private static void AddLocalNotice(string ticketId, string message)
        {
            var ticket = BugReportSystem.GetTicket(ticketId);
            if (ticket == null || string.IsNullOrEmpty(message)) return;
            ticket.Messages.Add(new BugReportSystem.TicketMessage
            {
                Id = Guid.NewGuid().ToString(),
                From = "system",
                Author = "",
                Text = $"<color=#ff6666>{message}</color>",
                At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
        }

        // メインメニューが開いている間、パネルが開いていれば定期ポーリングする。
        [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.LateUpdate))]
        public static class MainMenuBugChatUpdatePatch
        {
            private static float _mainMenuPollTimer = 0f;
            private const float MainMenuPollIntervalSeconds = 15f;

            public static void Postfix()
            {
                UpdateTick(UnityEngine.Time.deltaTime);

                // チャットパネルが閉じていても、メインメニューにいる間は未読バッジ用に
                // 定期的にチケットの新着を確認しておく。
                _mainMenuPollTimer += UnityEngine.Time.deltaTime;
                if (_mainMenuPollTimer < MainMenuPollIntervalSeconds) return;
                _mainMenuPollTimer = 0f;
                _ = BugReportSystem.PollAllTicketsAsync();
            }
        }
    }
}

namespace TownOfHost
{
    /// <summary>BUG専用チャットを開いている間は、ロビーの「ホスト：」「開始」等が重なって見えるので隠す</summary>
    [HarmonyLib.HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
    public static class BugChatHideLobbyUiPatch
    {
        private static bool _hidden;
        public static void Postfix(GameStartManager __instance)
        {
            try
            {
                var hide = TownOfHost.Patches.BugChatPanel.IsShowing;
                if (!hide && !_hidden) return;
                _hidden = hide;
                if (__instance.HostInfoPanel != null) __instance.HostInfoPanel.gameObject.SetActive(!hide);
                if (__instance.StartButton != null && hide) __instance.StartButton.gameObject.SetActive(false);
                if (__instance.GameStartTextParent != null && hide) __instance.GameStartTextParent.SetActive(false);
                if (!hide && __instance.StartButton != null && AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost)
                    __instance.StartButton.gameObject.SetActive(true);
            }
            catch { }
        }
    }
}
