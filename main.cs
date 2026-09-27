using System;

using System.Collections.Generic;

using System.IO;

using System.Linq;

using System.Reflection;

using AmongUs.GameOptions;

using BepInEx;

using BepInEx.Configuration;

using BepInEx.Unity.IL2CPP;

using HarmonyLib;

using Il2CppInterop.Runtime.Injection;

using TownOfHost.Attributes;

using TownOfHost.Modules;

using TownOfHost.Roles.Core;

using UnityEngine;

using static Unity.Services.LevelPlay.LevelPlayBannerPosition;



[assembly: AssemblyFileVersionAttribute(TownOfHost.Main.PluginVersion)]

[assembly: AssemblyInformationalVersionAttribute(TownOfHost.Main.PluginVersion)]

namespace TownOfHost

{

    [BepInPlugin(PluginGuid, BepInExPluginName, BepInExPluginVersion)]

    [BepInIncompatibility("jp.ykundesu.supernewrolesnext")]

    [BepInIncompatibility("jp.ykundesu.supernewroles")]

    [BepInIncompatibility("me.yukieiji.extremeroles")]

    [BepInIncompatibility("jp.dreamingpig.amongus.nebula")]

    [BepInProcess("Among Us.exe")]

    public class Main : BasePlugin

    {

        // == プログラム設定 / Program Config ==

        // modの名前 / Mod Name (Default: Town Of Host)

        public static readonly string ModName = "Town Of Host-hamo";

        // modの色 / Mod Color (Default: #00bfff)

        public static readonly string ModColor = "#fb85ff";

        // 公開ルームを許可する / Allow Public Room (Default: true)

        public static readonly bool AllowPublicRoom = true;

        // フォークID / ForkId (Default: OriginalTOH)

        public static readonly string ForkId = "TOH-hm";

        // Discordボタンを表示するか / Show Discord Button (Default: true)

        public static readonly bool ShowDiscordButton = true;

        // Discordサーバーの招待リンク / Discord Server Invite URL (Default: https://discord.gg/PQ5CrVHC25)

        public static readonly string DiscordInviteUrl = "https://discord.gg/vjMQ75nU8d";

        // マッチメイキングBotの招待(OAuth2招待)URL

        public static readonly string MatchmakingBotInviteUrl = "https://discord.com/oauth2/authorize?client_id=1528051378180849765";

        // 役職確認Botの招待(OAuth2招待)URL

        public static readonly string RoleCheckBotInviteUrl = "https://discord.com/oauth2/authorize?client_id=1522000252633354350";

        public static string PresetShareApiUrl = "https://pikeo-ai.pikeo-ai.workers.dev";
        public static string PresetShareApiSecret = "(AmongUsTOHPkoPukupuku142satokoVERYCUTE)";

        // ==========

        public const string OriginalForkId = "OriginalTOH"; // Don't Change The Value. / この値を変更しないでください。


        // デバッグキーの認証インスタンス

        public static HashAuth DebugKeyAuth { get; private set; }

        public static HashAuth ExplosionKeyAuth { get; private set; }

        // デバッグキーのハッシュ値

        public const string DebugKeyHash = "8e5f06e453e7d11f78ad96b2ca28ff472e085bdb053189612a0a2e0be7973841";

        // 部屋破壊キーのハッシュ値

        public const string ExplosionKeyHash = "e7d88aaf7ea075752792089196d9441c838e6ff47432a719fad6e17cd50a441e";

        // デバッグキーのソルト

        public const string DebugKeySalt = "59687b";

        // デバッグキーのコンフィグ入力

        public static ConfigEntry<string> DebugKeyInput { get; private set; }

