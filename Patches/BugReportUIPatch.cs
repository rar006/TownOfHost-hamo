using System;
using HarmonyLib;
using TMPro;
using UnityEngine;

using TownOfHost.Modules;
using TownOfHost.Templates;
using Object = UnityEngine.Object;

namespace TownOfHost.Patches
{
    // ===== バグ報告ボタン & ウィザードページ =====
    //
    // 要望により、/cmd のチャットコマンド方式をやめて、実際のテキストボックス(TextBoxTMP。
    // Among Us標準のプレイヤー名入力などと同じ、実機で動作実績のある入力欄コンポーネント)を
    // 使った入力に変更している。
    //
    // 流れ:
    //   Step0: これまでのバグ報告一覧(あれば) + 「新規で報告する」ボタン
    //   Step1: 「Discordでチャットしたい」か「ゲーム内チャットでいい」かを選んでもらう
    //   Step2: (Discordを選んだ場合のみ) DiscordユーザーIDをテキストボックスで入力(必須)
    //   Step3: バグの内容をテキストボックスで入力(必須・空欄では送信不可)
    //   Step4: 送信 → 結果表示
    public enum BugReportWizardStep
    {
        Hidden,
        TicketList,
        ChoosePreference,
        AskDiscordId,
        AskDescription,
        Submitting,
        Done,
    }

    public enum BugReportContactPreference
    {
        None,
        Discord,
        InGame,
    }

    public static class BugReportWizard
    {
        public static BugReportWizardStep Step { get; private set; } = BugReportWizardStep.Hidden;
        public static BugReportContactPreference Preference { get; private set; } = BugReportContactPreference.None;
        public static string DiscordUserId { get; private set; } = "";
        public static string ResultMessage { get; private set; } = "";
        public static string ErrorMessage { get; private set; } = "";

        public static void Open()
        {
            // ゲーム設定画面では、設定UIとBUG報告UIの入力フォーカスが競合するため開かない。
            if (GameSettingMenu.Instance != null) return;
            // 要望により、専用チャットが開いていたらバグ報告を開いたタイミングで閉じる
            // (2つのパネルが同時に画面中央に重なって表示されるのを防ぐ)。
            BugChatPanel.Close();

            // 既存のバグ報告チケットがあれば、まず一覧画面を出す(要望:
            // 「BUGをしたら今報告しているバグと新規で報告を追加してそこから進める」)。
            Step = BugReportSystem.HasAnyTicket ? BugReportWizardStep.TicketList : BugReportWizardStep.AskDescription;
            // 要望により、連絡方法の選択自体を廃止し、常にゲーム内チャットでの報告に統一する。
            Preference = BugReportContactPreference.InGame;
            DiscordUserId = "";
            ResultMessage = "";
            ErrorMessage = "";
            BugReportUIPatch.TakePendingAttachmentPath();
            BugReportUIPatch.ShowPanel();
        }

        /// <summary>チケット一覧画面で「新規で報告する」を押したときに呼ぶ。</summary>
        public static void StartNewReport()
        {
            if (Step != BugReportWizardStep.TicketList) return;
            // 要望により、連絡方法の選択を挟まず、常にゲーム内チャットでの報告に直行する。
            Preference = BugReportContactPreference.InGame;
            DiscordUserId = "";
            ErrorMessage = "";
            BugReportUIPatch.TakePendingAttachmentPath();
            Step = BugReportWizardStep.AskDescription;
            BugReportUIPatch.RefreshPanelText();
        }

        /// <summary>チケット一覧画面で既存のチケットを選んだときに呼ぶ。専用チャットを開く。</summary>
        public static void OpenExistingTicket(string ticketId)
        {
            Close();
            BugChatPanel.Open(BugReportUIPatch.CurrentParent, ticketId);
        }

        public static void Close()
        {
            Step = BugReportWizardStep.Hidden;
            BugReportUIPatch.HidePanel();
        }

        public static void ChooseDiscordPreference()
        {
            if (Step != BugReportWizardStep.ChoosePreference) return;
            Preference = BugReportContactPreference.Discord;
            ErrorMessage = "";
            Step = BugReportWizardStep.AskDiscordId;
            BugReportUIPatch.RefreshPanelText();
        }

        public static void ChooseInGamePreference()
        {
            if (Step != BugReportWizardStep.ChoosePreference) return;
            Preference = BugReportContactPreference.InGame;
            DiscordUserId = "";
            ErrorMessage = "";
            Step = BugReportWizardStep.AskDescription;
            BugReportUIPatch.RefreshPanelText();
        }

        /// <summary>Discordでのチャットを選んだ場合のみ呼ばれる。IDは必須入力。</summary>
        public static void ConfirmDiscordId(string idText)
        {
            if (Step != BugReportWizardStep.AskDiscordId) return;

            var trimmed = (idText ?? "").Trim();
            if (trimmed.Length == 0)
            {
                ErrorMessage = "Discordでのチャットを希望する場合、ユーザーIDの入力が必須です。";
                BugReportUIPatch.RefreshPanelText();
                return;
            }

            DiscordUserId = trimmed;
            ErrorMessage = "";
            Step = BugReportWizardStep.AskDescription;
            BugReportUIPatch.RefreshPanelText();
        }

        public static void BackToPreference()
        {
            if (Step != BugReportWizardStep.AskDiscordId) return;
            ErrorMessage = "";
            Step = BugReportWizardStep.ChoosePreference;
            BugReportUIPatch.RefreshPanelText();
        }

