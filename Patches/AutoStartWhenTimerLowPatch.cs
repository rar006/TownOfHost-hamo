using HarmonyLib;
using UnityEngine;
using TownOfHost.Modules;

namespace TownOfHost.Patches
{
    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Update))]
    public static class AutoStartPatch
    {
        private static float timer = 0f;
        private static int lastPlayerCount = 0;
        private static int lastGameId = 0;
        private static float lastCountChangedAt = 0f;
        private const float JoinSettleSeconds = 5f;
        private const int MinPlayers = 4;

        public static void Postfix()
        {
            if (!AmongUsClient.Instance.AmHost) return;

            if (!Options.OptionAutoStartSetting.GetBool()
                || (Options.OptionAutoStartGM.GetBool() && !Options.EnableGM.GetBool())
                || !GameStates.IsLobby)
            {
                // 自動スタートを使わない間は数えない(後でONにした時に古い時間から始まらないように)
                timer = 0f;
                lastPlayerCount = 0;
                return;
            }

            // 部屋が変わったら0から数え直す。
            // (以前は前の部屋の時間が残っていて、作り直した部屋で人が入ってきた直後に試合が始まり、
            //  準備のできていない人がいるせいで開始処理が壊れ、未導入の人がまとめて落ちていた)
            var gameId = AmongUsClient.Instance.GameId;
            if (gameId != lastGameId)
            {
                lastGameId = gameId;
                timer = 0f;
                lastPlayerCount = 0;
                lastCountChangedAt = Time.time;
            }

            int playerCount = PlayerControl.AllPlayerControls.Count;

            if (Options.OptionAutoStartLimitAnotherSetting.GetBool() && lastPlayerCount != 15 && playerCount == 15)
            {
                float limit15 = Options.OptionAutoStartLimitAnother.GetFloat();
                if (timer > limit15 - 20f)
                    timer = limit15 - 20f;
            }
            if (playerCount != lastPlayerCount) lastCountChangedAt = Time.time;
            lastPlayerCount = playerCount;

            timer += Time.deltaTime;

            float limit = (Options.OptionAutoStartLimitAnotherSetting.GetBool() && playerCount == 15)
                ? Options.OptionAutoStartLimitAnother.GetFloat()
                : Options.OptionAutoStartLimit.GetFloat();

            if (timer < limit) return;
            // 時間になっても、全員の準備ができるまでは待つ(タイマーはそのままなので、準備ができたらすぐ始まる)
            if (!IsReadyToStart(playerCount)) return;

            timer = 0f;

            // 自動スタート前にモデレーター名プレフィックスを除去（暗転防止）
            Moderator.StripModeratorDisplayNamesForGame();

            _ = new LateTask(() =>
            {
                var gsm = DestroyableSingleton<GameStartManager>.Instance;
                if (gsm == null || !GameStates.IsLobby) return;
                if (!IsReadyToStart(PlayerControl.AllPlayerControls.Count)) return;
                Modules.YouTubeMapVote.ApplyAtGameStart(); // 自動スタートでもマップ投票を反映する
                gsm.countDownTimer = 0.1f;
                gsm.startState = GameStartManager.StartingStates.Countdown;
            }, 0.15f, "AutoStart.DelayedStart", true);
        }

        private static bool IsReadyToStart(int playerCount)
        {
            if (playerCount < MinPlayers) return false;
            if (Time.time - lastCountChangedAt < JoinSettleSeconds) return false;
            var gsm = DestroyableSingleton<GameStartManager>.Instance;
            if (gsm != null && gsm.startState != GameStartManager.StartingStates.NotStarting) return false; // もう開始中
            foreach (var pc in PlayerControl.AllPlayerControls)
            {
                if (pc == null || pc.Data == null || pc.Data.Disconnected) return false;
                if (pc.Data.DefaultOutfit == null || pc.Data.DefaultOutfit.ColorId < 0) return false;
            }
            return true;
        }
    }
}