        public static ConfigEntry<string> ExplosionKeyInput { get; private set; }



#if NON_HOST_CLIENT
        // Non-host visual client. It shares the hamo identity but has its own BepInEx GUID.
        public const bool IsNonHostClient = true;
        public const string PluginGuid = "com.rar006.TownOfHost-hamo.client";
        public const string BepInExPluginName = "Town Of Host-hamo Client";
#else
        // Host build. Keep the hamo plugin GUID unchanged.
        public const bool IsNonHostClient = false;
        public const string PluginGuid = "com.rar006.TownOfHost-hamo";
        public const string BepInExPluginName = "Town Of Host-hamo";
#endif

        public const string BepInExPluginVersion = "4.00.00.30";

        public const string PluginVersion = "4.00.00.30";//ほんとはx.y.z表記にしたかったけどx.y.z.km.ks表記だと警告だされる

        public const string PluginShowVersion = "4.31.00α";

        public const string ModVersion = ".00.30";//リリースver用バージョン変更dc9b79



        /// 配布するデバッグ版なのであればtrue。リリース時にはfalseにすること。

<<<<<<< HEAD
        public static bool DebugVersion = false;
        public static bool WarnDuplicatetranslate = false;
=======
        public static bool DebugVersion = true;
>>>>>>> 0612396829af94732bb4b322484a31855f60fd1c



        // サポートされている最低のAmongUsバージョン(Readmeも変える)

        public static readonly string LowestSupportedVersion = "2026.3.31";

        // このバージョンのみで公開ルームを無効にする場合

        public static readonly bool IsPublicAvailableOnThisVersion = true;



        public static readonly string MatchmakingRelayUrl = "https://rising-earn-excel-closed.trycloudflare.com/matchmaking";

        public static readonly string MatchmakingRelaySecret = "z1xjsyqynboz";

        public Harmony Harmony { get; } = new Harmony(PluginGuid);

        public static Version version = Version.Parse(PluginVersion);

        public static BepInEx.Logging.ManualLogSource Logger;

        public static bool hasArgumentException = false;

        public static string ExceptionMessage;

        public static bool ExceptionMessageIsShown = false;

        public static string credentialsText;

        public static NormalGameOptionsV11 NormalOptions => GameOptionsManager.Instance.currentNormalGameOptions;

        public static HideNSeekGameOptionsV11 HideNSeekSOptions => GameOptionsManager.Instance.currentHideNSeekGameOptions;

        //Client Options

        public static ConfigEntry<string> HideName { get; private set; }

        public static ConfigEntry<string> HideColor { get; private set; }

        public static ConfigEntry<bool> ForceJapanese { get; private set; }

        public static ConfigEntry<bool> JapaneseRoleName { get; private set; }

        public static ConfigEntry<float> MessageWait { get; private set; }

        public static ConfigEntry<bool> ShowResults { get; private set; }

        public static ConfigEntry<bool> Hiderecommendedsettings { get; private set; }

        public static ConfigEntry<bool> UseWebHook { get; private set; }

        public static ConfigEntry<bool> UseYomiage { get; private set; }

        public static ConfigEntry<bool> CustomName { get; private set; }

        public static ConfigEntry<bool> ShowGameSettingsTMP { get; private set; }

        public static ConfigEntry<bool> CustomSprite { get; private set; }

        public static ConfigEntry<bool> HideSomeFriendCodes { get; private set; }

        public static ConfigEntry<bool> AutoSaveScreenShot { get; private set; }

        public static ConfigEntry<bool> ShowPresetInWebhook { get; private set; }

        public static ConfigEntry<bool> PreloadMapAssets { get; private set; }

        public static ConfigEntry<bool> AutoRehost { get; private set; }

        // クラッシュしたら自動でAmong Usを再起動するか(外部ウォッチドッグと連携する必須ログを出力するかどうか)

        public static ConfigEntry<bool> AutoRestartOnCrash { get; private set; }

        // Discordのプレイ中ステータスに"TownOfHost-hamo"と表示するか

        public static ConfigEntry<bool> EnableDiscordRichPresence { get; private set; }

        public static ConfigEntry<bool> ShowRoomTimer { get; private set; }

        public static ConfigEntry<bool> ShowPlayerCount { get; private set; }