        /// <summary>バグ内容は必須。空欄の場合は送信させない。</summary>
        public static void SubmitDescription(string description)
        {
            if (Step != BugReportWizardStep.AskDescription) return;

            var trimmed = (description ?? "").Trim();
            if (trimmed.Length == 0)
            {
                ErrorMessage = "バグの内容は必ず入力してください。";
                BugReportUIPatch.RefreshPanelText();
                return;
            }

            ErrorMessage = "";
            Step = BugReportWizardStep.Submitting;
            BugReportUIPatch.RefreshPanelText();
            _ = SubmitAsync(trimmed);
        }

        private static async System.Threading.Tasks.Task SubmitAsync(string description)
        {
            var playerName = PlayerControl.LocalPlayer != null
                ? PlayerControl.LocalPlayer.GetNameWithRole().RemoveHtmlTags()
                : "プレイヤー";

            var (ok, message, ticketId) = await BugReportSystem.SubmitAsync(DiscordUserId, description, playerName).ConfigureAwait(false);

            // 送信に成功し、添付ファイルが選択されていればあわせてアップロードする。
            var attachmentPath = BugReportUIPatch.TakePendingAttachmentPath();
            if (ok && !string.IsNullOrEmpty(attachmentPath))
            {
                var (attachOk, attachMessage) = await BugReportSystem.UploadAttachmentAsync(ticketId, attachmentPath).ConfigureAwait(false);
                if (!attachOk)
                {
                    message += $"\n(添付ファイルの送信に失敗しました: {attachMessage})";
                }
            }

            ResultMessage = message;
            Step = BugReportWizardStep.Done;
            BugReportUIPatch.RefreshPanelText();

            // ゲーム内チャットにも結果を残しておく(パネルを閉じた後でも見返せるように)。
            Utils.SendMessage(message, byte.MaxValue);
        }
    }

    public static class BugReportUIPatch
    {
        private static GameObject _panelRoot;
        private static TextMeshPro _titleText;
        private static TextMeshPro _bodyText;

        private static SimpleButton _chooseDiscordButton;
        private static SimpleButton _chooseInGameButton;
        private static SimpleButton _idNextButton;
        private static SimpleButton _idBackButton;
        private static SimpleButton _submitButton;
        private static SimpleButton _descriptionBackButton;
        private static SimpleButton _attachButton;
        // 送信予定の添付ファイルの絶対パス(未選択ならnull)。
        private static string _pendingAttachmentPath;
        private static SimpleButton _closeButton;
        private static SimpleButton _newReportButton;
        private const int MaxTicketListButtons = BugReportRemoteConfig.MaxTickets;
        private static readonly SimpleButton[] _ticketButtons = new SimpleButton[MaxTicketListButtons];

        private static SimpleTextBox _discordIdBox;
        private static SimpleTextBox _descriptionBox;
        private static GameObject _discordIdBoxFrame;
        private static GameObject _descriptionBoxFrame;

        // パネル全体のサイズ。背景・縁取り・各要素の位置決めで共通して使う。
        private static readonly Vector2 PanelSize = new(5.0f, 5.0f);

        // ボタンが今表示されているシーンの親Transform。パネルを遅延生成する際にここへぶら下げる。
        private static Transform _currentParent;

        /// <summary>他のボタン実装(HELP風ボタンなど)からパネルの親を更新するためのAPI。</summary>
        public static void SetCurrentParent(Transform parent) => _currentParent = parent;
        public static Transform CurrentParent => _currentParent;

