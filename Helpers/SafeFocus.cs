using UnityEngine;

namespace TownOfHost;

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
