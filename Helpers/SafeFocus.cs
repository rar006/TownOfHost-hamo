using UnityEngine;

namespace TownOfHost;

/// <summary>
/// 入力欄に自動でフォーカスを当てる時の安全版。
/// ・壊れた(Destroyされた)入力欄に GiveFocus すると、ログも残らずゲームが落ちる。
///   `?.` では「C#の参照があるか」しか見ないので、Unity の null 判定(==)で本当に生きているか確認する。
/// ・全画面の時に自動でフォーカスを当てると、日本語入力(IME)が絡んでゲームが落ちることがあるため、全画面では自動フォーカスしない
///   (入力欄をクリックすれば入力できる)。
/// </summary>
internal static class SafeFocus
{
    public static void Give(TextBoxTMP textBox)
    {
        try
        {
            if (textBox == null || textBox.gameObject == null) return;
            if (!textBox.gameObject.activeInHierarchy || !textBox.isActiveAndEnabled) return;
            if (Screen.fullScreen) return;
            CrashTrace.Mark("SafeFocus.GiveFocus");
            textBox.GiveFocus();
            CrashTrace.Mark("SafeFocus.GiveFocus done");
        }
        catch (System.Exception e)
        {
            Logger.Warn($"SafeFocus: {e.Message}", "SafeFocus");
        }
    }
}
