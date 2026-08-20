using System;

using System.Text.RegularExpressions;



using TMPro;

using HarmonyLib;

using UnityEngine;

using AmongUs.Data;

using Assets.InnerNet;



using TownOfHost.Templates;

using Object = UnityEngine.Object;

using TownOfHost.Modules;

using System.Linq;



namespace TownOfHost

{

    [HarmonyPatch(typeof(MainMenuManager))]

    public class MainMenuManagerPatch

    {

        private const string OnlineButtonScalerPath = "MainUI/AspectScaler/RightPanel/MaskedBlackScreen/OnlineButtons/AspectSize/Scaler";

        private static SimpleButton discordButton;

        private static SimpleButton StatisticsButton;

        private static GameObject Statistics_ScrollStuff;

        public static SimpleButton UpdateButton { get; private set; }

        public static SimpleButton UpdateButton2;

        private static SimpleButton gitHubButton;

        private static SimpleButton TwitterXButton;

        private static SimpleButton TOHhmBOTButton;

        private static SimpleButton MatchmakingBotButton;

        private static SimpleButton RoleCheckBotButton;

        private static SimpleButton RoleInfoButton;

        private static SimpleButton betaversionchange;

        private const float MainButtonFontSize = 2.1f; // 従来のデフォルトより拡大

        public static TextMeshPro Statistics_TMP;

        public static GameObject VersionMenu;

        public static GameObject betaVersionMenu;

        public static AnnouncementPopUp updatea;



        [HarmonyPatch(nameof(MainMenuManager.Start)), HarmonyPostfix, HarmonyPriority(Priority.Normal)]

        public static void StartPostfix(MainMenuManager __instance)

