using HarmonyLib;



namespace TownOfHost.Patches

{

    // ===== 定期的な自動立て直し (N試合ごと) =====

    // From: TownOfHost_hamo

    //

    // ホストが設定するゲームルールオプション「設定試合数ごとに自動で部屋を立て直す」がONの場合、

    // 設定した試合数ごと、かつ部屋に一定人数以上いる場合に、部屋を一度離脱して新しく立て直す。

    // 試合結果画面が表示されたタイミング(EndGameManager.ShowButtons)で判定する。

    //

    // AutoReturnToRoomPatch(「自動でロビーに戻る」機能)と同じフックを使うため、

    // 周期立て直しの条件を満たした場合はこちらを優先し、通常のNextGame()は呼ばせない。

    [HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.ShowButtons))]

    public static class PeriodicAutoRehostPatch

    {

        private static bool _scheduled;

        private static int _lastVoidGameDecrementedFor = -1; // 廃村カウント戻しを既に行った試合番号(二重減算防止)



        /// <summary>

        /// 今回の試合が周期立て直しの対象かどうかを判定する。

        /// AutoReturnToRoomPatch側からも同じ条件で参照するため公開しておく。

        /// </summary>

        public static bool IsScheduledForThisGame()

        {

            if (!Options.OptionPeriodicAutoRehost.GetBool()) return false;



            int interval = Options.OptionPeriodicAutoRehostInterval.GetInt();

            if (interval <= 0) return false;

            if (Main.GameCount <= 0) return false;

            if (Main.GameCount % interval != 0) return false;



            if (Options.OptionPeriodicAutoRehostMinPlayersEnabled.GetBool())

            {

                int minPlayers = Options.OptionPeriodicAutoRehostMinPlayers.GetInt();

                // 「現在の人数」を毎回参照するのではなく、前回の試合終了時に1回だけ

                // 記録しておいた人数を使う(軽量化のため)。

                int previousPlayers = Modules.TimedAutoRehost.PreviousGamePlayerCount;

                if (previousPlayers < 0) return false; // まだ記録が無い(初回など)場合は判定しない

                if (previousPlayers < minPlayers) return false;

            }



            return true;

        }



        // HarmonyPriorityを高めに設定し、AutoReturnToRoomPatchより先に判定を確定させる。

        [HarmonyPriority(Priority.High)]

        public static void Postfix(EndGameManager __instance)

        {

            // ===== 廃村(引き分け終了)はカウントしない =====

            // Main.GameCountは試合「開始時」にインクリメントされるため、

            // 廃村になるかどうかはこの時点(試合終了時)にならないと分からない。

            // 該当する場合はここで1つ戻し、「この試合は無かったこと」にする。

            // (定期立て直しの判定より先に行う必要があるため、このPostfixの先頭で処理する)

            // ShowButtonsが同じ試合中に複数回呼ばれても二重で減算しないよう、

            // 「どの試合番号に対して既に減算したか」を記録しておく。

            if (AmongUsClient.Instance.AmHost

                && Options.OptionPeriodicAutoRehostExcludeVoidGames.GetBool()

                && CustomWinnerHolder.WinnerTeam == CustomWinner.Draw

                && Main.GameCount > 0

                && _lastVoidGameDecrementedFor != Main.GameCount)

            {

                _lastVoidGameDecrementedFor = Main.GameCount;

                Main.GameCount--;

                Logger.Info($"廃村のため試合数カウントを1戻しました。(現在: {Main.GameCount}試合)", "AutoRehost");

            }



            // 「前回の試合人数」の記録は、立て直しの条件に関わらず常に行う

            // (次のロビーでTimedAutoRehostの判定に使うため)。

            if (AmongUsClient.Instance.AmHost)

            {

                Modules.TimedAutoRehost.NotifyGameEnded();

            }



            if (!AmongUsClient.Instance.AmHost) return;

            if (_scheduled) return;

            if (!IsScheduledForThisGame()) return;



            _scheduled = true;



            int interval = Options.OptionPeriodicAutoRehostInterval.GetInt();

            Logger.Info($"{Main.GameCount}試合目・{GameData.Instance?.PlayerCount ?? 0}人在室のため、定期データリセットを予約しました。(設定: {interval}試合ごと)", "AutoRehost");



            _ = new LateTask(() =>

            {

                _scheduled = false;

                if (!AmongUsClient.Instance.AmHost) return;

                Modules.AutoRehost.TriggerPeriodicRehost();

            }, 5f, "PeriodicAutoRehost", true);

        }

    }

}