        public static ConfigEntry<bool> ShowRoomCode { get; private set; }

        public static ConfigEntry<float> MapTheme { get; private set; }

        public static ConfigEntry<bool> ViewPingDetails { get; private set; }

        public static ConfigEntry<bool> DebugChatopen { get; private set; }

        public static ConfigEntry<bool> DebugSendAmout { get; private set; }

        public static ConfigEntry<bool> DebugTours { get; private set; }

        public static ConfigEntry<bool> ShowDistance { get; private set; }

        public static ConfigEntry<bool> FpsLimitRemoval { get; private set; }

        public static Dictionary<byte, PlayerVersion> playerVersion = new();

        //Preset Name Options

        public static ConfigEntry<string> Preset1 { get; private set; }

        public static ConfigEntry<string> Preset2 { get; private set; }

        public static ConfigEntry<string> Preset3 { get; private set; }

        public static ConfigEntry<string> Preset4 { get; private set; }

        public static ConfigEntry<string> Preset5 { get; private set; }

        public static ConfigEntry<string> Preset6 { get; private set; }

        public static ConfigEntry<string> Preset7 { get; private set; }

        public static ConfigEntry<string> Preset8 { get; private set; }

        public static ConfigEntry<string> Preset9 { get; private set; }

        public static ConfigEntry<string> Preset10 { get; private set; }

        public static ConfigEntry<string> Preset11 { get; private set; }

        public static ConfigEntry<string> Preset12 { get; private set; }

        public static ConfigEntry<string> Preset13 { get; private set; }

        public static ConfigEntry<string> Preset14 { get; private set; }

        public static ConfigEntry<string> Preset15 { get; private set; }

        public static ConfigEntry<string> Preset16 { get; private set; }



        public static ConfigEntry<string> SKey { get; private set; }

        public static ConfigEntry<string> JoinWord { get; private set; }

        public static ConfigEntry<string> RemoveWord { get; private set; }

        //Other Configs

        public static ConfigEntry<string> BetaBuildURL { get; private set; }

        public static ConfigEntry<float> LastKillCooldown { get; private set; }

        public static ConfigEntry<float> LastShapeshifterCooldown { get; private set; }

        public static ConfigEntry<bool> LastKickModClient { get; private set; }

        public static bool UseingJapanese => ForceJapanese.Value || TranslationController.Instance.currentLanguage.languageID is SupportedLangs.Japanese;

        public static OptionBackupData RealOptionsData;

        public static Dictionary<byte, string> AllPlayerNames = new();

        public static Dictionary<(byte, byte), string> LastNotifyNames;

        public static Dictionary<byte, Color32> PlayerColors = new();

        public static Dictionary<byte, CustomDeathReason> AfterMeetingDeathPlayers = new();

        // 直近の会議で各プレイヤーが投票した相手を記録する。Key: 投票者PlayerId, Value: 投票先PlayerId(NoVote/Skipは255)

        // 会議終了時に更新され、次の会議が終わるまで保持される(Nautilus等が参照する)。

        public static Dictionary<byte, byte> LastMeetingVotedFor = new();

        public static List<byte> meetingdeadlist = new();

        public static Dictionary<CustomRoles, string> roleColors;

        public static Dictionary<byte, List<uint>> AllPlayerTask = new();

        public static List<byte> winnerList;

        public static List<int> clientIdList;

        public static List<byte> DisableTaskPlayerList;

        public static List<(string, byte, string)> MessagesToSend;

        public static int MegCount;

        public static Dictionary<byte, float> AllPlayerKillCooldown = new();

        public static bool HnSFlag = false;

        public static bool showkillbutton = false;

        public static bool AssignSameRoles = false;

        public static string Alltask;

        public static byte LastSab;

        public static SystemTypes SabotageType;

        public static bool IsActiveSabotage;

        public static float SabotageActivetimer;

        public static (float DiscussionTime, float VotingTime) MeetingTime;