        {

            // Watchdogはメインメニュー到達後にのみ有効化し、再起動マーカーがあればロビーを立て直す。

            WatchdogLauncher.NotifyMainMenuReached();

            AutoRehost.CheckRelaunchMarker();



            SimpleButton.SetBase(__instance.quitButton);



            if (SimpleButton.IsNullOrDestroyed(RoleInfoButton))

            {

                RoleInfoButton = CreateButton(

                    "RoleInfoButton",

                    new(2.7f, 1.05f, 1f), // さらに右上へ調整

                    new Color32(51, 156, 126, byte.MaxValue),

                    new Color32(103, 224, 190, byte.MaxValue),

                    () =>

                    {

                        RoleInfoShower.CreateMenu(__instance);

                    },

                    "Role/Achivement"

                );

                RoleInfoButton.FontSize = MainButtonFontSize;

            }

            //Discordボタンを生成

            if (SimpleButton.IsNullOrDestroyed(discordButton))

            {

                discordButton = CreateButton(

                    "DiscordButton",

                    new(-2.5f, -1.45f, 1f), // さらに下方向へ調整(ROLE/ACHIVEMENTを上げた分、間隔を確保)

                    new(88, 101, 242, byte.MaxValue),

                    new(148, 161, byte.MaxValue, byte.MaxValue),

                    () => Application.OpenURL(Main.DiscordInviteUrl),

                    "Discord",

                    isActive: Main.ShowDiscordButton);

                discordButton.FontSize = MainButtonFontSize;

            }



            // GitHubボタンを生成

            if (SimpleButton.IsNullOrDestroyed(gitHubButton))

            {

                gitHubButton = CreateButton(

                    "GitHubButton",

                    new(-0.8f, -1.45f, 1f), // さらに下方向へ調整(ROLE/ACHIVEMENTを上げた分、間隔を確保)

                    new(153, 153, 153, byte.MaxValue),

                    new(209, 209, 209, byte.MaxValue),

                    () => Application.OpenURL("https://github.com/rar006/TownOfHost-hamo"),

                    "GitHub");

                gitHubButton.FontSize = MainButtonFontSize;

            }



            // Youtubeボタンを生成

            if (SimpleButton.IsNullOrDestroyed(TwitterXButton))

            {

                TwitterXButton = CreateButton(

                    "TwitterXButton",

                    new(0.9f, -1.45f, 1f), // さらに下方向へ調整(ROLE/ACHIVEMENTを上げた分、間隔を確保)

                    new(0, 202, 255, byte.MaxValue),

                    new(60, 255, 255, byte.MaxValue),

                    () => Application.OpenURL("youtube.com/@harudayo1210?si=XFtImV4TE2FO9o-U"),

                    "Youtube");

                TwitterXButton.FontSize = MainButtonFontSize;

            }

            // TOHhmBOTボタンを生成

            if (SimpleButton.IsNullOrDestroyed(TOHhmBOTButton))

            {

                TOHhmBOTButton = CreateButton(

                    "TOHhmBOTButton",

                    new(2.6f, -1.45f, 1f), // さらに下方向へ調整(ROLE/ACHIVEMENTを上げた分、間隔を確保)

                    new(0, 201, 87, byte.MaxValue),

                    new(60, 201, 87, byte.MaxValue),

                    () => ToggleBotSubButtons(),

                    "TOHhmBOT");

                TOHhmBOTButton.FontSize = MainButtonFontSize;

            }

            // マッチメイキングBot招待ボタン（TOHhmBOTボタン押下でトグル表示）

            if (SimpleButton.IsNullOrDestroyed(MatchmakingBotButton))

            {

                MatchmakingBotButton = CreateButton(

                    "MatchmakingBotButton",

                    new(2.6f, -1.95f, 1f), // TOHhmBOTButtonの再移動に合わせて調整

                    new(0, 170, 120, byte.MaxValue),

                    new(60, 220, 170, byte.MaxValue),

                    () => Application.OpenURL(Main.MatchmakingBotInviteUrl),

                    "マッチメイキングBot",

                    scale: new Vector2(2.2f, 0.4f),

                    isActive: false);

                MatchmakingBotButton.FontSize = 1.7f; // 従来1.4f→拡大

            }

            // 役職確認Bot招待ボタン（TOHhmBOTボタン押下でトグル表示）

            if (SimpleButton.IsNullOrDestroyed(RoleCheckBotButton))

            {

                RoleCheckBotButton = CreateButton(

                    "RoleCheckBotButton",

                    new(2.6f, -2.35f, 1f), // TOHhmBOTButtonの再移動に合わせて調整

                    new(0, 140, 150, byte.MaxValue),

                    new(60, 190, 200, byte.MaxValue),

                    () => Application.OpenURL(Main.RoleCheckBotInviteUrl),

                    "役職確認Bot",

                    scale: new Vector2(2.2f, 0.4f),

                    isActive: false);

                RoleCheckBotButton.FontSize = 1.7f; // 従来1.4f→拡大

            }

            if (SimpleButton.IsNullOrDestroyed(StatisticsButton))

            {

                StatisticsButton = CreateButton(

                    "StatisticsButton",

                    new Vector3(0, -2.6963f, -5f),

                    new(255, 242, 104, byte.MaxValue),

                    new(255, 248, 173, byte.MaxValue),

                    () =>

                    {

                        CredentialsPatch.TOHhmLogo.gameObject.SetActive(false);

                        __instance.screenTint.enabled = true;

                        Statistics_TMP.gameObject.SetActive(true);

                        Statistics_TMP.text = $"<size=60%>{SaveStatistics.ShowText()}";

                        Statistics_ScrollStuff.gameObject.SetActive(true);

                        var St_Scroller = Statistics_ScrollStuff.transform.GetChild(0);

                        Statistics_TMP.transform.parent = St_Scroller.GetChild(3).transform;

                        St_Scroller.GetChild(1).gameObject.SetActive(false);

                        St_Scroller.GetChild(2).gameObject.SetActive(false);

                        St_Scroller.localPosition = new(3.18f, -5.45f, 0.1473f);

                        St_Scroller.GetChild(0).localPosition = new(2.1f, 2.6f, 2f);

                        St_Scroller.GetChild(0).localScale = new Vector3(0.7f, 1, 0);

                        St_Scroller.GetChild(3).SetLocalY(0);

                        var ages = Statistics_TMP.text.Split("\n").Count();

                        St_Scroller.GetComponentInParent<Scroller>().ContentYBounds.max = ages > 16 ? (ages - 16) * 0.25f : 0;

                    },

                    Translator.GetString("Statistics")

                    );

                StatisticsButton.FontSize = MainButtonFontSize;

            }



            if (Statistics_ScrollStuff == null || Statistics_ScrollStuff.gameObject == null)

            {

                var sc = GameObject.Find("StoreMenu/Background/Scroll Stuff");

                Statistics_ScrollStuff = Object.Instantiate(sc, __instance.transform);

                Statistics_ScrollStuff.gameObject.name = "stscroll";

                var Scroller = Statistics_ScrollStuff.transform.GetChild(0);

                Scroller.GetChild(3).DestroyChildren();//inner全削除

                Statistics_ScrollStuff.gameObject.SetActive(false);

            }



            //Updateボタンを生成

            if (SimpleButton.IsNullOrDestroyed(UpdateButton))

            {

                UpdateButton = CreateButton(

                    "UpdateButton",

                    new(0f, -1.7f, 1f),

                    new(0, 202, 255, byte.MaxValue),

                    new(60, 255, 255, byte.MaxValue),

                    () =>

                    {

                        //if (!Main.AllowPublicRoom)

                        //{

                        UpdateButton.Button.gameObject.SetActive(false);

                        ModUpdater.StartUpdate(ModUpdater.downloadUrl);

                        //}

                        /*else

                        {

                            UpdateButton.Button.gameObject.SetActive(false);

                            ModUpdater.GoGithub();

                        }*/

                    },

                    $"{Translator.GetString("updateButton")}\n{ModUpdater.latestTitle}",

                    new(2.5f, 1f),

                    isActive: false);

            }

            // アップデート(詳細)ボタンを生成

            if (SimpleButton.IsNullOrDestroyed(UpdateButton2))

            {

                UpdateButton2 = CreateButton(

                    "UpdateButton2",

                    new(1.3f, -1.9f, 1f),

                    new(153, 153, 153, byte.MaxValue),

                    new(209, 209, 209, byte.MaxValue),

                    () =>

                    {

                        if (updatea == null)

                        {

                            updatea = Object.Instantiate(__instance.announcementPopUp);

                        }

                        updatea.name = "Update Detail";

                        updatea.gameObject.SetActive(true);

                        updatea.AnnouncementListSlider.SetActive(false);

                        updatea.Title.text = "TOH-hm " + ModUpdater.latestTitle;

                        updatea.AnnouncementBodyText.text = Regex.Replace(ModUpdater.body.Replace("#", "").Replace("**", ""), @"\[(.*?)\]\(.*?\)", "$1");

                        updatea.DateString.text = "Latest Release";

                        updatea.SubTitle.text = "";

                        updatea.ListScroller.gameObject.SetActive(false);

                    },

                    "▽",

                    new(0.5f, 0.5f),

                    isActive: false);

            }

            //同じバージョンの 安定ver,デバッグバージョンの切り替えの奴

            if (SimpleButton.IsNullOrDestroyed(betaversionchange))

            {

                betaversionchange = CreateButton(

                    "betaversionchange",

                    new(-2.3f, -2.6963f, 1f),

                    new(0, 255, 183, byte.MaxValue),

                    new(60, 255, 183, byte.MaxValue),

                    () =>

                    {

                        CredentialsPatch.TOHhmLogo.gameObject.SetActive(false);

                        __instance.screenTint.enabled = true;

                        if (betaVersionMenu != null)

                        {

                            betaVersionMenu.SetActive(true);

                            return;

                        }

                        betaVersionMenu = new GameObject("verPanel");

                        betaVersionMenu.transform.parent = __instance.gameModeButtons.transform.parent;

                        betaVersionMenu.transform.localPosition = new(-0.0964f, 0.1378f, 1f);

                        betaVersionMenu.SetActive(true);

                        ModUpdater.CheckRelease(all: true).GetAwaiter().GetResult();

                        int i = 0;

                        if (ModUpdater.snapshots.Count == 0) return;



                        foreach (var release in ModUpdater.snapshots)

                        {

                            int column = i % 4;

                            int row = i / 4;

                            // X 座標と Y 座標を計算

                            float x = -1.6891f + (1.6891f * column);

                            float y = 0.8709f - (0.3927f * row);

                            var button2 = new SimpleButton(

                            betaVersionMenu.transform,

                            release.TagName,

                            new(x, y, 1f),

                            release.TagName.Contains("S") ? new(0, 255, 183, byte.MaxValue) : new(0, 202, 255, byte.MaxValue),

                            release.TagName.Contains("S") ? new(60, 255, 183, byte.MaxValue) : new(60, 255, 255, byte.MaxValue),

                            () =>

                            {

                                if (release.DownloadUrl != null)

                                    ModUpdater.StartUpdate(release.DownloadUrl, release.OpenURL);

                            },

                            "v" + release.TagName.TrimStart('v').Trim('S').Trim('s') + (release.DownloadUrl == null ? "(ERROR)" : ""));

                            i++;

                            button2.Button.OnMouseOver.AddListener((Action)(() => ToolTip.Show(button2.Button, release.Info, null)));

                            button2.Button.OnMouseOut.AddListener((Action)ToolTip.Hide);

                        }

                    },

                    Translator.GetString("versionchangebutton"));

                betaversionchange.FontSize = 2;

            }

            CreateStreameMenu.CreateMenu(__instance);

            __instance.ResetScreen();



            // フリープレイの無効化

            var howToPlayButton = __instance.howToPlayButton;

            var freeplayButton = howToPlayButton.transform.parent.Find("FreePlayButton");

#if RELEASE

            if (freeplayButton != null)

            {

                var textm = freeplayButton.transform.FindChild("Text_TMP").GetComponent<TextMeshPro>();

                textm.DestroyTranslator();

                textm.text = Translator.GetString("EditCSp");



                freeplayButton.GetComponent<PassiveButton>().OnClick.AddListener((Action)(() => CustomSpawnEditor.ActiveEditMode = true));

            }

            // フリープレイが消えるのでHowToPlayをセンタリング | 消えないのでしません☆

            //howToPlayButton.transform.SetLocalX(0);

#endif

#if DEBUG

            var csbutton = GameObject.Instantiate(freeplayButton, freeplayButton.parent);

            var textm = csbutton.transform.FindChild("Text_TMP").GetComponent<TextMeshPro>();

            textm.DestroyTranslator();

            textm.text = Translator.GetString("EditCSp");



            csbutton.transform.localPosition = new Vector3(2.8704f, -1.9916f);

            csbutton.transform.localScale = new Vector3(0.6f, 0.6f);

            var pb = csbutton.GetComponent<PassiveButton>();

            pb.inactiveSprites.GetComponent<SpriteRenderer>().color = new(88, 101, 242, byte.MaxValue);

            pb.activeSprites.GetComponent<SpriteRenderer>().color = new(148, 161, byte.MaxValue, byte.MaxValue);

            pb.OnClick.AddListener((Action)(() => CustomSpawnEditor.ActiveEditMode = true));

            freeplayButton.GetComponent<PassiveButton>().OnClick.AddListener((Action)(() => CustomSpawnEditor.ActiveEditMode = false));//ボタンを生成

#endif

        }



