using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using BepInEx;

namespace TownOfHost.Modules;

/// <summary>
/// Among Usとは別プロセスで動くPowerShell Watchdogを管理する。
/// ゲームがクラッシュまたはハングしてMOD内コードが停止しても、Watchdogが再起動して
/// restart_request.flagを置くため、起動後のAutoRehostが同じ設定でロビーを再作成できる。
/// </summary>
public static class WatchdogLauncher
{
    private const string MutexName = "Local\\TOHhamoWatchdog";
    private const string ResourceName = "TownOfHost.Resources.TOHhamoWatchdog.ps1";
    private const string TaskName = "TOHhamoWatchdog";

        // 日本語・OneDriveを含むデスクトップパスを避け、ゲームと同じBepInEx配下に統一する。
        // Among Usの既定導入先では英数字パスとなり、PowerShellのパス解決も安定する。
        public static string DataDirectory => Path.Combine(Paths.BepInExRootPath, "TOHhamo_Logs");
        private static string BaseDirectory => DataDirectory;
        private static string ScriptPath => Path.Combine(BaseDirectory, "TOHhamoWatchdog.ps1");
    private static string StopFlagPath => Path.Combine(BaseDirectory, "watchdog-stop.flag");
    private static string LauncherLogPath => Path.Combine(BaseDirectory, "watchdog_launcher.log");

    private static volatile bool startInFlight;
    // 埋め込みスクリプトが存在しないビルドでは、毎FixedUpdateで再起動を試みると
    // 同じ警告が大量に出力されるため、この起動中は再試行しない。
    private static volatile bool scriptResourceUnavailable;
    // PowerShellが即時終了した場合は、毎FixedUpdateで再起動して画面を点滅させない。
    private static volatile bool watchdogLaunchUnavailable;
    private static DateTime lastStartAttemptUtc = DateTime.MinValue;
    private const int StartRetrySeconds = 60;
    private static bool reconciled;
    private static bool lastWanted;

    /// <summary>WindowsのPowerShellを利用できる環境だけで有効。</summary>
    public static bool IsSupported => Environment.OSVersion.Platform == PlatformID.Win32NT;

    /// <summary>メインメニュー到達前の終了をユーザーの意図的終了と誤認しないためのラッチ。</summary>
    public static bool MainMenuReached { get; private set; }

    public static bool IsRunning
    {
        get
        {
            try
            {
                if (Mutex.TryOpenExisting(MutexName, out var mutex))
                {
                    mutex.Dispose();
                    return true;
                }
            }
            catch { }
            return false;
        }
    }

    /// <summary>メインメニュー到達時に呼ぶ。再起動マーカー消費とWatchdog有効化の起点。</summary>
    public static void NotifyMainMenuReached()
    {
        MainMenuReached = true;
        ReconcileWithOption();
    }

    /// <summary>
    /// 自動再起動はWindows環境で常時有効にし、Watchdogの起動状態を維持する。
    /// HealthLogのFixedUpdateから軽量に繰り返し呼ばれる。
    /// </summary>
    public static void ReconcileWithOption()
    {
        // クラッシュ時の自動再起動は廃止した。
        // 以前のバージョンで起動済みのWatchdogが残っていれば、起動後に一度だけ停止を要求して終了させる。
        if (!MainMenuReached || reconciled) return;
        reconciled = true;
        if (IsRunning) Stop();
    }

    /// <summary>Watchdogをゲームプロセスから切り離して起動する。</summary>
    public static void Start()
    {
        // クラッシュ時の自動再起動は廃止したため、Watchdogは起動しない。
    }