        public static int GameCount = 0;

        public static bool SetRoleOverride = true;

        /// <summary>ラグを許容した幅。アジア、カスタム、ローカルは200ms(0.2s),他は400ms(0.4s)</summary>

        public static float LagTime = 0.2f;

        public static int ForcedGameEndColl;

        public static bool ShowRoleIntro;

        public static bool DontGameSet;

        public static bool CanUseAbility;

        public static CustomRoles HostRole = CustomRoles.NotAssigned;



        /// <summary>

        /// 基本的に速度の代入は禁止.スピードの増減で対応してください.

        /// </summary>

        public static Dictionary<byte, float> AllPlayerSpeed = new();

        public const float MinSpeed = 0.0001f;

        public static Dictionary<byte, bool> CheckShapeshift = new();

        public static Dictionary<byte, byte> ShapeshiftTarget = new();

        public static Dictionary<byte, CustomDeathReason> HostKill = new();

        public static bool VisibleTasksCount;

        public static string nickName = "";

        public static string lobbyname = "";

        public static float DefaultCrewmateVision;

        public static float DefaultImpostorVision;

        public static bool DebugAntiblackout = true;



        public const float RoleTextSize = 2f;

        public static Main Instance;

        public static string BaseDirectory

            => Path.GetFullPath(Path.Combine(

                string.IsNullOrEmpty(BepInEx.Paths.BepInExRootPath) ? Application.persistentDataPath : BepInEx.Paths.BepInExRootPath,

                "../TOHhm_DATA"));

        public override void Load()