        // 起動直後(MainMenuManager.Start等)はTMPTemplate/SimpleButtonの土台(SetBase)が
        // まだ用意できていないことがあるため、パネルは「実際にボタンを押した瞬間」に初めて
        // 生成する(遅延生成)。この時点ならプレイヤーは既にメニューを操作しているはずなので、
        // 土台の初期化は確実に完了している。
        private static bool EnsurePanel(Transform parent, Vector3 localPosition)
        {
            if (_panelRoot != null && !_panelRoot.IsDestroyedOrNull())
            {
                _panelRoot.transform.SetParent(parent, false);
                _panelRoot.transform.localPosition = localPosition;
                return true;
            }

            try
            {
                Logger.Info("BugReport: パネル生成開始", "BugReportUIPatch");
                _panelRoot = new GameObject("BugReportPanel");
                _panelRoot.transform.SetParent(parent, false);
                _panelRoot.transform.localPosition = localPosition;

                // 半透明の背景(1x1の白ピクセルを生成してタイル無しで引き伸ばす、シンプルな板)。
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

                // 要望により、パネル全体にピンクの縁を付ける。
                Logger.Info("BugReport: 背景生成完了 / 枠生成開始", "BugReportUIPatch");
                CreatePinkBorder(_panelRoot.transform, PanelSize, 949);

                Logger.Info("BugReport: タイトル/本文テキスト生成開始", "BugReportUIPatch");
                _titleText = TMPTemplate.Create(
                    name: "BugReportTitle",
                    text: "バグ報告", // 絵文字はこのフォントで表示できず「□」になるため使わない
                    color: Color.white,
                    fontSize: 2.4f,
                    alignment: TextAlignmentOptions.Top,
                    setActive: true,
                    parent: _panelRoot.transform);
                // 位置はSetTmpPosition(localPosition基準)で明示的に指定する(rectTransform.anchoredPosition3D
                // を使うと複製元のアンカー設定が残ってパネル外に表示される不具合があったため、localPositionに変更済み)。
                SetTmpPosition(_titleText, new Vector3(0f, 2.25f, -1f), new Vector2(4.4f, 0.5f));
                SetSortingOrder(_titleText, 960);

                _bodyText = TMPTemplate.Create(
                    name: "BugReportBody",
                    text: "",
                    color: Color.white,
                    fontSize: 1.4f,
                    alignment: TextAlignmentOptions.Center,
                    setActive: true,
                    parent: _panelRoot.transform);
                SetTmpPosition(_bodyText, new Vector3(0f, 1.7f, -1f), new Vector2(4.4f, 0.6f));
                _bodyText.enableWordWrapping = true;
                SetSortingOrder(_bodyText, 961);

                Logger.Info("BugReport: ボタン生成開始", "BugReportUIPatch");
                // ===== Step: これまでのバグ報告一覧(最大4件) + 新規報告ボタン =====
                for (var i = 0; i < MaxTicketListButtons; i++)
                {
                    var index = i; // クロージャ用にローカルへコピー
                    var listButton = new SimpleButton(
                        parent: _panelRoot.transform,
                        name: $"BugReportTicketButton_{i}",
                        localPosition: new Vector3(0f, 1.05f - i * 0.5f, -1f),
                        normalColor: new Color32(70, 70, 90, 230),
                        hoverColor: new Color32(95, 95, 120, 230),
                        action: () => OpenTicketByIndex(index),
                        label: "");
                    listButton.Scale = new Vector2(4.0f, 0.42f);
                    listButton.FontSize = 1.1f;
                    _ticketButtons[i] = listButton;
                }

                _newReportButton = new SimpleButton(
                    parent: _panelRoot.transform,
                    name: "BugReportNewButton",
                    localPosition: new Vector3(0f, -1.1f, -1f),
                    normalColor: new Color32(200, 40, 40, 230),
                    hoverColor: new Color32(230, 60, 60, 230),
                    action: () => BugReportWizard.StartNewReport(),
                    label: "追加で報告する");
                _newReportButton.Scale = new Vector2(2.8f, 0.42f);
                _newReportButton.FontSize = 1.3f;

                // ===== Step0: チャット希望の選択ボタン =====
                _chooseDiscordButton = new SimpleButton(
                    parent: _panelRoot.transform,
                    name: "BugReportChooseDiscord",
                    localPosition: new Vector3(0f, 0.6f, -1f),
                    normalColor: new Color32(88, 101, 242, 230), // Discordブランドカラー
                    hoverColor: new Color32(114, 128, 250, 230),
                    action: () => BugReportWizard.ChooseDiscordPreference(),
                    label: "Discordでチャットしたい");
                _chooseDiscordButton.Scale = new Vector2(3.4f, 0.42f);
                _chooseDiscordButton.FontSize = 1.4f;

                _chooseInGameButton = new SimpleButton(
                    parent: _panelRoot.transform,
                    name: "BugReportChooseInGame",
                    localPosition: new Vector3(0f, -0.05f, -1f),
                    normalColor: new Color32(60, 60, 60, 230),
                    hoverColor: new Color32(90, 90, 90, 230),
                    action: () => BugReportWizard.ChooseInGamePreference(),
                    label: "ゲーム内チャットでいい");
                _chooseInGameButton.Scale = new Vector2(3.4f, 0.42f);
                _chooseInGameButton.FontSize = 1.4f;

                // ===== Step1: DiscordユーザーID入力欄 =====
                // 要望により右上寄りに配置する。
                var discordIdBoxWidth = 3.0f;
                var discordIdBoxX = 0.3f;
                var discordIdBoxY = 1.0f;
                Logger.Info("BugReport: 入力欄(Discord ID)生成開始", "BugReportUIPatch");
                _discordIdBox = new SimpleTextBox(
                    parent: _panelRoot.transform,
                    name: "BugReportDiscordIdBox",
                    localPosition: new Vector3(discordIdBoxX - discordIdBoxWidth / 2f, discordIdBoxY, -2f),
                    width: discordIdBoxWidth,
                    height: 0.34f,
                    characterLimit: 25,
                    placeholder: "例: 123456789012345678",
                    onFocusGained: () => SetTextBoxHighlight(_discordIdBoxFrame, true),
                    onFocusLost: () => SetTextBoxHighlight(_discordIdBoxFrame, false));
                _discordIdBoxFrame = CreateTextBoxFrame(
                    _panelRoot.transform,
                    "BugReportDiscordIdBoxFrame",
                    // 枠(CreateTextBoxFrame)はSpriteRendererのデフォルトpivot(中心)基準で
                    // 描画されるため、渡す座標は「枠の中心」でなければならない。
                    // 以前はここに余計な高さ補正(discordIdBoxY + 0.17f = 入力欄の上端)を
                    // 加えてしまっており、枠の中心が入力欄の上端に来てしまう
                    // (=枠が入力欄より大きくズレて上にずれる)不具合があったため、
                    // 単純に入力欄と同じ中心座標を渡すよう修正する。
                    new Vector3(discordIdBoxX, discordIdBoxY, -1.5f),
                    new Vector2(discordIdBoxWidth + 0.16f, 0.42f));

                _idNextButton = new SimpleButton(
                    parent: _panelRoot.transform,
                    name: "BugReportIdNext",
                    localPosition: new Vector3(1.1f, -0.55f, -1f),
                    normalColor: new Color32(60, 150, 60, 230),
                    hoverColor: new Color32(80, 180, 80, 230),
                    action: () => BugReportWizard.ConfirmDiscordId(_discordIdBox.Text),
                    label: "次へ");
                _idNextButton.Scale = new Vector2(1.6f, 0.42f);
                _idNextButton.FontSize = 1.4f;

                _idBackButton = new SimpleButton(
                    parent: _panelRoot.transform,
                    name: "BugReportIdBack",
                    localPosition: new Vector3(-1.1f, -0.55f, -1f),
                    normalColor: new Color32(60, 60, 60, 230),
                    hoverColor: new Color32(90, 90, 90, 230),
                    action: () => BugReportWizard.BackToPreference(),
                    label: "戻る");
                _idBackButton.Scale = new Vector2(1.6f, 0.42f);
                _idBackButton.FontSize = 1.4f;

                // ===== Step2: バグ内容入力欄 =====
                // 要望により、9行表示できるよう枠を大きくする。
                // (以前は「枠は動かさないで」との要望で位置固定していたが、
                //  枠と実際の入力範囲のズレを直した今回、改めて「枠ごと上に」との
                //  要望があったため、位置・高さ・横幅すべてを調整している)
                var descriptionBoxWidth = 4.75f;
                // 要望により、ボタン列(戻る/添付/送信する)は動かさない前提で、
                // その隙間に収まる範囲で高さ・位置を調整する(8行相当)。
                // → さらに要望により、4行追加(合計12行相当)。ボタン列との隙間には
                //   収まらなくなるため、この分は画面(パネル)からはみ出ることを承知の上で拡大する。
                // 要望により、枠の大きさ自体は前の状態(8行相当)に戻す。
                // 「もっと行数を書きたい」という点は、枠を大きくするのではなく、
                // 既存のスクロール機能(マウスホイール/カーソル移動で自動追従)で
                // 対応する形にする(characterLimit:1000まで入力可能)。
                // 要望により、行数を12行相当に調整。
                var descriptionBoxHeight = 3.3f;
                var descriptionBoxX = 0f;
                // 要望により、枠(見た目)ごと全体を上に上げる。
                var descriptionBoxY = 0.05f;
                Logger.Info("BugReport: 入力欄(内容)生成開始", "BugReportUIPatch");
                _descriptionBox = new SimpleTextBox(
                    parent: _panelRoot.transform,
                    name: "BugReportDescriptionBox",
                    localPosition: new Vector3(descriptionBoxX - descriptionBoxWidth / 2f, descriptionBoxY, -2f),
                    width: descriptionBoxWidth,
                    height: descriptionBoxHeight,
                    characterLimit: 1000,
                    onFocusGained: () => SetTextBoxHighlight(_descriptionBoxFrame, true),
                    onFocusLost: () => SetTextBoxHighlight(_descriptionBoxFrame, false),
                    placeholder: "例: タスクの◯◯をやると画面が固まる\n(Shift+Enterで改行できます)",
                    multiline: true);
                _descriptionBoxFrame = CreateTextBoxFrame(
                    _panelRoot.transform,
                    "BugReportDescriptionBoxFrame",
                    // 【重要】枠(CreateTextBoxFrame)はSpriteRendererのデフォルトpivot(中心)基準で
                    // 描画されるため、渡す座標は「枠の中心」でなければならない。
                    // 以前は「descriptionBoxY + descriptionBoxHeight*0.5f」という、入力欄の
                    // 上端に相当する座標を渡してしまっており、枠の中心が実際の入力欄より
                    // 大きく上にズレていた(枠の上半分近くが入力欄と重ならない空白になり、
                    // 逆に入力欄の下半分近くが枠からはみ出す)不具合があった。
                    // これが「枠の上のほうが空白で、実際に書けるのはその下だけ」という
                    // 見た目のズレの原因だったため、入力欄と同じ中心座標を渡すよう修正する。
                    new Vector3(descriptionBoxX, descriptionBoxY, -1.5f),
                    new Vector2(descriptionBoxWidth + 0.10f, descriptionBoxHeight + 0.06f));

                // 要望により、ボタン列は動かさず、代わりに入力欄側の高さ・位置だけで調整する。
                _submitButton = new SimpleButton(
                    parent: _panelRoot.transform,
                    name: "BugReportSubmit",
                    localPosition: new Vector3(1.3f, -2.1f, -1f),
                    normalColor: new Color32(200, 40, 40, 230),
                    hoverColor: new Color32(230, 60, 60, 230),
                    action: () => BugReportWizard.SubmitDescription(_descriptionBox.Text),
                    label: "送信する");
                _submitButton.Scale = new Vector2(1.8f, 0.42f);
                _submitButton.FontSize = 2.4f;

                // 画像・動画を添付するボタン。送信・戻るボタンと横並びの中央に配置する。
                _attachButton = new SimpleButton(
                    parent: _panelRoot.transform,
                    name: "BugReportAttach",
                    localPosition: new Vector3(0f, -2.1f, -1f),
                    normalColor: new Color32(60, 60, 60, 230),
                    hoverColor: new Color32(90, 90, 90, 230),
                    action: OnAttachButtonClicked,
                    label: "添付");
                _attachButton.Scale = new Vector2(1.3f, 0.42f);
                _attachButton.FontSize = 2.2f;

                // 要望により、押すとバグ報告全体を閉じる「戻る」ボタン。
                _descriptionBackButton = new SimpleButton(
                    parent: _panelRoot.transform,
                    name: "BugReportDescriptionBack",
                    localPosition: new Vector3(-1.3f, -2.1f, -1f),
                    normalColor: new Color32(60, 60, 60, 230),
                    hoverColor: new Color32(90, 90, 90, 230),
                    action: () => BugReportWizard.Close(),
                    label: "戻る");
                _descriptionBackButton.Scale = new Vector2(1.3f, 0.4f);
                _descriptionBackButton.FontSize = 2.4f;

                _closeButton = new SimpleButton(
                    parent: _panelRoot.transform,
                    name: "BugReportCloseButton",
                    localPosition: new Vector3(-2.1f, 2.25f, -1f),
                    normalColor: new Color32(120, 30, 30, 255),
                    hoverColor: new Color32(160, 40, 40, 255),
                    action: () => BugReportWizard.Close(),
                    label: "X"); // 「✕」は絵文字扱いで表示できないフォントがあるため通常のXにする
                _closeButton.Scale = new Vector2(0.4f, 0.4f);
                _closeButton.FontSize = 3f;

                _panelRoot.SetActive(false);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error($"バグ報告パネルの生成に失敗しました: {ex}", "BugReportUIPatch");
                if (_panelRoot != null) Object.Destroy(_panelRoot);
                _panelRoot = null;
                return false;
            }
        }