        private static void ShowOnlineJoinControls(MainMenuManager mainMenu)

        {

            var scaler = mainMenu.transform.Find(OnlineButtonScalerPath);

            if (scaler == null) return;



            scaler.Find("Enter Code Button")?.gameObject.SetActive(true);

            scaler.Find("Find Game Button")?.gameObject.SetActive(true);

            scaler.Find("Line")?.gameObject.SetActive(true);

            scaler.Find("Create Lobby Button")?.gameObject.SetActive(true);



            // ===== 「ゲームを探す」を消して「ゲーム作成」「コード入力」を横並びにする =====

            // 別MOD(TownOfHost-K)の実装を参考に、正確なシーン階層パスを特定して対応。

            ApplyOnlineButtonsLayout(scaler);

        }



        // 「ゲーム作成」「コード入力」ボタンの、MOD適用前の元の位置・スケール(初回のみ記録)。

        // これが無いと、OpenOnlineMenuが呼ばれるたびに「既に移動済みの位置」を基準に

        // 再計算してしまい、呼ばれるたびに位置やサイズがズレていく不具合になる。

        private static Vector3? _originalCreateLobbyPos;

        private static Vector3? _originalEnterCodePos;

        private static Vector3? _originalCreateLobbyScale;

        private static Vector3? _originalEnterCodeScale;