        {

            GameCount = 0;

            Instance = this;



            //Client Options

            HideName = Config.Bind("Client Options", "Hide Game Code Name", "Town Of Host-hamo");

            HideColor = Config.Bind("Client Options", "Hide Game Code Color", $"{ModColor}");

            ForceJapanese = Config.Bind("Client Options", "Force Japanese", false);

            JapaneseRoleName = Config.Bind("Client Options", "Japanese Role Name", true);

            ShowResults = Config.Bind("Result", "Show Results", true);

            Hiderecommendedsettings = Config.Bind("Client Options", "Hide recommended settings", false);

            UseWebHook = Config.Bind("Client Options", "UseWebHook", false);

            UseYomiage = Config.Bind("Client Options", "UseYomiage", false);

            CustomName = Config.Bind("Client Options", "CustomName", true);

            ShowGameSettingsTMP = Config.Bind("Client Options", "Show GameSettings", true);

            CustomSprite = Config.Bind("Client Options", "CustomSprite", true);

            HideSomeFriendCodes = Config.Bind("Client Options", "Hide Some Friend Codes", false);

            AutoSaveScreenShot = Config.Bind("Client Options", "Auto Save Autro ScreenShot", false);

            ShowPresetInWebhook = Config.Bind("Client Options", "Show Preset In Webhook", true);

            PreloadMapAssets = Config.Bind("Client Options", "Preload Map Assets", false);

            AutoRehost = Config.Bind("Client Options", "Auto Rehost", false);

            AutoRestartOnCrash = Config.Bind("Client Options", "Auto Restart On Crash", false);

#if NON_HOST_CLIENT
            // 表示専用版では外部監視の自動再ホストを絶対に動かさない。
            // イントロ、役職ガイド、カスタムボタン、受信RPCによる表示同期はそのまま有効。
            AutoRestartOnCrash.Value = false;
#endif

            EnableDiscordRichPresence = Config.Bind("Client Options", "Enable Discord Rich Presence", false);



            ShowRoomTimer = Config.Bind("Client Options", "Show Room Timer", false);

            ShowPlayerCount = Config.Bind("Client Options", "Show Player Count", false);

            ShowRoomCode = Config.Bind("Client Options", "Show Room Code", false);

            MapTheme = Config.Bind("Client Options", "MapTheme", AmongUs.Data.Settings.AudioSettingsData.DEFAULT_MUSIC_VOLUME);

            ViewPingDetails = Config.Bind("Client Options", "View Ping Details", false);

            DebugChatopen = Config.Bind("Client Options", "Debug Chat open", false);

            DebugSendAmout = Config.Bind("Client Options", "Debug Send Amout", false);

            DebugTours = Config.Bind("Client Options", "DebugTours", false);

            ShowDistance = Config.Bind("Client Options", "Show Distance", false);

            FpsLimitRemoval = Config.Bind("Client Options", "Fps Limit Removal", false);

            JoinWord = Config.Bind("StreamMenu", "JoinWord", "");

            RemoveWord = Config.Bind("StreamMenu", "RemoveWord", "");

            DebugKeyInput = Config.Bind("Authentication", "Debug Key", "");

            ExplosionKeyInput = Config.Bind("Authentication", "Explosion Key", "");



            Logger = BepInEx.Logging.Logger.CreateLogSource(BepInExPluginName);

            TownOfHost.Logger.Enable();

            TownOfHost.Logger.Disable("NotifyRoles");

            TownOfHost.Logger.Disable("SendRPC");

            TownOfHost.Logger.Disable("ReceiveRPC");

            TownOfHost.Logger.Disable("SwitchSystem");

            TownOfHost.Logger.Disable("CustomRpcSender");

            TownOfHost.Logger.Disable("CoroutinPatcher");

            //TownOfHost.Logger.isDetail = true;



            try

            {

                System.Console.OutputEncoding = System.Text.Encoding.UTF8;

            }

            catch

            {

                TownOfHost.Logger.Error("System.Console.OutputEncodingの変更に失敗", "Main");

            }



            // 隱崎ｨｼ髢｢騾｣-蛻晄悄蛹・

            DebugKeyAuth = new HashAuth(DebugKeyHash, DebugKeySalt);

            ExplosionKeyAuth = new HashAuth(ExplosionKeyHash, DebugKeySalt);



            // 隱崎ｨｼ髢｢騾｣-隱崎ｨｼ

            DebugModeManager.Auth(DebugKeyAuth, DebugKeyInput.Value);



            winnerList = new();

            VisibleTasksCount = false;

            MessagesToSend = new List<(string, byte, string)>();



            Preset1 = Config.Bind("Preset Name Options", "Preset1", "Preset_1");

            Preset2 = Config.Bind("Preset Name Options", "Preset2", "Preset_2");

            Preset3 = Config.Bind("Preset Name Options", "Preset3", "Preset_3");

            Preset4 = Config.Bind("Preset Name Options", "Preset4", "Preset_4");

            Preset5 = Config.Bind("Preset Name Options", "Preset5", "Preset_5");

            Preset6 = Config.Bind("Preset Name Options", "Preset6", "Preset_6");

            Preset7 = Config.Bind("Preset Name Options", "Preset7", "Preset_7");

            Preset8 = Config.Bind("Preset Name Options", "Preset8", "Preset_8");

            Preset9 = Config.Bind("Preset Name Options", "Preset9", "Preset_9");

            Preset10 = Config.Bind("Preset Name Options", "Preset10", "Preset_10");

            Preset11 = Config.Bind("Preset Name Options", "Preset11", "Preset_11");

            Preset12 = Config.Bind("Preset Name Options", "Preset12", "Preset_12");

            Preset13 = Config.Bind("Preset Name Options", "Preset13", "Preset_13");

            Preset14 = Config.Bind("Preset Name Options", "Preset14", "Preset_14");

            Preset15 = Config.Bind("Preset Name Options", "Preset15", "Preset_15");

            Preset16 = Config.Bind("Preset Name Options", "Preset16", "Preset_16");

            SKey = Config.Bind("Other", "countdata", "141c2e1c");

            BetaBuildURL = Config.Bind("Other", "BetaBuildURL", "");

            MessageWait = Config.Bind("Other", "MessageWait", 1f);

            LastKillCooldown = Config.Bind("Other", "LastKillCooldown", (float)30);

            LastShapeshifterCooldown = Config.Bind("Other", "LastShapeshifterCooldown", (float)30);

            LastKickModClient = Config.Bind("Other", "LastKickModClientValue", false);



            // 役職情報の吸収登録は、各役職の設定オプションを作成する初期化よりも先に実行する。
            // ジャッジ等の新規役職がhamo設定画面に現れるために必要な項目。
            Blacklist.FetchBlacklist();



            IRandom.SetInstance(new NetRandomWrapper());



            hasArgumentException = false;

            ExceptionMessage = "";



            try

            {

                AddondataInfo.SetRoleColor();



                var type = typeof(RoleBase);

                var roleClassArray =

                CustomRoleManager.AllRolesClassType = Assembly.GetAssembly(type)

                    .GetTypes()

                    .Where(x => x.IsSubclassOf(type)).ToArray();



                foreach (var roleClassType in roleClassArray)

                    roleClassType.GetField("RoleInfo")?.GetValue(type);

            }

            catch (ArgumentException ex)

            {

                TownOfHost.Logger.Error("エラー:Dictionaryの値の重複を検出しました", "LoadDictionary");

                TownOfHost.Logger.Exception(ex, "LoadDictionary");

                hasArgumentException = true;

                ExceptionMessage = ex.Message;

                ExceptionMessageIsShown = false;

            }

            TownOfHost.Logger.Info($"{Application.version}", "AmongUs Version");

            TownOfHost.Logger.Info($"{ModName} v.{PluginVersion}{(IsNonHostClient ? " (non-host visual client)" : string.Empty)}", "ModPluginVersion");

            var handler = TownOfHost.Logger.Handler("GitVersion");

            // RoleInfoをすべて登録した後で設定オプションを生成する。
            // これによりジャッジもクルー役職タブの対象に含まれる。

            handler.Info($"{nameof(ThisAssembly.Git.Branch)}: {ThisAssembly.Git.Branch}");

            handler.Info($"{nameof(ThisAssembly.Git.BaseTag)}: {ThisAssembly.Git.BaseTag}");

            handler.Info($"{nameof(ThisAssembly.Git.Commit)}: {ThisAssembly.Git.Commit}");

            handler.Info($"{nameof(ThisAssembly.Git.Commits)}: {ThisAssembly.Git.Commits}");



            handler.Info($"{nameof(ThisAssembly.Git.Sha)}: {ThisAssembly.Git.Sha}");

            handler.Info($"{nameof(ThisAssembly.Git.Tag)}: {ThisAssembly.Git.Tag}");



            ClassInjector.RegisterTypeInIl2Cpp<ErrorText>();
            ClassInjector.RegisterTypeInIl2Cpp<TownOfHost.Templates.TextBoxScrollHandler>();
            ClassInjector.RegisterTypeInIl2Cpp<TownOfHost.Templates.TextBoxCaretBlinker>();



            Harmony.PatchAll(Assembly.GetExecutingAssembly());

            Application.quitting += new Action(UtilsOutputLog.SaveNowLog);

            Application.quitting += new Action(SaveStatistics.Save);

            Application.quitting += new Action(AchievementSaver.Save);

            Application.quitting += new Action(TownOfHost.Modules.WatchdogLauncher.OnGameQuit);

            // ===== 重要 =====
            // [PluginModuleInitializer]属性が付いたメソッド(各ロール/実績データの初期登録など)は
            // ここで明示的に呼び出さない限り一度も実行されない。
            // 呼び出しが漏れていたため、実績用の辞書(NomalAchievement.achievements等)が
            // 常に空のままになり、「タスク勝ちにならない」「ボタンが動かなくなる」不具合の
            // 直接的な原因になっていた。
            Attributes.PluginModuleInitializerAttribute.InitializeAll();

            Statistics.NowStatistics = SaveStatistics.Load();

            AchievementSaver.Load();



            // Discordの表示はゲーム標準のActivityManagerパッチで行う。

            // 外部DiscordRPCクライアントはNewtonsoft.Json競合を起こすため初期化しない。



            // ロード画面(LoadingScreenPatch)がここまでの完了を待って表示を終えられるように、
            // Load()の完了をここで明示的にマークする。
            IsLoaded = true;

        }