        /// <summary>
        /// TMPTemplateで複製したTextMeshProの表示位置を確実に指定するヘルパー。
        /// TextMeshProはRectTransformを持つため、transform.localPositionではなく
        /// anchoredPosition3Dで指定しないと、複製元(credentialsテキスト)のアンカー設定が
        /// 残ってしまい、意図しない位置(他のテキストと重なる位置)に表示されることがある。
        /// </summary>
        /// <summary>
        /// TMPTemplateで複製したTextMeshProの表示位置を確実に指定するヘルパー。
        /// 以前はrectTransform.anchoredPosition3Dを使っていたが、このパネルの親には
        /// RectTransformが無いため、x座標を0以外にした要素だけ計算が不安定になり、
        /// 想定と全く違う位置(パネルの外側)に表示される不具合が起きていた。
        /// localPositionは常にTransform基準の単純なオフセットとして扱われ、
        /// RectTransformのアンカー計算に影響されないため、こちらを使う。
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

        /// <summary>
        /// パネル全体(背景・枠・テキスト・ボタン等すべて)を、他のUI(設定画面等)より
        /// 確実に手前へ描画させる。sortingOrderの数値だけでなくsortingLayerIDも
        /// 揃える必要がある(レイヤーが食い違っていると数値をいくら上げても効かないため)。
        /// バグ報告パネルを開いた直後、および設定画面が開かれた時に呼び出す。
        /// </summary>
        internal static void BringPanelToFront()
        {
            if (_panelRoot == null || _panelRoot.IsDestroyedOrNull()) return;
            // パネルが非表示の状態で呼ばれても意味がないどころか、
            // 隠れているはずのテキストだけが手前に出てしまう不具合があったため、
            // 表示中(SetActive(true))の時だけ実行する。
            if (!_panelRoot.activeSelf) return;
            try
            {
                // sortingLayerIDを決め打ちで0(Default)にすると、設定画面側が
                // それより優先度の高い名前付きソートレイヤーを使っていた場合に
                // 数値(sortingOrder)をいくら上げても勝てず、背景のSpriteRendererだけ
                // 隠れてしまう不具合があった。実際に画面に存在するSpriteRendererの中で
                // 最も優先度が高いレイヤーIDを探し、それに合わせる。
                var targetLayerID = FindHighestPrioritySortingLayerID();

                foreach (var sr in _panelRoot.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    // TextBoxTMP配下のSpriteRenderer(内部のカーソル・背景等)も同様に対象外にする。
                    if (sr.GetComponentInParent<TextBoxTMP>() != null) continue;

                    if (sr.sortingOrder < 20000) sr.sortingOrder += 20000;
                    sr.sortingLayerID = targetLayerID;
                }
                foreach (var tmp in _panelRoot.GetComponentsInChildren<TextMeshPro>(true))
                {
                    // TextBoxTMP自身が内部管理する特殊な出力用テキスト(outputText)は、
                    // sortingOrderを書き換えると入力欄自体の表示が崩れる不具合があったため対象外にする。
                    // ただし、SimpleTextBoxが表示用に追加した_displayText/_placeholderText/カーソルは
                    // TextBoxTMPの子ではあるものの「TextBoxTMP内部が管理するテキストではない」ため、
                    // 名前で明示的に除外対象から外す(これらを底上げ対象から漏らすと、
                    // 設定画面等を開いた時にテキストだけ取り残されて浮いて見える不具合になる)。
                    var isTextBoxInternal = tmp.GetComponentInParent<TextBoxTMP>() != null
                        && tmp.gameObject.name != "DisplayText"
                        && tmp.gameObject.name != "Placeholder";
                    if (isTextBoxInternal) continue;

                    var renderer = tmp.GetComponent<Renderer>();
                    if (renderer == null) continue;
                    if (renderer.sortingOrder < 20000) renderer.sortingOrder += 20000;
                    renderer.sortingLayerID = targetLayerID;
                }
            }
            catch (System.Exception ex)
            {
                TownOfHost.Logger.Exception(ex, "BugReportUIPatch.BringPanelToFront");
            }
        }