        /// <summary>

        /// オンライン画面の「ゲームを探す」ボタンを非表示にし、

        /// 「ゲーム作成」「コード入力」ボタンを横並びに再配置・拡大する。

        /// </summary>

        private static void ApplyOnlineButtonsLayout(Transform scaler)

        {

            try

            {

                var findGameButton = scaler.Find("Find Game Button");

                var line = scaler.Find("Line");

                var createLobbyButton = scaler.Find("Create Lobby Button");

                var enterCodeButton = scaler.Find("Enter Code Button");



                // 「ゲームを探す」ボタンと、その隣の区切り線を非表示にする

                if (findGameButton != null) findGameButton.gameObject.SetActive(false);

                if (line != null) line.gameObject.SetActive(false);



                if (createLobbyButton == null || enterCodeButton == null) return;



                // 元の位置・スケールを初回だけ記録する(2回目以降はこのキャッシュ値を基準にする)

                _originalCreateLobbyPos ??= createLobbyButton.localPosition;

                _originalEnterCodePos ??= enterCodeButton.localPosition;

                _originalCreateLobbyScale ??= createLobbyButton.localScale;

                _originalEnterCodeScale ??= enterCodeButton.localScale;



                var createPos = _originalCreateLobbyPos.Value;

                var codePos = _originalEnterCodePos.Value;

                float centerY = (createPos.y + codePos.y) / 2f;

                // 「ゲームを探す」が消えて空いた右側のスペースも使えるよう、

                // 中心を元の中間よりさらに右へ寄せる。

                float centerX = (createPos.x + codePos.x) / 2f + 1.6f;



                // 「ゲームを探す」が消えて広がった横幅を使えるよう、左右に間隔を空けて配置する。

                const float horizontalGap = 2.1f;

                createLobbyButton.localPosition = new Vector3(centerX - horizontalGap, centerY, createPos.z);

                enterCodeButton.localPosition = new Vector3(centerX + horizontalGap, centerY, codePos.z);



                // ボタン自体も少し大きくする

                const float scaleMultiplier = 1.35f;

                createLobbyButton.localScale = _originalCreateLobbyScale.Value * scaleMultiplier;

                enterCodeButton.localScale = _originalEnterCodeScale.Value * scaleMultiplier;

            }

            catch (Exception e)

            {

                Logger.Error($"オンライン画面ボタンの再配置に失敗: {e.Message}", "MainMenuManagerPatch");

            }

        }