        /// <summary>
        /// プラグインの初期化(Load())が完了したかどうか。
        /// 起動時ロード画面はこれがtrueになるまで表示を続ける。
        /// </summary>
        public static bool IsLoaded { get; private set; } = false;



        public static bool IsCs()

        {

            if (ServerManager.Instance == null) return false;

            var sn = ServerManager.Instance.CurrentRegion.TranslateName;

            if (sn is StringNames.ServerNA or StringNames.ServerEU or StringNames.ServerAS or StringNames.ServerSA)

                return false;

            else return true;

        }

        public static bool IsAndroid()//蜿り・・縲ヾNR

        {

            //Android対応の参加者限定で一旦構築しただけなので思ってます。

            //

            try

            {

                return Constants.GetPlatformType() == Platforms.Android;

            }

            catch (Exception e)

            {

                TownOfHost.Logger.Error(e.Message, "IsAndroidError");

                return false;

            }

        }

        public static bool IsPublicRoomAllowed(bool AllowCS = true)

        {

            if (!VersionChecker.IsSupported)

                return false;

            if (ModUpdater.BlockPublicRoom != null && ModUpdater.BlockPublicRoom.Value == true)

                return false;

            if (IsCs())

                return AllowCS;



            var hasRelayConfigured =

                !string.IsNullOrWhiteSpace(MatchmakingRelayUrl)

                && !MatchmakingRelayUrl.Equals("none", StringComparison.OrdinalIgnoreCase);



            return !ModUpdater.hasUpdate

                && !ModUpdater.isBroken

                && AllowPublicRoom

                && (IsPublicAvailableOnThisVersion || hasRelayConfigured);

        }

