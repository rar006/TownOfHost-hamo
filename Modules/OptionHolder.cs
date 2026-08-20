using System;

using System.Collections.Generic;

using System.Linq;

using HarmonyLib;

using UnityEngine;



using TownOfHost.Modules;

using TownOfHost.Roles;

using TownOfHost.Roles.Core;

using TownOfHost.Roles.AddOns.Neutral;



namespace TownOfHost

{

    [Flags]

    public enum CustomGameMode

    {

        Standard, //= 0x01,

        HideAndSeek, //= 0x02,

        TaskBattle, //= 0x03,

        StandardHAS,//= 0x04

        SuddenDeath,//= 0x05

        MurderMystery,//= 0x06

        DummyBattleRoyale,//= 0x07

        DummyHunter,//= 0x08

        ShuffleRole,//= 0x09

        All = int.MaxValue

    }



    [Flags]

    public enum CustomOptionTags

    {

        Standard,//スタンダードモード

        HideAndSeek,//かくれんぼモード

        TaskBattle,//タスクバトルMode

        SuddenDeath,//サドンデス(Sta)

        MurderMystery,

        DummyBattleRoyale,

        DummyHunter,

        ShuffleRole,

        StandardHAS,//役職入りかくれんぼ(Sta)

        Role,//役職設定

        GameOption,//ゲーム設定

        OtherOption,//その他の設定



        All = int.MaxValue,

    }



    [HarmonyPatch]

    public static class Options

    {

        //static Task taskOptionsLoad;

        public static bool LoadError;

        [HarmonyPatch(typeof(TranslationController), nameof(TranslationController.Initialize)), HarmonyPostfix]

        public static void OptionsLoadStart(TranslationController __instance)

        {

            Logger.Info("Options.Load Start", "Options");

            Main.UseYomiage.Value = false;

#if RELEASE

            Main.ViewPingDetails.Value = false;

            Main.DebugSendAmout.Value = false;

            Main.DebugTours.Value = false;

            Main.ShowDistance.Value = false;

            Main.DebugChatopen.Value  =false;

#endif

            //taskOptionsLoad = Task.Run(Load);

            try

            {

                Load();

                LoadError = false;

            }

            catch (Exception ex)

            {

                LoadError = true;

                Logger.Exception(ex, "Options");



                ErrorText.Instance?.AddError(ErrorCode.OptionError);

            }

        }