        /// <summary>TOHロゴの子としてボタンを生成</summary>

        /// <param name="name">オブジェクト名</param>

        /// <param name="normalColor">普段のボタンの色</param>

        /// <param name="hoverColor">マウスが乗っているときのボタンの色</param>

        /// <param name="action">押したときに発火するアクション</param>

        /// <param name="label">ボタンのテキスト</param>

        /// <param name="scale">ボタンのサイズ 変更しないなら不要</param>

        private static void ToggleBotSubButtons()

        {

            if (SimpleButton.IsNullOrDestroyed(MatchmakingBotButton) || SimpleButton.IsNullOrDestroyed(RoleCheckBotButton)) return;



            var show = !MatchmakingBotButton.Button.gameObject.activeSelf;

            MatchmakingBotButton.Button.gameObject.SetActive(show);

            RoleCheckBotButton.Button.gameObject.SetActive(show);

        }



        public static SimpleButton CreateButton(

            string name,

            Vector3 localPosition,

            Color32 normalColor,

            Color32 hoverColor,

            Action action,

            string label,

            Vector2? scale = null,

            bool isActive = true,

            Transform transform = null)

        {

            var button = new SimpleButton(transform == null ? CredentialsPatch.TOHhmLogo.transform : transform, name, localPosition, normalColor, hoverColor, action, label, isActive);

            if (scale.HasValue)

            {

                button.Scale = scale.Value;

            }

            return button;

        }



        [HarmonyPatch(nameof(MainMenuManager.OpenFindGame))]

