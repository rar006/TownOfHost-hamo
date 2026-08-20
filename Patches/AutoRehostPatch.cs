using HarmonyLib;

using TownOfHost.Modules;



namespace TownOfHost

{

    // ===== 部屋の自動立て直し (AutoRehost) 関連パッチ =====



    // MainMenuManagerのインスタンスを拾っておく (OpenCreateGame呼び出しに使う)

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]

    class AutoRehostMainMenuCapturePatch

    {

        public static void Postfix(MainMenuManager __instance)

        {

            MainMenuManagerCapture.Instance = __instance;



            // 外部ウォッチドッグによるクラッシュ復帰起動かどうかをここで確認する

            AutoRehost.CheckRelaunchMarker();



            Modules.DiscordRichPresenceService.UpdatePresence("メニュー画面", "待機中");

        }

    }



    // オンラインホスト状態のラッチ更新 & 新しい部屋に入れたかどうかの判定

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameJoined))]

    class AutoRehostGameJoinedPatch

    {

        public static void Postfix()

        {

            AutoRehost.NotifyGameJoined();

        }

    }



    // 部屋作成を開始した瞬間(「設定を確認」画面などを含む、成功前の段階)。

    // ここでラッチを立てておくことで、部屋作成の途中で切断された場合も立て直し対象にする。

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.OpenCreateGame))]

    class AutoRehostCreateGameStartedPatch

    {

        public static void Postfix()

        {

            AutoRehost.NotifyCreatingGame();

        }

    }



    // 切断検知 → 自動立て直し開始

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]

    class AutoRehostDisconnectedPatch

    {

        public static void Postfix()

        {

            AutoRehost.NotifyDisconnected();

        }

    }



    // ロビー画面に戻るたびに呼ばれる。試合終了後、同じ部屋のロビーに戻ってきた場合、

    // 従来はGameStates.InGameがOnGameJoined(=新しい部屋への参加)時にしかfalseに戻らず、

    // 「試合後の待機中(まだ次の試合が始まっていない)」なのにtrueのままになってしまい、

    // NotifyDisconnectedの試合中判定が誤ってブロックしてしまう不具合があった。ここで確実にリセットする。

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]

    class AutoRehostLobbyStartPatch

    {

        public static void Postfix()

        {

            GameStates.InGame = false;

        }

    }

}

