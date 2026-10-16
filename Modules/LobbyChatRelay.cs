using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TownOfHost.Modules;

// ===== Discordの /chat コマンドで送られたアナウンスをロビーチャットに表示する =====
// 特定のDiscordサーバー(管理者限定)から /chat と送信すると、
// 現在ロビーにいる全員のAmong Usチャットにアナウンスとして表示される。
// ポーリングはBugReportPollLobbyStartPatchと同じタイミング(ロビー内・15秒間隔)で行う。
public static class LobbyChatRelay
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static string _lastShownId = "";

    private class Announcement
    {
        public string Id { get; set; } = "";
        public string Text { get; set; } = "";
        public string Author { get; set; } = "";
        public string Target { get; set; } = "";
        public string Tag { get; set; } = "hamo";
        public long At { get; set; } = 0;
    }

    // サーバーに繋がらない間は、間隔を空けて(最大5分)問い合わせる。繋がったら元の間隔に戻す(軽量化)
    private static int _failCount;
    private static DateTime _retryAfter = DateTime.MinValue;

    public static async Task PollAsync()
    {
        if (string.IsNullOrEmpty(LobbyChatRemoteConfig.EndpointUrl)) return;
        if (DateTime.UtcNow < _retryAfter) return;
        // ホストが全員に送る(MOD導入者がそれぞれ送ると、同じメッセージが何回も出てしまう)
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;

        try
        {
            var roomCode = GetCurrentRoomCode();
            var url = $"{LobbyChatRemoteConfig.EndpointUrl.TrimEnd('/')}/current?roomCode={Uri.EscapeDataString(roomCode)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(LobbyChatRemoteConfig.ApiKey))
                request.Headers.Add("Authorization", $"Bearer {LobbyChatRemoteConfig.ApiKey}");

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            _failCount = 0;
            _retryAfter = DateTime.MinValue;
            if (!response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NoContent) return;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var announcement = JsonSerializer.Deserialize<Announcement>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (announcement == null || string.IsNullOrEmpty(announcement.Id)) return;
            if (announcement.Id == _lastShownId) return; // 既に表示済み

            _lastShownId = announcement.Id;

            // ロビーに入った直後、過去のアナウンス(かなり古いもの)を毎回表示しないよう、
            // 直近5分以内のものだけ表示する。
            var ageMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - announcement.At;
            if (ageMs > TimeSpan.FromMinutes(5).TotalMilliseconds) return;

            if (!string.IsNullOrWhiteSpace(announcement.Text))
            {
                // 要望により、送信者名ではなく "[タグ] 内容" という表示形式にしている。
                // タグはDiscord側の /chat コマンドで毎回変更できる(省略時は "hamo")。
                var tag = string.IsNullOrWhiteSpace(announcement.Tag) ? "hamo" : announcement.Tag;
                var text = $"<size=90%><color=#ffcc00>[{tag}]</color> {announcement.Text}</size>";
                // ここは通信後のワーカースレッドなので、チャット送信(ゲームのオブジェクト操作)はメインスレッドで行う
                MainThreadDispatcher.Enqueue(() => Utils.SendMessage(text, byte.MaxValue));
            }
        }
        catch (Exception ex)
        {
            _failCount++;
            var wait = TimeSpan.FromSeconds(Math.Min(300, 12 * Math.Pow(2, Math.Min(_failCount, 5))));
            _retryAfter = DateTime.UtcNow + wait;
            Logger.Warn($"ロビーチャットアナウンスのポーリングに失敗しました({_failCount}回目・次は{(int)wait.TotalSeconds}秒後): {ex.Message}", "LobbyChatRelay");
        }
    }

    private static string GetCurrentRoomCode()
    {
        try
        {
            return AmongUsClient.Instance?.GameId != 0
                ? InnerNet.GameCode.IntToGameName(AmongUsClient.Instance.GameId)
                : "";
        }
        catch
        {
            return "";
        }
    }
}

public static class LobbyChatRemoteConfig
{
    /// <summary>Discord Bot側のロビーチャットAPIベースURL</summary>
    public static string EndpointUrl = "https://hmbot.nkzk.win/api/lobbychat";

    /// <summary>MOD↔Bot間の認証キー。Bot側の.env(SURVEY_API_SECRET)と必ず同じ値にすること。</summary>
    public static string ApiKey = "e14ow2cvdpx5";
}