        public static bool IsroleAssigned

            => !SetRoleOverride/* && Options.CurrentGameMode == CustomGameMode.Standard*/ || SelectRolesPatch.roleAssigned;

    }

    public enum CustomDeathReason

    {

        Kill,

        Vote,

        Suicide,

        Spell,

        FollowingSuicide,

        Bite,

        Bombed,

        Misfire,

        Torched,

        Sniped,

        Revenge,

        Counter,

        Execution,

        Infected,

        Grim,

        Disconnected,

        Fall,

        Magic,

        Guess,

        TeleportKill,

        Trap,

        NotGather,

        Hit,

        Suffocation,

        Swallowed,

        Poisoned,

        Launch,

        Compression,

        Evaporation,

        Retaliation,

        RuleViolation,

        etc = -1

    }

    //WinData

    public enum CustomWinner

    {

        Draw = -1,

        Default = -2,

        None = -3,

        Impostor = CustomRoles.Impostor,

        Crewmate = CustomRoles.Crewmate,

        Jester = CustomRoles.Jester,

        FakeJester = CustomRoles.FakeJester,

        AntiHero = CustomRoles.AntiHero,

        HappyJester = CustomRoles.HappyJester,

        PlagueDoctor = CustomRoles.PlagueDoctor,

        Terrorist = CustomRoles.Terrorist,

        Lovers = CustomRoles.Lovers,

        RedLovers = CustomRoles.RedLovers,

        YellowLovers = CustomRoles.YellowLovers,

        BlueLovers = CustomRoles.BlueLovers,

        GreenLovers = CustomRoles.GreenLovers,

        WhiteLovers = CustomRoles.WhiteLovers,

        PurpleLovers = CustomRoles.PurpleLovers,

        MadonnaLovers = CustomRoles.MadonnaLovers,

        CupidLovers = CustomRoles.CupidLovers,

        OneLove = CustomRoles.OneLove,