        [HarmonyPrefix]

        public static bool ClickFindGame()

        {

            return true;

        }

        [HarmonyPatch(nameof(MainMenuManager.OpenEnterCodeMenu))]

        [HarmonyPrefix]

        public static bool ClickOpenEnterCodeMenu()

        {

            return true;

        }

        [HarmonyPatch(nameof(MainMenuManager.OpenOnlineMenu))]

        [HarmonyPostfix]

        public static void OpenOnlineMenuPostfix(MainMenuManager __instance)

        {

            ShowOnlineJoinControls(__instance);

        }

        // プレイメニュー，アカウントメニュー，クレジット画面が開かれたらロゴとボタンを消す

        [HarmonyPatch(nameof(MainMenuManager.OpenGameModeMenu))]

        [HarmonyPatch(nameof(MainMenuManager.OpenAccountMenu))]

        [HarmonyPatch(nameof(MainMenuManager.OpenCredits))]

        [HarmonyPatch(nameof(MainMenuManager.OpenOnlineMenu))]

        [HarmonyPatch(nameof(MainMenuManager.OpenEnterCodeMenu))]

        [HarmonyPostfix]

        public static void OpenMenuPostfix(MainMenuManager __instance)

        {

            CreateStreameMenu.CloseMenu();

            var onlineButtonScaler = __instance.transform.Find(OnlineButtonScalerPath);



            if (CredentialsPatch.TOHhmLogo != null)

            {

                CredentialsPatch.TOHhmLogo.gameObject.SetActive(false);

            }

            if (VersionMenu != null)

                VersionMenu.SetActive(false);

            if (betaVersionMenu != null)

                betaVersionMenu.SetActive(false);

            if (Statistics_TMP?.gameObject != null)

                Statistics_TMP?.gameObject.SetActive(false);

            if (Statistics_ScrollStuff?.gameObject != null)

                Statistics_ScrollStuff?.gameObject.SetActive(false);



            var warning = onlineButtonScaler?.parent?.Find("CrossplayWarning")?.gameObject;

            var TMP = warning?.transform.Find("CrossPlayText/Text_TMP")?.GetComponent<TextMeshPro>();

            if (warning != null && TMP != null)

            {

                warning.SetActive(true);

                var cantJoin = VersionInfoManager.version != null && VersionInfoManager.version.DisableRoomJoin == true;

                cantJoin |= VersionInfoManager.allversion != null && VersionInfoManager.allversion.DisableRoomJoin == true;

                var text = Main.IsAndroid() ? Translator.GetString("CantAndroidCreateGame") : cantJoin ? Translator.GetString("CantPublickAndJoin") : "";

                TMP.SetText(text);

                _ = new LateTask(() => TMP.SetText(text), 0.05f, "Set", true);

                if (text == "") warning.SetActive(false);

            }

            OptionsMenuBehaviourStartPatch.Instance = null;

        }

        [HarmonyPatch(nameof(MainMenuManager.ResetScreen)), HarmonyPostfix]

        public static void ResetScreenPostfix(MainMenuManager __instance)

        {

            if (CredentialsPatch.TOHhmLogo != null)

            {

                CredentialsPatch.TOHhmLogo?.gameObject?.SetActive(true);

            }

            if (VersionMenu != null)

                VersionMenu.SetActive(false);

            if (betaVersionMenu != null)

                betaVersionMenu.SetActive(false);

            if (Statistics_TMP != null)

                Statistics_TMP?.gameObject.SetActive(false);

            if (Statistics_ScrollStuff != null)

                Statistics_ScrollStuff?.gameObject.SetActive(false);

            CreateStreameMenu.CloseMenu();

            _ = new LateTask(() =>

            {

                if (__instance == null) return;



                var ejectButton = __instance.ejectMenu?.ejectButton;

                if (ejectButton != null && ejectButton.gameObject != null)

                    ejectButton.gameObject.SetActive(true);

            }, 0.5f, "ShowButton", true);

        }

        public static void DestroyButton()

        {

            VersionMenu = null;

            betaVersionMenu = null;

        }

    }

    public class ModNews

    {

        public int Number;

