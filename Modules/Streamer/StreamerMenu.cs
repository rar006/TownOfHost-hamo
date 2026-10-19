using TMPro;
using UnityEngine;

using TownOfHost.Templates;
using static TownOfHost.Translator;
using Object = UnityEngine.Object;

namespace TownOfHost
{
    class CreateStreameMenu
    {
        public static SimpleButton OpenMenu;
        public static SimpleButton SetURL; public static TextMeshPro NowURLText; public static TextMeshPro NowURLTitle;
        /// <summary>YouTubeのコメント枠の表示ON/OFF</summary>
        public static SimpleButton CommentFrameToggle;
        /// <summary>YouTubeのコメント枠の位置を最初の場所に戻す</summary>
        public static SimpleButton CommentFrameReset;
        /// <summary>OBS用の試合の状況ページ(ON/OFF)と、役職の出し方</summary>
        public static SimpleButton ObsToggle;
        public static SimpleButton ObsRoleMode;
        static string ObsLabel() => Main.ObsOverlay.Value ? $"OBS表示: ON\n{ObsOverlay.Url}" : "OBS表示: OFF";
        static string ObsRoleLabel() => $"OBSの役職: {ObsOverlay.RoleModeLabel(Main.ObsShowRoles.Value)}";
        static string CommentFrameLabel() => Main.ShowStreamComments.Value ? "コメント枠: ON" : "コメント枠: OFF";
        public static TextBoxTMP JoinWord; public static TextMeshPro JoinWordTitle;
        public static TextBoxTMP RemoveWord; public static TextMeshPro RemoveWordTitle;
        public static void CreateMenu(MainMenuManager mainmenumanager)
        {
            var textbox = GameObject.Find("AccountManager/AccountTab/AccountWindow/SubWindows/EditName/NameText");
            if (SimpleButton.IsNullOrDestroyed(OpenMenu))
            {
                OpenMenu = MainMenuManagerPatch.CreateButton(
                    "OpenStreamMenu",
                    new Vector3(2.4036f, -2.6963f, 1f),
                    new(245, 46, 32, byte.MaxValue),
                    new(212, 113, 106, byte.MaxValue),
                    () =>
                    {
                        SetOpenMenu();
                        // ホームのUI(ロゴ・DISCORD等のボタン群)をまとめて隠し、✘ボタンで戻れるようにする
                        MainMenuManagerPatch.EnterSubMenu(mainmenumanager);
                    },
                    "Stream"
                    );
                {
                    JoinWord = Object.Instantiate(textbox, mainmenumanager.screenTint.transform).GetComponent<TextBoxTMP>();
                    RemoveWord = Object.Instantiate(textbox, mainmenumanager.screenTint.transform).GetComponent<TextBoxTMP>();
                    DestroyNameMono(JoinWord.gameObject);
                    DestroyNameMono(RemoveWord.gameObject);
                    JoinWord.name = "joinword";
                    RemoveWord.name = "removeword";
                    JoinWord.gameObject.SetActive(false);
                    RemoveWord.gameObject.SetActive(false);
                    JoinWord.allowAllCharacters = true;
                    JoinWord.AllowSymbols = true;
                    JoinWord.AllowEmail = true;
                    JoinWord.AllowPaste = true;
                    JoinWord.characterLimit = 20;
                    JoinWord.SetText(Main.JoinWord.Value);
                    RemoveWord.allowAllCharacters = true;
                    RemoveWord.AllowSymbols = true;
                    RemoveWord.AllowEmail = true;
                    RemoveWord.AllowPaste = true;
                    RemoveWord.characterLimit = 20;
                    RemoveWord.SetText(Main.RemoveWord.Value);
                }
            }
            if (SimpleButton.IsNullOrDestroyed(SetURL))
            {
                SetURL = MainMenuManagerPatch.CreateButton(
                    "PasteURL",
                    new Vector3(-1.9f, 0.85f, -5),
                    new(245, 46, 32, byte.MaxValue),
                    new(212, 113, 106, byte.MaxValue),
                    () =>
                    {
                        var url = GUIUtility.systemCopyBuffer;
                        if (StreamerInfo.TrySetStreamURL(url))
                        {
                            NowURLText.text = StreamerInfo.StreamURL;
                        }
                        else
                        {
                            NowURLText.text = "Error!" + GetString("ClickToSetURL");
                        }
                    },
                    "Set URL",
                    transform: mainmenumanager.screenTint.transform
                    );
                SetURL.Button.gameObject.SetActive(false);
            }

            if (SimpleButton.IsNullOrDestroyed(CommentFrameToggle))
            {
                CommentFrameToggle = MainMenuManagerPatch.CreateButton(
                    "CommentFrameToggle",
                    new Vector3(-1.9f, -2.35f, -5),
                    new(245, 46, 32, byte.MaxValue),
                    new(212, 113, 106, byte.MaxValue),
                    () =>
                    {
                        Main.ShowStreamComments.Value = !Main.ShowStreamComments.Value;
                        // ゲーム内の設定(OTHER2)の ON/OFF も合わせる
                        Modules.ExtraFeatureOptions.ShowStreamComments?.SetValue(Main.ShowStreamComments.Value ? 1 : 0, false, false);
                        if (CommentFrameToggle?.Label != null) CommentFrameToggle.Label.text = CommentFrameLabel();
                    },
                    CommentFrameLabel(),
                    transform: mainmenumanager.screenTint.transform
                    );
                CommentFrameToggle.Button.gameObject.SetActive(false);
            }
            if (SimpleButton.IsNullOrDestroyed(ObsToggle))
            {
                ObsToggle = MainMenuManagerPatch.CreateButton(
                    "ObsToggle",
                    new Vector3(2.1f, -2.35f, -5),
                    new(80, 80, 200, byte.MaxValue),
                    new(110, 110, 230, byte.MaxValue),
                    () =>
                    {
                        Main.ObsOverlay.Value = !Main.ObsOverlay.Value;
                        // ONにしたら、OBSに入れるURLをコピーしておく
                        if (Main.ObsOverlay.Value) GUIUtility.systemCopyBuffer = ObsOverlay.Url;
                        if (ObsToggle?.Label != null) ObsToggle.Label.text = ObsLabel();
                    },
                    ObsLabel(),
                    transform: mainmenumanager.screenTint.transform
                    );
                ObsToggle.Button.gameObject.SetActive(false);
                ObsRoleMode = MainMenuManagerPatch.CreateButton(
                    "ObsRoleMode",
                    new Vector3(2.1f, -1.8f, -5),
                    new(80, 80, 200, byte.MaxValue),
                    new(110, 110, 230, byte.MaxValue),
                    () =>
                    {
                        Main.ObsShowRoles.Value = (Main.ObsShowRoles.Value + 1) % 3;
                        if (ObsRoleMode?.Label != null) ObsRoleMode.Label.text = ObsRoleLabel();
                    },
                    ObsRoleLabel(),
                    transform: mainmenumanager.screenTint.transform
                    );
                ObsRoleMode.Button.gameObject.SetActive(false);
            }
            if (SimpleButton.IsNullOrDestroyed(CommentFrameReset))
            {
                CommentFrameReset = MainMenuManagerPatch.CreateButton(
                    "CommentFrameReset",
                    new Vector3(0.1f, -2.35f, -5),
                    new(245, 46, 32, byte.MaxValue),
                    new(212, 113, 106, byte.MaxValue),
                    () => StreamCommentFrame.ResetPosition(),
                    "枠の位置・大きさを戻す",
                    transform: mainmenumanager.screenTint.transform
                    );
                CommentFrameReset.Button.gameObject.SetActive(false);
            }
            void SetOpenMenu()
            {
                if (!SimpleButton.IsNullOrDestroyed(CommentFrameToggle))
                {
                    CommentFrameToggle.Button.gameObject.SetActive(true);
                    if (CommentFrameToggle.Label != null) CommentFrameToggle.Label.text = CommentFrameLabel();
                }
                if (!SimpleButton.IsNullOrDestroyed(CommentFrameReset)) CommentFrameReset.Button.gameObject.SetActive(true);
                if (!SimpleButton.IsNullOrDestroyed(ObsToggle)) { ObsToggle.Button.gameObject.SetActive(true); ObsToggle.Label.text = ObsLabel(); }
                if (!SimpleButton.IsNullOrDestroyed(ObsRoleMode)) { ObsRoleMode.Button.gameObject.SetActive(true); ObsRoleMode.Label.text = ObsRoleLabel(); }
                mainmenumanager.screenTint.enabled = true;
                if (JoinWord is not null)
                {
                    JoinWord.gameObject.SetActive(true);
                    JoinWord.transform.localPosition = new Vector3(-1.2f, -0.25f, 0);
                    JoinWord.transform.localScale = new Vector3(0.7f, 0.7f);
                    JoinWord.allowAllCharacters = true;
                    JoinWord.AllowSymbols = true;
                    JoinWord.AllowEmail = true;
                    JoinWord.AllowPaste = true;
                    JoinWord.characterLimit = 20;
                    JoinWord.SetText(Main.JoinWord.Value);
                }
                if (RemoveWord is not null)
                {
                    RemoveWord.gameObject.SetActive(true);
                    RemoveWord.transform.localPosition = new Vector3(-1.2f, -1.65f, -5);
                    RemoveWord.transform.localScale = new Vector3(0.7f, 0.7f);
                    RemoveWord.allowAllCharacters = true;
                    RemoveWord.AllowSymbols = true;
                    RemoveWord.AllowEmail = true;
                    RemoveWord.AllowPaste = true;
                    RemoveWord.characterLimit = 20;
                    RemoveWord.SetText(Main.RemoveWord.Value);
                }
                if (NowURLText is not null)
                {
                    NowURLText.gameObject.SetActive(true);
                    NowURLText.transform.localPosition = new Vector3(1f, 1.2267f, -5);
                }
                if (NowURLTitle is not null)
                {
                    NowURLTitle?.gameObject?.SetActive(true);
                    NowURLTitle.transform.localPosition = new Vector3(0.8f, 1.8267f, 0);
                }
                if (JoinWordTitle is not null)
                {
                    JoinWordTitle?.gameObject?.SetActive(true);
                    JoinWordTitle.transform.localPosition = new Vector3(0.8f, 0.4267f, 0);
                }
                if (RemoveWordTitle is not null)
                {
                    RemoveWordTitle?.gameObject?.SetActive(true);
                    RemoveWordTitle.transform.localPosition = new Vector3(0.8f, -0.9733f, 0);
                }
                if (SetURL is not null)
                    SetURL?.Button?.gameObject?.SetActive(true);
            }

            void DestroyNameMono(GameObject @object)
            {
                if (@object.TryGetComponent<NameTextBehaviour>(out var nameTextBehaviour))
                {
                    Object.Destroy(nameTextBehaviour);
                }
            }
        }

