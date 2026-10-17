using System;
using System.IO;

namespace TownOfHost;

/// <summary>
/// ゲームごと落ちる(ログも残らない)不具合の調査用の足跡。
/// BepInEx のログは落ちる直前の分が書き込まれずに消えるので、1行ずつその場でファイルへ書き出す。
/// 保存先: BepInEx/TOHhamo_Logs/TOHhamo-Trace.log (起動ごとに前回分を .prev.log へ退避)
/// </summary>
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
