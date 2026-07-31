using System;
using System.IO;
using HarmonyLib;
using InnerNet;

namespace TownOfHost.Modules;

// ===== 配信者向け: ルームコード・部屋タイマー・人数のテキスト自動保存 =====
// OBS等の「テキスト(ファイルから読み込む)」ソースとして使えるよう、
// TOHhm_DATAフォルダ内に以下の3ファイルを常に最新の内容で保存し続ける。
//   RoomCode.txt    : 現在のルームコード(6文字)
//   RoomTimer.txt   : ロビー中は10分からのカウントダウン(mm:ss)。
//                      試合が始まったら「{N}試合目」の表示に切り替わる。
//   RoomPlayers.txt : 現在の参加人数/最大人数
//
// ロビー画面(LobbyBehaviour)に入った瞬間をカウントダウン開始時刻とする。
// 試合開始(AmongUsClient.CoStartGame)を検知したら、以降はGameCountを表示する。
// 次にロビーへ戻ったら、カウントダウンを再度10分からやり直す。
public static class RoomStatusTextSaver
{
    private static readonly string RoomCodePath = Path.Combine(Main.BaseDirectory, "RoomCode.txt");
    private static readonly string RoomTimerPath = Path.Combine(Main.BaseDirectory, "RoomTimer.txt");
    private static readonly string RoomPlayersPath = Path.Combine(Main.BaseDirectory, "RoomPlayers.txt");

    // カウントダウンの初期値(秒)。10分。
    private const float CountdownStartSeconds = 10f * 60f;

    private static DateTime? lobbyStartedAt;
    private static bool isInGame;

    // 毎フレーム書き込むと負荷になるため、一定間隔ごとにのみ実際のファイル書き込みを行う。
    private static float writeIntervalTimer;
    private const float WriteIntervalSeconds = 1f;

    private static void EnsureDirectory()
    {
        if (!Directory.Exists(Main.BaseDirectory))
            Directory.CreateDirectory(Main.BaseDirectory);
    }

    private static void SafeWriteAllText(string path, string content)
    {
        try
        {
            EnsureDirectory();
            File.WriteAllText(path, content);
        }
        catch (Exception e)
        {
            Logger.Exception(e, "RoomStatusTextSaver");
        }
    }

    private static string GetCurrentRoomCode()
    {
        try
        {
            if (AmongUsClient.Instance == null) return "";
            return GameCode.IntToGameName(AmongUsClient.Instance.GameId);
        }
        catch
        {
            return "";
        }
    }

    private static void WriteAll()
    {
        var roomCode = GetCurrentRoomCode();
        SafeWriteAllText(RoomCodePath, roomCode);

        string timerText;
        if (isInGame)
        {
            // 試合開始後は「何試合目か」を表示する。
            timerText = $"{Main.GameCount}試合目";
        }
        else
        {
            var elapsed = lobbyStartedAt.HasValue ? DateTime.UtcNow - lobbyStartedAt.Value : TimeSpan.Zero;
            var remainingSeconds = CountdownStartSeconds - (float)elapsed.TotalSeconds;
            if (remainingSeconds < 0f) remainingSeconds = 0f;
            var remaining = TimeSpan.FromSeconds(remainingSeconds);
            timerText = $"{(int)remaining.TotalMinutes:00}:{remaining.Seconds:00}";
        }
        SafeWriteAllText(RoomTimerPath, timerText);

        var players = AmongUsClient.Instance?.allClients?.Count ?? 0;
        var maxPlayers = Main.NormalOptions?.MaxPlayers ?? 15;
        SafeWriteAllText(RoomPlayersPath, $"{players}/{maxPlayers}");
    }

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
    private static class LobbyBehaviourStartPatch
    {
        public static void Postfix()
        {
            lobbyStartedAt = DateTime.UtcNow;
            isInGame = false;
            writeIntervalTimer = 0f;
            WriteAll();
        }
    }

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Update))]
    private static class LobbyBehaviourUpdatePatch
    {
        public static void Postfix()
        {
            writeIntervalTimer += UnityEngine.Time.deltaTime;
            if (writeIntervalTimer < WriteIntervalSeconds) return;
            writeIntervalTimer = 0f;
            WriteAll();
        }
    }

    // 試合開始を検知して、以降は「{N}試合目」の表示に切り替える。
    // GameCountのインクリメント(UtilsGameLog.Reset)も同じCoStartGameのPostfixで
    // 行われるため、順序保証のため少し遅延させてから切り替える。
    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.CoStartGame))]
    private static class CoStartGamePatch
    {
        public static void Postfix()
        {
            _ = new LateTask(() =>
            {
                isInGame = true;
                writeIntervalTimer = 0f;
                WriteAll();
            }, 0.5f, "RoomStatusTextSaver.GameStarted", NoLog: true);
        }
    }
}