        public static void CloseMenu()
        {
            if (JoinWord?.text is not "" and not null) Main.JoinWord.Value = JoinWord.text;
            if (RemoveWord?.text is not "" and not null) Main.RemoveWord.Value = RemoveWord.text;
            try
            {
                StreamerInfo.SetstreamKey();
                JoinWord?.gameObject?.SetActive(false);
                RemoveWord?.gameObject?.SetActive(false);
                NowURLText?.gameObject?.SetActive(false);
                SetURL?.Button?.gameObject?.SetActive(false);
                if (!SimpleButton.IsNullOrDestroyed(CommentFrameToggle)) CommentFrameToggle.Button.gameObject.SetActive(false);
                if (!SimpleButton.IsNullOrDestroyed(CommentFrameReset)) CommentFrameReset.Button.gameObject.SetActive(false);
                if (!SimpleButton.IsNullOrDestroyed(ObsToggle)) ObsToggle.Button.gameObject.SetActive(false);
                if (!SimpleButton.IsNullOrDestroyed(ObsRoleMode)) ObsRoleMode.Button.gameObject.SetActive(false);
                NowURLTitle?.gameObject?.SetActive(false);
                JoinWordTitle?.gameObject?.SetActive(false);
                RemoveWordTitle?.gameObject?.SetActive(false);
            }
            catch { }
        }

