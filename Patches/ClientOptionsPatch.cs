using HarmonyLib;

using UnityEngine;



using TownOfHost.Modules.ClientOptions;

using Rewired.Utils;



namespace TownOfHost

{

    [HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.Start))]

    public static class OptionsMenuBehaviourStartPatch

    {

        private static ClientActionItem ForceJapanese;

        private static ClientActionItem JapaneseRoleName;

        private static ClientActionItem UnloadMod;

        private static ClientActionItem DumpLog;

        private static ClientActionItem OpenLogFolder;

        private static ClientActionItem ForceEnd;

        private static ClientActionItem UseWebHook;

        private static ClientActionItem Yomiage;

        private static ClientActionItem CustomName;

        private static ClientActionItem CustomSprite;

        private static ClientActionItem HideSomeFriendCodes;

        private static ToggleButtonBehaviour soundSettingsButton;

        public static ToggleButtonBehaviour StreamHopeButton;

        private static ClientActionItem ViewPingDetails;

        private static ClientActionItem DebugChatopen;

        private static ClientActionItem DebugSendAmout;

        private static ClientActionItem DebugTours;

        private static ClientActionItem ShowDistance;

        private static ClientActionItem FpsLimitRemoval;

        private static ClientActionItem AutoSaveScreenShot;

        private static ClientActionItem PreloadMapAssets;

        private static ClientActionItem ShowPresetInWebhook;
        private static ClientActionItem WebhookUrlInput;
        private static ClientActionItem WebhookWinTag;
        private static ClientActionItem WebhookTagInput;
        private static ClientActionItem RecorderSettings;
        private static ClientActionItem IntroShotSettings;

                private static ClientActionItem AutoRehost;

        private static ClientActionItem EnableDiscordRichPresence;



        private static ClientActionItem ShowRoomTimer;
        private static ClientActionItem CustomStartLoading;
        private static ClientActionItem LoadingMiniGame;
        private static ToggleButtonBehaviour listManagerButton;
        private static ClientActionItem ShowPlayerCount;

        private static ClientActionItem ShowRoomCode;
        private static ClientActionItem ExpandRoleHeaders;

        public static OptionsMenuBehaviour Instance;



        public static void Postfix(OptionsMenuBehaviour __instance)

        {

            Instance = __instance;

            // 旧メニューの残骸を参照している場合は、UI参照を全て破棄して作り直す
            // (設定を閉じて開き直すと2回目以降ボタンが反応しない不具合の対策)
            if (ClientActionItem.IsStale(__instance))
            {
                ResetItemReferences();
                ClientActionItem.ResetAll();
            }



            if (__instance.DisableMouseMovement == null)

            {

                return;

            }



            // 1ページ目: 日本語に強制/日本語の役職名

            if (ForceJapanese == null || ForceJapanese.ToggleButton == null)

            {

                ForceJapanese = ClientOptionItem.Create("ForceJapanese", Main.ForceJapanese, __instance, page: 0);

            }

            if (JapaneseRoleName == null || JapaneseRoleName.ToggleButton == null)

            {

                JapaneseRoleName = ClientOptionItem.Create("JapaneseRoleName", Main.JapaneseRoleName, __instance, page: 0);

            }

            // ログを出力/ログフォルダを開く

            if (DumpLog == null || DumpLog.ToggleButton == null)

            {


                    DumpLog = ClientActionItem.Create("DumpLog", UtilsOutputLog.DumpLog, __instance, page: 0);

            }

            if (OpenLogFolder == null || OpenLogFolder.ToggleButton == null)

            {


                    OpenLogFolder = ClientActionItem.Create("OpenLogFolder", UtilsOutputLog.OpenLogFolder, __instance, page: 0);

            }

            // Modを無効化/廃村

            if (UnloadMod == null || UnloadMod.ToggleButton == null)

            {


                    UnloadMod = ClientActionItem.Create("UnloadMod", ModUnloaderScreen.Show, __instance, page: 0);

            }

            if ((ForceEnd == null || ForceEnd.ToggleButton == null) && AmongUsClient.Instance.AmHost && !CustomSpawnEditor.ActiveEditMode)

            {

                ForceEnd = ClientActionItem.Create("ForceEnd", ForceEndProcess, __instance, page: 0);

            }

            // ウェブフック/Webhook試合結果にプリセットファイルを添付する

            if (UseWebHook == null || UseWebHook.ToggleButton == null)

            {


                    UseWebHook = ClientOptionItem.Create("UseWebHook", Main.UseWebHook, __instance, page: 0);

            }

            if (ShowPresetInWebhook == null || ShowPresetInWebhook.ToggleButton == null)

            {


                    ShowPresetInWebhook = ClientOptionItem.Create("ShowPresetInWebhook", Main.ShowPresetInWebhook, __instance, showTooltip: true, page: 1);

            }

            // Webhook の URL と、勝った陣営のタグを、ゲームの中で設定する
            if (WebhookUrlInput == null || WebhookUrlInput.ToggleButton == null)
                WebhookUrlInput = ClientActionItem.Create("WebhookUrlInput", () => Modules.WebhookSettingDialog.Open(Modules.WebhookSettingDialog.Mode.Url), __instance, page: 1);
            if (WebhookWinTag == null || WebhookWinTag.ToggleButton == null)
                WebhookWinTag = ClientOptionItem.Create("WebhookWinTag", Main.WebhookWinTag, __instance, page: 1);
            if (WebhookTagInput == null || WebhookTagInput.ToggleButton == null)
                WebhookTagInput = ClientActionItem.Create("WebhookTagInput", () => Modules.WebhookSettingDialog.Open(Modules.WebhookSettingDialog.Mode.Tags), __instance, page: 1);
            // 録画(ホストでなくても使える)
            if (RecorderSettings == null || RecorderSettings.ToggleButton == null)
                RecorderSettings = ClientActionItem.Create("RecorderSettings", RecorderScreen.Show, __instance, page: 1);
            // イントロ撮影(デバッグ版だけ)
            if (DebugModeManager.AmDebugger && (IntroShotSettings == null || IntroShotSettings.ToggleButton == null))
                IntroShotSettings = ClientActionItem.Create("IntroShotSettings", IntroShotScreen.Show, __instance, page: 1);
            // 読み上げ機能/ボタンの見た目を変更する

            if (Yomiage == null || Yomiage.ToggleButton == null)

            {


                    Yomiage = ClientOptionItem.Create("UseYomiage", Main.UseYomiage, __instance, page: 0);

            }

            if (CustomSprite == null || CustomSprite.ToggleButton == null)

            {

                CustomSprite = ClientOptionItem.Create("CustomSprite", Main.CustomSprite, __instance, () =>

                {

                    if (GameStates.InGame)

                        CustomButtonHud.BottonHud();

                }, page: 0);

            }

            // 一部画面のフレンドコードを隠す/試合結果のスクショを自動保存する

            if (HideSomeFriendCodes == null || HideSomeFriendCodes.ToggleButton == null)

            {

                HideSomeFriendCodes = ClientOptionItem.Create("HideSomeFriendCodes", Main.HideSomeFriendCodes, __instance, showTooltip: true, page: 0);

            }

            if (AutoSaveScreenShot == null || AutoSaveScreenShot.ToggleButton == null)

            {


                    AutoSaveScreenShot = ClientOptionItem.Create("AutoSaveScreenShot", Main.AutoSaveScreenShot, __instance, page: 0);

            }

            // 事前にマップアセットを読み込む/部屋の自動立て直し

            if ((PreloadMapAssets == null || PreloadMapAssets.ToggleButton == null))

            {

                PreloadMapAssets = ClientOptionItem.Create("PreloadMapAssets", Main.PreloadMapAssets, __instance, showTooltip: true, page: 0);

            }

            // 部屋の自動立て直しは Windows 専用の処理を使っていないので Android でも使える
            if (AutoRehost == null || AutoRehost.ToggleButton == null)

            {

                AutoRehost = ClientOptionItem.Create("AutoRehost", Main.AutoRehost, __instance, showTooltip: true, page: 0);

            }

            

            // Discordのプレイ中ステータスに"TownOfHost-hamo"と表示するか

            if ((EnableDiscordRichPresence == null || EnableDiscordRichPresence.ToggleButton == null))

            {

                EnableDiscordRichPresence = ClientOptionItem.Create("EnableDiscordRichPresence", Main.EnableDiscordRichPresence, __instance, showTooltip: true, page: 1);

            }

            // 部屋タイマー表示/人数表示 (1ページ目最後の枠)

            if (ShowRoomTimer == null || ShowRoomTimer.ToggleButton == null)

            {

                ShowRoomTimer = ClientOptionItem.Create("ShowRoomTimer", Main.ShowRoomTimer, __instance, page: 0);

            }

            if (ShowPlayerCount == null || ShowPlayerCount.ToggleButton == null)

            {

                ShowPlayerCount = ClientOptionItem.Create("ShowPlayerCount", Main.ShowPlayerCount, __instance, page: 0);

            }

            // 2ページ目: 部屋コード表示

            if (ShowRoomCode == null || ShowRoomCode.ToggleButton == null)

            {

                ShowRoomCode = ClientOptionItem.Create("ShowRoomCode", Main.ShowRoomCode, __instance, page: 1);
            }
            // 2ページ目: 役職の見出し(あ行など)を開いておくか
            if (ExpandRoleHeaders == null || ExpandRoleHeaders.ToggleButton == null)
            {
                ExpandRoleHeaders = ClientOptionItem.Create("ExpandRoleHeaders", Main.ExpandRoleHeaders, __instance,
                    () => RoleGojuon.SetAllCollapsed(!Main.ExpandRoleHeaders.Value), page: 1);
            }
            // 2ページ目: ゲーム開始後の読み込みを TOH-hamo のロード画面にするか(OFFでふつうのバー)
            if (CustomStartLoading == null || CustomStartLoading.ToggleButton == null)
            {
                CustomStartLoading = ClientOptionItem.Create("CustomStartLoading", Main.CustomStartLoading, __instance, page: 1);
            }
            if (LoadingMiniGame == null || LoadingMiniGame.ToggleButton == null)
            {
                LoadingMiniGame = ClientOptionItem.Create("LoadingMiniGame", Main.LoadingMiniGame, __instance, page: 1);
            }
            // ロビーでは表示しない
            if (ExpandRoleHeaders?.ToggleButton != null) ExpandRoleHeaders.ToggleButton.gameObject.SetActive(!GameStates.IsLobby);
            // YouTubeのコメント枠の設定は、メインメニューの「Stream」と、ゲーム設定のOTHER2に移した

            //使う人いそうなので常時で...
            if (ViewPingDetails == null || ViewPingDetails.ToggleButton == null)
            {
                ViewPingDetails = ClientOptionItem.Create("ViewPingDetails", Main.ViewPingDetails, __instance);
            }


#if DEBUG

            if (DebugChatopen == null || DebugChatopen.ToggleButton == null)

            {

                DebugChatopen = ClientOptionItem.Create("DebugChatopen", Main.DebugChatopen, __instance);

            }

            if (DebugSendAmout == null || DebugSendAmout.ToggleButton == null)

            {

                DebugSendAmout = ClientOptionItem.Create("DebugSendAmout", Main.DebugSendAmout, __instance);

            }

            if (ShowDistance == null || ShowDistance.ToggleButton == null)

            {

                ShowDistance = ClientOptionItem.Create("ShowDistance", Main.ShowDistance, __instance);

            }

            if (DebugTours == null || DebugTours.ToggleButton == null)

            {

                DebugTours = ClientOptionItem.Create("DebugTours", Main.DebugTours, __instance);

            }

            if (FpsLimitRemoval == null || FpsLimitRemoval.ToggleButton == null)

            {

                Application.targetFrameRate = Main.FpsLimitRemoval.Value ? -1 : 60;//起動時

                FpsLimitRemoval = ClientOptionItem.Create("FpsLimitRemoval", Main.FpsLimitRemoval, __instance, () =>

                {

                    Application.targetFrameRate = Main.FpsLimitRemoval.Value ? -1 : 60;//クリック時

                });

            }

#endif

            if ((CustomName == null || CustomName.ToggleButton == null) && Event.IsEventDay)

            {

                CustomName = ClientOptionItem.Create("CustomName", Main.CustomName, __instance);

            }

            if (ModUnloaderScreen.Popup == null)

            {

                ModUnloaderScreen.Init(__instance);

            }

            if (SoundSettingsScreen.Popup == null)

            {

                SoundSettingsScreen.Init(__instance);

            }

            if (StreamerHopeMenu.Popup == null)

            {

                StreamerHopeMenu.Init(__instance);

            }

            // BANリスト・ホワイトリスト・モデレーターを画面で追加・解除する(サウンド設定の右)
            if (listManagerButton.IsDestroyedOrNull())
            {
                listManagerButton = Object.Instantiate(__instance.DisableMouseMovement, __instance.transform.FindChild("GeneralTab/MiscGroup"));
                listManagerButton.transform.localPosition = new(0.85f, 1.27f, __instance.DisableMouseMovement.transform.localPosition.z);
                listManagerButton.transform.localScale = new(0.55f, 0.7f);
                listManagerButton.name = "ListManagerButton";
                listManagerButton.Text.text = Translator.GetString("ListManager");
                listManagerButton.Background.color = Palette.DisabledGrey;
                var listPassiveButton = listManagerButton.GetComponent<PassiveButton>();
                listPassiveButton.OnClick = new();
                listPassiveButton.OnClick.AddListener((System.Action)ListManagerScreen.Show);
            }
            if (soundSettingsButton.IsDestroyedOrNull())

            {

                soundSettingsButton = Object.Instantiate(__instance.DisableMouseMovement, __instance.transform.FindChild("GeneralTab/MiscGroup"));

                // 右に「BAN・ホワイト・モデレーターの管理」を並べるので、少し左に寄せて小さくする
                soundSettingsButton.transform.localPosition = new(-0.85f, 1.27f, __instance.DisableMouseMovement.transform.localPosition.z);//左側:-1.3127f,1.5588f

                soundSettingsButton.transform.localScale = new(0.55f, 0.7f);

                soundSettingsButton.name = "SoundStgButton";

                soundSettingsButton.Text.text = Translator.GetString("SoundOptions");

                soundSettingsButton.Background.color = Palette.DisabledGrey;

                var soundSettingsPassiveButton = soundSettingsButton.GetComponent<PassiveButton>();

                soundSettingsPassiveButton.OnClick = new();

                soundSettingsPassiveButton.OnClick.AddListener((System.Action)(() =>

                {

                    SoundSettingsScreen.Show();

                }));

            }

            if (StreamHopeButton.IsNullOrDestroyed())

            {

                StreamHopeButton = Object.Instantiate(__instance.DisableMouseMovement, __instance.transform.FindChild("GeneralTab/MiscGroup"));

                StreamHopeButton.transform.localPosition = new(0f, 0.9264f, __instance.DisableMouseMovement.transform.localPosition.z);//左側:-1.3127f,1.5588f

                StreamHopeButton.transform.localScale = new(0.7f, 0.7f);

                StreamHopeButton.name = "StreamList";

                StreamHopeButton.Text.text = Translator.GetString("StreamList");

                StreamHopeButton.Background.color = Palette.ImpostorRed;

                StreamHopeButton.gameObject.SetActive(StreamerInfo.StreamURL is not "");

                var soundSettingsPassiveButton = StreamHopeButton.GetComponent<PassiveButton>();

                soundSettingsPassiveButton.OnClick = new();

                soundSettingsPassiveButton.OnClick.AddListener((System.Action)(() =>

                {

                    StreamerHopeMenu.Show(__instance);

                }));

            }



            if ((!AmongUsClient.Instance.AmHost || CustomSpawnEditor.ActiveEditMode) && ForceEnd != null)

                ForceEnd = null;

        }

        /// <summary>保持している各アイテム/ポップアップ/ボタンの参照を全て破棄する</summary>
        private static void ResetItemReferences()
        {
            ForceJapanese = JapaneseRoleName = UnloadMod = DumpLog = OpenLogFolder = ForceEnd = ViewPingDetails = null;
            UseWebHook = Yomiage = CustomName = CustomSprite = HideSomeFriendCodes = null;
            FpsLimitRemoval = AutoSaveScreenShot = PreloadMapAssets = ShowPresetInWebhook = null;
            WebhookUrlInput = WebhookWinTag = WebhookTagInput = RecorderSettings = IntroShotSettings = null;
            RecorderScreen.ForgetPopup();
            IntroShotScreen.ForgetPopup();
            CustomStartLoading = null;
            LoadingMiniGame = null;
            ListManagerScreen.ForgetPopup();
            if (listManagerButton != null && listManagerButton.gameObject != null)
                Object.Destroy(listManagerButton.gameObject);
            listManagerButton = null;
            AutoRehost = EnableDiscordRichPresence = ShowRoomTimer = ShowPlayerCount = ShowRoomCode = ExpandRoleHeaders = null;
            ModUnloaderScreen.ForgetPopup();
            SoundSettingsScreen.ForgetPopup();
            StreamerHopeMenu.ForgetPopup();
            if (soundSettingsButton != null && soundSettingsButton.gameObject != null)
                Object.Destroy(soundSettingsButton.gameObject);
            soundSettingsButton = null;
            if (StreamHopeButton != null && StreamHopeButton.gameObject != null)
                Object.Destroy(StreamHopeButton.gameObject);
            StreamHopeButton = null;
        }

        /// <summary>Modオプション用UIを全て作り直す(旧メニューの残骸を参照していた場合の復旧用)</summary>
        public static void RebuildClientOptions(OptionsMenuBehaviour menu)
        {
            if (menu == null) return;
            ResetItemReferences();
            ClientActionItem.ResetAll();
            Postfix(menu);
        }

        private static void ForceEndProcess()

        {

            if (CustomSpawnEditor.ActiveEditMode) return;

            //左シフトが押されているなら強制廃村

            if (Input.GetKey(KeyCode.LeftShift) || ((Main.ForcedGameEndColl != 0) && !GameStates.IsLobby))

            {

                GameManager.Instance.enabled = false;

                CustomWinnerHolder.WinnerTeam = CustomWinner.Draw;

                GameManager.Instance.RpcEndGame(GameOverReason.ImpostorDisconnect, false);

                return;

            }

            if (!GameStates.IsLobby) Main.ForcedGameEndColl++;

            Logger.Info($"廃村コール{Main.ForcedGameEndColl}回目", "fe");

            if (!GameStates.IsInGame) return;

            CustomWinnerHolder.ResetAndSetWinner(CustomWinner.Draw);

            GameManager.Instance.LogicFlow.CheckEndCriteria();

        }

    }



    [HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.Close))]

    public static class OptionsMenuBehaviourClosePatch

    {

        public static void Postfix()

        {

            if (ClientActionItem.CustomBackground != null)

            {

                ClientActionItem.CustomBackground.gameObject.SetActive(false);

                ClientActionItem.ShowPage(0);

            }

            ModUnloaderScreen.Hide();

            SoundSettingsScreen.Hide();
            ListManagerScreen.Hide();
            StreamerHopeMenu.Hide();

            TownOfHost.Modules.MatchmakingWordManager.HideEditor();

        }

    }

    [HarmonyPatch(typeof(OptionsMenuBehaviour), nameof(OptionsMenuBehaviour.Open))]

    public static class OptionsMenuBehaviourOpenPatch

    {
        public static void Postfix(OptionsMenuBehaviour __instance)
        {
            // 開いた時点で旧メニューの残骸を指していたら作り直す
            if (__instance != null && ClientActionItem.IsStale(__instance))
            {
                OptionsMenuBehaviourStartPatch.RebuildClientOptions(__instance);
            }
        }

        public static void Prefix()

        {

            if (OptionsMenuBehaviourStartPatch.StreamHopeButton.IsNullOrDestroyed() is false)

            {

                OptionsMenuBehaviourStartPatch.StreamHopeButton.gameObject.SetActive(StreamerInfo.StreamURL is not "");

            }

        }

    }

}

