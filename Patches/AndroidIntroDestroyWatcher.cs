#if HAMO_ANDROID
using System;
using HarmonyLib;

namespace TownOfHost;

/// <summary>
/// Android版のAmong Usには IntroCutscene.OnDestroy が無く、Harmonyでフックできない。
/// 代わりに、イントロ開始(CoBegin)後に「イントロが破棄されたか」を0.1秒ごとに監視し、
/// 破棄された時点で、Windows版が OnDestroy に仕掛けていた3つの処理を呼び出す。
/// </summary>
[HarmonyPatch(typeof(IntroCutscene), nameof(IntroCutscene.CoBegin))]
internal static class AndroidIntroDestroyWatcher
{
    public static void Postfix(IntroCutscene __instance)
    {
        if (__instance == null) return;
        Watch(__instance, 0);
    }

    private static void Watch(IntroCutscene intro, int count)
    {
        _ = new LateTask(() =>
        {
            // 破棄されるまで待つ(最大60秒)
            if (intro != null && count < 600) { Watch(intro, count + 1); return; }
            try
            {
                IntroCutsceneDestroyPatch.Postfix(intro);
                Utils.IntroCutsceneOnDestroyPatchForChat.Postfix();
                Patches.AutoPetAssignPatch.Postfix();
            }
            catch (Exception e)
            {
                Logger.Warn($"AndroidIntroDestroyWatcher: {e}", "AndroidIntro");
            }
        }, 0.1f, "AndroidIntroDestroyWatcher", true);
    }
}
#endif
