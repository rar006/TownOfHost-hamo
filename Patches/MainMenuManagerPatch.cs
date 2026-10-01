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

        // プレイ画面（ローカル／オンラインの上）に表示するMODモード切替ボタン。
        // 切替後はロード済みDLLを安全に入れ替えるため、Among Usを自動再起動する。
        private static GameObject HostModeSwitchButton;
        private static GameObject ClientModeSwitchButton;

        // hamoロゴ差し替えを停止した後でも、追加ボタンを初期hamo版と同じ座標系へ配置する透明アンカー。
        // 元のhamoロゴは右パネル直下・localPosition (0, 0.15, 1) だったため、その位置だけを再現する。
        // ロゴ画像そのものは作成・変更しない。
        private static Transform MenuButtonParent;
        private static GameObject MenuButtonAnchor;

        private const float MainButtonFontSize = 2.1f; // 従来のデフォルトより拡大

        public static TextMeshPro Statistics_TMP;

        public static GameObject VersionMenu;

        public static GameObject betaVersionMenu;

        public static AnnouncementPopUp updatea;



        [HarmonyPatch(nameof(MainMenuManager.Start)), HarmonyPostfix, HarmonyPriority(Priority.Normal)]

        public static void StartPostfix(MainMenuManager __instance)

        {

            // 参加者専用表示版ではクラッシュ監視・自動部屋立て直しを行わず、
            // カスタムボタン表示とHELP役職説明だけを残して通常Among Usとして動作させる。
            if (!Main.IsNonHostClient)
            {
                WatchdogLauncher.NotifyMainMenuReached();
                AutoRehost.CheckRelaunchMarker();
            }



            SimpleButton.SetBase(__instance.quitButton);

            // オンラインを押した時の遷移先をモードごとに固定する。
            // ホスト版はゲーム作成画面、参加者専用版は部屋コード入力画面を直接開く。
            if (__instance.PlayOnlineButton != null)
            {
                __instance.PlayOnlineButton.OnClick = new();
                __instance.PlayOnlineButton.OnClick.AddListener((Action)(() =>
                {
                    if (Main.IsNonHostClient)
                        __instance.OpenEnterCodeMenu(false);
                    else
                        __instance.OpenCreateGame();
                }));
            }

            // 左上はAMONG USロゴのまま維持する。追加ボタンだけは初期hamo版の
            // titleLogo_TOHhm と同じ右パネル直下の透明アンカーへ配置する。
            var originalButtonParent = __instance.gameModeButtons?.transform?.parent ?? __instance.transform;
            if (MenuButtonAnchor == null || MenuButtonAnchor.transform.parent != originalButtonParent)
            {
                if (MenuButtonAnchor != null) Object.Destroy(MenuButtonAnchor);

                MenuButtonAnchor = new GameObject("TOHMenuButtonAnchor");
                // 初期版の titleLogo_TOHhm と同じく、Transform.parent代入で
                // 右パネルのスケールを継承してからローカル座標を設定する。
                MenuButtonAnchor.transform.parent = originalButtonParent;
                MenuButtonAnchor.transform.localPosition = new Vector3(0f, 0.15f, 1f);
            }
            MenuButtonParent = MenuButtonAnchor.transform;
            SetInitialMenuUiVisible(true);
            // 右下に出る赤い✘ボタン(要望により小さくする)。生成タイミングが遅いため少し待ってから縮小する。
            _ = new LateTask(() => ShrinkBottomRightRedButton(), 1.0f, "ShrinkBottomRightRedButton", true);
            // BUG/HELPボタンの枠用に、バニラのボタン枠スプライトを控えておく(フリープレイ等で枠が消えるのを防ぐ)
            _ = new LateTask(() => RoleGuideButtonPatch.CacheFrameTemplate(), 0.8f, "CacheFrameTemplate", true);
            _ = new LateTask(() => ShrinkBottomRightRedButton(), 3.0f, "ShrinkBottomRightRedButton2", true);

            // モード切替ボタンはゲームモード画面が実際に開かれた後に生成する。
            // Start時点ではgameModeButtonsが未生成のことがあるため、ここでは生成しない。



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

                        CredentialsPatch.TOHhmLogo?.gameObject?.SetActive(false);

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

                    GetMenuText("Statistics", "統計")

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

                    $"{GetMenuText("updateButton", "アップデート")}\n{ModUpdater.latestTitle}",

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

                        CredentialsPatch.TOHhmLogo?.gameObject?.SetActive(false);

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

                    GetMenuText("versionchangebutton", "バージョン切り替え"));

                betaversionchange.FontSize = 2;

            }

            CreateStreameMenu.CreateMenu(__instance);

            __instance.ResetScreen();



            // フリープレイ → 「カスタムスポーンを設定」ボタン化
            // 【修正】以前は OnClick = new() でバニラのクリック処理(フリープレイ起動)ごと
            // 消してしまっており、フラグを立てるだけで何も起きなかった。
            // TownOfHost-K と同じく、バニラの処理は残したままリスナーを「追加」する。
            var howToPlayButton = __instance.howToPlayButton;
            var freeplayButton = howToPlayButton?.transform?.parent?.Find("FreePlayButton");
            if (freeplayButton != null)
            {
                var textm = freeplayButton.transform.FindChild("Text_TMP")?.GetComponent<TextMeshPro>();
                if (textm != null)
                {
                    textm.DestroyTranslator();
                    textm.text = GetMenuText("EditCSp", "カスタムスポーンを設定");
                }
                var freeplayPassiveButton = freeplayButton.GetComponent<PassiveButton>();
                if (freeplayPassiveButton != null)
                {
                    // OnClick = new() はしない(バニラのフリープレイ開始処理を残す)
                    freeplayPassiveButton.OnClick.AddListener((Action)(() => CustomSpawnEditor.ActiveEditMode = true));
                }
            }

        }



        /// <summary>
        /// 実際に画面へ表示されている「プレイ方法」と「カスタムスポーンを設定」を複製する。
        /// SimpleButton用の別UI階層は使わず、バニラの実ボタン・当たり判定・表示順をそのまま利用する。
        /// </summary>
        private static void CreateModeSwitchButtons(MainMenuManager mainMenu)
        {
            if (mainMenu?.howToPlayButton == null)
            {
                Logger.Info("Mode switch UI: HowToPlayButton is not ready", "ModeSwitchUI");
                return;
            }

            var hostPrototype = mainMenu.howToPlayButton.gameObject;
            var parent = hostPrototype.transform.parent;
            if (parent == null)
            {
                Logger.Info("Mode switch UI: HowToPlayButton parent was not found", "ModeSwitchUI");
                return;
            }

            var clientPrototype = parent.Find("FreePlayButton")?.gameObject;
            if (clientPrototype == null)
            {
                Logger.Info("Mode switch UI: FreePlayButton was not found", "ModeSwitchUI");
                return;
            }

            // プレイ方法／カスタムスポーン設定の真上へ同じ幅・同じ当たり判定で複製する。
            // ローカル／オンラインの上端ではなく、プレイ画面の横線のすぐ下へ配置する。
            // プレイ画面の横線より上側へさらに引き上げる。
            const float verticalOffset = 4.15f;
            var hostPosition = hostPrototype.transform.localPosition + new Vector3(0f, verticalOffset, -1f);
            var clientPosition = clientPrototype.transform.localPosition + new Vector3(0f, verticalOffset, -1f);
            var hostCurrent = ModeSwitchManager.IsHostMode;

            if (HostModeSwitchButton != null && HostModeSwitchButton.transform.parent != parent)
            {
                Object.Destroy(HostModeSwitchButton);
                HostModeSwitchButton = null;
            }
            if (ClientModeSwitchButton != null && ClientModeSwitchButton.transform.parent != parent)
            {
                Object.Destroy(ClientModeSwitchButton);
                ClientModeSwitchButton = null;
            }

            if (HostModeSwitchButton == null)
            {
                HostModeSwitchButton = CreateCopiedModeSwitchButton(hostPrototype, parent, hostPosition, true, hostCurrent);
                Logger.Info("Mode switch UI: Host button copied from HowToPlayButton", "ModeSwitchUI");
            }
            if (ClientModeSwitchButton == null)
            {
                ClientModeSwitchButton = CreateCopiedModeSwitchButton(clientPrototype, parent, clientPosition, false, hostCurrent);
                Logger.Info("Mode switch UI: Client button copied from FreePlayButton", "ModeSwitchUI");
            }

            UpdateCopiedModeSwitchButton(HostModeSwitchButton, true, hostCurrent);
            UpdateCopiedModeSwitchButton(ClientModeSwitchButton, false, hostCurrent);
        }

        private static GameObject CreateCopiedModeSwitchButton(GameObject prototype, Transform parent, Vector3 position, bool isHostButton, bool hostCurrent)
        {
            var clone = Object.Instantiate(prototype, parent);
            clone.name = isHostButton ? "TOHHostModeSwitchButton" : "TOHClientModeSwitchButton";
            clone.transform.localPosition = position;
            clone.transform.localScale = prototype.transform.localScale;

            var button = clone.GetComponent<PassiveButton>();
            if (button == null)
            {
                Logger.Warn("Mode switch UI: コピー元にPassiveButtonがありません。", "ModeSwitchUI");
                Object.Destroy(clone);
                return null;
            }

            button.OnClick = new();
            button.OnClick.AddListener((Action)(() => ModeSwitchManager.RequestSwitchAndRestart(isHostButton)));

            var normal = button.inactiveSprites?.GetComponent<SpriteRenderer>();
            var hover = button.activeSprites?.GetComponent<SpriteRenderer>();
            if (normal != null)
                normal.color = isHostButton ? new Color32(168, 56, 68, byte.MaxValue) : new Color32(43, 109, 171, byte.MaxValue);
            if (hover != null)
                hover.color = isHostButton ? new Color32(230, 88, 102, byte.MaxValue) : new Color32(73, 165, 237, byte.MaxValue);

            UpdateCopiedModeSwitchButton(clone, isHostButton, hostCurrent);
            clone.SetActive(true);
            return clone;
        }

        private static void UpdateCopiedModeSwitchButton(GameObject buttonObject, bool isHostButton, bool hostCurrent)
        {
            if (buttonObject == null) return;
            var text = buttonObject.transform.FindChild("Text_TMP")?.GetComponent<TextMeshPro>() ?? buttonObject.GetComponentInChildren<TextMeshPro>(true);
            if (text == null) return;

            text.DestroyTranslator();
            var current = isHostButton == hostCurrent;
            text.text = isHostButton
                ? current ? "ホスト専用MOD\n<color=#ffe69a>現在使用中</color>" : "ホスト専用MOD"
                : current ? "参加者専用MOD\n<color=#ffe69a>現在使用中</color>" : "参加者専用MOD";
            text.fontSize = text.fontSizeMin = text.fontSizeMax = 1.55f;
        }

        // 縮小済みオブジェクトの記録(同じオブジェクトを何度も縮めないため)
        private static readonly System.Collections.Generic.HashSet<int> shrunkRedButtonIds = new();
        private const float RedButtonShrinkScale = 0.55f;
        /// <summary>
        /// メインメニュー右下の赤い✘ボタンを小さくする。
        /// 名前(exit/close/quit/cross)と、画面右下に位置することを条件に探す。
        /// 見つけた候補はログにも出す(別のオブジェクトを縮めてしまった/見つからない場合の調査用)。
        /// </summary>
        private static void ShrinkBottomRightRedButton()
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) return;
                foreach (var sr in Object.FindObjectsOfType<SpriteRenderer>())
                {
                    if (sr == null || sr.sprite == null || !sr.gameObject.activeInHierarchy) continue;
                    var key = (sr.gameObject.name + " " + sr.sprite.name).ToLowerInvariant();
                    if (!(key.Contains("exit") || key.Contains("close") || key.Contains("quit") || key.Contains("cross"))) continue;
                    var vp = cam.WorldToViewportPoint(sr.transform.position);
                    if (vp.x < 0.75f || vp.y > 0.35f) continue;
                    var button = sr.GetComponentInParent<PassiveButton>();
                    var target = button != null ? button.transform : sr.transform;
                    Logger.Info($"RedButton候補: {target.name} / sprite={sr.sprite.name} / viewport=({vp.x:F2},{vp.y:F2})", "MainMenu");
                    if (!shrunkRedButtonIds.Add(target.gameObject.GetInstanceID())) continue;
                    target.localScale *= RedButtonShrinkScale;
                }
            }
            catch (Exception e)
            {
                Logger.Warn($"ShrinkBottomRightRedButton: {e.Message}", "MainMenu");
            }
        }
        public static void SetInitialMenuUiVisible(bool visible)
        {
            // 追加ボタン群とhamoロゴだけを切り替える。バニラのAMONG USロゴ・プレイ画面UIは触らない。
            if (MenuButtonAnchor != null) MenuButtonAnchor.SetActive(visible);
            if (CredentialsPatch.TOHhmLogo != null) CredentialsPatch.TOHhmLogo.gameObject.SetActive(visible);
        }

        [HarmonyPatch(nameof(MainMenuManager.ResetScreen))]
        [HarmonyPostfix]
        private static void ResetScreenInitialMenuUiPostfix()
        {
            // 戻る操作または初期画面復帰では追加メニューを再表示する。
            SetInitialMenuUiVisible(true);
        }

        // ロビー画面から「退出」した時、バニラはResetScreen()を経由せずに
        // メインメニューへ戻ることがあり、その場合OpenCreateGame等で隠した
        // 追加メニュー(hamoロゴ・ボタン群)が復活しないまま(ゲーム作成画面に
        // 見えたまま)になってしまう不具合があった。ロビー自体が破棄される
        // タイミングでも確実に復活させる。
        [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.OnDestroy))]
        [HarmonyPostfix]
        private static void LobbyDestroyedInitialMenuUiPostfix()
        {
            SetInitialMenuUiVisible(true);
        }

        [HarmonyPatch(nameof(MainMenuManager.OpenCreateGame))]
        [HarmonyPrefix]
        private static void OpenCreateGameInitialMenuUiPrefix()
        {
            // ホストのゲーム作成へ進む時も、初期メニュー用UIを直ちに隠す。
            SetInitialMenuUiVisible(false);
        }

        [HarmonyPatch(nameof(MainMenuManager.OpenCreateGame))]
        [HarmonyPostfix]
        private static void OpenCreateGameInitialMenuUiPostfix()
        {
            // OpenCreateGame内部の画面リセット後にも再表示させない。
            SetInitialMenuUiVisible(false);
        }

        [HarmonyPatch(typeof(EnterCodeManager), nameof(EnterCodeManager.OnEnable))]
        [HarmonyPrefix]
        private static void EnterCodeInitialMenuUiPrefix()
        {
            // 参加者専用MODのコード入力画面でも初期メニューUIを残さない。
            SetInitialMenuUiVisible(false);
        }

        // 「ゲームをやめる」等で試合/ロビーから抜けた直後かどうか。
        // 退出後にバニラが呼ぶ OpenOnlineMenu を、ゲーム作成画面へ置き換えてしまうと
        // 「ゲームをやめる→ゲーム作成画面に飛ばされる」不具合になるため、
        // 退出直後の1回だけはバニラ(TownOfHost-Kと同じ)の遷移に任せる。
        private static bool leftGameRecently;
        private static float mainMenuEnteredAt = -100f;
        private const float LeftGameOnlineMenuWindow = 5f;
        /// <summary>ゲームから退出した(=メインメニューへ戻る)ことを記録する</summary>
        public static void NotifyLeftGame()
        {
            leftGameRecently = true;
        }
        [HarmonyPatch(nameof(MainMenuManager.Start))]
        [HarmonyPrefix]
        private static void MainMenuStartRecordPrefix()
        {
            mainMenuEnteredAt = UnityEngine.Time.realtimeSinceStartup;
        }
        [HarmonyPatch(nameof(MainMenuManager.OpenOnlineMenu))]
        [HarmonyPrefix]
        private static bool OpenOnlineMenuDirectModePrefix(MainMenuManager __instance)
        {
            // 退出直後(メインメニュー読み込みから一定時間内)のOpenOnlineMenuは
            // バニラの遷移に任せ、ゲーム作成画面へは飛ばさない。
            if (leftGameRecently)
            {
                leftGameRecently = false;
                if (UnityEngine.Time.realtimeSinceStartup - mainMenuEnteredAt <= LeftGameOnlineMenuWindow)
                {
                    // 退出後は「オンライン」中間画面(ゲーム作成カード)ではなく、
                    // 通常のメインメニュー(hamoロゴ画面)をそのまま表示する。
                    SetInitialMenuUiVisible(true);
                    try { __instance.ResetScreen(); } catch (Exception e) { Logger.Warn($"ResetScreen: {e.Message}", "MainMenuManagerPatch"); }
                    return false;
                }
            }
            // バニラのオンライン中間画面（ゲーム作成／コード入力カード）は使用しない。
            // どのボタン経路でも、起動モードに必要な最終画面へ直接遷移する。
            if (Main.IsNonHostClient)
                __instance.OpenEnterCodeMenu(false);
            else
                __instance.OpenCreateGame();
            return false;
        }

        [HarmonyPatch(nameof(MainMenuManager.OpenGameModeMenu))]
        [HarmonyPrefix]
        private static void OpenGameModeMenuInitialMenuUiPrefix()
        {
            // プレイを押した時点で、初期メニュー用のロゴ・YouTube・GitHub等を消す。
            SetInitialMenuUiVisible(false);
        }

        [HarmonyPatch(nameof(MainMenuManager.OpenGameModeMenu))]
        [HarmonyPostfix]
        private static void OpenGameModeMenuModeSwitchPostfix(MainMenuManager __instance)
        {
            // OpenGameModeMenu内部のResetScreen等で再表示されても、遷移後は必ず隠す。
            SetInitialMenuUiVisible(false);

            // バニラの補助ボタンが生成された後に、その実オブジェクトを複製する。
            // 要望によりモード切替ボタン(ホスト専用MOD/参加者専用MODバッジ)の表示を停止した。
            // ModeSwitchManager自体(実際の切替・再起動ロジック)は他機能からも参照されるため残す。
            //_ = new LateTask(() => CreateModeSwitchButtons(__instance), 0.10f, "CopyModeSwitchButtonsEarly", true);
            //_ = new LateTask(() => CreateModeSwitchButtons(__instance), 0.35f, "CopyModeSwitchButtonsLayout", true);
            //_ = new LateTask(() => CreateModeSwitchButtons(__instance), 0.80f, "CopyModeSwitchButtonsFinal", true);
        }

        private static string GetMenuText(string key, string fallback)
        {
            try
            {
                var translated = Translator.GetString(key);
                return string.IsNullOrWhiteSpace(translated) ? fallback : translated;
            }
            catch (Exception error)
            {
                Logger.Warn($"Menu translation is not ready for {key}: {error.Message}", "MainMenuManagerPatch");
                return fallback;
            }
        }

        private static void ShowOnlineJoinControls(MainMenuManager mainMenu)

        {

            var scaler = mainMenu.transform.Find(OnlineButtonScalerPath);

            if (scaler == null) return;



            // 「ゲームを探す」はホスト・参加者専用版のどちらでも使用しない。
            scaler.Find("Find Game Button")?.gameObject.SetActive(false);
            scaler.Find("Line")?.gameObject.SetActive(false);

            // ホスト専用MODは部屋作成だけ、参加者専用MODはコード入力だけを表示する。
            scaler.Find("Enter Code Button")?.gameObject.SetActive(Main.IsNonHostClient);
            scaler.Find("Create Lobby Button")?.gameObject.SetActive(!Main.IsNonHostClient);

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



                // 「ゲームを探す」ボタンと、その隣の区切り線を非表示にする。
                if (findGameButton != null) findGameButton.gameObject.SetActive(false);
                if (line != null) line.gameObject.SetActive(false);

                if (enterCodeButton == null) return;
                if (createLobbyButton == null) return;



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



                // ホスト専用MODは「ゲームを作成」だけ、参加者専用MODは「コードを入力」だけを中央に置く。
                const float scaleMultiplier = 1.35f;
                if (Main.IsNonHostClient)
                {
                    createLobbyButton.gameObject.SetActive(false);
                    enterCodeButton.gameObject.SetActive(true);
                    enterCodeButton.localPosition = new Vector3(centerX, centerY, codePos.z);
                    enterCodeButton.localScale = _originalEnterCodeScale.Value * scaleMultiplier;
                }
                else
                {
                    createLobbyButton.gameObject.SetActive(true);
                    enterCodeButton.gameObject.SetActive(false);
                    createLobbyButton.localPosition = new Vector3(centerX, centerY, createPos.z);
                    createLobbyButton.localScale = _originalCreateLobbyScale.Value * scaleMultiplier;
                }

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

            var parent = transform ?? MenuButtonParent;

            if (parent == null)
            {
                Logger.Error($"追加ボタン {name} の親Transformを取得できませんでした。", "MainMenuManagerPatch");
                return null;
            }

            var button = new SimpleButton(parent, name, localPosition, normalColor, hoverColor, action, label, isActive);

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

            // UIを隠すだけでなく、ショートカット等からのゲーム検索も停止する。
            return false;

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

                CredentialsPatch.TOHhmLogo?.gameObject?.SetActive(false);

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

                var text = Main.IsAndroid() ? GetMenuText("CantAndroidCreateGame", "この環境では部屋を作成できません") : cantJoin ? GetMenuText("CantPublickAndJoin", "現在は部屋の作成・参加ができません") : "";

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

