using AmongUs.GameOptions;
using HarmonyLib;
namespace TownOfHost
{
    // バニラのかくれんぼは使えないようにする(選んでもクラシックのままにする)。
    // 以前はかくれんぼを選ぶと MOD を止めてしまい、部屋も立てられなかった。
    [HarmonyPatch(typeof(GameOptionsManager), nameof(GameOptionsManager.SwitchGameMode))]
    class SwitchGameModePatch
    {
        public static void Prefix(ref GameModes gameMode)
        {
            if (gameMode != GameModes.HideNSeek) return;
            Logger.Info("バニラのかくれんぼは使えないため、クラシックのままにします", "SwitchGameMode");
            gameMode = GameModes.Normal;
        }

        public static void Postfix() => Main.HnSFlag = false;
    }
}
