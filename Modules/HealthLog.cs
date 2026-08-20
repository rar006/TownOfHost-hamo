using System;

using System.IO;

using HarmonyLib;



namespace TownOfHost.Modules;



// ===== クラッシュ自動再起動用 心拍ログ =====

// From: TownOfHost_hamo (Aeterna-End-K-not の HealthLog.cs を参考に、必要最小限だけ移植した軽量版)

//

// 一定間隔(既定5秒)で「生きています」という時刻を専用ファイルに書き出す。

// 外部ウォッチドッグ(TOHhamoWatchdog.ps1)がこのファイルの更新時刻を監視し、

// 一定時間更新が止まったら「フリーズ/クラッシュした」と判断して再起動する。

//

// 注意: このクラス自体はプロセス生死の判定はしない(それは外部スクリプトの仕事)。

// あくまで「今どこまで生きていたか」の足跡を残すだけ。

public static class HealthLog

{

    private const long HeartbeatIntervalSeconds = 5;



    private static bool inited;

    private static DateTime lastBeat = DateTime.MinValue;

    public static string FilePath { get; private set; }



    private static void EnsureInit()

    {

        if (inited) return;

        inited = true;



        try

        {

            string basePath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

            string dir = Path.Combine(basePath, "TOHhamo_Logs");

            Directory.CreateDirectory(dir);

            FilePath = Path.Combine(dir, "TOHhamo-Health.log");



            // 前回セッションの最後の状態を軽く退避しておく(クラッシュ直前の様子を後から確認できるように)

            if (File.Exists(FilePath))

            {

                string prev = Path.Combine(dir, "TOHhamo-Health.prev.log");

                try

                {

                    if (File.Exists(prev)) File.Delete(prev);

                    File.Move(FilePath, prev);

                }

                catch { /* 退避に失敗しても致命的ではないので握りつぶす */ }

            }



            Write($"SESSION START t={DateTime.UtcNow:O}");

        }

        catch (Exception e)

        {

            Logger.Error($"HealthLogの初期化に失敗: {e.Message}", "HealthLog");

        }

    }



    private static void Write(string line)

    {

        if (FilePath == null) return;

        try

        {

            File.AppendAllText(FilePath, line + Environment.NewLine);

        }

        catch { /* ログ書き込み失敗は握りつぶす(本編の動作を止めない) */ }

    }



    /// <summary>

    /// 毎フレーム(FixedUpdate)呼び出される。内部で間隔を見て、必要な時だけファイルに書き込む。

    /// </summary>

    public static void Tick()

    {

        EnsureInit();



        var now = DateTime.UtcNow;

        if ((now - lastBeat).TotalSeconds < HeartbeatIntervalSeconds) return;

        lastBeat = now;



        try

        {

            bool host = false;

            try { host = AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost; }

            catch { }



            int players = 0;

            try { players = GameData.Instance != null ? GameData.Instance.PlayerCount : 0; }

            catch { }



            string state = "?";

            try

            {

                state = TownOfHost.GameStates.IsLobby ? "Lobby"

                    : TownOfHost.GameStates.IsMeeting ? "Meeting"

                    : TownOfHost.GameStates.IsInGame ? "InGame"

                    : "Other";

            }

            catch { }



            Write($"HB t={now:O} host={host} players={players} server={state}");

        }

        catch { /* 心拍そのものが本編を落とさないように */ }

    }



    /// <summary>

    /// 切断・キック等が発生した時に軽く記録しておく(任意で呼び出し用)。

    /// </summary>

    public static void NoteDisconnect(string reason)

    {

        EnsureInit();

        Write($"DISCONNECT reason={reason} t={DateTime.UtcNow:O}");

    }

}



// FixedUpdateへのフック。既存のClientPatch.csと同じパターンでHealthLog.Tick()を毎フレーム呼び出す。

[HarmonyPatch(typeof(InnerNet.InnerNetClient), nameof(InnerNet.InnerNetClient.FixedUpdate))]

public static class HealthLogFixedUpdatePatch

{

    [HarmonyPostfix]

    public static void Postfix()

    {

        // 設定のON/OFFを外部Watchdogへ反映する。OFF時も停止要求のために呼ぶ。

        WatchdogLauncher.ReconcileWithOption();

        if (!Main.AutoRestartOnCrash.Value) return;

        HealthLog.Tick();

    }

}

