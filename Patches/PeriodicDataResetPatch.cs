using HarmonyLib;

namespace TownOfHost.Patches
{
    // ===== 定期的なデータリセット (N試合ごと) =====
    // 「設定試合数ごとに部屋を立て直す」(PeriodicAutoRehostPatch)とは完全に独立した機能。
    // 部屋はそのまま維持し、蓄積したログ・キャッシュ類だけをその場でクリアして軽量化する。
    // 試合結果画面が表示されたタイミング(EndGameManager.ShowButtons)で判定する。
    // 通算試合数はPeriodicAutoRehostPatchと共通のMain.GameCountをそのまま参照する
    // (こちらはMain.GameCountを0に戻したりはしない)。
    [HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.ShowButtons))]
    public static class PeriodicDataResetPatch
    {
        private static bool _scheduled;
        private static int _lastVoidGameCheckedFor = -1; // 廃村判定を既に行った試合番号(二重判定防止)
        private static bool _lastVoidGameWasSkipped;      // 直近判定時、廃村としてスキップしたかどうか

        /// <summary>今回の試合がデータリセットの対象かどうかを判定する。</summary>
        public static bool IsScheduledForThisGame()
        {
            if (!Options.OptionPeriodicDataReset.GetBool()) return false;

            int interval = Options.OptionPeriodicDataResetInterval.GetInt();
            if (interval <= 0) return false;
            if (Main.GameCount <= 0) return false;

            // 廃村(引き分け終了)は対象外にする設定の場合、この試合番号自体を無かったことにする
            // (1つ前の試合番号で判定し直す。二重判定にならないよう試合番号ごとに1回だけ計算する)。
            int effectiveGameCount = Main.GameCount;
            if (Options.OptionPeriodicDataResetExcludeVoidGames.GetBool())
            {
                if (_lastVoidGameCheckedFor != Main.GameCount)
                {
                    _lastVoidGameCheckedFor = Main.GameCount;
                    _lastVoidGameWasSkipped = CustomWinnerHolder.WinnerTeam == CustomWinner.Draw;
                }
                if (_lastVoidGameWasSkipped) effectiveGameCount--;
            }

            if (effectiveGameCount <= 0) return false;
            if (effectiveGameCount % interval != 0) return false;

            return true;
        }

        public static void Postfix(EndGameManager __instance)
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (_scheduled) return;
            if (!IsScheduledForThisGame()) return;

            _scheduled = true;

            int interval = Options.OptionPeriodicDataResetInterval.GetInt();
            Logger.Info($"{Main.GameCount}試合目のため、定期データリセットを予約しました。(設定: {interval}試合ごと)", "AutoRehost");

            _ = new LateTask(() =>
            {
                _scheduled = false;
                if (!AmongUsClient.Instance.AmHost) return;
                Modules.AutoRehost.TriggerPeriodicDataReset();
            }, 5f, "PeriodicDataReset", true);
        }
    }
}