        [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start)), HarmonyPostfix]

        public static void WaitOptionsLoad()

        {

            if (IsLoaded) return;

            //taskOptionsLoad.Wait();

            Logger.Info("Options.Load End", "Options");

        }



        // プリセット

        private static readonly string[] presets =

        {

            Main.Preset1.Value, Main.Preset2.Value, Main.Preset3.Value,

            Main.Preset4.Value, Main.Preset5.Value,Main.Preset6.Value,

            Main.Preset7.Value, Main.Preset8.Value, Main.Preset9.Value, Main.Preset10.Value,

            Main.Preset11.Value, Main.Preset12.Value,Main.Preset13.Value,

            Main.Preset14.Value, Main.Preset15.Value, Main.Preset16.Value,

        };



        // ゲームモード

        public static OptionItem GameMode;

        public static CustomGameMode CurrentGameMode => (CustomGameMode)GameMode.GetValue();



        public static readonly string[] gameModes =

        {

            // CustomGameMode enum (Standard, HideAndSeek, TaskBattle, StandardHAS, SuddenDeath,

            // MurderMystery, DummyBattleRoyale, DummyHunter, ShuffleRole) の並び順と

            // 1対1で対応している必要がある(CurrentGameModeが選択インデックスを直接

            // enumにキャストしているため)。以前はここに謎の重複・欠落があり、

            // DummyBattleRoyale以降の選択肢がenumとずれてしまっていた。

            "Standard", "HideAndSeek", "TaskBattle", "StandardHAS", "SuddenDeath", "MurderMystery",

            "DummyBattleRoyale", "DummyHunter", "ShuffleRole",

        };



        // MapActive

        public static bool IsActiveSkeld => AddedTheSkeld.GetBool() || Main.NormalOptions?.MapId is 0 or null;

        public static bool IsActiveMiraHQ => AddedMiraHQ.GetBool() || Main.NormalOptions?.MapId is 1 or null;

        public static bool IsActivePolus => AddedPolus.GetBool() || Main.NormalOptions?.MapId is 2 or null;

        public static bool IsActiveAirship => AddedTheAirShip.GetBool() || Main.NormalOptions?.MapId is 4 or null;

        public static bool IsActiveFungle => AddedTheFungle.GetBool() || Main.NormalOptions?.MapId is 5 or null;



        // 役職数・確率

        public static Dictionary<CustomRoles, OptionItem> CustomRoleCounts;

        public static Dictionary<CustomRoles, IntegerOptionItem> CustomRoleSpawnChances;

        public static readonly string[] rates =

        {

            "Rate0",  "Rate5",  "Rate10", "Rate20", "Rate30", "Rate40",

            "Rate50", "Rate60", "Rate70", "Rate80", "Rate90", "Rate100",

        };



        // 各役職の詳細設定

        public static OptionItem EnableGM;

        public static float DefaultKillCooldown = Main.NormalOptions?.KillCooldown ?? 20;

        public static OptionItem DoubleTriggerThreshold;

        public static OptionItem DefaultShapeshiftCooldown;

        public static OptionItem DefaultShapeshiftDuration;

        public static OptionItem DefaultEngineerCooldown;

        public static OptionItem DefaultEngineerInVentMaxTime;

        public static OptionItem CanMakeMadmateCount;

        public static OptionItem SkMadCanUseVent;

        public static OptionItem MadMateOption;

        public static OptionItem MadmateCanSeeKillFlash;

        public static OptionItem MadmateCanSeeDeathReason;

        public static OptionItem MadmateRevengePlayer;

        public static OptionItem MadmateRevengeCanImpostor;

        public static OptionItem MadmateRevengeNeutral;

        public static OptionItem MadmateRevengeMadmate;

        public static OptionItem MadmateRevengeCrewmate;

        public static OptionItem MadCanSeeImpostor;

        public static OptionItem MadmateCanFixLightsOut;

        public static OptionItem MadmateCanFixComms;

        public static OptionItem MadmateHasLighting;

        public static OptionItem MadmateHasMoon;

        public static OptionItem MadmateCanSeeOtherVotes;

        public static OptionItem MadmateTell;

        static string[] Tellopt =

        {"NoProcessing","Crewmate","Madmate","Impostor"};

        public static CustomRoles MadTellOpt()

        {

            switch (Tellopt[MadmateTell.GetValue()])

            {

                case "NoProcessing": return CustomRoles.NotAssigned;

                case "Crewmate": return CustomRoles.Crewmate;

                case "Madmate": return CustomRoles.Madmate;

                case "Impostor": return CustomRoles.Impostor;

            }

            return CustomRoles.NotAssigned;

        }

        public static OptionItem MadmateVentCooldown;

        public static OptionItem MadmateVentMaxTime;

        public static OptionItem MadmateCanMovedByVent;



        //試験的機能

        public static OptionItem ExperimentalMode;

        public static OptionItem ExAftermeetingflash;

        public static OptionItem ExHideChatCommand;

        public static OptionItem ExCsHideChat;

        public static OptionItem FixSpawnPacketSize;

        public static OptionItem ExRpcWeightR;

        public static OptionItem ExCallMeetingBlackout;

        public static OptionItem ExIntroWeight;

        public static OptionItem ExChatMonochrome;



        //幽霊役職

        public static OptionItem GhostRoleOption;

        public static OptionItem GhostRoleCanSeeOtherRoles;

        public static OptionItem GhostRoleCanSeeOtherTasks;

        public static OptionItem GhostRoleCanSeeOtherVotes;

        public static OptionItem GhostRoleCanSeeDeathReason;

        public static OptionItem GhostRoleCanSeeKillerColor;

        public static OptionItem GhostRoleCanSeeAllTasks;

        public static OptionItem GhostRoleCanSeeKillflash;

        public static OptionItem GhostRoleCanSeeNumberOfButtonsOnOthers;



        public static OptionItem KillFlashDuration;



        // HideAndSeek

        public static OptionItem AllowCloseDoors;

        public static OptionItem AfterTurnCantCloseDoor;

        public static OptionItem KillDelay;

        public static OptionItem IgnoreVent;

        public static float HideAndSeekKillDelayTimer = 0f;

        //特殊モード

        public static OptionItem ONspecialMode;

        public static OptionItem ColorNameMode;

        public static OptionItem InsiderMode;

        public static OptionItem InsiderModeCanSeeTask;

        public static OptionItem CanSeeImpostorRole;

        public static OptionItem AllPlayerSkinShuffle;



        public static OptionItem TaskOption;

        public static OptionItem UploadDataIsLongTask;

        // タスク無効化

        public static OptionItem DisableTasks;

        public static OptionItem DisableSwipeCard;

        public static OptionItem DisableSubmitScan;

        public static OptionItem DisableUnlockSafe;

        public static OptionItem DisableUploadData;

        public static OptionItem DisableStartReactor;

        public static OptionItem DisableResetBreaker;

        public static OptionItem DisableCatchFish;

        public static OptionItem DisableDivertPower;

        public static OptionItem DisableFuelEngins;

        public static OptionItem DisableInspectSample;

        public static OptionItem DisableRebootWifi;

        public static OptionItem DisableFixWeatherNode;

        //

        public static OptionItem DisableInseki;

        public static OptionItem DisableCalibrateDistributor;

        public static OptionItem DisableVentCleaning;

        public static OptionItem DisableHelpCritter;

        public static OptionItem Disablefixwiring;

        //デバイスブロック

        public static OptionItem DevicesOption;

        public static OptionItem DisableDevices;

        public static OptionItem DisableSkeldDevices;

        public static OptionItem DisableSkeldAdmin;

        public static OptionItem DisableSkeldCamera;

        public static OptionItem DisableMiraHQDevices;

        public static OptionItem DisableMiraHQAdmin;

        public static OptionItem DisableMiraHQDoorLog;

        public static OptionItem DisablePolusDevices;

        public static OptionItem DisablePolusAdmin;

        public static OptionItem DisablePolusCamera;

        public static OptionItem DisablePolusVital;

        public static OptionItem DisableAirshipDevices;

        public static OptionItem DisableAirshipCockpitAdmin;

        public static OptionItem DisableAirshipRecordsAdmin;

        public static OptionItem DisableAirshipCamera;

        public static OptionItem DisableAirshipVital;

        public static OptionItem DisableFungleDevices;

        public static OptionItem DisableFungleVital;

        public static OptionItem DisableDevicesIgnoreConditions;

        public static OptionItem DisableDevicesIgnoreImpostors;

        public static OptionItem DisableDevicesIgnoreMadmates;

        public static OptionItem DisableDevicesIgnoreNeutrals;

        public static OptionItem DisableDevicesIgnoreCrewmates;

        public static OptionItem DisableDevicesIgnoreAfterAnyoneDied;

        public static OptionItem DisableDevicesIgnoreCompleteTask;

        public static OptionItem DisableForceRecordsAdomin;



        public static OptionItem TimeLimitDevices;

        public static OptionItem TimeLimitAdmin;

        public static OptionItem TimeLimitCamAndLog;

        public static OptionItem TimeLimitVital;

        public static OptionItem CanSeeTimeLimit;

        public static OptionItem CanseeImpTimeLimit;

        public static OptionItem CanseeMadTimeLimit;

        public static OptionItem CanseeCrewTimeLimit;

        public static OptionItem CanseeNeuTimeLimit;

        public static OptionItem ReviveTimelimitplayercount;

        public static OptionItem ReviveAddAdmin;

        public static OptionItem ReviveAddCamAndLog;

        public static OptionItem ReviveAddVital;



        public static OptionItem TurnTimeLimitDevice;

        public static OptionItem TurnTimeLimitAdmin;

        public static OptionItem TurnTimeLimitCamAndLog;

        public static OptionItem TurnTimeLimitVital;



        // ランダムマップ

        public static OptionItem RandomMapsMode;

        public static OptionItem AddedTheSkeld;

        public static OptionItem AddedMiraHQ;

        public static OptionItem AddedPolus;

        public static OptionItem AddedTheAirShip;

        public static OptionItem AddedTheFungle;

        // public static OptionItem AddedDleks;

        // ランダムプリセット

        public static OptionItem RandomPreset;

        public static OptionItem AddedPreset1;

        public static OptionItem AddedPreset2;

        public static OptionItem AddedPreset3;

        public static OptionItem AddedPreset4;

        public static OptionItem AddedPreset5;

        public static OptionItem AddedPreset6;

        public static OptionItem AddedPreset7;

        public static OptionItem AddedPreset8;

        public static OptionItem AddedPreset9;

        public static OptionItem AddedPreset10;

        public static OptionItem AddedPreset11;

        public static OptionItem AddedPreset12;

        public static OptionItem AddedPreset13;

        public static OptionItem AddedPreset14;

        public static OptionItem AddedPreset15;

        public static OptionItem AddedPreset16;



        // ランダムスポーン

        public static OptionItem EnableRandomSpawn;

        public static OptionItem CanSeeNextRandomSpawn;

        //Skeld

        public static OptionItem RandomSpawnSkeld;

        public static OptionItem RandomSpawnSkeldCafeteria;

        public static OptionItem RandomSpawnSkeldWeapons;

        public static OptionItem RandomSpawnSkeldLifeSupp;

        public static OptionItem RandomSpawnSkeldNav;

        public static OptionItem RandomSpawnSkeldShields;

        public static OptionItem RandomSpawnSkeldComms;

        public static OptionItem RandomSpawnSkeldStorage;

        public static OptionItem RandomSpawnSkeldAdmin;

        public static OptionItem RandomSpawnSkeldElectrical;

        public static OptionItem RandomSpawnSkeldLowerEngine;

        public static OptionItem RandomSpawnSkeldUpperEngine;

        public static OptionItem RandomSpawnSkeldSecurity;

        public static OptionItem RandomSpawnSkeldReactor;

        public static OptionItem RandomSpawnSkeldMedBay;

        //Mira

        public static OptionItem RandomSpawnMira;

        public static OptionItem RandomSpawnMiraCafeteria;

        public static OptionItem RandomSpawnMiraBalcony;

        public static OptionItem RandomSpawnMiraStorage;

        public static OptionItem RandomSpawnMiraJunction;

        public static OptionItem RandomSpawnMiraComms;

        public static OptionItem RandomSpawnMiraMedBay;

        public static OptionItem RandomSpawnMiraLockerRoom;

        public static OptionItem RandomSpawnMiraDecontamination;

        public static OptionItem RandomSpawnMiraLaboratory;

        public static OptionItem RandomSpawnMiraReactor;

        public static OptionItem RandomSpawnMiraLaunchpad;

        public static OptionItem RandomSpawnMiraAdmin;

        public static OptionItem RandomSpawnMiraOffice;

        public static OptionItem RandomSpawnMiraGreenhouse;

        //Polus

        public static OptionItem RandomSpawnPolus;

        public static OptionItem RandomSpawnPolusOfficeLeft;

        public static OptionItem RandomSpawnPolusOfficeRight;

        public static OptionItem RandomSpawnPolusAdmin;

        public static OptionItem RandomSpawnPolusComms;

        public static OptionItem RandomSpawnPolusWeapons;

        public static OptionItem RandomSpawnPolusBoilerRoom;

        public static OptionItem RandomSpawnPolusLifeSupp;

        public static OptionItem RandomSpawnPolusElectrical;

        public static OptionItem RandomSpawnPolusSecurity;

        public static OptionItem RandomSpawnPolusDropship;

        public static OptionItem RandomSpawnPolusStorage;

        public static OptionItem RandomSpawnPolusRocket;

        public static OptionItem RandomSpawnPolusLaboratory;

        public static OptionItem RandomSpawnPolusToilet;

        public static OptionItem RandomSpawnPolusSpecimens;

        //AIrShip

        public static OptionItem RandomSpawnAirship;

        public static OptionItem RandomSpawnAirshipBrig;

        public static OptionItem RandomSpawnAirshipEngine;

        public static OptionItem RandomSpawnAirshipKitchen;

        public static OptionItem RandomSpawnAirshipCargoBay;

        public static OptionItem RandomSpawnAirshipRecords;

        public static OptionItem RandomSpawnAirshipMainHall;

        public static OptionItem RandomSpawnAirshipNapRoom;

        public static OptionItem RandomSpawnAirshipMeetingRoom;

        public static OptionItem RandomSpawnAirshipGapRoom;

        public static OptionItem RandomSpawnAirshipVaultRoom;

        public static OptionItem RandomSpawnAirshipComms;

        public static OptionItem RandomSpawnAirshipCockpit;

        public static OptionItem RandomSpawnAirshipArmory;

        public static OptionItem RandomSpawnAirshipViewingDeck;

        public static OptionItem RandomSpawnAirshipSecurity;

        public static OptionItem RandomSpawnAirshipElectrical;

        public static OptionItem RandomSpawnAirshipMedical;

        public static OptionItem RandomSpawnAirshipToilet;

        public static OptionItem RandomSpawnAirshipShowers;

        //Fungle

        public static OptionItem RandomSpawnFungle;

        public static OptionItem RandomSpawnFungleKitchen;

        public static OptionItem RandomSpawnFungleBeach;

        public static OptionItem RandomSpawnFungleCafeteria;

        public static OptionItem RandomSpawnFungleRecRoom;

        public static OptionItem RandomSpawnFungleBonfire;

        public static OptionItem RandomSpawnFungleDropship;

        public static OptionItem RandomSpawnFungleStorage;

        public static OptionItem RandomSpawnFungleMeetingRoom;

        public static OptionItem RandomSpawnFungleSleepingQuarters;

        public static OptionItem RandomSpawnFungleLaboratory;

        public static OptionItem RandomSpawnFungleGreenhouse;

        public static OptionItem RandomSpawnFungleReactor;

        public static OptionItem RandomSpawnFungleJungleTop;

        public static OptionItem RandomSpawnFungleJungleBottom;

        public static OptionItem RandomSpawnFungleLookout;

        public static OptionItem RandomSpawnFungleMiningPit;

        public static OptionItem RandomSpawnFungleHighlands;

        public static OptionItem RandomSpawnFungleUpperEngine;

        public static OptionItem RandomSpawnFunglePrecipice;

        public static OptionItem RandomSpawnFungleComms;



        // CustomSpawn

        public static OptionItem EnableCustomSpawn;

        public static OptionItem RandomSpawnCustom1;

        public static OptionItem RandomSpawnCustom2;

        public static OptionItem RandomSpawnCustom3;

        public static OptionItem RandomSpawnCustom4;

        public static OptionItem RandomSpawnCustom5;

        public static OptionItem RandomSpawnCustom6;

        public static OptionItem RandomSpawnCustom7;

        public static OptionItem RandomSpawnCustom8;

        public static OptionItem MeetingAndVoteOpt;



        public static OptionItem ShowVoteResult;

        public static OptionItem ShowVoteJudgment;

        public static readonly string[] ShowVoteJudgments =

        {

            "Impostor","Neutral", "CrewMate(Mad)", "Crewmate","Role","ShowTeam"

        };

        // 投票モード

        public static OptionItem VoteMode;

        public static OptionItem WhenSkipVote;

        public static OptionItem WhenSkipVoteIgnoreFirstMeeting;

        public static OptionItem WhenSkipVoteIgnoreNoDeadBody;

        public static OptionItem WhenSkipVoteIgnoreEmergency;

        public static OptionItem WhenNonVote;

        public static OptionItem WhenTie;

        public static readonly string[] voteModes =

        {

            "Default", "Suicide", "SelfVote", "Skip"

        };

        public static readonly string[] tieModes =

        {

            "TieMode.Default", "TieMode.All", "TieMode.Random"

        };

        public static VoteMode GetWhenSkipVote() => (VoteMode)WhenSkipVote.GetValue();

        public static VoteMode GetWhenNonVote() => (VoteMode)WhenNonVote.GetValue();



        // ボタン回数

        public static OptionItem SyncButtonMode;

        public static OptionItem SyncedButtonCount;

        public static int UsedButtonCount = 0;



        // 全員生存時の会議時間

        public static OptionItem AllAliveMeeting;

        public static OptionItem AllAliveMeetingTime;



        // 追加の緊急ボタンクールダウン

        public static OptionItem AdditionalEmergencyCooldown;

        public static OptionItem AdditionalEmergencyCooldownThreshold;

        public static OptionItem AdditionalEmergencyCooldownTime;



        //会議時間

        public static OptionItem LowerLimitVotingTime;

        public static OptionItem MeetingTimeLimit;



        //転落死

        public static OptionItem LadderDeath;

        public static OptionItem LadderDeathChance;

        public static OptionItem LadderDeathNuuun;

        public static OptionItem LadderDeathZipline;



        // 通常モードでかくれんぼ

        public static bool IsStandardHAS => CurrentGameMode == CustomGameMode.StandardHAS;

        public static OptionItem StandardHAS;

        public static OptionItem StandardHASWaitingTime;



        // リアクターの時間制御

        public static OptionItem SabotageActivetimerControl;

        public static OptionItem SkeldReactorTimeLimit;

        public static OptionItem SkeldO2TimeLimit;

        public static OptionItem MiraReactorTimeLimit;

        public static OptionItem MiraO2TimeLimit;

        public static OptionItem PolusReactorTimeLimit;

        public static OptionItem AirshipReactorTimeLimit;

        public static OptionItem FungleReactorTimeLimit;

        public static OptionItem FungleMushroomMixupDuration;



        // サボタージュのクールダウン変更

        public static OptionItem ModifySabotageCooldown;

        public static OptionItem SabotageCooldown;



        // 停電の特殊設定

        public static OptionItem LightsOutSpecialSettings;

        public static OptionItem LightOutDonttouch;

        public static OptionItem LightOutDonttouchTime;

        public static OptionItem DisableAirshipViewingDeckLightsPanel;

        public static OptionItem DisableAirshipGapRoomLightsPanel;

        public static OptionItem DisableAirshipCargoLightsPanel;

        public static OptionItem BlockDisturbancesToSwitches;

        public static OptionItem CommsSpecialSettings;

        public static OptionItem CommsCamouflage;

        public static OptionItem CommsDonttouch;

        public static OptionItem CommsDonttouchTime;

        // 他サボ

        public static OptionItem ChangeSabotageWinRole;

        public static OptionItem OptionSabotageFinAllKill;

        // マップ改造

        public static OptionItem Sabotage;

        public static OptionItem MapModification;

        public static OptionItem AirShipVariableElectrical;

        public static OptionItem AirShipPlatform;

        public static OptionItem DisableAirshipMovingPlatform;

        public static OptionItem CantUseVentMode;

        public static OptionItem CantUseVentTrueCount;

        public static OptionItem MaxInVentMode;

        public static OptionItem MaxInVentTime;

        public static OptionItem ResetDoorsEveryTurns;

        public static OptionItem DoorsResetMode;

        public static OptionItem DisableFungleSporeTrigger;

        public static OptionItem CantUseZipLineTotop;

        public static OptionItem CantUseZipLineTodown;

        public static string[] PlatformOption =

        {

            "ColoredOff" , "AssignAlgorithm.Random" , "PlatfromLeft" , "PlatfromRight"

        };

        // その他

        public static OptionItem OptionCommandSetting;

        public static OptionItem OptionCommandNow;

        public static OptionItem OptionCommandNowRole;

        public static OptionItem OptionCommandNowSet;

        public static OptionItem OptionCommandNowW;

        public static OptionItem OptionCommandHNow;

        public static OptionItem OptionCommandHRoles;

        public static OptionItem OptionCommandMyrole;

        public static OptionItem OptionCommandMeetinginfo;

        public static OptionItem OptionCommandNumberDNumber;

        public static OptionItem OptionCommand8ball;

        public static OptionItem OptionCommandRename;

        public static OptionItem OptionCommandRule;

        public static OptionItem OptionCommandLastresult;

        public static OptionItem OptionCommandKilllog;

        public static OptionItem OptionCommandTimer;

        public static OptionItem OptionCommandTp;

        public static OptionItem OptionCommandCo;

        public static OptionItem OptionCommandColist;

        public static OptionItem OptionCommandAco;

        public static OptionItem OptionCommandAcl;



        public static OptionItem OptionNameCharLimit;

        public static OptionItem OptionUltraLightweightMode;

        public static OptionItem OptionAutoFunction;

        public static OptionItem OptionAutoStartSetting;

        public static OptionItem OptionAutoStartGM;

        public static OptionItem OptionAutoStartLimit;

        public static OptionItem OptionAutoStartLimitAnotherSetting;

        public static OptionItem OptionAutoStartLimitAnother;

        public static OptionItem OptionAutoReturnRoom;

        public static OptionItem OptionAutoReturnRoomGM;

        public static OptionItem OptionAutoForceEndOnDisconnect;

        public static OptionItem OptionAutoForceEndDisconnectCount;

        public static OptionItem OptionPeriodicAutoRehost;

        public static OptionItem OptionPeriodicAutoRehostInterval;

        public static OptionItem OptionPeriodicAutoRehostMinPlayersEnabled;

        public static OptionItem OptionPeriodicAutoRehostMinPlayers;

        public static OptionItem OptionPeriodicAutoRehostExcludeVoidGames;

        public static OptionItem OptionPeriodicDataReset;

        public static OptionItem OptionPeriodicDataResetInterval;

        public static OptionItem OptionPeriodicDataResetExcludeVoidGames;

        public static OptionItem OptionPresetSwitchByPlayerCount;

        public static OptionItem OptionPresetSwitchThreshold;

        public static OptionItem OptionPresetSwitchTargetPreset;

        public static OptionItem OptionGMAutoPossessPreferImpostor;

        public static OptionItem OptionGMAutoPossessAliveOnly;

        public static OptionItem OptionTimedAutoRehost;

        public static OptionItem OptionTimedAutoRehostMinutes;

        public static OptionItem OptionStreamerSetting;

        public static OptionItem OptionGMAutoChat;

        public static OptionItem OptionGMAutoPossess;

        public static OptionItem OptionGMHideTaskButton;

        public static OptionItem OptionGMCanSeeRoles;

        public static OptionItem OptionGMCanSeeTaskProgress;

        public static OptionItem OptionGMCanSeeVotes;

        public static OptionItem OptionGMCanSeeDeathReason;

        public static OptionItem OptionGMCanSeeOverallTaskProgress;

        public static OptionItem OptionGMCanSeeEmergencyCount;

        public static OptionItem OptionGMCanSeeKillFlash;

        public static OptionItem OptionJoinKick;

        public static OptionItem OptionNotifyJoinKick;

        public static OptionItem OptionNotModeJoinKick;

        public static OptionItem OptionDrawJoinKick;

        public static OptionItem OptionGameChatSetting;

        public static OptionItem OptionGameChatNormalChat;

        public static OptionItem OptionGameChatNormalNearChat;

        public static OptionItem OptionGameChatNormalNearChatRange;

        public static OptionItem OptionGameChatHideChat;

        public static OptionItem OptionGameChatHideNearChat;

        public static OptionItem OptionGameChatHideNearChatRange;

        public static OptionItem ConvenientOptions;

        public static OptionItem AutoGrantPet;

        public static OptionItem FirstTurnMeeting;

        public static bool firstturnmeeting;

        public static OptionItem FirstTurnMeetingCantability;

        public static OptionItem FixFirstKillCooldown;

        public static OptionItem CanseeVoteresult;

        public static OptionItem CommnTaskResetAssing;

        public static OptionItem OutroCrewWinreasonchenge;

        public static OptionItem TeamHideChat;

        public static OptionItem ImpostorHideChat;

        public static OptionItem LoversHideChat;

        public static OptionItem CupidHideChat;

        public static OptionItem JackalHideChat;

        public static OptionItem TwinsHideChat;

        public static OptionItem ConnectingHideChat;

        public static OptionItem OnmyojiHideChat;

        public static OptionItem PavlovHideChat;

        public static OptionItem StandHideChat;

        public static OptionItem FreeterHideChat;

        public static OptionItem RaccoonHideChat;



        public static OptionItem DisableTaskWin;



        public static OptionItem GhostOptions;

        public static OptionItem GhostCanSeeOtherRoles;

        public static OptionItem GhostCanSeeOtherTasks;

        public static OptionItem GhostCanSeeOtherVotes;

        public static OptionItem GhostCanSeeDeathReason;

        public static OptionItem GhostCanSeeKillerColor;

        public static OptionItem GhostIgnoreTasks;

        public static OptionItem GhostIgnoreTasksplayer;

        public static OptionItem GhostCanSeeAllTasks;

        public static OptionItem GhostCanSeeKillflash;

        public static OptionItem GhostCanSeeNumberOfButtonsOnOthers;



        public static OptionItem OptionBatchSetting;

        public static OptionItem OptionAllImpostorKillCool;

        public static OptionItem OptionAllNeutralKillCool;



        // プリセット対象外

        public static OptionItem NoGameEnd;

        public static OptionItem AutoDisplayLastResult;

        public static OptionItem AutoDisplayKillLog;

        public static OptionItem SuffixMode;

        public static OptionItem HideGameSettings;

        public static OptionItem HideSettingsDuringGame;

        public static OptionItem ChangeNameToRoleInfo;

        public static OptionItem RoleAssigningAlgorithm;

        public static OptionItem UseZoom;



        public static OptionItem ApplyDenyNameList;

        public static OptionItem KickPlayerFriendCodeNotExist;

        public static OptionItem ApplyBanList;

        public static OptionItem KiclHotNotFriend;

        public static OptionItem KickInitialName;

        public static OptionItem BANKickjoinplayer;



        public static readonly string[] suffixModes =

        {

            "SuffixMode.None",

            "SuffixMode.Version",

            "SuffixMode.Streaming",

            "SuffixMode.Recording",

            "SuffixMode.RoomHost",

            "SuffixMode.OriginalName",

            "SuffixMode.Timer"

        };

        public static readonly string[] RoleAssigningAlgorithms =

        {

            "RoleAssigningAlgorithm.Default",

            "RoleAssigningAlgorithm.NetRandom",

            "RoleAssigningAlgorithm.HashRandom",

            "RoleAssigningAlgorithm.Xorshift",

            "RoleAssigningAlgorithm.MersenneTwister",

        };

        public static SuffixModes GetSuffixMode()

        {

            return (SuffixModes)SuffixMode.GetValue();

        }



        public static int SnitchExposeTaskLeft = 1;



        public static bool IsLoaded = false;

        public static int GetRoleCount(CustomRoles role)

        {

            return GetRoleChance(role) == 0 ? 0 : CustomRoleCounts.TryGetValue(role, out var option) ? option.GetInt() : 0;

        }



        public static int GetRoleChance(CustomRoles role)

        {

            if (!Event.CheckRole(role)) return 0;



            if (CustomRoleSpawnChances.TryGetValue(role, out var option))

            {

                if (option.GetBool())

                    return option.GetInt();

            }

            return 0;

        }

        public static void Load()

        {

            if (IsLoaded) return;

            OptionSaver.Initialize();

            // プリセット

            PresetOptionItem.Preset = PresetOptionItem.Create(0, TabGroup.MainSettings)

                .SetColor(new Color32(204, 204, 0, 255))

                .SetHeader(true);



            // ゲームモード

            GameMode = StringOptionItem.Create(1, "GameMode", gameModes, 0, TabGroup.MainSettings, false)

                .SetHeader(true)

                .SetColor(ModColors.bluegreen)

                .SetTooltip(() => Translator.GetString($"GameModeInfo_{CurrentGameMode}"));



            #region 役職・詳細設定

            CustomRoleCounts = new();

            CustomRoleSpawnChances = new();



            var sortedRoleInfo = CustomRoleManager.AllRolesInfo.Values.OrderBy(role => role.ConfigId);

            // GM

            EnableGM = BooleanOptionItem.Create(100, "GM", false, TabGroup.MainSettings, false)

                .SetColor(UtilsRoleText.GetRoleColor(CustomRoles.GM))

                .SetHeader(true)

                .SetTooltip(() => Translator.GetString($"GM_Info"));



            // ゲームマスターがONの場合だけ、直下にGM詳細設定を表示する。

            OptionGMAutoChat = BooleanOptionItem.Create(1_300_280, "GMAutoChat", false, TabGroup.MainSettings, true)

                .SetParent(EnableGM)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "GM中会議スタート時チャットを開く");

            OptionGMAutoPossess = BooleanOptionItem.Create(1_300_290, "GMAutoPossess", false, TabGroup.MainSettings, true)

                .SetParent(EnableGM)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "GM中タスクターン中生存者の\n誰かに憑依する");

            OptionGMAutoPossessAliveOnly = BooleanOptionItem.Create(1_300_285, "OptionGMAutoPossessAliveOnly", true, TabGroup.MainSettings, true)

                .SetParent(OptionGMAutoPossess)

                .SetColorcode("#ff3300")

                .SetOptionName(() => "生存者のみに憑依する");

            OptionGMAutoPossessPreferImpostor = BooleanOptionItem.Create(1_300_284, "OptionGMAutoPossessPreferImpostor", false, TabGroup.MainSettings, true)

                .SetParent(OptionGMAutoPossessAliveOnly)

                .SetColorcode("#ff3300")

                .SetOptionName(() => "インポスターを優先的に憑依する");

            OptionGMHideTaskButton = BooleanOptionItem.Create(1_300_291, "GMHideTaskButton", false, TabGroup.MainSettings, true)

                .SetParent(EnableGM)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "GMのタスクボタンを非表示にする");

            OptionGMCanSeeRoles = BooleanOptionItem.Create(1_300_292, "GMCanSeeRoles", true, TabGroup.MainSettings, true)

                .SetParent(EnableGM)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "GMが他人の役職を見ることができる");

            OptionGMCanSeeTaskProgress = BooleanOptionItem.Create(1_300_293, "GMCanSeeTaskProgress", true, TabGroup.MainSettings, true)

                .SetParent(EnableGM)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "GMが他人のタスク進捗を見ることができる");

            OptionGMCanSeeVotes = BooleanOptionItem.Create(1_300_294, "GMCanSeeVotes", true, TabGroup.MainSettings, true)

                .SetParent(EnableGM)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "GMが他人の投票先を見ることができる");

            OptionGMCanSeeDeathReason = BooleanOptionItem.Create(1_300_295, "GMCanSeeDeathReason", true, TabGroup.MainSettings, true)

                .SetParent(EnableGM)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "GMが死因を見ることができる");

            OptionGMCanSeeOverallTaskProgress = BooleanOptionItem.Create(1_300_296, "GMCanSeeOverallTaskProgress", true, TabGroup.MainSettings, true)

                .SetParent(EnableGM)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "GMが全体のタスク進捗を見ることができる");

            OptionGMCanSeeEmergencyCount = BooleanOptionItem.Create(1_300_297, "GMCanSeeEmergencyCount", true, TabGroup.MainSettings, true)

                .SetParent(EnableGM)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "GMが他人の緊急ボタン残数を見ることができる");

            OptionGMCanSeeKillFlash = BooleanOptionItem.Create(1_300_298, "GMCanSeeKillFlash", true, TabGroup.MainSettings, true)

                .SetParent(EnableGM)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "GMがキルフラッシュを見ることができる");



            RoleAssignManager.SetupOptionItem();

            WinOption.SetupCustomOption();

            ObjectOptionitem.Create(1_000_124, "RoleOption", true, null, TabGroup.MainSettings).SetOptionName(() => "Role Setting").SetTag(CustomOptionTags.Role).SetEnabled(() => GameSettingMenuStartPatch.NowRoleTab is not CustomRoles.NotAssigned);

            //タスクバトル

            TaskBattle.SetupOptionItem();

            //最初のオプションのみここ

            SuddenDeathMode.CreateOption();

            MurderMystery.SetUpMurderMysteryOption();

            //ダミーハンター

            DummyHunter.SetupOptionItem();

            //DummyBattleRoyaleManager.SetupOptionItem();

            ObjectOptionitem.Create(1_000_121, "StandardHAS", true, null, TabGroup.MainSettings).SetOptionName(() => "Standard HAS").SetColorcode("#ecff41ff").SetTag(CustomOptionTags.StandardHAS);

            StandardHASWaitingTime = FloatOptionItem.Create(100007, "StandardHASWaitingTime", new(0f, 180f, 2.5f), 10f, TabGroup.MainSettings, false)

                .SetValueFormat(OptionFormat.Seconds).SetTag(CustomOptionTags.StandardHAS).SetHeader(true);

            // HideAndSeek

            ObjectOptionitem.Create(1_000_123, "StandardHAS", true, null, TabGroup.MainSettings).SetOptionName(() => "HideAndSeek").SetColorcode("#ff1919").SetTag(CustomOptionTags.HideAndSeek);

            SetupRoleOptions(112000, TabGroup.NeutralRoles, CustomRoles.HASFox, customGameMode: CustomGameMode.HideAndSeek);

            SetupRoleOptions(112100, TabGroup.NeutralRoles, CustomRoles.HASTroll, customGameMode: CustomGameMode.HideAndSeek);

            KillDelay = FloatOptionItem.Create(112200, "HideAndSeekWaitingTime", new(0f, 180f, 5f), 10f, TabGroup.MainSettings, false)

                .SetValueFormat(OptionFormat.Seconds)

                .SetHeader(true).SetTag(CustomOptionTags.HideAndSeek);

            IgnoreVent = BooleanOptionItem.Create(112002, "IgnoreVent", false, TabGroup.MainSettings, false)

                .SetTag(CustomOptionTags.HideAndSeek);



            //特殊モード

            ObjectOptionitem.Create(1_000_113, "GameOption", true, null, TabGroup.Game).SetOptionName(() => "Game").SetColorcode("#ea633eff");

            ONspecialMode = BooleanOptionItem.Create(100000, "ONspecialMode", false, TabGroup.Game, false)

                .SetHeader(true)

                .SetColorcode("#00c1ff");

            InsiderMode = BooleanOptionItem.Create(100001, "InsiderMode", false, TabGroup.Game, false).SetParent(ONspecialMode)

                .SetTag(CustomOptionTags.Standard)

                .SetTooltip(() => Translator.GetString("InsiderModeOptionInfo"));

            InsiderModeCanSeeTask = BooleanOptionItem.Create(200002, "InsiderModeCanSeeTask", false, TabGroup.Game, false).SetParent(InsiderMode);

            ColorNameMode = BooleanOptionItem.Create(100003, "ColorNameMode", false, TabGroup.Game, false).SetParent(ONspecialMode)

                .SetTooltip(() => Translator.GetString("ColorNameModeInfo")); ;

            CanSeeImpostorRole = BooleanOptionItem.Create(100004, "CanSeeImpostorRole", false, TabGroup.Game, false).SetParent(ONspecialMode)

                .SetTag(CustomOptionTags.Standard);

            AllPlayerSkinShuffle = BooleanOptionItem.Create(100005, "AllPlayerSkinShuffle", false, TabGroup.Game, false).SetParent(ONspecialMode)

                .SetEnabled(() => Event.April || Event.Special).SetInfo(Translator.GetString("AprilfoolOnly"));





            // 試験的機能

            ExperimentalMode = BooleanOptionItem.Create(105000, "ExperimentalMode", false, TabGroup.Game, false).SetColor(Palette.CrewmateSettingChangeText)

                .SetTag(CustomOptionTags.Standard);

            ExAftermeetingflash = BooleanOptionItem.Create(105001, "ExAftermeetingflash", false, TabGroup.Game, false).SetParent(ExperimentalMode)

                .SetTag(CustomOptionTags.Standard);

            ExHideChatCommand = BooleanOptionItem.Create(105002, "ExHideChatCommand", false, TabGroup.Game, false).SetParent(ExperimentalMode)

                .SetTag(CustomOptionTags.Standard).SetInfo(Translator.GetString("ExHideChatCommandInfo"));

            TeamHideChat = BooleanOptionItem.Create(105003, "TeamHideChat", false, TabGroup.Game, false)

                .SetTag(CustomOptionTags.Standard)

                .SetParent(ExHideChatCommand);

            ImpostorHideChat = BooleanOptionItem.Create(105004, "ImpostorHideChat", false, TabGroup.Game, false)

                .SetTag(CustomOptionTags.Standard)

                .SetColor(ModColors.ImpostorRed).SetParent(TeamHideChat);

            JackalHideChat = BooleanOptionItem.Create(105005, "JackalHideChat", false, TabGroup.Game, false)

                .SetTag(CustomOptionTags.Standard)

                .SetColor(UtilsRoleText.GetRoleColor(CustomRoles.Jackal)).SetParent(TeamHideChat);

            LoversHideChat = BooleanOptionItem.Create(105006, "LoversHideChat", false, TabGroup.Game, false)

                .SetTag(CustomOptionTags.Standard)

                .SetColor(UtilsRoleText.GetRoleColor(CustomRoles.Lovers)).SetParent(TeamHideChat);

            CupidHideChat = BooleanOptionItem.Create(125006, "CupidHideChat", false, TabGroup.Game, false)

                .SetTag(CustomOptionTags.Standard)

                .SetColor(UtilsRoleText.GetRoleColor(CustomRoles.Cupid)).SetParent(LoversHideChat);

            TwinsHideChat = BooleanOptionItem.Create(105007, "TwinsCanUseHideChet", false, TabGroup.Game, false)

                .SetTag(CustomOptionTags.Standard)

                .SetColor(UtilsRoleText.GetRoleColor(CustomRoles.Twins)).SetParent(TeamHideChat);

            ConnectingHideChat = BooleanOptionItem.Create(105008, "ConnectingHideChat", false, TabGroup.Game, false)

                .SetTag(CustomOptionTags.Standard)

                .SetColor(UtilsRoleText.GetRoleColor(CustomRoles.Connecting)).SetParent(TeamHideChat);

            OnmyojiHideChat = BooleanOptionItem.Create(105013, "OnmyojiHideChat", false, TabGroup.Game, false)

                .SetTag(CustomOptionTags.Standard)

                .SetColor(UtilsRoleText.GetRoleColor(CustomRoles.Onmyoji)).SetParent(TeamHideChat);

            PavlovHideChat = BooleanOptionItem.Create(155014, "PavlovHideChat", false, TabGroup.Game, false)

                .SetTag(CustomOptionTags.Standard)

                .SetColor(UtilsRoleText.GetRoleColor(CustomRoles.PavlovDog)).SetParent(TeamHideChat);

            StandHideChat = BooleanOptionItem.Create(155015, "StandHideChat", false, TabGroup.Game, false)

                .SetTag(CustomOptionTags.Standard)

                .SetColor(UtilsRoleText.GetRoleColor(CustomRoles.StandMaster)).SetParent(TeamHideChat);

            FreeterHideChat = BooleanOptionItem.Create(155016, "FreeterHideChat", false, TabGroup.Game, false)

                .SetTag(CustomOptionTags.Standard)

                .SetColor(UtilsRoleText.GetRoleColor(CustomRoles.Freeter)).SetParent(TeamHideChat);

            RaccoonHideChat = BooleanOptionItem.Create(155017, "RaccoonHideChat", false, TabGroup.Game, false)

                .SetTag(CustomOptionTags.Standard)

                .SetColor(UtilsRoleText.GetRoleColor(CustomRoles.RaccoonParent)).SetParent(TeamHideChat);

            ExRpcWeightR = BooleanOptionItem.Create(105009, "ExRpcWeightR", false, TabGroup.Game, false).SetParent(ExperimentalMode);

            ExCallMeetingBlackout = BooleanOptionItem.Create(105012, "ExCallMeetingBlackout", false, TabGroup.Game, false)

                .SetParent(ExperimentalMode)

                .SetInfo(Translator.GetString("ExCallMeetingBlackoutInfo"));

            ExIntroWeight = StringOptionItem.Create(105014, "ExIntroWeight", ["Weight_0", "Weight_1", "Weight_2"], 0, TabGroup.Game, false)

                .SetParent(ExperimentalMode)

                .SetInfo(Translator.GetString("ExIntroWeightInfo"));

            ExChatMonochrome = BooleanOptionItem.Create(105015, "ExChatMonochrome", false, TabGroup.Game, false)

                .SetParent(ExperimentalMode);



            //9人以上部屋で落ちる現象の対策

            FixSpawnPacketSize = BooleanOptionItem.Create(105010, "FixSpawnPacketSize", false, TabGroup.Game, true)

                .SetColor(new Color32(255, 255, 0, 255))

                .SetInfo(Translator.GetString("FixSpawnPacketSizeInfo"));



            // Impostor

            CreateRoleOption(sortedRoleInfo, CustomRoleTypes.Impostor);



            DoubleTriggerThreshold = FloatOptionItem.Create(102500, "DoubleTriggerThreashould", new(0.3f, 1f, 0.1f), 0.5f, TabGroup.ImpostorRoles, false)

                .SetHeader(true)

                .SetValueFormat(OptionFormat.Seconds).SetTooltip(() => Translator.GetString("DoubleTriggerThresholdInfo"));

            DefaultShapeshiftCooldown = FloatOptionItem.Create(102501, "DefaultShapeshiftCooldown", new(1f, 999f, 1f), 15f, TabGroup.ImpostorRoles, false)

                .SetHeader(true)

                .SetValueFormat(OptionFormat.Seconds);

            DefaultShapeshiftDuration = FloatOptionItem.Create(102502, "DefaultShapeshiftDuration", new(1, 300, 1f), 10, TabGroup.ImpostorRoles, false)

                .SetValueFormat(OptionFormat.Seconds);



            // Madmate, Crewmate, Neutral

            CreateRoleOption(sortedRoleInfo, CustomRoleTypes.Madmate);

            CreateRoleOption(sortedRoleInfo, CustomRoleTypes.Crewmate);

            DefaultEngineerCooldown = FloatOptionItem.Create(102503, "DefaultEngineerCooldown", new(0, 180, 1f), 15, TabGroup.CrewmateRoles, false)

                .SetHeader(true).SetValueFormat(OptionFormat.Seconds);

            DefaultEngineerInVentMaxTime = FloatOptionItem.Create(102504, "DefaultEngineerInVentMaxTime", new(0, 180, 1), 5, TabGroup.CrewmateRoles, false)

                .SetValueFormat(OptionFormat.Seconds).SetZeroNotation(OptionZeroNotation.Infinity);



            CreateRoleOption(sortedRoleInfo, CustomRoleTypes.Neutral);



            SetupRoleOptions(102800, TabGroup.Game, CustomRoles.NotAssigned, new(1, 1, 1));

            RoleAddAddons.Create(102810, TabGroup.Game, CustomRoles.NotAssigned);

            // Madmate Common Options

            CanMakeMadmateCount = IntegerOptionItem.Create(102000, "CanMakeMadmateCount", new(0, 15, 1), 0, TabGroup.MadmateRoles, false)

                .SetValueFormat(OptionFormat.Players)

                .SetHeader(true)

                .SetColor(Palette.ImpostorRed);

            SkMadCanUseVent = BooleanOptionItem.Create(102001, "SkMadCanUseVent", false, TabGroup.MadmateRoles, false)

                .SetParent(CanMakeMadmateCount);

            MadMateOption = BooleanOptionItem.Create(102002, "MadmateOption", false, TabGroup.MadmateRoles, false)

                .SetHeader(true)

                .SetColorcode("#ffa3a3");

            MadmateCanFixLightsOut = BooleanOptionItem.Create(102003, "MadmateCanFixLightsOut", false, TabGroup.MadmateRoles, false).SetColorcode("#ffcc66").SetParent(MadMateOption);

            MadmateCanFixComms = BooleanOptionItem.Create(102004, "MadmateCanFixComms", false, TabGroup.MadmateRoles, false).SetColorcode("#999999").SetParent(MadMateOption);

            MadmateHasLighting = BooleanOptionItem.Create(102005, "MadmateHasLighting", false, TabGroup.MadmateRoles, false).SetColorcode("#ec6800").SetParent(MadMateOption);

            MadmateHasMoon = BooleanOptionItem.Create(102006, "MadmateHasMoon", false, TabGroup.MadmateRoles, false).SetColorcode("#ffff33").SetParent(MadMateOption);



            MadmateCanSeeKillFlash = BooleanOptionItem.Create(102007, "MadmateCanSeeKillFlash", false, TabGroup.MadmateRoles, false).SetColorcode("#61b26c").SetParent(MadMateOption);

            MadmateCanSeeOtherVotes = BooleanOptionItem.Create(102008, "MadmateCanSeeOtherVotes", false, TabGroup.MadmateRoles, false).SetColorcode("#800080").SetParent(MadMateOption);

            MadmateCanSeeDeathReason = BooleanOptionItem.Create(102009, "MadmateCanSeeDeathReason", false, TabGroup.MadmateRoles, false).SetColorcode("#80ffdd").SetParent(MadMateOption);

            MadmateRevengePlayer = BooleanOptionItem.Create(102010, "MadmateExileCrewmate", false, TabGroup.MadmateRoles, false).SetColorcode("#00fa9a").SetParent(MadMateOption);

            MadmateRevengeCanImpostor = BooleanOptionItem.Create(102011, "NekoKabochaImpostorsGetRevenged", false, TabGroup.MadmateRoles, false).SetParent(MadmateRevengePlayer);

            MadmateRevengeCrewmate = BooleanOptionItem.Create(102012, "RevengeToCrewmate", true, TabGroup.MadmateRoles, false).SetParent(MadmateRevengePlayer);

            MadmateRevengeMadmate = BooleanOptionItem.Create(102013, "NekoKabochaMadmatesGetRevenged", true, TabGroup.MadmateRoles, false).SetParent(MadmateRevengePlayer);

            MadmateRevengeNeutral = BooleanOptionItem.Create(102014, "RevengeToNeutral", true, TabGroup.MadmateRoles, false).SetParent(MadmateRevengePlayer);

            MadCanSeeImpostor = BooleanOptionItem.Create(102015, "MadmateCanSeeImpostor", false, TabGroup.MadmateRoles, false).SetColor(UtilsRoleText.GetRoleColor(CustomRoles.Snitch)).SetParent(MadMateOption);

            MadmateTell = StringOptionItem.Create(102016, "MadmateTellOption", Tellopt, 0, TabGroup.MadmateRoles, false).SetColor(UtilsRoleText.GetRoleColor(CustomRoles.FortuneTeller)).SetParent(MadMateOption);



            MadmateVentCooldown = FloatOptionItem.Create(102017, "MadmateVentCooldown", new(0f, 180f, 0.5f), 0f, TabGroup.MadmateRoles, false).SetColorcode("#8cffff").SetParent(MadMateOption)

                .SetHeader(true)

                .SetValueFormat(OptionFormat.Seconds);

            MadmateVentMaxTime = FloatOptionItem.Create(102018, "MadmateVentMaxTime", new(0f, 180f, 0.5f), 0f, TabGroup.MadmateRoles, false).SetZeroNotation(OptionZeroNotation.Infinity).SetColorcode("#8cffff").SetParent(MadMateOption)

                .SetValueFormat(OptionFormat.Seconds);

            MadmateCanMovedByVent = BooleanOptionItem.Create(102019, "MadmateCanMovedByVent", true, TabGroup.MadmateRoles, false).SetColorcode("#8cffff").SetParent(MadMateOption);



            //Com



            ObjectOptionitem.Create(1_000_115, "Group-Addon", true, null, TabGroup.Combinations).SetOptionName(() => "Combi Add-on").SetColor(ModColors.AddonsColor).SetTag(CustomOptionTags.Role);

            Faction.SetUpOption();

            Twins.SetUpTwinsOptions();

            Triplets.SetUpTripletsOptions();

            Lovers.SetLoversOptions();

            GhostRoleCore.SetupCustomOptionAddonAndIsGhostRole();



            SlotRoleAssign.SetupOptionItem();

            //幽霊役職の設定

            GhostRoleOption = BooleanOptionItem.Create(106000, "GhostRoleOptions", false, TabGroup.GhostRoles, false)

                .SetHeader(true)

                .SetColorcode("#666699");

            GhostRoleCanSeeOtherRoles = BooleanOptionItem.Create(106001, "GhostRoleCanSeeOtherRoles", false, TabGroup.GhostRoles, false)

                .SetColorcode("#7474ab")

                .SetParent(GhostRoleOption);

            GhostRoleCanSeeOtherTasks = BooleanOptionItem.Create(106002, "GhostRoleCanSeeOtherTasks", false, TabGroup.GhostRoles, false)

                .SetColor(Color.yellow)

                .SetParent(GhostRoleOption);

            GhostRoleCanSeeOtherVotes = BooleanOptionItem.Create(106003, "GhostRoleCanSeeOtherVotes", false, TabGroup.GhostRoles, false)

                .SetColorcode("#800080")

                .SetParent(GhostRoleOption);

            GhostRoleCanSeeDeathReason = BooleanOptionItem.Create(106004, "GhostRoleCanSeeDeathReason", false, TabGroup.GhostRoles, false)

                .SetColorcode("#80ffdd")

                .SetParent(GhostRoleOption);

            GhostRoleCanSeeKillerColor = BooleanOptionItem.Create(106005, "GhostRoleCanSeeKillerColor", false, TabGroup.GhostRoles, false)

                .SetColorcode("#80ffdd")

                .SetParent(GhostRoleCanSeeDeathReason);

            GhostRoleCanSeeAllTasks = BooleanOptionItem.Create(106006, "GhostRoleCanSeeAllTasks", false, TabGroup.GhostRoles, false)

                .SetColorcode("#cee4ae")

                .SetParent(GhostRoleOption);

            GhostRoleCanSeeNumberOfButtonsOnOthers = BooleanOptionItem.Create(106007, "GhostRoleCanSeeNumberOfButtonsOnOthers", false, TabGroup.GhostRoles, false)

                .SetColorcode("#d7c447")

                .SetParent(GhostRoleOption);

            GhostRoleCanSeeKillflash = BooleanOptionItem.Create(106008, "GhostRoleCanSeeKillflash", false, TabGroup.GhostRoles, false)

                .SetColorcode("#61b26c")

                .SetParent(GhostRoleOption);

            #endregion



            KillFlashDuration = FloatOptionItem.Create(90000, "KillFlashDuration", new(0.1f, 0.45f, 0.05f), 0.3f, TabGroup.Game, false)

                .SetHeader(true)

                .SetValueFormat(OptionFormat.Seconds)

                .SetColorcode("#bf483f");



            // マップ改造

            MapModification = BooleanOptionItem.Create(107000, "MapModification", false, TabGroup.Game, false)

                .SetHeader(true)

                .SetColorcode("#ccff66").SetTag(CustomOptionTags.GameOption);

            AirShipVariableElectrical = BooleanOptionItem.Create(107001, "AirShipVariableElectrical", false, TabGroup.Game, false).SetParent(MapModification)

                .SetEnabled(() => IsActiveAirship);

            AirShipPlatform = StringOptionItem.Create(107002, "AirShipPlatform", PlatformOption, 0, TabGroup.Game, false).SetParent(MapModification)

                .SetEnabled(() => IsActiveAirship);

            DisableAirshipMovingPlatform = BooleanOptionItem.Create(107003, "DisableAirshipMovingPlatform", false, TabGroup.Game, false).SetParent(MapModification)

                .SetEnabled(() => IsActiveAirship);

            DisableFungleSporeTrigger = BooleanOptionItem.Create(107004, "DisableFungleSporeTrigger", false, TabGroup.Game, false).SetParent(MapModification)

                .SetEnabled(() => IsActiveFungle);

            CantUseZipLineTotop = BooleanOptionItem.Create(107005, "CantUseZipLineTotop", false, TabGroup.Game, false).SetParent(MapModification)

                .SetEnabled(() => IsActiveFungle);

            CantUseZipLineTodown = BooleanOptionItem.Create(107006, "CantUseZipLineTodown", false, TabGroup.Game, false).SetParent(MapModification)

                .SetEnabled(() => IsActiveFungle);

            ResetDoorsEveryTurns = BooleanOptionItem.Create(107007, "ResetDoorsEveryTurns", false, TabGroup.Game, false).SetParent(MapModification)

                .SetEnabled(() => IsActiveAirship || IsActiveFungle || IsActivePolus);

            DoorsResetMode = StringOptionItem.Create(107008, "DoorsResetMode", EnumHelper.GetAllNames<DoorsReset.ResetMode>(), 0, TabGroup.Game, false).SetParent(ResetDoorsEveryTurns);

            CantUseVentMode = BooleanOptionItem.Create(107009, "Can'tUseVent", false, TabGroup.Game, false).SetParent(MapModification);

            CantUseVentTrueCount = IntegerOptionItem.Create(107010, "CantUseVentTrueCount", new(1, 15, 1), 5, TabGroup.Game, false).SetValueFormat(OptionFormat.Players).SetParent(CantUseVentMode);

            MaxInVentMode = BooleanOptionItem.Create(107011, "MaxInVentMode", false, TabGroup.Game, false).SetParent(MapModification);

            MaxInVentTime = FloatOptionItem.Create(107012, "MaxInVentTime", new(3f, 300, 0.5f), 30f, TabGroup.Game, false).SetValueFormat(OptionFormat.Seconds).SetParent(MaxInVentMode);



            //タスク設定

            TaskOption = BooleanOptionItem.Create(107199, "TaskOption", false, TabGroup.Game, false)

                .SetTag(CustomOptionTags.GameOption)

                .SetColorcode("#eada2eff")

                .SetHeader(true);

            // タスク無効化

            UploadDataIsLongTask = BooleanOptionItem.Create(107200, "UploadDataIsLongTask", false, TabGroup.Game, false).SetParent(TaskOption);

            DisableTasks = BooleanOptionItem.Create(107201, "DisableTasks", false, TabGroup.Game, false).SetParent(TaskOption).SetColorcode("#6b6b6b");

            DisableSwipeCard = BooleanOptionItem.Create(107202, "DisableSwipeCardTask", false, TabGroup.Game, false).SetParent(DisableTasks);

            DisableSubmitScan = BooleanOptionItem.Create(107203, "DisableSubmitScanTask", false, TabGroup.Game, false).SetParent(DisableTasks);

            DisableUnlockSafe = BooleanOptionItem.Create(107204, "DisableUnlockSafeTask", false, TabGroup.Game, false).SetParent(DisableTasks);

            DisableUploadData = BooleanOptionItem.Create(107205, "DisableUploadDataTask", false, TabGroup.Game, false).SetParent(DisableTasks);

            DisableStartReactor = BooleanOptionItem.Create(107206, "DisableStartReactorTask", false, TabGroup.Game, false).SetParent(DisableTasks);

            DisableResetBreaker = BooleanOptionItem.Create(107207, "DisableResetBreakerTask", false, TabGroup.Game, false).SetParent(DisableTasks);

            DisableCatchFish = BooleanOptionItem.Create(107208, "DisableCatchFish", false, TabGroup.Game, false).SetParent(DisableTasks);

            DisableDivertPower = BooleanOptionItem.Create(107209, "DisableDivertPower", false, TabGroup.Game, false).SetParent(DisableTasks);

            DisableFuelEngins = BooleanOptionItem.Create(107210, "DisableFuelEngins", false, TabGroup.Game, false).SetParent(DisableTasks);

            DisableInspectSample = BooleanOptionItem.Create(107211, "DisableInspectSample", false, TabGroup.Game, false).SetParent(DisableTasks);

            DisableRebootWifi = BooleanOptionItem.Create(107212, "DisableRebootWifi", false, TabGroup.Game, false).SetParent(DisableTasks);

            DisableInseki = BooleanOptionItem.Create(107213, "DisableInseki", false, TabGroup.Game, false).SetParent(DisableTasks);

            DisableCalibrateDistributor = BooleanOptionItem.Create(107214, "DisableCalibrateDistributor", false, TabGroup.Game, false).SetParent(DisableTasks);

            DisableVentCleaning = BooleanOptionItem.Create(107215, "DisableVentCleaning", false, TabGroup.Game, false).SetParent(DisableTasks);

            DisableHelpCritter = BooleanOptionItem.Create(107216, "DisableHelpCritter", false, TabGroup.Game, false).SetParent(DisableTasks);

            Disablefixwiring = BooleanOptionItem.Create(107217, "Disablefixwiring", false, TabGroup.Game, false).SetParent(DisableTasks);

            DisableFixWeatherNode = BooleanOptionItem.Create(107218, "DisableFixWeatherNodeTask", false, TabGroup.Game, false).SetParent(DisableTasks);



            //デバイス設定

            DevicesOption = BooleanOptionItem.Create(104000, "DevicesOption", false, TabGroup.Game, false)

                .SetHeader(true)

                .SetColorcode("#d860e0").SetTag(CustomOptionTags.GameOption); ;

            DisableDevices = BooleanOptionItem.Create(104001, "DisableDevices", false, TabGroup.Game, false).SetParent(DevicesOption)

                .SetColorcode("#00ff99");

            DisableSkeldDevices = BooleanOptionItem.Create(104002, "DisableSkeldDevices", false, TabGroup.Game, false).SetParent(DisableDevices)

                .SetEnabled(() => IsActiveSkeld);

            DisableSkeldAdmin = BooleanOptionItem.Create(104003, "DisableSkeldAdmin", false, TabGroup.Game, false).SetParent(DisableSkeldDevices)

                .SetColorcode("#00ff99");

            DisableSkeldCamera = BooleanOptionItem.Create(104004, "DisableSkeldCamera", false, TabGroup.Game, false).SetParent(DisableSkeldDevices)

                .SetColorcode("#cccccc");

            DisableMiraHQDevices = BooleanOptionItem.Create(104005, "DisableMiraHQDevices", false, TabGroup.Game, false).SetParent(DisableDevices)

                .SetEnabled(() => IsActiveMiraHQ);

            DisableMiraHQAdmin = BooleanOptionItem.Create(104006, "DisableMiraHQAdmin", false, TabGroup.Game, false).SetParent(DisableMiraHQDevices)

                .SetColorcode("#00ff99");

            DisableMiraHQDoorLog = BooleanOptionItem.Create(104007, "DisableMiraHQDoorLog", false, TabGroup.Game, false).SetParent(DisableMiraHQDevices)

                .SetColorcode("#cccccc");

            DisablePolusDevices = BooleanOptionItem.Create(104008, "DisablePolusDevices", false, TabGroup.Game, false).SetParent(DisableDevices)

                .SetEnabled(() => IsActivePolus);

            DisablePolusAdmin = BooleanOptionItem.Create(104009, "DisablePolusAdmin", false, TabGroup.Game, false).SetParent(DisablePolusDevices)

                .SetColorcode("#00ff99");

            DisablePolusCamera = BooleanOptionItem.Create(104010, "DisablePolusCamera", false, TabGroup.Game, false).SetParent(DisablePolusDevices)

                .SetColorcode("#cccccc");

            DisablePolusVital = BooleanOptionItem.Create(104011, "DisablePolusVital", false, TabGroup.Game, false).SetParent(DisablePolusDevices)

                .SetColorcode("#33ccff");

            DisableAirshipDevices = BooleanOptionItem.Create(104012, "DisableAirshipDevices", false, TabGroup.Game, false).SetParent(DisableDevices)

                .SetEnabled(() => IsActiveAirship);

            DisableAirshipCockpitAdmin = BooleanOptionItem.Create(104013, "DisableAirshipCockpitAdmin", false, TabGroup.Game, false).SetParent(DisableAirshipDevices)

                .SetColorcode("#00ff99");

            DisableAirshipRecordsAdmin = BooleanOptionItem.Create(104014, "DisableAirshipRecordsAdmin", false, TabGroup.Game, false).SetParent(DisableAirshipDevices)

                .SetColorcode("#00ff99");

            DisableAirshipCamera = BooleanOptionItem.Create(104015, "DisableAirshipCamera", false, TabGroup.Game, false).SetParent(DisableAirshipDevices)

                .SetColorcode("#cccccc");

            DisableAirshipVital = BooleanOptionItem.Create(104016, "DisableAirshipVital", false, TabGroup.Game, false).SetParent(DisableAirshipDevices)

                .SetColorcode("#33ccff");

            DisableFungleDevices = BooleanOptionItem.Create(104017, "DisableFungleDevices", false, TabGroup.Game, false).SetParent(DisableDevices)

                .SetEnabled(() => IsActiveFungle);

            DisableFungleVital = BooleanOptionItem.Create(104018, "DisableFungleVital", false, TabGroup.Game, false).SetParent(DisableFungleDevices)

                .SetColorcode("#33ccff");



            DisableDevicesIgnoreConditions = BooleanOptionItem.Create(104100, "IgnoreConditions", false, TabGroup.Game, false).SetParent(DisableDevices);

            DisableDevicesIgnoreImpostors = BooleanOptionItem.Create(104101, "IgnoreImpostors", false, TabGroup.Game, false).SetParent(DisableDevicesIgnoreConditions)

                .SetColorcode("#ff1919");

            DisableDevicesIgnoreMadmates = BooleanOptionItem.Create(104102, "IgnoreMadmates", false, TabGroup.Game, false).SetParent(DisableDevicesIgnoreConditions)

                .SetColorcode("#ff7f50");

            DisableDevicesIgnoreNeutrals = BooleanOptionItem.Create(104103, "IgnoreNeutrals", false, TabGroup.Game, false).SetParent(DisableDevicesIgnoreConditions)

                .SetColorcode("#808080");

            DisableDevicesIgnoreCrewmates = BooleanOptionItem.Create(104104, "IgnoreCrewmates", false, TabGroup.Game, false).SetParent(DisableDevicesIgnoreConditions)

                .SetColorcode("#8cffff");

            DisableDevicesIgnoreCompleteTask = BooleanOptionItem.Create(104106, "DisableDevicesIgnoreComleteTask", false, TabGroup.Game, false).SetParent(DisableDevicesIgnoreCrewmates)

                .SetColorcode("#00ffff");

            DisableDevicesIgnoreAfterAnyoneDied = BooleanOptionItem.Create(104105, "IgnoreAfterAnyoneDied", false, TabGroup.Game, false).SetParent(DisableDevicesIgnoreConditions)

                .SetColorcode("#666699");

            DisableForceRecordsAdomin = BooleanOptionItem.Create(104107, "DisableForceRecordsAdomin", true, TabGroup.Game, false).SetParent(DisableDevicesIgnoreConditions)

                .SetColorcode("#00ff99");



            TimeLimitDevices = BooleanOptionItem.Create(104200, "TimeLimitDevices", false, TabGroup.Game, false)

                .SetColorcode("#948e50")

                .SetParent(DevicesOption);

            TimeLimitAdmin = FloatOptionItem.Create(104201, "TimeLimitAdmin", new(-1f, 300f, 1), 20f, TabGroup.Game, false)

                .SetColorcode("#00ff99").SetValueFormat(OptionFormat.Seconds).SetZeroNotation(OptionZeroNotation.Infinity).SetParent(TimeLimitDevices);

            TimeLimitCamAndLog = FloatOptionItem.Create(104202, "TimeLimitCamAndLog", new(-1f, 300f, 1), 20f, TabGroup.Game, false)

                .SetColorcode("#cccccc").SetValueFormat(OptionFormat.Seconds).SetZeroNotation(OptionZeroNotation.Infinity).SetParent(TimeLimitDevices);

            TimeLimitVital = FloatOptionItem.Create(104203, "TimeLimitVital", new(-1f, 300f, 1), 20f, TabGroup.Game, false)

                .SetColorcode("#33ccff").SetValueFormat(OptionFormat.Seconds).SetZeroNotation(OptionZeroNotation.Infinity).SetParent(TimeLimitDevices);

            CanSeeTimeLimit = BooleanOptionItem.Create(104204, "CanSeeTimeLimit", false, TabGroup.Game, false)

                .SetColorcode("#cc8b60").SetParent(TimeLimitDevices);

            CanseeImpTimeLimit = BooleanOptionItem.Create(104205, "CanseeImpTimeLimit", false, TabGroup.Game, false)

            .SetColor(ModColors.ImpostorRed).SetParent(CanSeeTimeLimit);

            CanseeMadTimeLimit = BooleanOptionItem.Create(104206, "CanseeMadTimeLimit", false, TabGroup.Game, false)

            .SetColor(ModColors.MadMateOrenge).SetParent(CanSeeTimeLimit);

            CanseeCrewTimeLimit = BooleanOptionItem.Create(104207, "CanseeCrewTimeLimit", false, TabGroup.Game, false)

            .SetColor(ModColors.CrewMateBlue).SetParent(CanSeeTimeLimit);

            CanseeNeuTimeLimit = BooleanOptionItem.Create(104208, "CanseeNeuTimeLimit", false, TabGroup.Game, false)

            .SetColor(Palette.DisabledGrey).SetParent(CanSeeTimeLimit);

            ReviveTimelimitplayercount = IntegerOptionItem.Create(104210, "ReviveTimelimitplayercount", new(0, 15, 1), 0, TabGroup.Game, false)

                .SetParent(TimeLimitDevices).SetValueFormat(OptionFormat.Players).SetZeroNotation(OptionZeroNotation.Off).SetColor(ModColors.Tan).SetTooltip(() => Translator.GetString("ReviveTimelimitplayercount_Info"));

            ReviveAddAdmin = FloatOptionItem.Create(104211, "ReviveAddAdmin", new(-100, 100, 1), 20, TabGroup.Game, false).SetColorcode("#00ff99").SetValueFormat(OptionFormat.Seconds).SetParent(ReviveTimelimitplayercount);

            ReviveAddCamAndLog = FloatOptionItem.Create(104212, "ReviveAddCamAndLog", new(-100, 100, 1), 20, TabGroup.Game, false).SetColorcode("#cccccc").SetValueFormat(OptionFormat.Seconds).SetParent(ReviveTimelimitplayercount);

            ReviveAddVital = FloatOptionItem.Create(104213, "ReviveAddVital", new(-100, 100, 1), 20, TabGroup.Game, false).SetColorcode("#33ccff").SetValueFormat(OptionFormat.Seconds).SetParent(ReviveTimelimitplayercount);



            TurnTimeLimitDevice = BooleanOptionItem.Create(104300, "TurnTimeLimitDevice", false, TabGroup.Game, false)

                .SetColorcode("#b06927")

                .SetParent(DevicesOption);

            TurnTimeLimitAdmin = FloatOptionItem.Create(104301, "TimeLimitAdmin", new(0f, 300f, 1), 20f, TabGroup.Game, false)

                .SetColorcode("#00ff99").SetValueFormat(OptionFormat.Seconds).SetZeroNotation(OptionZeroNotation.Infinity).SetParent(TurnTimeLimitDevice);

            TurnTimeLimitCamAndLog = FloatOptionItem.Create(104302, "TimeLimitCamAndLog", new(0f, 300f, 1), 20f, TabGroup.Game, false)

                .SetColorcode("#cccccc").SetValueFormat(OptionFormat.Seconds).SetZeroNotation(OptionZeroNotation.Infinity).SetParent(TurnTimeLimitDevice);

            TurnTimeLimitVital = FloatOptionItem.Create(104303, "TimeLimitVital", new(0f, 300f, 1), 20f, TabGroup.Game, false)

                .SetColorcode("#33ccff").SetValueFormat(OptionFormat.Seconds).SetZeroNotation(OptionZeroNotation.Infinity).SetParent(TurnTimeLimitDevice);



            //サボ

            Sabotage = BooleanOptionItem.Create(108000, "Sabotage", false, TabGroup.Game, false)

                .SetHeader(true)

                .SetColorcode("#b71e1e")

                .SetTag(CustomOptionTags.Standard).SetDisableTag([CustomOptionTags.SuddenDeath, CustomOptionTags.StandardHAS, CustomOptionTags.MurderMystery]);

            // リアクターの時間制御

            SabotageActivetimerControl = BooleanOptionItem.Create(108001, "SabotageActivetimerControl", false, TabGroup.Game, false).SetParent(Sabotage)

                .SetColorcode("#f22c50");

            SkeldReactorTimeLimit = FloatOptionItem.Create(108002, "SkeldReactorTimeLimit", new(1f, 90f, 1f), 30f, TabGroup.Game, false).SetParent(SabotageActivetimerControl)

                .SetValueFormat(OptionFormat.Seconds)

                .SetEnabled(() => IsActiveSkeld);

            SkeldO2TimeLimit = FloatOptionItem.Create(108003, "SkeldO2TimeLimit", new(1f, 90f, 1f), 30f, TabGroup.Game, false).SetParent(SabotageActivetimerControl)

                .SetValueFormat(OptionFormat.Seconds)

                .SetEnabled(() => IsActiveSkeld);

            MiraReactorTimeLimit = FloatOptionItem.Create(108004, "MiraReactorTimeLimit", new(1f, 90f, 1f), 30f, TabGroup.Game, false).SetParent(SabotageActivetimerControl)

                .SetValueFormat(OptionFormat.Seconds)

                .SetEnabled(() => IsActiveMiraHQ);

            MiraO2TimeLimit = FloatOptionItem.Create(108005, "MiraO2TimeLimit", new(1f, 90f, 1f), 30f, TabGroup.Game, false).SetParent(SabotageActivetimerControl)

                .SetValueFormat(OptionFormat.Seconds)

                .SetEnabled(() => IsActiveMiraHQ);

            PolusReactorTimeLimit = FloatOptionItem.Create(108006, "PolusReactorTimeLimit", new(1f, 90f, 1f), 30f, TabGroup.Game, false).SetParent(SabotageActivetimerControl)

                .SetValueFormat(OptionFormat.Seconds)

                .SetEnabled(() => IsActivePolus);

            AirshipReactorTimeLimit = FloatOptionItem.Create(108007, "AirshipReactorTimeLimit", new(1f, 90f, 1f), 60f, TabGroup.Game, false).SetParent(SabotageActivetimerControl)

                .SetValueFormat(OptionFormat.Seconds)

                .SetEnabled(() => IsActiveAirship);

            FungleReactorTimeLimit = FloatOptionItem.Create(108008, "FungleReactorTimeLimit", new(1f, 90f, 1f), 60f, TabGroup.Game, false).SetParent(SabotageActivetimerControl)

                .SetValueFormat(OptionFormat.Seconds)

                .SetEnabled(() => IsActiveFungle);

            FungleMushroomMixupDuration = FloatOptionItem.Create(108009, "FungleMushroomMixupDuration", new(1f, 20f, 1f), 10f, TabGroup.Game, false).SetParent(SabotageActivetimerControl)

                .SetValueFormat(OptionFormat.Seconds)

                .SetEnabled(() => IsActiveFungle);

            // 他

            ChangeSabotageWinRole = BooleanOptionItem.Create(108100, "ChangeSabotageWinRole", false, TabGroup.Game, false).SetParent(Sabotage);

            OptionSabotageFinAllKill = BooleanOptionItem.Create(10815, "OptionSabotageFinAllKill", false, TabGroup.Game, false).SetParent(Sabotage);

            // サボタージュのクールダウン変更

            ModifySabotageCooldown = BooleanOptionItem.Create(108101, "ModifySabotageCooldown", false, TabGroup.Game, false).SetParent(Sabotage);

            SabotageCooldown = FloatOptionItem.Create(108102, "SabotageCooldown", new(1f, 60f, 1f), 30f, TabGroup.Game, false).SetParent(ModifySabotageCooldown)

                .SetValueFormat(OptionFormat.Seconds);



            CommsSpecialSettings = BooleanOptionItem.Create(108103, "CommsSpecialSettings", false, TabGroup.Game, false).SetParent(Sabotage)

                .SetColorcode("#999999");

            CommsDonttouch = BooleanOptionItem.Create(108104, "CommsDonttouch", false, TabGroup.Game, false).SetParent(CommsSpecialSettings);

            CommsDonttouchTime = FloatOptionItem.Create(108105, "CommsDonttouchTime", new(0f, 180f, 0.5f), 3.0f, TabGroup.Game, false).SetParent(CommsDonttouch)

                .SetValueFormat(OptionFormat.Seconds);

            CommsCamouflage = BooleanOptionItem.Create(108106, "CommsCamouflage", false, TabGroup.Game, false).SetParent(CommsSpecialSettings).SetEnabled(() => false);



            // 停電の特殊設定

            LightsOutSpecialSettings = BooleanOptionItem.Create(108107, "LightsOutSpecialSettings", false, TabGroup.Game, false).SetParent(Sabotage)

                .SetColorcode("#ffcc66");

            LightOutDonttouch = BooleanOptionItem.Create(108108, "LightOutDonttouch", false, TabGroup.Game, false).SetParent(LightsOutSpecialSettings)

                .SetValueFormat(OptionFormat.Seconds);

            LightOutDonttouchTime = FloatOptionItem.Create(108109, "LightOutDonttouchTime", new(0f, 180f, 0.5f), 3.0f, TabGroup.Game, false).SetParent(LightOutDonttouch)

            .SetValueFormat(OptionFormat.Seconds);

            DisableAirshipViewingDeckLightsPanel = BooleanOptionItem.Create(108110, "DisableAirshipViewingDeckLightsPanel", false, TabGroup.Game, false).SetParent(LightsOutSpecialSettings)

                .SetEnabled(() => IsActiveAirship);

            DisableAirshipGapRoomLightsPanel = BooleanOptionItem.Create(108111, "DisableAirshipGapRoomLightsPanel", false, TabGroup.Game, false).SetParent(LightsOutSpecialSettings)

                .SetEnabled(() => IsActiveAirship);

            DisableAirshipCargoLightsPanel = BooleanOptionItem.Create(108112, "DisableAirshipCargoLightsPanel", false, TabGroup.Game, false).SetParent(LightsOutSpecialSettings)

                .SetEnabled(() => IsActiveAirship);

            BlockDisturbancesToSwitches = BooleanOptionItem.Create(108113, "BlockDisturbancesToSwitches", false, TabGroup.Game, false).SetParent(LightsOutSpecialSettings)

                .SetEnabled(() => IsActiveAirship);

            AllowCloseDoors = BooleanOptionItem.Create(108114, "AllowCloseDoors", false, TabGroup.Game, false)

                .SetParent(Sabotage);



            // ランダムマップ

            RandomMapsMode = BooleanOptionItem.Create(108700, "RandomMapsMode", false, TabGroup.Game, false)

                .SetHeader(true)

                .SetColorcode("#ffcc66");

            AddedTheSkeld = BooleanOptionItem.Create(108701, "AddedTheSkeld", false, TabGroup.Game, false).SetParent(RandomMapsMode)

                .SetColorcode("#666666");

            AddedMiraHQ = BooleanOptionItem.Create(108702, "AddedMIRAHQ", false, TabGroup.Game, false).SetParent(RandomMapsMode)

                .SetColorcode("#ff6633");

            AddedPolus = BooleanOptionItem.Create(108703, "AddedPolus", false, TabGroup.Game, false).SetParent(RandomMapsMode)

                .SetColorcode("#980098");

            AddedTheAirShip = BooleanOptionItem.Create(108704, "AddedTheAirShip", false, TabGroup.Game, false).SetParent(RandomMapsMode)

                .SetColorcode("#ff3300");

            AddedTheFungle = BooleanOptionItem.Create(108705, "AddedTheFungle", false, TabGroup.Game, false).SetParent(RandomMapsMode)

                .SetColorcode("#ff9900");



            // ランダムスポーン

            EnableRandomSpawn = BooleanOptionItem.Create(101300, "RandomSpawn", false, TabGroup.Game, false)

                .SetHeader(true)

                .SetColorcode("#ff99cc");

            CanSeeNextRandomSpawn = BooleanOptionItem.Create(101301, "CanSeeNextRandomSpawn", false, TabGroup.Game, false).SetParent(EnableRandomSpawn);

            RandomSpawn.SetupCustomOption();



            //会議設定

            MeetingAndVoteOpt = BooleanOptionItem.Create(109000, "MeetingAndVoteOpt", false, TabGroup.Game, false)

                .SetTag(CustomOptionTags.Standard)

                .SetDisableTag([CustomOptionTags.SuddenDeath, CustomOptionTags.StandardHAS])

                .SetColorcode("#64ff0a")

                .SetHeader(true);

            LowerLimitVotingTime = FloatOptionItem.Create(109001, "LowerLimitVotingTime", new(5f, 300f, 1f), 60f, TabGroup.Game, false)

                .SetValueFormat(OptionFormat.Seconds)

                .SetParent(MeetingAndVoteOpt);

            MeetingTimeLimit = FloatOptionItem.Create(109002, "LimitMeetingTime", new(5f, 300f, 1f), 300f, TabGroup.Game, false)

                .SetValueFormat(OptionFormat.Seconds)

                .SetParent(MeetingAndVoteOpt);

            // 全員生存時の会議時間

            AllAliveMeeting = BooleanOptionItem.Create(109003, "AllAliveMeeting", false, TabGroup.Game, false)

                .SetParent(MeetingAndVoteOpt);

            AllAliveMeetingTime = FloatOptionItem.Create(109004, "AllAliveMeetingTime", new(1f, 300f, 1f), 10f, TabGroup.Game, false).SetParent(AllAliveMeeting)

                .SetValueFormat(OptionFormat.Seconds);

            // 投票モード

            VoteMode = BooleanOptionItem.Create(109005, "VoteMode", false, TabGroup.Game, false)

                .SetColorcode("#33ff99")

                .SetParent(MeetingAndVoteOpt);

            WhenSkipVote = StringOptionItem.Create(109006, "WhenSkipVote", voteModes[0..3], 0, TabGroup.Game, false).SetParent(VoteMode);

            WhenSkipVoteIgnoreFirstMeeting = BooleanOptionItem.Create(109007, "WhenSkipVoteIgnoreFirstMeeting", false, TabGroup.Game, false).SetParent(WhenSkipVote);

            WhenSkipVoteIgnoreNoDeadBody = BooleanOptionItem.Create(109008, "WhenSkipVoteIgnoreNoDeadBody", false, TabGroup.Game, false).SetParent(WhenSkipVote);

            WhenSkipVoteIgnoreEmergency = BooleanOptionItem.Create(109009, "WhenSkipVoteIgnoreEmergency", false, TabGroup.Game, false).SetParent(WhenSkipVote);

            WhenNonVote = StringOptionItem.Create(109010, "WhenNonVote", voteModes, 0, TabGroup.Game, false).SetParent(VoteMode);

            WhenTie = StringOptionItem.Create(109011, "WhenTie", tieModes, 0, TabGroup.Game, false).SetParent(VoteMode);

            SyncButtonMode = BooleanOptionItem.Create(109012, "SyncButtonMode", false, TabGroup.Game, false)

                .SetParent(MeetingAndVoteOpt)

                .SetColorcode("#64ff0a");

            SyncedButtonCount = IntegerOptionItem.Create(109013, "SyncedButtonCount", new(0, 100, 1), 10, TabGroup.Game, false).SetParent(SyncButtonMode)

                .SetValueFormat(OptionFormat.Times);

            // 生存人数ごとの緊急会議

            AdditionalEmergencyCooldown = BooleanOptionItem.Create(109014, "AdditionalEmergencyCooldown", false, TabGroup.Game, false).SetParent(MeetingAndVoteOpt);

            AdditionalEmergencyCooldownThreshold = IntegerOptionItem.Create(109015, "AdditionalEmergencyCooldownThreshold", new(1, 15, 1), 1, TabGroup.Game, false).SetParent(AdditionalEmergencyCooldown)

                .SetValueFormat(OptionFormat.Players);

            AdditionalEmergencyCooldownTime = FloatOptionItem.Create(109016, "AdditionalEmergencyCooldownTime", new(1f, 60f, 1f), 1f, TabGroup.Game, false).SetParent(AdditionalEmergencyCooldown)

                .SetValueFormat(OptionFormat.Seconds);

            ShowVoteResult = BooleanOptionItem.Create(109017, "ShowVoteResult", false, TabGroup.Game, false).SetParent(MeetingAndVoteOpt);

            ShowVoteJudgment = StringOptionItem.Create(109018, "ShowVoteJudgment", ShowVoteJudgments, 0, TabGroup.Game, false).SetParent(ShowVoteResult);



            // 転落死

            LadderDeath = BooleanOptionItem.Create(109900, "LadderDeath", false, TabGroup.Game, false)

                .SetHeader(true)

                .SetColorcode("#ffcc00").SetTag(CustomOptionTags.GameOption).SetDisableTag([CustomOptionTags.TaskBattle]);

            LadderDeathChance = StringOptionItem.Create(109901, "LadderDeathChance", rates[1..], 0, TabGroup.Game, false).SetParent(LadderDeath);

            LadderDeathNuuun = BooleanOptionItem.Create(109902, "LadderDeathNuuun", false, TabGroup.Game, false).SetParent(LadderDeath);

            LadderDeathZipline = BooleanOptionItem.Create(109903, "LadderDeathZipline", false, TabGroup.Game, false).SetParent(LadderDeath);



            //幽霊設定

            GhostOptions = BooleanOptionItem.Create(110000, "GhostOptions", false, TabGroup.Game, false)

                .SetHeader(true)

                .SetColorcode("#6c6ce0");

            GhostCanSeeOtherRoles = BooleanOptionItem.Create(110001, "GhostCanSeeOtherRoles", true, TabGroup.Game, false)

                .SetColorcode("#7474ab").SetParent(GhostOptions);

            GhostCanSeeOtherTasks = BooleanOptionItem.Create(110002, "GhostCanSeeOtherTasks", true, TabGroup.Game, false)

                .SetColor(Color.yellow).SetParent(GhostOptions);

            GhostCanSeeOtherVotes = BooleanOptionItem.Create(110003, "GhostCanSeeOtherVotes", true, TabGroup.Game, false)

                .SetColorcode("#800080").SetParent(GhostOptions);

            GhostCanSeeDeathReason = BooleanOptionItem.Create(110004, "GhostCanSeeDeathReason", true, TabGroup.Game, false)

                .SetColorcode("#80ffdd").SetParent(GhostOptions);

            GhostCanSeeKillerColor = BooleanOptionItem.Create(110005, "GhostCanSeeKillerColor", true, TabGroup.Game, false)

                .SetColorcode("#80ffdd").SetParent(GhostCanSeeDeathReason);

            GhostCanSeeAllTasks = BooleanOptionItem.Create(110006, "GhostCanSeeAllTasks", true, TabGroup.Game, false)

                .SetColorcode("#cee4ae").SetParent(GhostOptions);

            GhostCanSeeNumberOfButtonsOnOthers = BooleanOptionItem.Create(110007, "GhostCanSeeNumberOfButtonsOnOthers", true, TabGroup.Game, false)

                .SetColorcode("#d7c447").SetParent(GhostOptions);

            GhostCanSeeKillflash = BooleanOptionItem.Create(110008, "GhostCanSeeKillflash", true, TabGroup.Game, false)

                .SetColorcode("#61b26c").SetParent(GhostOptions);

            GhostIgnoreTasks = BooleanOptionItem.Create(110009, "GhostIgnoreTasks", false, TabGroup.Game, false)

                .SetColorcode("#bbbbdd").SetParent(GhostOptions);

            GhostIgnoreTasksplayer = IntegerOptionItem.Create(110010, "GhostIgnoreTasksplayer", new(1, 15, 1), 6, TabGroup.Game, false)

                .SetParent(GhostIgnoreTasks);



            // その他

            ConvenientOptions = BooleanOptionItem.Create(111000, "ConvenientOptions", true, TabGroup.Game, false)

                    .SetColorcode("#cc3366")

                    .SetHeader(true);

            FirstTurnMeeting = BooleanOptionItem.Create(111001, "FirstTurnMeeting", false, TabGroup.Game, false)

                .SetDisableTag([CustomOptionTags.TaskBattle, CustomOptionTags.HideAndSeek, CustomOptionTags.SuddenDeath])//初手強制会議

                .SetColorcode("#4fd6a7")

                .SetParent(ConvenientOptions);

            FirstTurnMeetingCantability = BooleanOptionItem.Create(111002, "FirstTurnMeetingCantability", false, TabGroup.Game, false).SetParent(FirstTurnMeeting);

            FixFirstKillCooldown = BooleanOptionItem.Create(111003, "FixFirstKillCooldown", false, TabGroup.Game, false)

                .SetColorcode("#fa7373")

                .SetParent(ConvenientOptions);

            CommnTaskResetAssing = BooleanOptionItem.Create(111004, "CommnTaskResetAssing", false, TabGroup.Game, false)

                .SetColor(Color.yellow)

                .SetParent(ConvenientOptions);

            CanseeVoteresult = BooleanOptionItem.Create(111005, "CanseeVoteresult", false, TabGroup.Game, false)

                .SetColorcode("#64ff0a")

                .SetParent(ConvenientOptions);

            OutroCrewWinreasonchenge = BooleanOptionItem.Create(111006, "OutroCrewWinreasonchenge", true, TabGroup.Game, false)

                .SetColorcode("#66ffff")

                .SetParent(ConvenientOptions);

            AutoGrantPet = BooleanOptionItem.Create(111007, "AutoGrantPet", true, TabGroup.Game, false)

                .SetColorcode("#f0d36a")

                .SetParent(ConvenientOptions);



            OptionBatchSetting = BooleanOptionItem.Create(113000, "OptionBatchSetting", false, TabGroup.Game, false)

                .SetHeader(true)

                .SetColorcode("#6c831aff");

            OptionAllImpostorKillCool = FloatOptionItem.Create(113001, "OptionAllImpostorKillCool", new(0, 180, 0.5f), 29.5f, TabGroup.Game, false)

                .SetParent(OptionBatchSetting)

                .RegisterUpdateValueEvent((object obj, OptionItem.UpdateValueEventArgs args) =>

                {

                    foreach (var option in OptionItem.KillCoolOption.Where(opt => opt.ParentRole.IsImpostor()))

                    {

                        option.SetValue(args.CurrentValue, false, false);

                    }

                    OptionItem.SyncAllOptions();

                    OptionSaver.Save();

                });

            OptionAllNeutralKillCool = FloatOptionItem.Create(113002, "OptionAllNeutralKillCool", new(0, 180, 0.5f), 29.5f, TabGroup.Game, false)

                .SetParent(OptionBatchSetting)

                .RegisterUpdateValueEvent((object obj, OptionItem.UpdateValueEventArgs args) =>

                {

                    foreach (var option in OptionItem.KillCoolOption.Where(opt => opt.ParentRole.IsNeutral()))

                    {

                        option.SetValue(args.CurrentValue, false, false);

                    }

                    OptionItem.SyncAllOptions();

                    OptionSaver.Save();

                });

            RandomPreset = BooleanOptionItem.Create(113500, "RandomPreset", false, TabGroup.Game, true)

                .SetHeader(true)

                .SetColorcode("#49a484");

            AddedPreset1 = BooleanOptionItem.Create(113501, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset1.Value))

                .SetParent(RandomPreset);

            AddedPreset2 = BooleanOptionItem.Create(113502, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset2.Value))

                .SetParent(RandomPreset);

            AddedPreset3 = BooleanOptionItem.Create(113503, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset3.Value))

                .SetParent(RandomPreset);

            AddedPreset4 = BooleanOptionItem.Create(113504, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset4.Value))

                .SetParent(RandomPreset);

            AddedPreset5 = BooleanOptionItem.Create(113505, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset5.Value))

                .SetParent(RandomPreset);

            AddedPreset6 = BooleanOptionItem.Create(113506, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset6.Value))

                .SetParent(RandomPreset);

            AddedPreset7 = BooleanOptionItem.Create(113507, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset7.Value))

                .SetParent(RandomPreset);

            AddedPreset8 = BooleanOptionItem.Create(113508, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset8.Value))

                .SetParent(RandomPreset);

            AddedPreset9 = BooleanOptionItem.Create(113509, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset9.Value))

                .SetParent(RandomPreset);

            AddedPreset10 = BooleanOptionItem.Create(113510, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset10.Value))

                .SetParent(RandomPreset);

            AddedPreset11 = BooleanOptionItem.Create(113511, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset11.Value))

                .SetParent(RandomPreset);

            AddedPreset12 = BooleanOptionItem.Create(113512, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset12.Value))

                .SetParent(RandomPreset);

            AddedPreset13 = BooleanOptionItem.Create(113513, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset13.Value))

                .SetParent(RandomPreset);

            AddedPreset14 = BooleanOptionItem.Create(113514, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset14.Value))

                .SetParent(RandomPreset);

            AddedPreset15 = BooleanOptionItem.Create(113515, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset15.Value))

                .SetParent(RandomPreset);

            AddedPreset16 = BooleanOptionItem.Create(113516, "RandomPreset", false, TabGroup.Game, true)

                .SetOptionName(() => string.Format(Translator.GetString("AddedPreset"), Main.Preset16.Value))

                .SetParent(RandomPreset);



            ObjectOptionitem.Create(1_000_129, "TaskWinSection", true, null, TabGroup.Game)

                .SetOptionName(() => "タスク勝利を無効化・ゲームを終了しない")

                .SetColorcode("#ccff00");



            DisableTaskWin = BooleanOptionItem.Create(1_000_200, "DisableTaskWin", false, TabGroup.Game, false)

                .SetHeader(true)

                .SetColorcode("#ccff00");

            NoGameEnd = BooleanOptionItem.Create(1_000_201, "NoGameEnd", false, TabGroup.Game, false)

                .SetColorcode("#ff1919");



            ObjectOptionitem.Create(1_000_112, "OtherOption", true, null, TabGroup.Other).SetOptionName(() => "Other").SetColorcode("#4f9bffff");

            // プリセット対象外

            HideGameSettings = BooleanOptionItem.Create(1_000_002, "HideGameSettings", false, TabGroup.Other, true)

                .SetHeader(true)

                .SetColorcode("#00c1ff");

            HideSettingsDuringGame = BooleanOptionItem.Create(1_000_003, "HideGameSettingsDuringGame", false, TabGroup.Other, true)

                .SetColorcode("#00c1ff");

            SuffixMode = StringOptionItem.Create(1_000_001, "SuffixMode", suffixModes, 0, TabGroup.Other, true)

                .SetColorcode("#00c1ff");

            ChangeNameToRoleInfo = BooleanOptionItem.Create(1_000_004, "ChangeNameToRoleInfo", true, TabGroup.Other, true)

                .SetColorcode("#00c1ff");

            RoleAssigningAlgorithm = StringOptionItem.Create(1_000_005, "RoleAssigningAlgorithm", RoleAssigningAlgorithms, 0, TabGroup.Other, true)

                .SetColorcode("#00c1ff")

                .RegisterUpdateValueEvent(

                    (object obj, OptionItem.UpdateValueEventArgs args) => IRandom.SetInstanceById(args.CurrentValue)

                );

            UseZoom = BooleanOptionItem.Create(1_000_008, "UseZoom", true, TabGroup.Other, true)

                .SetColorcode("#9199a1");



            ObjectOptionitem.Create(1_300_112, "OtherOption", true, null, TabGroup.Other2).SetOptionName(() => "Other2").SetColorcode("#4f9bffff");



            OptionCommandSetting = BooleanOptionItem.Create(1_300_114, "CommandSetting", true, TabGroup.Other2, true)

                .SetHeader(true)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "一部コマンドを禁止する");



            OptionCommandNow = BooleanOptionItem.Create(1_601_090, "DisableCommandNow", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/now - 現在の設定を表示");



            OptionCommandNowRole = BooleanOptionItem.Create(1_601_100, "DisableCommandNowRole", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/now role - 役職一覧を表示 ");



            OptionCommandNowSet = BooleanOptionItem.Create(1_601_110, "DisableCommandNowSet", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/now set - アサイン/バニラ設定を表示");



            OptionCommandNowW = BooleanOptionItem.Create(1_601_120, "DisableCommandNowW", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/now w - 勝利優先度を表示");



            OptionCommandHNow = BooleanOptionItem.Create(1_601_130, "DisableCommandNowH", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/h now - 有効な設定の説明を表示");



            OptionCommandHRoles = BooleanOptionItem.Create(1_601_140, "DisableCommandHRoles", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/h roles - 役職の説明を表示");



            OptionCommandMyrole = BooleanOptionItem.Create(1_601_150, "DisableCommandMyrole", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/myrole - 自身の役職を表示");



            OptionCommandMeetinginfo = BooleanOptionItem.Create(1_601_160, "DisableCommandMeetinginfo", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/meetinginfo - MeetingInfoを再表示");



            OptionCommandNumberDNumber = BooleanOptionItem.Create(1_601_170, "DisableCommandNumberDNumber", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/(number)d(number) - 乱数を振れます");



            OptionCommand8ball = BooleanOptionItem.Create(1_601_180, "DisableCommand8ball", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/8ball - 8ballができます");



            OptionCommandRename = BooleanOptionItem.Create(1_601_200, "DisableCommandRename", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/rename - 自身の名前を変更");



            OptionNameCharLimit = IntegerOptionItem.Create(1_600_201, "NameCharLimit", new(1, 100, 1), 30, TabGroup.Other2, true)

                .SetParent(OptionCommandRename, invertParentValueForDisplay: true)

                .SetEnabled(() => !OptionCommandRename.GetBool())

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "名前の文字制限");



            OptionCommandRule = BooleanOptionItem.Create(1_601_210, "DisableCommandRule", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/rule - ルールを表示");



            OptionCommandLastresult = BooleanOptionItem.Create(1_601_220, "DisableCommandLastresult", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/lastresult - 試合結果を表示");



            OptionCommandKilllog = BooleanOptionItem.Create(1_601_230, "DisableCommandKilllog", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/killlog - ゲームログを表示");



            OptionCommandTimer = BooleanOptionItem.Create(1_601_240, "DisableCommandTimer", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/timer - ルームタイマーを表示");



            OptionCommandTp = BooleanOptionItem.Create(1_601_250, "DisableCommandTp", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/tp o,i ロビー内へテレポート");



            OptionCommandCo = BooleanOptionItem.Create(1_601_260, "DisableCommandCo", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/co - 役職をCO(宣言)する");



            OptionCommandColist = BooleanOptionItem.Create(1_601_270, "DisableCommandColist", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/colist, /cl - COの一覧を表示");



            OptionCommandAco = BooleanOptionItem.Create(1_601_280, "DisableCommandAco", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/aco - 属性をCO(宣言)する");



            OptionCommandAcl = BooleanOptionItem.Create(1_601_290, "DisableCommandAcl", false, TabGroup.Other2, true)

                .SetParent(OptionCommandSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "/acl - 属性COの一覧を表示");



            OptionUltraLightweightMode = BooleanOptionItem.Create(1_400_140, "UltraLightweightMode", false, TabGroup.Other2, true)

                .SetHeader(true)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "超軽量化モード")

                .SetTooltip(() => "役職名・マーク・外見などの表示更新をさらに間引き、負荷を下げます。");



            OptionAutoFunction = BooleanOptionItem.Create(1_400_150, "AutoFunction", false, TabGroup.Other2, true)

                .SetHeader(true)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "自動機能");



            AutoDisplayLastResult = BooleanOptionItem.Create(1_000_000, "AutoDisplayLastResult", true, TabGroup.Other2, true)

                .SetParent(OptionAutoFunction)

                .SetColorcode("#66ffff");



            AutoDisplayKillLog = BooleanOptionItem.Create(1_000_006, "AutoDisplayKillLog", true, TabGroup.Other2, true)

                .SetParent(OptionAutoFunction)

                .SetColorcode("#66ffff");



            OptionAutoStartSetting = BooleanOptionItem.Create(1_300_200, "AutoStartSetting", false, TabGroup.Other2, true)

                .SetParent(OptionAutoFunction)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "自動スタートを有効にする");



            OptionAutoStartGM = BooleanOptionItem.Create(1_300_210, "AutoStartGM", false, TabGroup.Other2, true)

                .SetParent(OptionAutoStartSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "GMの場合のみ自動スタートを有効にする");



            OptionAutoStartLimit = IntegerOptionItem.Create(1_300_220, "AutoStartLimit", new(30, 570, 30), 180, TabGroup.Other2, true)

                .SetParent(OptionAutoStartSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "部屋を建ててから何秒でスタートするか");



            OptionAutoStartLimitAnotherSetting = BooleanOptionItem.Create(1_300_230, "AutoStartLimitAnotherSetting", false, TabGroup.Other2, true)

                .SetParent(OptionAutoStartSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "15人の場合個別に設定する");



            OptionAutoStartLimitAnother = IntegerOptionItem.Create(1_300_240, "AutoStartLimitAnother", new(30, 570, 30), 420, TabGroup.Other2, true)

                .SetParent(OptionAutoStartLimitAnotherSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "部屋を建ててから何秒でスタートするか");



            OptionAutoReturnRoom = BooleanOptionItem.Create(1_300_250, "AutoReturnRoom", false, TabGroup.Other2, true)

                .SetParent(OptionAutoFunction)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "試合終了後自動で部屋に戻る");



            OptionAutoReturnRoomGM = BooleanOptionItem.Create(1_300_260, "AutoReturnRoomGM", false, TabGroup.Other2, true)

                .SetParent(OptionAutoReturnRoom)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "GMの場合のみ自動で部屋に戻る");



            OptionAutoForceEndOnDisconnect = BooleanOptionItem.Create(1_300_261, "AutoForceEndOnDisconnect", false, TabGroup.Other2, true)

                .SetParent(OptionAutoFunction)

                .SetColorcode("#00c1ff");



            OptionAutoForceEndDisconnectCount = IntegerOptionItem.Create(1_300_262, "AutoForceEndDisconnectCount", new(1, 14, 1), 1, TabGroup.Other2, true)

                .SetParent(OptionAutoForceEndOnDisconnect)

                .SetColorcode("#00c1ff")

                .SetValueFormat(OptionFormat.Players);



            OptionPeriodicAutoRehost = BooleanOptionItem.Create(1_300_263, "PeriodicAutoRehost", false, TabGroup.Other2, true)

                .SetParent(OptionAutoFunction)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "設定試合数ごとに自動で部屋を立て直す");



            OptionPeriodicAutoRehostInterval = IntegerOptionItem.Create(1_300_264, "PeriodicAutoRehostInterval", new(1, 50, 1), 10, TabGroup.Other2, true)

                .SetParent(OptionPeriodicAutoRehost)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "何試合ごとに立て直すか")

                .SetValueFormat(OptionFormat.Games);



            OptionPeriodicAutoRehostMinPlayersEnabled = BooleanOptionItem.Create(1_300_266, "PeriodicAutoRehostMinPlayersEnabled", true, TabGroup.Other2, true)

                .SetParent(OptionPeriodicAutoRehost)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "最低人数を指定する(OFFなら人数に関係なく作動)");



            OptionPeriodicAutoRehostMinPlayers = IntegerOptionItem.Create(1_300_265, "PeriodicAutoRehostMinPlayers", new(1, 15, 1), 4, TabGroup.Other2, true)

                .SetParent(OptionPeriodicAutoRehostMinPlayersEnabled)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "作動する最低人数(この人数以上なら作動)")

                .SetValueFormat(OptionFormat.Players);



            OptionPeriodicAutoRehostExcludeVoidGames = BooleanOptionItem.Create(1_300_269, "PeriodicAutoRehostExcludeVoidGames", true, TabGroup.Other2, true)

                .SetParent(OptionPeriodicAutoRehost)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "廃村(引き分け終了)はカウントしない");



            // ===== 定期的なデータリセット(N試合ごと。部屋の立て直しとは独立した別機能) =====

            OptionPeriodicDataReset = BooleanOptionItem.Create(1_300_271, "PeriodicDataReset", false, TabGroup.Other2, true)

                .SetParent(OptionAutoFunction)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "設定試合数ごとにデータをリセットする(軽量化)");



            OptionPeriodicDataResetInterval = IntegerOptionItem.Create(1_300_272, "PeriodicDataResetInterval", new(1, 50, 1), 10, TabGroup.Other2, true)

                .SetParent(OptionPeriodicDataReset)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "何試合ごとにリセットするか")

                .SetValueFormat(OptionFormat.Games);



            OptionPeriodicDataResetExcludeVoidGames = BooleanOptionItem.Create(1_300_275, "PeriodicDataResetExcludeVoidGames", true, TabGroup.Other2, true)

                .SetParent(OptionPeriodicDataReset)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "廃村(引き分け終了)はカウントしない");



            // ===== 一定人数以下でプリセットを自動切り替え =====

            OptionPresetSwitchByPlayerCount = BooleanOptionItem.Create(1_300_281, "PresetSwitchByPlayerCount", false, TabGroup.Other2, true)

                .SetParent(OptionAutoFunction)

                .SetColorcode("#f0a030")

                .SetOptionName(() => "一定人数以下でプリセットを自動切り替え");



            OptionPresetSwitchThreshold = IntegerOptionItem.Create(1_300_282, "PresetSwitchThreshold", new(1, 15, 1), 7, TabGroup.Other2, true)

                .SetParent(OptionPresetSwitchByPlayerCount)

                .SetColorcode("#f0a030")

                .SetOptionName(() => "この人数以下なら切り替える")

                .SetValueFormat(OptionFormat.Players);



            OptionPresetSwitchTargetPreset = IntegerOptionItem.Create(1_300_283, "PresetSwitchTargetPreset", new(1, 7, 1), 1, TabGroup.Other2, true)

                .SetParent(OptionPresetSwitchByPlayerCount)

                .SetColorcode("#f0a030")

                .SetOptionName(() => "切り替え先のプリセット番号");



            // ===== 試合終了後、一旦ロビーに戻ってから〇分経過したら自動で立て直す機能 =====

            // 「設定試合数ごとに立て直す」(OptionPeriodicAutoRehost)とは独立した機能。

            // 試合終了→通常通りロビーに戻る(AutoReturnToRoomPatch)→ロビー滞在中に

            // 指定した分数が経過したら、そのタイミングで部屋を立て直す。

            // なお、一度立て直しを実行したら、以降はこのプロセス内では自動発動しない(1回のみ)。

            //

            // 「最低人数を指定する」と「次のロビーで一定時間経過後に立て直す」は、

            // どちらも独立した項目として常に表示する(両方同時にONにすることも可能)。

            // ただし、どちらか一方は必ずONでなければならない、という制約だけは

            // コード側(RegisterUpdateValueEvent)で維持する(両方同時にOFFにはできない)。

            OptionTimedAutoRehost = BooleanOptionItem.Create(1_300_267, "TimedAutoRehost", false, TabGroup.Other2, true)

                .SetParent(OptionAutoFunction)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "次のロビーで一定時間経過後に自動で立て直す(1回のみ)");



            // ===== 「最低人数を指定する」と「次のロビーで一定時間経過後」の

            //       どちらか一方は必ずONでなければならない、という制約を強制する =====

            // 両方ONは許可するが、両方同時にOFFにはできないようにする。

            // どちらかがOFFにされた瞬間、もう片方も既にOFFであれば自動でONに戻す。

            OptionPeriodicAutoRehostMinPlayersEnabled.RegisterUpdateValueEvent((sender, args) =>

            {

                bool turnedOff = args.CurrentValue == 0;

                if (turnedOff && OptionTimedAutoRehost.GetBool() == false)

                {

                    OptionTimedAutoRehost.SetValue(1);

                }

            });

            OptionTimedAutoRehost.RegisterUpdateValueEvent((sender, args) =>

            {

                bool turnedOff = args.CurrentValue == 0;

                if (turnedOff && OptionPeriodicAutoRehostMinPlayersEnabled.GetBool() == false)

                {

                    OptionPeriodicAutoRehostMinPlayersEnabled.SetValue(1);

                }

            });



            OptionTimedAutoRehostMinutes = IntegerOptionItem.Create(1_300_268, "TimedAutoRehostMinutes", new(1, 9, 1), 5, TabGroup.Other2, true)

                .SetParent(OptionTimedAutoRehost)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "次のロビーで何分たったら立て直すか");



            OptionStreamerSetting = BooleanOptionItem.Create(1_300_270, "StreamerSetting", false, TabGroup.Other2, true)

                .SetParent(OptionAutoFunction)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "配信者向けオプション");



            OptionJoinKick = BooleanOptionItem.Create(1_300_300, "JoinKick", false, TabGroup.Other2, true)

                .SetParent(OptionStreamerSetting)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "連続参加のプレイヤーをキック");



            OptionNotifyJoinKick = BooleanOptionItem.Create(1_300_310, "NotifyJoinKick", false, TabGroup.Other2, true)

                .SetParent(OptionJoinKick)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "キックではなく検知だけにする");



            OptionNotModeJoinKick = BooleanOptionItem.Create(1_300_320, "NotModeJoinKick", false, TabGroup.Other2, true)

                .SetParent(OptionJoinKick)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "モデレーターはカウントしない");



            OptionDrawJoinKick = BooleanOptionItem.Create(1_300_330, "DrawJoinKick", false, TabGroup.Other2, true)

                .SetParent(OptionJoinKick)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "廃村した試合はカウントしない");



            OptionGameChatSetting = BooleanOptionItem.Create(1_300_350, "GameChatSetting", false, TabGroup.Other2, true)

                      .SetHeader(true)

                      .SetColorcode("#00c1ff")

                      .SetOptionName(() => "タスクターン中のチャットを表示");



            OptionGameChatNormalChat = BooleanOptionItem.Create(1_300_360, "GameChatNormalChat", false, TabGroup.Other2, true)

                .SetParent(OptionGameChatSetting)

                      .SetColorcode("#00c1ff")

                      .SetOptionName(() => "通常チャットを有効にする(未完成)");



            OptionGameChatNormalNearChat = BooleanOptionItem.Create(1_300_370, "GameChatNormalNearChat", false, TabGroup.Other2, true)

                      .SetParent(OptionGameChatNormalChat)

                      .SetColorcode("#00c1ff")

                      .SetOptionName(() => "近チャを有効にする");



            OptionGameChatNormalNearChatRange = IntegerOptionItem.Create(1_300_380, "GameChatNormalNearChatRange", new(1, 100, 1), 10, TabGroup.Other2, true)

                      .SetParent(OptionGameChatNormalNearChat)

                      .SetColorcode("#00c1ff")

                      .SetOptionName(() => "近チャの範囲");



            OptionGameChatHideChat = BooleanOptionItem.Create(1_300_390, "GameChatHideChat", false, TabGroup.Other2, true)

                      .SetParent(OptionGameChatSetting)

                      .SetHeader(true)

                      .SetColorcode("#00c1ff")

                      .SetOptionName(() => "秘匿チャットを有効にする(未完成)");



            OptionGameChatHideNearChat = BooleanOptionItem.Create(1_300_400, "GameChatHideNearChat", false, TabGroup.Other2, true)

                      .SetParent(OptionGameChatHideChat)

                      .SetColorcode("#00c1ff")

                      .SetOptionName(() => "近チャを有効にする");



            OptionGameChatHideNearChatRange = IntegerOptionItem.Create(1_300_410, "GameChatHideNearChatRange", new(1, 100, 1), 10, TabGroup.Other2, true)

                .SetParent(OptionGameChatHideNearChat)

                .SetColorcode("#00c1ff")

                .SetOptionName(() => "近チャの範囲");





            ObjectOptionitem.Create(1_000_127, "KickBanSection", true, null, TabGroup.Other2)

                .SetOptionName(() => "参加制限・BAN/KICK設定")

                .SetColorcode("#ff3333");



            ApplyDenyNameList = BooleanOptionItem.Create(1_000_100, "ApplyDenyNameList", true, TabGroup.Other2, true)

                .SetHeader(true)

                .SetInfo(Translator.GetString("KickBanOptionWhiteList"))

                .SetColor(Color.red);

            KickPlayerFriendCodeNotExist = BooleanOptionItem.Create(1_000_101, "KickPlayerFriendCodeNotExist", false, TabGroup.Other2, true)

                .SetInfo(Translator.GetString("KickBanOptionWhiteList"))

                .SetColor(Color.red);

            ApplyBanList = BooleanOptionItem.Create(1_000_110, "ApplyBanList", true, TabGroup.Other2, true)

                .SetColor(Color.red);

            KiclHotNotFriend = BooleanOptionItem.Create(1_000_111, "KiclHotNotFriend", false, TabGroup.Other2, true)

                .SetInfo(Translator.GetString("KickBanOptionWhiteList"))

                .SetColor(Color.red);

            KickInitialName = BooleanOptionItem.Create(1_000_125, "KickInitialName", false, TabGroup.Other2, true)

                .SetColor(Color.red)

                .SetInfo(Translator.GetString("KickBanOptionWhiteList"));

            BANKickjoinplayer = BooleanOptionItem.Create(1_000_126, "BanKickjoinplayer", false, TabGroup.Other2, true)

                .SetColor(Color.red)

                .SetInfo(Translator.GetString("BanKickjoinplayerInfo"));



            VanillaOptionHolder.Initialize();

            DebugModeManager.SetupCustomOption();



            OptionSaver.Load();



            Combinations = null; //使わないから消す



            IsLoaded = true;



            var missioneerMaxOptions = OptionItem.AllOptions.Where(o => o.Name == "Maximum" && o.ParentRole == CustomRoles.Missioneer).ToList();

            Logger.Info($"[DEBUG] Missioneer用Maximumオプション数: {missioneerMaxOptions.Count}", "DebugMissioneer");

            foreach (var o in missioneerMaxOptions)

            {

                Logger.Info($"[DEBUG]   Id={o.Id}, Tab={o.Tab}, ParentRole={o.ParentRole}", "DebugMissioneer");

            }



            static void CreateRoleOption(IOrderedEnumerable<SimpleRoleInfo> sortedRoleInfo, CustomRoleTypes roleTypes)

            {

                // ===== 修正 =====

                // 以前はタブ番号(OptionSort.TabNumber)を0から連番でチェックし、

                // 該当役職が0件のタブに当たった時点でループを打ち切っていた。

                // そのため、そのタイプの役職群に「欠番のタブ番号」(例:

                // Neutralはタブ0を使う役職が1つも無くタブ1から始まっていた、

                // Madmateはタブ6・7が欠番でタブ8が孤立していた)が存在すると、

                // 欠番以降のタブに属する役職が設定画面に一切表示されなくなる

                // 不具合があった。

                // 実際に使われているタブ番号だけを昇順に処理するよう変更し、

                // 欠番があっても後続のタブが表示されなくなることを防ぐ。

                var roleTypeList = sortedRoleInfo.Where(role => role.CustomRoleType == roleTypes);

                var usedTabNumbers = roleTypeList

                    .Select(role => role.OptionSort.TabNumber)

                    .Distinct()

                    .OrderBy(n => n);

                foreach (var tabNum in usedTabNumbers)

                {

                    var RoleList = roleTypeList.Where(role => role.OptionSort.TabNumber == tabNum);

                    foreach (var info in RoleList.OrderBy(role => role.OptionSort.SortNumber))

                    {

                        if (info.RoleName is CustomRoles.AlienHijack) continue;

                        SetupRoleOptions(info);

                        info.OptionCreator?.Invoke();

                        MonkeyBehaviorBanOption.Create(info);

                    }

                }

            }

        }

        private static List<CombinationRoles> Combinations = new();

        public static void SetupRoleOptions(SimpleRoleInfo info) => SetupRoleOptions(info.ConfigId, info.Tab, info.RoleName, info.AssignInfo.AssignCountRule, fromtext: UtilsOption.GetFrom(info), combination: info.Combination);

        public static OptionItem SetupRoleOptions(int id, TabGroup tab, CustomRoles role, IntegerValueRule assignCountRule = null, CustomGameMode customGameMode = CustomGameMode.Standard, string fromtext = "", CombinationRoles combination = CombinationRoles.None, int defo = -1)

        {

            if ((role is CustomRoles.Phantom) || (combination != CombinationRoles.None && Combinations.Contains(combination))) return null;

            if (role.IsVanilla())

            {

                switch (role)

                {

                    case CustomRoles.Impostor: id = 10; break;

                    case CustomRoles.Shapeshifter: id = 30; break;

                    case CustomRoles.Phantom: id = 40; break;

                    case CustomRoles.Viper: id = 23050; break;

                    case CustomRoles.Crewmate: id = 11; break;

                    case CustomRoles.Engineer: id = 200; break;

                    case CustomRoles.Scientist: id = 250; break;

                    case CustomRoles.Tracker: id = 300; break;

                    case CustomRoles.Noisemaker: id = 350; break;

                    case CustomRoles.Detective: id = 23100; break;

                    case CustomRoles.Judge: id = 25100; break;

                }

            }

            assignCountRule ??= new(1, 15, 1);

            var from = "<line-height=25%><size=25%>\n</size><size=60%><pos=10%></color> <b>" + fromtext + "</b></size>";



            var tag = CustomOptionTags.Role;

            if (role is CustomRoles.MMArcher) customGameMode = CustomGameMode.MurderMystery;

            switch (customGameMode)

            {

                case CustomGameMode.HideAndSeek: tag = CustomOptionTags.HideAndSeek; break;

                case CustomGameMode.MurderMystery: tag = CustomOptionTags.MurderMystery; break;

            }

            CustomRoleManager.SortCustomRoles.Add(role);

            if (role.GetCombination() is not CustomRoles.NotAssigned) CustomRoleManager.SortCustomRoles.Add(role.GetCombination());

            var spawnOption = IntegerOptionItem.Create(id, combination == CombinationRoles.None ? role.ToString() : combination.ToString(), new(0, 100, 10), 0, tab, false, from)

                    .SetColorcode(UtilsRoleText.GetRoleColorCode(role))

                    .SetColor(UtilsRoleText.GetRoleColor(role, true))

                    .SetCustomRole(role)

                    .SetValueFormat(OptionFormat.Percent)

                    .SetHeader(true)

                    .SetEnabled(() => role is not CustomRoles.Crewmate and not CustomRoles.Impostor and not CustomRoles.Eater)

                    // ===== イーターのリストラ =====

                    // コード自体は残すが、ホストが選択できないよう設定画面から完全に非表示にする。

                    // (常に0%扱いになるので role.IsEnable() も false のままになり、

                    //  役職プールにも一切出てこなくなる)

                    .SetHidden(role == CustomRoles.NotAssigned || role == CustomRoles.Eater)

                    .SetTag(tag) as IntegerOptionItem;

            var hidevalue = role.IsCombinationRole() || role.IsLovers() || (assignCountRule.MaxValue == assignCountRule.MinValue);



            if (role is CustomRoles.Crewmate or CustomRoles.Impostor) return spawnOption;



            var countOption = IntegerOptionItem.Create(id + 1, "Maximum", assignCountRule, defo is -1 ? assignCountRule.Step : defo, tab, false, HideValue: hidevalue)

                .SetParent(spawnOption)

                .SetValueFormat(assignCountRule.MaxValue is 7 ? OptionFormat.Set : OptionFormat.Players)

                .SetEnabled(() => !SlotRoleAssign.IsSeted(role))

                .SetTag(tag)

                .SetHidden(hidevalue)

                .SetParentRole(role)

                .RegisterUpdateValueEvent((object obj, OptionItem.UpdateValueEventArgs args) => spawnOption.Refresh());



            if (combination != CombinationRoles.None) Combinations.Add(combination);

            CustomRoleSpawnChances.Add(role, spawnOption);

            CustomRoleCounts.Add(role, countOption);

            return spawnOption;

        }

    }

}