        private static string GetStreamText(string key, string fallback)
        {
            try
            {
                var translated = GetString(key);
                return string.IsNullOrWhiteSpace(translated) ? fallback : translated;
            }
            catch
            {
                return fallback;
            }
        }

        public static void CreateText()
        {
            if (NowURLText != null) return;

            NowURLText = TMPTemplate.Create("Nowurl", GetStreamText("Unsettled", "未設定"), Color.white, 2, TextAlignmentOptions.TopLeft, false);
            NowURLText.text = StreamerInfo.StreamURL is "" ? GetStreamText("ClickToSetURL", "URLを設定") : StreamerInfo.StreamURL;
            NowURLTitle = TMPTemplate.Create("NowURLTitle", $"<b><u>{GetStreamText("NowURLTitle", "現在のURL")}</b>", Color.white, 3, TextAlignmentOptions.TopLeft, false);
            JoinWordTitle = TMPTemplate.Create("JoinWordTitle", $"<u><b>{GetStreamText("JoinWordTitle", "参加ワード")}</b>", Color.white, 3, TextAlignmentOptions.TopLeft, false);
            RemoveWordTitle = TMPTemplate.Create("RemoveWordTitle", $"<u><b>{GetStreamText("RemoveWordTitle", "退出ワード")}</b>", Color.white, 3, TextAlignmentOptions.TopLeft, false);
        }
    }

    public class HopeInfo
    {
        public string Comment;
        public string PlayerName;
        public string AccountName;
        public string AccountID;
        public int Id;

        public static HopeInfo TryCrateHopeInfo(string comment, string accountid, string accountname)
        {
            if (comment.Contains(Main.JoinWord.Value))
            {
                var playername = comment.RemoveDeltext(Main.JoinWord.Value);
                return new HopeInfo(comment, accountid, accountname, playername);
            }
            return null;
        }
        HopeInfo(string comment, string accountid, string accountname, string playername)
        {
            this.Comment = comment;
            this.AccountID = accountid;
            this.AccountName = accountname;
            this.PlayerName = playername;
            Id = -1;
        }

        public bool IsUpdate(HopeInfo info)
        {
            //アカウントIDが一緒かつ、AmongUsのプレイヤー名が異なる
            if (this.AccountID == info.AccountID && info.PlayerName != this.PlayerName) return true;
            else return false;
        }

        public void SetId(int id)
        {
            this.Id = id;
        }
    }
}