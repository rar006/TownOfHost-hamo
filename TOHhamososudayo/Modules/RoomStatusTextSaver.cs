using System;
using System.IO;
using HarmonyLib;
using InnerNet;

namespace TownOfHost.Modules;

// ===== 配信者向け: ルームコード・ロビー経過時間・人数のテキスト自動保存 =====
// OBS等の「テキスト(ファイルから読み込む)」ソースとして使えるよう、
// TOHhm_DATAフォルダ内に以下の3ファイルを常に最新の内容で保存し続ける。
//   RoomCode.txt    : 現在のルームコード(6文字)
//   RoomTimer.txt   : ロビーで待機を始めてからの経過時間(mm:ss形式)
//   RoomPlayers.txt : 現在の参加人数/最大人数
//
// ロビー画面(LobbyBehaviour)に入った瞬間を開始時刻とし、Updateのたびに
// 経過時間を再計算する。ロビーを離れたら(ゲーム開始・退出)、次にロビーへ
// 戻った際に改めて計測をやり直す。
public static class RoomStatusTextSaver
{
    private static readonly string RoomCodePath = Path.Combine(Main.BaseDirectory, "RoomCode.txt");
    private static readonly string RoomTimerPath = Path.Combine(Main.BaseDirectory, "RoomTimer.txt");
    private static readonly string RoomPlayersPath = Path.Combine(Main.BaseDirectory, "RoomPlayers.txt");

    private static DateTime? lobbyStartedAt;

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

        var elapsed = lobbyStartedAt.HasValue ? DateTime.UtcNow - lobbyStartedAt.Value : TimeSpan.Zero;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        var timerText = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
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
}
