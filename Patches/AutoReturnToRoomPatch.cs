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



            _ = new LateTask(() =>

            {

                ReturnScheduled = false;

                if (!AmongUsClient.Instance.AmHost) return;



                // Unity/IL2CPPの「破棄済みオブジェクトの偽null」対策:

                // 5秒待つ間に何らかの理由でシーン遷移が既に始まっている等、

                // EndGameNavigation側の内部状態が壊れている場合がある。

                // (実際に、接続が不安定なセッションでNextGame()内部から

                //  NullReferenceExceptionが発生する事例が確認されている)

                var nav = DestroyableSingleton<EndGameNavigation>.Instance;

                if (nav == null) return;



                try

                {

                    nav.NextGame();

                }

                catch (System.Exception ex)

                {

                    // NextGame()自体はバニラ側の実装のため、内部で失敗した場合に

                    // こちらから安全に復旧させる手段が無い。

                    // ExitGame()等でロビーごと切断させるのは参加者全員に影響する

                    // 過剰な対処になるため行わず、ここでは失敗をログに残すだけに留める。

                    // (次の試合で会議が呼べない等の不具合が起きた場合、この直前に

                    //  このエラーが出ていないか確認することで原因の切り分けに使える)

                    Logger.Error($"EndGameNavigation.NextGame()が失敗しました: {ex}", "AutoReturnToRoom");

                }

            }, 5f, "AutoReturnToRoom", true);

        }

    }

}

