using System;
using System.IO;

namespace TownOfHost;

internal static class CrashTrace
{
    private static string path;
    private static bool inited;

    public static void Mark(string where)
    {
        try
        {
            if (!inited)
            {
                inited = true;
                var dir = Modules.WatchdogLauncher.DataDirectory;
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, "TOHhamo-Trace.log");
                if (File.Exists(path))
                {
                    var prev = Path.Combine(dir, "TOHhamo-Trace.prev.log");
                    if (File.Exists(prev)) File.Delete(prev);
                    File.Move(path, prev);
                }
            }
            if (path == null) return;
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {where} fullscreen={UnityEngine.Screen.fullScreen}{Environment.NewLine}");
        }
        catch { }
    }
}
