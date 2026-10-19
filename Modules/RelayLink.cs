using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TownOfHost.Modules;

public static class RelayLink
{
    public const string Url = "https://raw.githubusercontent.com/rar006/TownOfHost-hamo/main/relay.json";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8) };

    public static void Start()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // GitHub 側のキャッシュを避けるため、時刻をクエリに付ける
                var json = await Client.GetStringAsync($"{Url}?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}").ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("baseUrl", out var b) || b.ValueKind != JsonValueKind.String) return;
                Apply(b.GetString());
            }
            catch (Exception e)
            {
                Logger.Warn($"relay.json を取得できませんでした(今までのリンクを使います): {e.Message}", "RelayLink");
            }
        });
    }

    private static void Apply(string baseUrl)
    {
        baseUrl = (baseUrl ?? "").Trim().TrimEnd('/');
        if (!baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return;
        Main.MatchmakingRelayUrl = baseUrl + "/matchmaking";
        BugReportRemoteConfig.EndpointUrl = baseUrl + "/api/bugreport";
        LobbyChatRemoteConfig.EndpointUrl = baseUrl + "/api/lobbychat";
        SurveyRemoteConfig.EndpointUrl = baseUrl + "/api/survey"; // 二つ名の接続先もここから作られる
        Logger.Info($"リンクを更新しました: {baseUrl}", "RelayLink");
    }
}