        public int BeforeNumber;

        public string Title;

        public string SubTitle;

        public string ShortTitle;

        public string Text;

        public string Date;



        public Announcement ToAnnouncement()

        {

            var result = new Announcement

            {

                Number = Number,

                Title = Title,

                SubTitle = SubTitle,

                ShortTitle = ShortTitle,

                Text = Text,

                Language = (uint)DataManager.Settings.Language.CurrentLanguage,

                Date = Date,

                Id = "ModNews"

            };



            return result;

        }

    }

    public class JsonModNews

    {

        public JsonModNews(int Number, string Title, string SubTitle, string ShortTitle,

            string Text, string Date)

        {

            var news = new ModNews

            {

                Number = Number,

                Title = Title,

                SubTitle = SubTitle,

                ShortTitle = ShortTitle,

                Text = Text,

                Date = Date

            };

            ModNewsHistory.JsonAndAllModNews.Add(news);

        }

    }

    [HarmonyPatch(typeof(EjectMainMenu), nameof(EjectMainMenu.EjectCrewmate))]

    class EjectMainMenuEjectCrewmatePatch

    {

        public static int i = 0;

        // TownOfHost-K 61a7bb5: pressStateがnullになるバニラのeject処理を回避する。
        // hamo側の既存Postfix（連続eject処理）はそのまま保持する。
        public static bool Prefix(EjectMainMenu __instance)
        {
            PlayerParticle playerParticle = __instance.pool.Get<PlayerParticle>();
            PlayerMaterial.SetColors(IRandom.Instance.Next(0, 18), (Renderer)(object)playerParticle.myRend);
            __instance.PlacePlayer(playerParticle, initial: false);
            return false;
        }

        public static void Postfix(EjectMainMenu __instance)

        {

            try

            {

                i++;

                __instance.pressState?.SetActive(false);

                __instance.ejectButton?.SetActive(true);

                __instance.onCooldown = false;

                if (10 < i && i < 60)

                {

                    __instance.EjectCrewmate();

                }

                if (80 < i)

                {

                    i = 0;

                }



                if (IRandom.Instance.Next(3) is 1)

                {

                    __instance.EjectCrewmate();

                }

            }

            catch { }

        }

    }

    [HarmonyPatch(typeof(EjectMainMenu), nameof(EjectMainMenu.PlacePlayer))]

    class EjectMainMenuEjectPlacePlayerPatch

    {

        public static Shader Shader = null;

        public static void Postfix(EjectMainMenu __instance, PlayerParticle part)

        {

            var chance = IRandom.Instance.Next(3);

            var size = 30 + IRandom.Instance.Next(50);

            if (Shader is null)

            {

                Shader = part.myRend.material.shader;

            }

            part.myRend.material.shader = Shader;

            part.myRend.sharedMaterial.shader = Shader;

            Shader shader = Shader.Find("Sprites/Default");

            if (chance is 1)

            {

                var allrole = CustomRolesHelper.AllStandardRoles;

                var role = allrole[IRandom.Instance.Next(allrole.Count())];

                var sprite = UtilsSprite.LoadSprite($"TownOfHost.Resources.TOHhm.Label.{role}.png", size);

                if (sprite is null) return;

                part.myRend.material.shader = shader;

                part.myRend.sharedMaterial.shader = shader;

                part.myRend.sprite = sprite;

            }

            if (chance is 2)

            {

                var allrole = CustomRolesHelper.AllRoles;

                var role = allrole[IRandom.Instance.Next(allrole.Count())];

                var sprite = UtilsSprite.LoadSprite($"TownOfHost.Resources.TOHhm.Button.{role}_Ability.png", size);

                if (sprite is null)

                    sprite = UtilsSprite.LoadSprite($"TownOfHost.Resources.TOHhm.Button.{role}_Kill.png", size);

                if (sprite is null)

                    sprite = UtilsSprite.LoadSprite($"TownOfHost.Resources.TOHhm.Button.{role}_Vent.png", size);

                if (sprite is null) return;

                part.myRend.material.shader = shader;

                part.myRend.sharedMaterial.shader = shader;

                part.myRend.sprite = sprite;

            }

        }

    }

}

