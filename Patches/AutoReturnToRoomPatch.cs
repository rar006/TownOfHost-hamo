using HarmonyLib;



namespace TownOfHost.Patches

{

    [HarmonyPatch(typeof(EndGameManager), nameof(EndGameManager.ShowButtons))]

    public static class AutoReturnToRoomPatch

    {

        private static bool ReturnScheduled;



        public static void Postfix(EndGameManager __instance)

        {

            if (!AmongUsClient.Instance.AmHost) return;



            // 定期立て直し(N試合ごと)が今回の試合で予約されている場合、そちらを優先し
            // 通常の「同じ部屋でもう一度」は行わない
            if (PeriodicAutoRehostPatch.IsScheduledForThisGame()) return;



            // 自動戻り設定がOFFなら終了
            if (!Options.OptionAutoReturnRoom.GetBool()) return;



            if (Options.OptionAutoReturnRoomGM.GetBool() && !Options.EnableGM.GetBool())

                return;



            if (ReturnScheduled) return;
            ReturnScheduled = true;
            AttemptNextGame(1);
        }

        // NextGame()呼び出し時に稀にNullReferenceExceptionで失敗する事例が確認されているため、
        // 1回で諦めずに間隔を空けて数回リトライする。IL2CPP側の内部状態(シーン遷移中など)が
        // 一時的なものであれば、少し待つだけで次の試行は成功することが多い。
        private const int MaxNextGameAttempts = 3;
        private const float RetryIntervalSeconds = 3f;

        private static void AttemptNextGame(int attempt)
        {
            _ = new LateTask(() =>
            {
                // 待っている間に試合が新しく始まっている等、状況が変わっていれば中断する。
                if (!AmongUsClient.Instance.AmHost)
                {
                    ReturnScheduled = false;
                    return;
                }

                // Unity/IL2CPPの「破棄済みオブジェクトの偽null」対策:
                // 5秒待つ間に何らかの理由でシーン遷移が既に始まっている等、
                // EndGameNavigation側の内部状態が壊れている場合がある。
                // もうロビーに戻っている時は何もしない(ロビーで NextGame を呼ぶと失敗していた)
                if (GameStates.IsLobby)
                {
                    ReturnScheduled = false;
                    return;
                }
                var nav = DestroyableSingleton<EndGameNavigation>.Instance;
                if (nav == null)
                {
                    ReturnScheduled = false;
                    return;
                }

                try
                {
                    nav.NextGame();
                    ReturnScheduled = false;
                }
                catch (System.Exception ex)
                {
                    if (attempt < MaxNextGameAttempts)
                    {
                        Logger.Warn($"EndGameNavigation.NextGame()が失敗しました({attempt}/{MaxNextGameAttempts}回目)。{RetryIntervalSeconds}秒後に再試行します: {ex.Message}", "AutoReturnToRoom");
                        AttemptNextGame(attempt + 1);
                    }
                    else
                    {
                        // 既定回数リトライしても失敗した場合、NextGame()自体はバニラ側の実装のため、
                        // こちらから安全に復旧させる手段が無い。ExitGame()等でロビーごと切断させるのは
                        // 参加者全員に影響する過剰な対処になるため行わず、失敗をログに残すだけに留める。
                        // (次の試合で会議が呼べない等の不具合が起きた場合、この直前に
                        //  このエラーが出ていないか確認することで原因の切り分けに使える)
                        Logger.Error($"EndGameNavigation.NextGame()が{MaxNextGameAttempts}回とも失敗しました: {ex}", "AutoReturnToRoom");
                        ReturnScheduled = false;
                    }
                }
            }, attempt == 1 ? 5f : RetryIntervalSeconds, "AutoReturnToRoom", true);
        }

    }

}

