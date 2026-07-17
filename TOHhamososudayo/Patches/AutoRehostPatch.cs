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

    // 切断検知 → 自動立て直し開始
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnDisconnected))]
    class AutoRehostDisconnectedPatch
    {
        public static void Postfix()
        {
            AutoRehost.NotifyDisconnected();
        }
    }
}
