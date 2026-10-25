namespace TownOfHost;

/// <summary>
/// Android版のTextBoxTMPには caretPos が存在しない(カーソル移動をキーボード矢印で行うのはWindows向けの仕様)。
/// Android用ビルド(HAMO_ANDROID)では「常に末尾」として扱い、書き込みは何もしない。
/// </summary>
internal static class TextBoxCaretCompat
{
    public static int GetCaret(this TextBoxTMP box)
    {
#if HAMO_ANDROID
        return box?.text?.Length ?? 0;
#else
        return box.caretPos;
#endif
    }

    public static void SetCaret(this TextBoxTMP box, int value)
    {
#if !HAMO_ANDROID
        box.caretPos = value;
#endif
    }
}