    private static void StartWorker()
    {
        try
        {
            Directory.CreateDirectory(BaseDirectory);
            TryDelete(StopFlagPath);

            if (!MaterializeScript())
            {
                // このスクリプト資源は実行中に復活しないため、以後のFixedUpdateからの
                // 再試行を止める。警告はこの1回だけ出力される。
                scriptResourceUnavailable = true;
                Logger.Warn("Watchdog script resource was not found; automatic crash recovery is disabled.", "Watchdog");
                return;
            }

            // 旧版で登録されたタスクが残っている場合は先に削除する。
            // タスクスケジューラ経由では、PowerShellの構文・権限エラー時に
            // コンソールが何度も開閉することがあるため、非表示の直接起動だけを使用する。
            RunSchtasks($"/Delete /F /TN {TaskName}");
            if (LaunchDirect())
            {
                Logger.Info("Crash Watchdog launched in the background.", "Watchdog");
            }
            else
            {
                // 一時的なMutex残留・PowerShell起動競合でも次回の自動再試行を止めない。
                // StartRetrySeconds後に再試行し、詳細は専用ログへ残す。
                const string message = "Watchdog PowerShell exited immediately; it will retry automatically. See watchdog_launcher.log.";
                WriteLauncherLog(message);
                Logger.Warn(message, "Watchdog");
            }
        }
        catch (Exception e)
        {
            Logger.Error($"Watchdog start failed: {e.Message}", "Watchdog");
        }
        finally
        {
            startInFlight = false;
        }
    }

    /// <summary>通常終了時にWatchdogへ停止を要求する。</summary>
    public static void Stop()
    {
        try
        {
            Directory.CreateDirectory(BaseDirectory);
            File.WriteAllText(StopFlagPath, DateTime.UtcNow.ToString("O"));
            if (IsSupported)
            {
                var thread = new Thread(() => RunSchtasks($"/Delete /F /TN {TaskName}"))
                {
                    IsBackground = true,
                    Name = "TOHhamoWatchdogStop"
                };
                thread.Start();
            }
        }
        catch (Exception e)
        {
            Logger.Warn($"Watchdog stop request failed: {e.Message}", "Watchdog");
        }
    }

    /// <summary>正常なユーザー終了時だけWatchdogを止める。クラッシュ・強制終了では呼ばれない。</summary>
    public static void OnGameQuit()
    {
        if (!MainMenuReached) return;
        Stop();
    }

    private static bool MaterializeScript()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            // csprojで固定した正式名を最優先にし、過去のビルド環境で名前空間が変化した場合も
            // ファイル名末尾から安全に探索して抽出する。
            var resourceName = ResourceName;
            var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                resourceName = Array.Find(
                    assembly.GetManifestResourceNames(),
                    name => name.EndsWith(".TOHhamoWatchdog.ps1", StringComparison.OrdinalIgnoreCase));
                if (resourceName != null) stream = assembly.GetManifestResourceStream(resourceName);
            }
            if (stream == null) return false;

            using (stream)
            using (var file = File.Create(ScriptPath))
            {
                stream.CopyTo(file);
            }
            Logger.Info($"Watchdog script extracted from embedded resource: {resourceName}", "Watchdog");
            return true;
        }
        catch (Exception e)
        {
            Logger.Warn($"Watchdog script extraction failed: {e.Message}", "Watchdog");
            return false;
        }
    }

    private static bool LaunchDirect()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{ScriptPath}\" -HealthDirectory \"{BaseDirectory}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = BaseDirectory
            });
            if (process == null)
            {
                WriteLauncherLog("PowerShell process could not be created.");
                return false;
            }

            // 正常な監視プロセスは常駐する。直後に終了した場合は再起動を止め、
            // 終了コードを専用ログへ残す。
            if (process.WaitForExit(1500))
            {
                var standardError = process.StandardError.ReadToEnd().Trim();
                var standardOutput = process.StandardOutput.ReadToEnd().Trim();
                var detail = !string.IsNullOrEmpty(standardError) ? standardError : standardOutput;
                WriteLauncherLog($"PowerShell exited immediately. ExitCode={process.ExitCode} Detail={detail}");
                return false;
            }
            return true;
        }
        catch (Exception e)
        {
            WriteLauncherLog($"Direct Watchdog launch failed: {e}");
            Logger.Warn($"Direct Watchdog launch failed: {e.Message}", "Watchdog");
            return false;
        }
    }

    private static void WriteLauncherLog(string message)
    {
        try
        {
            Directory.CreateDirectory(BaseDirectory);
            File.AppendAllText(LauncherLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        }
        catch { }
    }

    private static bool RunSchtasks(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process == null) return false;
            if (!process.WaitForExit(8000))
            {
                try { process.Kill(); }
                catch { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}
