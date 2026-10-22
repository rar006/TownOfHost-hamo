using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using TownOfHost.Roles.Core;
using UnityEngine;

namespace TownOfHost.Patches;

// GM中、会議開始時にチャットを自動で開く。
// 会議が始まった瞬間はまだチャットが使えないことがあり、1回だけ開こうとすると開かないままになっていたため、
// 開けるようになるまで少しの間やり直す(開いた後にプレイヤーが閉じたら、それ以上は開かない)。
[HarmonyPatch(typeof(MeetingHud), nameof(MeetingHud.Start))]
public static class GMAutoChatPatch
{
    private const float TryInterval = 0.25f;
    private const float GiveUpSeconds = 8f;

    public static void Postfix()
    {
        if (!AmongUsClient.Instance.AmHost) return;
        if (!Options.OptionGMAutoChat.GetBool()) return;
        if (PlayerControl.LocalPlayer == null || !PlayerControl.LocalPlayer.Is(CustomRoles.GM)) return;
        var hud = DestroyableSingleton<HudManager>.Instance;
        if (hud == null) return;
        hud.StartCoroutine(CoOpenChat().WrapToIl2Cpp());
    }

    private static System.Collections.IEnumerator CoOpenChat()
    {
        var waited = 0f;
        while (waited < GiveUpSeconds)
        {
            if (MeetingHud.Instance == null) yield break; // 会議が終わった
            var chat = DestroyableSingleton<HudManager>.Instance?.Chat;
            if (chat != null)
            {
                if (chat.IsOpenOrOpening) yield break; // 開けた
                var buttonReady = chat.chatButton == null || chat.chatButton.gameObject.activeInHierarchy;
                if (chat.gameObject.activeInHierarchy && buttonReady)
                {
                    chat.Toggle();
                    Logger.Info("GM自動チャット: チャットを開きました", "GMAutoChat");
                }
            }
            yield return new WaitForSeconds(TryInterval);
            waited += TryInterval;
        }
        Logger.Info("GM自動チャット: チャットを開けませんでした", "GMAutoChat");
    }
}
