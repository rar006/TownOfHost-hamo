using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace TownOfHost.Patches
{
    /// <summary>
    /// 自分のチャット入力欄で記号(★☆♦♥・!?#%&amp;=~^| など)を打てるようにする。
    /// Among Us の入力欄は記号をはじく設定になっているので、自分の画面の入力欄だけ許可する(ホストでなくても効く)。
    /// &lt; &gt; は、チャットで文字の色や大きさを変える指定として読まれてしまうので、全角の ＜ ＞ に置き換える。
    /// </summary>
    [HarmonyPatch(typeof(ChatController), nameof(ChatController.Update))]
    public static class ChatSymbolInputPatch
    {
        private static bool fallbackTried;

        /// <summary>
        /// ゲームのフォントに無い記号(♪ ※ 〜 ♡ など)は、打っても何も表示されない(打てないように見える)。
        /// Windows の日本語フォントを「足りない文字を代わりに出すフォント」として追加する(自分の画面だけ)。
        /// </summary>
        private static void TryAddFallbackFont(TMPro.TMP_Text text)
        {
            if (fallbackTried || text?.font == null) return;
            fallbackTried = true;
            try
            {
                var names = new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic", "Segoe UI Symbol" };
                var osFont = UnityEngine.Font.CreateDynamicFontFromOSFont(names, 64);
                if (osFont == null) { Logger.Warn("代わりのフォントが見つかりませんでした", "ChatSymbol"); return; }
                var asset = TMPro.TMP_FontAsset.CreateFontAsset(osFont);
                if (asset == null) { Logger.Warn("代わりのフォントを作れませんでした", "ChatSymbol"); return; }
                asset.name = "TOHhamoSymbolFallback";
                var table = text.font.fallbackFontAssetTable;
                if (table == null) text.font.fallbackFontAssetTable = table = new Il2CppSystem.Collections.Generic.List<TMPro.TMP_FontAsset>();
                table.Add(asset);
                Logger.Info($"記号用の代わりのフォントを追加しました({text.font.name})", "ChatSymbol");
            }
            catch (System.Exception e)
            {
                Logger.Warn($"代わりのフォントを追加できませんでした: {e.Message}", "ChatSymbol");
            }
        }

        public static void Postfix(ChatController __instance)
        {
            var box = __instance?.freeChatField?.textArea;
            if (box == null) return;
            TryAddFallbackFont(box.outputText);
            // ゲーム側で設定が戻されることがあるので、毎フレーム付け直す
            box.AllowSymbols = true;
            box.allowAllCharacters = true;
            box.AllowPaste = true;
            var text = box.text;
            if (!string.IsNullOrEmpty(text) && (text.Contains('<') || text.Contains('>')))
                box.SetText(text.Replace('<', '＜').Replace('>', '＞'));
        }

        /// <summary>今チャットの入力欄か</summary>
        internal static bool IsChatBox(TextBoxTMP box)
        {
            if (box == null || !DestroyableSingleton<HudManager>.InstanceExists) return false;
            var chatBox = DestroyableSingleton<HudManager>.Instance.Chat?.freeChatField?.textArea;
            return chatBox != null && chatBox == box;
        }
    }

    /// <summary>
    /// 入力欄に打てる文字か(TextBoxTMP.IsCharAllowed)。チャット欄だけ、&lt; &gt; 以外の記号も通す。
    /// 同じ名前で static 版もあるので、インスタンス版をはっきり指定してパッチする。
    /// </summary>
    [HarmonyPatch]
    public static class ChatSymbolCharAllowedPatch
    {
        public static MethodBase TargetMethod()
            => typeof(TextBoxTMP).GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .First(m => m.Name == nameof(TextBoxTMP.IsCharAllowed) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(char));

        public static bool Prefix(TextBoxTMP __instance, [HarmonyArgument(0)] char c, ref bool __result)
        {
            if (!ChatSymbolInputPatch.IsChatBox(__instance)) return true; // チャット以外の入力欄はそのまま
            if (char.IsControl(c)) return true; // Enter・BackSpace などは元の処理に任せる
            __result = true; // < > は打てても、上で全角に置き換える
            return false;
        }
    }

    /// <summary>static 版の IsCharAllowed。どの入力欄か分からないので、チャットを開いている間だけ記号を通す</summary>
    [HarmonyPatch]
    public static class ChatSymbolCharAllowedStaticPatch
    {
        public static bool Prepare()
            => typeof(TextBoxTMP).GetMethods(BindingFlags.Static | BindingFlags.Public).Any(m => m.Name == nameof(TextBoxTMP.IsCharAllowed));

        public static MethodBase TargetMethod()
            => typeof(TextBoxTMP).GetMethods(BindingFlags.Static | BindingFlags.Public)
                .First(m => m.Name == nameof(TextBoxTMP.IsCharAllowed) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(char));

        public static bool Prefix([HarmonyArgument(0)] char c, ref bool __result)
        {
            if (char.IsControl(c) || !DestroyableSingleton<HudManager>.InstanceExists) return true;
            var chat = DestroyableSingleton<HudManager>.Instance.Chat;
            if (chat == null || !chat.IsOpenOrOpening) return true;
            __result = true;
            return false;
        }
    }
}