        /// <summary>
        /// シーン中に存在する全SpriteRendererを調べ、実際に使われているソートレイヤーの中で
        /// 最も優先度(描画順)が高いレイヤーIDを返す。バグ報告パネルをそのレイヤーへ合わせることで、
        /// 名前付きソートレイヤーを使う他のUI(設定画面等)よりも確実に手前へ描画させる。
        /// </summary>
        private static int FindHighestPrioritySortingLayerID()
        {
            // 【クラッシュ対策】SortingLayer.layers はIL2CPP環境でアクセス違反(AccessViolationException)を
            // 起こすことがあり、try/catchでも捕捉できずゲームごと落ちていた。
            // HELPパネルと同じく、画面内のRendererから SortingLayer.GetLayerValueFromID で最前面のレイヤーを探す。
            try
            {
                if (_panelRoot == null) return 0;
                var targetLayerId = 0;
                var highestLayerValue = int.MinValue;
                foreach (var renderer in _panelRoot.transform.root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer == null || renderer.transform.IsChildOf(_panelRoot.transform)) continue;
                    var layerValue = SortingLayer.GetLayerValueFromID(renderer.sortingLayerID);
                    if (layerValue > highestLayerValue)
                    {
                        highestLayerValue = layerValue;
                        targetLayerId = renderer.sortingLayerID;
                    }
                }
                return targetLayerId;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// テキスト入力欄のまわりに薄い枠を表示して、入力欄の存在をわかりやすくする。
        /// フォーカス時にSetTextBoxHighlightで色を明るくする。
        /// </summary>
        private static GameObject CreateTextBoxFrame(Transform parent, string name, Vector3 localPosition, Vector2 size)
        {
            var frame = new GameObject(name);
            frame.transform.SetParent(parent, false);
            frame.transform.localPosition = localPosition;

            var texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

            var fill = frame.AddComponent<SpriteRenderer>();
            fill.sprite = sprite;
            fill.drawMode = SpriteDrawMode.Sliced;
            fill.size = size;
            fill.color = new Color(1f, 1f, 1f, 0.06f);
            fill.sortingOrder = 955;

            CreatePinkBorder(frame.transform, size, 954, 0.025f);
            return frame;
        }

        /// <summary>入力欄がフォーカスされている間、枠を少し明るくして「入力中」がわかるようにする。</summary>
        private static void SetTextBoxHighlight(GameObject frame, bool focused)
        {
            if (frame == null) return;
            var fill = frame.GetComponent<SpriteRenderer>();
            if (fill != null) fill.color = focused ? new Color(1f, 0.75f, 0.85f, 0.16f) : new Color(1f, 1f, 1f, 0.06f);
        }

        /// <summary>
        /// 指定サイズのパネルの四辺にピンクの細い縁を作る(4本の細長い板を組み合わせているだけ)。
        /// sortingOrderは背景より1つ低い値を渡すと、背景の"外側"に縁取りのように見える。
        /// </summary>
        internal static void CreatePinkBorder(Transform parent, Vector2 panelSize, int sortingOrder, float thickness = 0.06f)
        {
            var pinkColor = new Color(1f, 0.45f, 0.75f, 1f);
            var borderTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            borderTexture.SetPixel(0, 0, Color.white);
            borderTexture.Apply();
            var borderSprite = Sprite.Create(borderTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

            void MakeEdge(string name, Vector2 size, Vector3 localPos)
            {
                var edge = new GameObject(name);
                edge.transform.SetParent(parent, false);
                edge.transform.localPosition = localPos;
                var renderer = edge.AddComponent<SpriteRenderer>();
                renderer.sprite = borderSprite;
                renderer.drawMode = SpriteDrawMode.Sliced;
                renderer.size = size;
                renderer.color = pinkColor;
                renderer.sortingOrder = sortingOrder;
            }

            var halfW = panelSize.x / 2f;
            var halfH = panelSize.y / 2f;
            MakeEdge("BorderTop", new Vector2(panelSize.x + thickness * 2f, thickness), new Vector3(0f, halfH, 0.05f));
            MakeEdge("BorderBottom", new Vector2(panelSize.x + thickness * 2f, thickness), new Vector3(0f, -halfH, 0.05f));
            MakeEdge("BorderLeft", new Vector2(thickness, panelSize.y), new Vector3(-halfW, 0f, 0.05f));
            MakeEdge("BorderRight", new Vector2(thickness, panelSize.y), new Vector3(halfW, 0f, 0.05f));
        }

        private static void OpenTicketByIndex(int index)
        {
            var tickets = BugReportSystem.Tickets;
            if (index < 0 || index >= tickets.Count) return;
            BugReportWizard.OpenExistingTicket(tickets[index].TicketId);
        }

        /// <summary>Discordから未読返信があるときにボタンの右上に出す、小さな赤丸バッジ。</summary>
        public static GameObject CreateUnreadBadge(Transform parent, Vector3 localPosition, float size = 0.12f)
        {
            var badgeTexture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
            badgeTexture.SetPixel(0, 0, Color.white);
            badgeTexture.Apply();
            var badgeSprite = Sprite.Create(badgeTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

            var badgeObj = new GameObject("UnreadBadge");
            badgeObj.transform.SetParent(parent, false);
            badgeObj.transform.localPosition = localPosition;
            var renderer = badgeObj.AddComponent<SpriteRenderer>();
            renderer.sprite = badgeSprite;
            renderer.drawMode = SpriteDrawMode.Sliced;
            renderer.color = new Color(0.95f, 0.15f, 0.15f, 1f);
            renderer.size = new Vector2(size, size);
            renderer.sortingOrder = 600;
            badgeObj.SetActive(false);
            return badgeObj;
        }

        public static void ShowPanel()
        {
            if (_currentParent == null)
            {
                Logger.Warn("バグ報告パネルを表示できませんでした(親オブジェクトが未設定です)。", "BugReportUIPatch");
                return;
            }

            // 要望により、画面のどこでも常に中央に表示されるようにする。
            // 親Transform基準の相対オフセットではなく、カメラから見た画面中央の
            // ワールド座標を毎回計算することで、解像度やシーンに関わらず中央に来る。
            var camera = Camera.main;
            var centerLocalPosition = new Vector3(0f, 0f, -20f);
            if (camera != null && _currentParent != null)
            {
                var worldPos = AspectPosition.ComputeWorldPosition(camera, AspectPosition.EdgeAlignments.Center, Vector3.zero);
                worldPos.z = camera.transform.position.z + 5f; // 手前に来るようZだけ調整
                centerLocalPosition = _currentParent.InverseTransformPoint(worldPos);
                centerLocalPosition.z = -20f;
            }

            Logger.Info("BugReport: ShowPanel 開始", "BugReportUIPatch");
            if (!EnsurePanel(_currentParent, centerLocalPosition)) return;

            _panelRoot.SetActive(true);
            Logger.Info("BugReport: パネル表示 / 前面化開始", "BugReportUIPatch");
            BringPanelToFront();
            Logger.Info("BugReport: 前面化完了 / テキスト更新開始", "BugReportUIPatch");
            RefreshPanelText();
            Logger.Info("BugReport: ShowPanel 完了", "BugReportUIPatch");
        }

        public static void HidePanel()
        {
            if (_panelRoot == null || _panelRoot.IsDestroyedOrNull()) return;
            _panelRoot.SetActive(false);
        }

        private static void SetActiveSafe(SimpleButton button, bool active) => button?.Button.gameObject.SetActive(active);
        private static void SetActiveSafe(SimpleTextBox box, bool active) => box?.SetActive(active);

        // 入力中表示は廃止。互換性のため呼び出し側が残っていても何もしない。
        private static void SetTypingIndicator(bool visible) { }

        /// <summary>「添付」ボタン押下時に呼ぶ。ファイル選択ダイアログを開き、
        /// 選んだファイルを次の送信時に一緒にアップロードする対象として保持する。</summary>
        private static void OnAttachButtonClicked()
        {
            // GetOpenFileNameWはダイアログを閉じるまで戻らないブロッキング呼び出しのため、
            // 呼んでいる間はゲーム全体が一時停止して見える(ダイアログ自体は正常に操作できる)。
            var path = Win32FileDialog.ShowOpenImageOrVideoDialog();
            if (string.IsNullOrEmpty(path)) return;

            _pendingAttachmentPath = path;
            UpdateAttachButtonLabel();
        }

        private static void UpdateAttachButtonLabel()
        {
            if (_attachButton?.Label == null) return;
            if (string.IsNullOrEmpty(_pendingAttachmentPath))
            {
                _attachButton.Label.text = "添付";
                return;
            }
            var fileName = System.IO.Path.GetFileName(_pendingAttachmentPath);
            // ボタン幅に収まるよう、長いファイル名は省略する。
            if (fileName.Length > 12) fileName = fileName[..10] + "…";
            _attachButton.Label.text = $"✓{fileName}";
        }

        /// <summary>選択済みの添付ファイルパスを取り出し、保持状態をクリアする。
        /// 送信処理からのみ呼ぶ想定(1回の送信で1回だけ消費する)。</summary>
        internal static string TakePendingAttachmentPath()
        {
            var path = _pendingAttachmentPath;
            _pendingAttachmentPath = null;
            UpdateAttachButtonLabel();
            return path;
        }

        public static void RefreshPanelText()
        {
            if (_bodyText == null) return;

            // 一旦全部隠してから、現在のStepに必要なものだけ表示する。
            for (var i = 0; i < MaxTicketListButtons; i++) SetActiveSafe(_ticketButtons[i], false);
            SetActiveSafe(_newReportButton, false);
            SetActiveSafe(_chooseDiscordButton, false);
            SetActiveSafe(_chooseInGameButton, false);
            SetActiveSafe(_discordIdBox, false);
            _discordIdBoxFrame?.SetActive(false);
            SetActiveSafe(_idNextButton, false);
            SetActiveSafe(_idBackButton, false);
            SetActiveSafe(_descriptionBox, false);
            _descriptionBoxFrame?.SetActive(false);
            SetActiveSafe(_submitButton, false);
            SetActiveSafe(_descriptionBackButton, false);
            SetActiveSafe(_attachButton, false);
            SetTypingIndicator(false);

            var errorSuffix = string.IsNullOrEmpty(BugReportWizard.ErrorMessage)
                ? ""
                : $"\n\n<color=#ff6666>⚠ {BugReportWizard.ErrorMessage}</color>";

            switch (BugReportWizard.Step)
            {
                case BugReportWizardStep.TicketList:
                    var tickets = BugReportSystem.Tickets;
                    _bodyText.text = tickets.Count == 0
                        ? "現在報告中のバグはありません。"
                        : "現在のバグ報告一覧です。修正完了後も履歴を確認できます。";
                    for (var i = 0; i < MaxTicketListButtons && i < tickets.Count; i++)
                    {
                        var t = tickets[tickets.Count - 1 - i]; // 新しい順
                        var unreadMark = t.HasUnread ? "[未読] " : "";
                        var button = _ticketButtons[i];
                        SetActiveSafe(button, true);
                        button.Label.text = unreadMark + t.Summary;
                    }
                    // Discord側の運用設定と、同時保持数3件の両方を満たす場合だけ表示する。
                    SetActiveSafe(_newReportButton, BugReportRemoteConfig.CanSubmitAdditionalReport);
                    break;

                case BugReportWizardStep.ChoosePreference:
                    _bodyText.text =
                        "バグ報告の返信は、どちらの方法で受け取りますか？\n" +
                        "(あとからでも、ゲーム内チャットには結果が届きます)" + errorSuffix;
                    SetActiveSafe(_chooseDiscordButton, true);
                    SetActiveSafe(_chooseInGameButton, true);
                    break;

                case BugReportWizardStep.AskDiscordId:
                    _bodyText.text =
                        "DiscordのユーザーIDを入力してください(必須)。\n" +
                        "入力欄をクリックしてから入力できます。" + errorSuffix;
                    SetActiveSafe(_discordIdBox, true);
                    _discordIdBoxFrame?.SetActive(true);
                    SetActiveSafe(_idNextButton, true);
                    SetActiveSafe(_idBackButton, true);
                    // Step切替時に親パネルへフォーカスを奪われても、入力欄を再選択する。
                    _ = new LateTask(() => _discordIdBox?.TextBox?.GiveFocus(), 0.05f, "BugReportDiscordId.Focus", true);
                    break;

                case BugReportWizardStep.AskDescription:
                    _bodyText.text = errorSuffix;
                    SetActiveSafe(_descriptionBox, true);
                    _descriptionBoxFrame?.SetActive(true);
                    SetActiveSafe(_submitButton, true);
                    SetActiveSafe(_descriptionBackButton, true);
                    SetActiveSafe(_attachButton, true);
                    UpdateAttachButtonLabel();
                    // 説明欄を表示した直後に確実に入力フォーカスを与える。
                    _ = new LateTask(() => _descriptionBox?.TextBox?.GiveFocus(), 0.05f, "BugReportDescription.Focus", true);
                    break;

                case BugReportWizardStep.Submitting:
                                            _bodyText.text = "送信中です…";

                    break;

                case BugReportWizardStep.Done:
                    _bodyText.text = BugReportWizard.ResultMessage;
                    break;
            }
        }

        // ===== メインメニュー: 左上あたりにバグ報告ボタンを追加 =====
        [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]
        public static class MainMenuBugReportButtonPatch
        {
            private static SimpleButton _button;
            private static GameObject _unreadBadge;

            [HarmonyPostfix, HarmonyPriority(Priority.Last)]
            public static void Postfix(MainMenuManager __instance)
            {
                if (__instance == null) return;

                try
                {
                    var parent = __instance.gameModeButtons?.transform?.parent ?? __instance.transform;
                    _currentParent = parent;

                    if (_button == null || _button.Button.IsDestroyedOrNull())
                    {
                        // 左上(AmongUsロゴのすぐ下)あたりに配置。見た目は環境によって多少ズレる可能性があるため、
                        // 実機で確認しながらlocalPositionを微調整すること。
                        _button = new SimpleButton(
                            parent: parent,
                            name: "BugReportButton_MainMenu",
                            localPosition: new Vector3(-4.7f, 2.0f, -20f),
                            normalColor: new Color32(200, 40, 40, 220),
                            hoverColor: new Color32(230, 60, 60, 220),
                            action: () => BugReportWizard.Open(),
                            label: "バグ報告");
                        _button.Scale = new Vector2(1.6f, 0.45f);
                        _button.FontSize = 1.3f;

                        _unreadBadge = CreateUnreadBadge(_button.Button.transform, new Vector3(0.68f, 0.19f, -0.5f));
                    }
                    _button.Button.gameObject.SetActive(true);
                    if (_unreadBadge != null) _unreadBadge.SetActive(BugReportSystem.HasAnyUnread);
                }
                catch (Exception ex)
                {
                    Logger.Error($"バグ報告ボタン(メインメニュー)の生成に失敗しました: {ex}", "BugReportUIPatch");
                }
            }

            // 未読バッジをメインメニューにいる間も定期更新する(チケット取得自体はロビー/専用チャット
            // が開いている間に行われるため、ここではフラグの反映のみ)。
            [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.LateUpdate))]
            public static class BadgeUpdatePatch
            {
                public static void Postfix()
                {
                    if (_unreadBadge != null && !_unreadBadge.IsDestroyedOrNull())
                        _unreadBadge.SetActive(BugReportSystem.HasAnyUnread);
                }
            }
        }

        // ロビー・ゲーム内でのボタンは Patches/BugReportHudButtonPatch.cs で
        // HELPボタンと同じ見た目(バニラボタン枠を複製したもの)として生成している。
    }

    // ===== バグ報告パネルと他の設定画面を同時に開けないようにする =====
    //
    // 要望により、ゲーム設定画面(GameSettingMenu)や一般設定画面(OptionsMenuBehaviour)を
    // 開いたときに、バグ報告のテキスト入力欄が裏で表示されたまま残ってしまう
    // (「テキストだけ浮く」)不具合を防ぐため、これらの画面が開かれたタイミングで
    // バグ報告側が開いていれば自動的に閉じる。
    [HarmonyPatch(typeof(GameSettingMenu), nameof(GameSettingMenu.Start))]
    public static class BugReportCloseOnGameSettingMenuPatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.First)]
        public static void Postfix()
        {
            if (BugReportWizard.Step != BugReportWizardStep.Hidden)
                BugReportWizard.Close();
        }
    }

    [HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.Open))]
    public static class BugReportCloseOnOptionsMenuPatch
    {
        [HarmonyPostfix, HarmonyPriority(Priority.First)]
        public static void Postfix()
        {
            if (BugReportWizard.Step != BugReportWizardStep.Hidden)
                BugReportWizard.Close();
        }
    }
}
