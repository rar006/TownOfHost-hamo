using System;
using DiscordRPC;
using DiscordRPC.Logging;

namespace TownOfHost.Modules;

// ===== Discord Rich Presence =====
// From: TownOfHost_hamo
//
// Discordのプレイ中ステータス(アクティビティ)に「TownOfHost-hamo」と表示するための機能。
// Among Us自体にはネイティブのDiscord連携が無いため、DiscordRPCライブラリ(NuGet)を使い、
// 専用のDiscordアプリケーション経由でアクティビティを更新する。
//
// 注意: DiscordMatchmakingRelayService(MakePublicDiscordPatch.cs)とは別物。
// あちらは「Discordチャンネルへの部屋情報投稿(Bot経由)」、こちらは
// 「本人のDiscordプロフィール上のプレイ中ステータス表示」を担当する。
public static class DiscordRichPresenceService
{
    // Discord Developer Portal で発行されたこのMOD専用のApplication ID
    private const string ApplicationId = "1530583847333527683";

    private static DiscordRpcClient _client;
    private static bool _initialized;
    private static string _lastState = "";
    private static string _lastDetails = "";
    private static DateTime? _sessionStartUtc;

    public static void Initialize()
    {
        if (_initialized) return;
        if (Main.IsAndroid()) return; // Android版はDiscordデスクトップアプリと連携できないため対象外

        // ===== 現在, 一時的に強制無効化中 =====
        // この環境ではDiscordRPCライブラリ内部の接続スレッド(MainLoop)が
        // Newtonsoft.Jsonのバージョン競合で例外を出し続け、ログが際限なく膨れ上がる問題が
        // 解決できていない(この例外は別スレッドで発生するため、こちら側のtry-catchでは捕まえられない)。
        // 設定のON/OFFに関わらず起動しないようにして、ログ肥大化を確実に止める。
        // 直った場合はこの return を削除すること。
        return;
#pragma warning disable CS0162 // 到達不能コード
        if (!Main.EnableDiscordRichPresence.Value) return;
#pragma warning restore CS0162

        try
        {
            _client = new DiscordRpcClient(ApplicationId)
            {
                Logger = new ConsoleLogger(LogLevel.Warning, false)
            };
            _client.OnReady += (sender, e) =>
                Logger.Info($"Discord Rich Presenceに接続しました (User: {e.User?.Username})", "DiscordRPC");
            _client.OnConnectionFailed += (sender, e) =>
                Logger.Warn("Discord Rich Presenceへの接続に失敗しました(Discordアプリが起動していない可能性があります)", "DiscordRPC");

            _client.Initialize();
            _sessionStartUtc = DateTime.UtcNow;
            _initialized = true;

            UpdatePresence("TownOfHost-hamo", "起動中");
        }
        catch (Exception e)
        {
            Logger.Error($"DiscordRichPresenceの初期化に失敗: {e.Message}", "DiscordRPC");
        }
    }

    /// <summary>
    /// 現在の状態をDiscordのプレイ中ステータスに反映する。
    /// details: 大きい方の行(例: "ロビーで待機中" / "ゲーム中")
    /// state: 小さい方の行(例: 部屋コードや人数など)
    /// </summary>
    public static void UpdatePresence(string details, string state)
    {
        if (!_initialized || _client == null) return;
        if (!Main.EnableDiscordRichPresence.Value)
        {
            Shutdown();
            return;
        }

        // 変化がない場合は余計な更新RPCを送らない
        if (details == _lastDetails && state == _lastState) return;
        _lastDetails = details;
        _lastState = state;

        try
        {
            _client.SetPresence(new RichPresence
            {
                // ゲーム名部分。ここに常に "TownOfHost-hamo" と表示させる。
                Details = $"TownOfHost-hamo",
                State = $"{details} - {state}",
                Timestamps = _sessionStartUtc.HasValue ? new Timestamps(_sessionStartUtc.Value) : null,
                Assets = new DiscordRPC.Assets
                {
                    LargeImageKey = "logo",
                    LargeImageText = "TownOfHost-hamo",
                }
            });
        }
        catch (Exception e)
        {
            Logger.Error($"DiscordRichPresenceの更新に失敗: {e.Message}", "DiscordRPC");
        }
    }

    /// <summary>毎フレーム呼ぶ(コールバック処理に必要)</summary>
    public static void Invoke()
    {
        if (!_initialized || _client == null) return;
        try
        {
            _client.Invoke();
        }
        catch { /* コールバック処理の失敗は握りつぶし、本編を止めない */ }
    }

    public static void Shutdown()
    {
        if (!_initialized || _client == null) return;
        try
        {
            _client.Dispose();
        }
        catch { }
        finally
        {
            _client = null;
            _initialized = false;
        }
    }
}