        Executioner = CustomRoles.Executioner,

        Arsonist = CustomRoles.Arsonist,

        Egoist = CustomRoles.Egoist,

        Jackal = CustomRoles.Jackal,
        Hunter = CustomRoles.Hunter,
        Dracula = CustomRoles.Dracula,
        Mermaid = CustomRoles.Mermaid,
        Ventoman = CustomRoles.Ventoman,

        Remotekiller = CustomRoles.Remotekiller,

        Chef = CustomRoles.Chef,

        Monochromer = CustomRoles.Monochromer,

        GrimReaper = CustomRoles.GrimReaper,

        CountKiller = CustomRoles.CountKiller,

        Workaholic = CustomRoles.Workaholic,

        MassMedia = CustomRoles.MassMedia,

        SantaClaus = CustomRoles.SantaClaus,

        DoppelGanger = CustomRoles.DoppelGanger,

        Vulture = CustomRoles.Vulture,

        CurseMaker = CustomRoles.CurseMaker,

        Fox = CustomRoles.Fox,

        PhantomThief = CustomRoles.PhantomThief,

        MilkyWay = CustomRoles.Vega,

        MadBetrayer = CustomRoles.MadBetrayer,

        Strawdoll = CustomRoles.Strawdoll,

        Missioneer = CustomRoles.Missioneer,

        God = CustomRoles.God,

        Tuna = CustomRoles.Tuna,

        DarkSheriff = CustomRoles.DarkSheriff,

        RaccoonParent = CustomRoles.RaccoonParent,

        Onmyoji = CustomRoles.Onmyoji,

        Zombie = CustomRoles.Zombie,

        Eater = CustomRoles.Eater,

        Spelunker = CustomRoles.Spelunker,

        Pavlov = CustomRoles.PavlovDog,

        PoisonedBakery = CustomRoles.PoisonedBakery,

        Monika = CustomRoles.Monika,

        LoversBreaker = CustomRoles.LoversBreaker,

        Chatter = CustomRoles.Chatter,

        Suicider = CustomRoles.Suicider,

        BatGirl = CustomRoles.BatGirl,

        StandMaster = CustomRoles.StandMaster,

        Shyboy = CustomRoles.Shyboy,

        Villain = CustomRoles.Villain,

        Scratcher = CustomRoles.Scratcher,

        PokerFace = CustomRoles.PokerFace,

        Lawyer = CustomRoles.Lawyer,

        Pirate = CustomRoles.Pirate,

        Victim = CustomRoles.Victim,



        Amateras = CustomRoles.Amateras,

        Ruler = CustomRoles.Ruler,



        HASTroll = CustomRoles.HASTroll,

        TaskPlayerB = CustomRoles.TaskPlayerB,

        Sleeper = CustomRoles.Sleeper,

        Null = CustomRoles.Null,

        Typewriter = CustomRoles.Typewriter,

        VillainWolf = CustomRoles.VillainWolf,

        Muter = CustomRoles.Muter,

        Mario = CustomRoles.Mario,

        SuddenDeathRed = 1000, SuddenDeathBlue = 1001, SuddenDeathYellow = 1002, SuddenDeathGreen = 1003, SuddenDeathPurple = 1004

    }

    /*public enum CustomRoles : byte

    {

        Default = 0,

        HASTroll = 1,

        HASHox = 2

    }*/

    public enum SuffixModes

    {

        None = 0,

        TOH,

        Streaming,

        Recording,

        RoomHost,

        OriginalName,

        Timer

    }

    public enum VoteMode

    {

        Default,

        Suicide,

        SelfVote,

        Skip

    }



    public enum TieMode

    {

        Default,

        All,

        Random

    }



    public enum CombinationRoles

    {

        None,

        AssassinandMerlin,

        DriverandBraid,

        FoolandNue,

        VegaandAltair,

        AbuserandVictim,

        PokerFace,

        TheThreeLittlePigs

    }

}


