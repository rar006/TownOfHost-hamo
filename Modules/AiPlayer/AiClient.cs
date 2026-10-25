using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace TownOfHost.Modules.AiPlayer;

// AI の設定ファイル(TOHhm_DATA/AIPlayer.json)。API キーは設定(部屋の参加者にも送られる)に入れず、ここに置く。
public static class AiConfig
{
    public static string FilePath => Path.GetFullPath(Path.Combine("TOHhm_DATA", "AIPlayer.json"));

    public sealed class Data
    {
        public string apiKey { get; set; } = "";
        public string model { get; set; } = "claude-opus-5-5";
        public string effort { get; set; } = "low";
        public string localEndpoint { get; set; } = "http://localhost:11434";
        public string localModel { get; set; } = "qwen2.5:7b";
        public string openaiKey { get; set; } = "";
        public string openaiModel { get; set; } = "gpt-4o-mini";
        public string geminiKey { get; set; } = "";
        public string geminiModel { get; set; } = "gemini-2.5-flash";
    }

    public static void EnsureFile()
    {
        try
        {
            if (File.Exists(FilePath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Data(), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { Logger.Warn($"AIの設定ファイルを作れませんでした: {e.Message}", "AiConfig"); }
    }

    public static void Save(Data data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) { Logger.Warn($"AIの設定ファイルに書けませんでした: {e.Message}", "AiConfig"); }
    }

    public static bool HasApiKey(AiProvider provider)
    {
        var d = Load();
        return provider switch
        {
            AiProvider.Claude => !string.IsNullOrWhiteSpace(d.apiKey) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")),
            AiProvider.ChatGpt => !string.IsNullOrWhiteSpace(d.openaiKey) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")),
            AiProvider.Gemini => !string.IsNullOrWhiteSpace(d.geminiKey) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")),
            _ => true,
        };
    }

    public static Data Load()
    {
        EnsureFile();
        try { return JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data(); }
        catch (Exception e)
        {
            Logger.Warn($"AIの設定ファイルを読めませんでした: {e.Message}", "AiConfig");
            return new Data();
        }
    }
}

// 使う AI(設定の「使うAI」の番号と同じ並び。前からある 0・1 の意味は変えない)
public enum AiProvider { Claude = 0, Local = 1, ChatGpt = 2, Gemini = 3 }

// AI に聞いて、会議の発言と投票先を返してもらう
public static class AiClient
{
    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(40) };

    public sealed class Answer
    {
        public string Say = "";
        public string Vote = "skip";
    }

    public static async Task<Answer> AskAsync(string system, string user, AiProvider provider)
        => Parse(await AskRawAsync(system, user, provider).ConfigureAwait(false));

    // AI の返事を、そのままの文で返す
    public static async Task<string> AskRawAsync(string system, string user, AiProvider provider)
    {
        var cfg = AiConfig.Load();
        return provider switch
        {
            AiProvider.Claude => await AskClaudeAsync(cfg, system, user).ConfigureAwait(false),
            AiProvider.ChatGpt => await AskOpenAiAsync(cfg, system, user).ConfigureAwait(false),
            AiProvider.Gemini => await AskGeminiAsync(cfg, system, user).ConfigureAwait(false),
            _ => await AskLocalAsync(cfg, system, user).ConfigureAwait(false),
        };
    }

    // ChatGPT(OpenAI の Chat Completions API)
    private static async Task<string> AskOpenAiAsync(AiConfig.Data cfg, string system, string user)
    {
        var key = string.IsNullOrWhiteSpace(cfg.openaiKey) ? Environment.GetEnvironmentVariable("OPENAI_API_KEY") : cfg.openaiKey;
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("ChatGPT の API キーが設定されていません");
        var body = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(cfg.openaiModel) ? "gpt-4o-mini" : cfg.openaiModel,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user },
            },
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("Authorization", $"Bearer {key}");
        using var res = await http.SendAsync(req).ConfigureAwait(false);
        var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"ChatGPT API {(int)res.StatusCode}: {Trim(json, 200)}");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    // Gemini(Google の generateContent API)
    private static async Task<string> AskGeminiAsync(AiConfig.Data cfg, string system, string user)
    {
        var key = string.IsNullOrWhiteSpace(cfg.geminiKey) ? Environment.GetEnvironmentVariable("GEMINI_API_KEY") : cfg.geminiKey;
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Gemini の API キーが設定されていません");
        var model = string.IsNullOrWhiteSpace(cfg.geminiModel) ? "gemini-2.5-flash" : cfg.geminiModel;
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = system } } },
            ["contents"] = new JsonArray { new JsonObject { ["role"] = "user", ["parts"] = new JsonArray { new JsonObject { ["text"] = user } } } },
            ["generationConfig"] = new JsonObject { ["responseMimeType"] = "application/json" },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        // キーは URL ではなくヘッダーで送る(ログやプロキシに残らないように)
        req.Headers.Add("x-goog-api-key", key);
        using var res = await http.SendAsync(req).ConfigureAwait(false);
        var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"Gemini API {(int)res.StatusCode}: {Trim(json, 200)}");
        using var doc = JsonDocument.Parse(json);
        var sb = new StringBuilder();
        foreach (var part in doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts").EnumerateArray())
            if (part.TryGetProperty("text", out var t)) sb.Append(t.GetString());
        return sb.ToString();
    }

    // Claude API(Messages API)。BepInEx の .NET 6 では公式 SDK の依存(System.Text.Json 10 など)が読み込めないため、HTTP で直接呼ぶ
    private static async Task<string> AskClaudeAsync(AiConfig.Data cfg, string system, string user)
    {
        var key = string.IsNullOrWhiteSpace(cfg.apiKey) ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") : cfg.apiKey;
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("API キーが設定されていません");
        var body = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(cfg.model) ? "claude-opus-5-5" : cfg.model,
            ["max_tokens"] = 2000,
            ["system"] = system,
            ["output_config"] = new JsonObject { ["effort"] = string.IsNullOrWhiteSpace(cfg.effort) ? "low" : cfg.effort },
            // 安全のための判定で断られた時は、サーバー側で別のモデルが続きを引き受ける
            ["fallbacks"] = "default",
            ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = user } },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-api-key", key);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Headers.Add("anthropic-beta", "server-side-fallback-2026-07-01");
        using var res = await http.SendAsync(req).ConfigureAwait(false);
        var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"Claude API {(int)res.StatusCode}: {Trim(json, 200)}");
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("stop_reason", out var stop) && stop.GetString() == "refusal") return "";
        var sb = new StringBuilder();
        foreach (var block in root.GetProperty("content").EnumerateArray())
            if (block.TryGetProperty("type", out var t) && t.GetString() == "text") sb.Append(block.GetProperty("text").GetString());
        return sb.ToString();
    }

    // ローカル AI(Ollama)
    private static async Task<string> AskLocalAsync(AiConfig.Data cfg, string system, string user)
    {
        var body = new JsonObject
        {
            ["model"] = string.IsNullOrWhiteSpace(cfg.localModel) ? "qwen2.5:7b" : cfg.localModel,
            ["stream"] = false,
            ["format"] = "json",
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user },
            },
        };
        var url = (string.IsNullOrWhiteSpace(cfg.localEndpoint) ? "http://localhost:11434" : cfg.localEndpoint).TrimEnd('/') + "/api/chat";
        using var res = await http.PostAsync(url, new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")).ConfigureAwait(false);
        var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!res.IsSuccessStatusCode) throw new HttpRequestException($"ローカルAI {(int)res.StatusCode}: {Trim(json, 200)}");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
    }

    // {"say": "...", "vote": "..."} を読む(前後に余計な文があっても、最初の {...} を探す)
    private static Answer Parse(string text)
    {
        var answer = new Answer();
        if (string.IsNullOrWhiteSpace(text)) return answer;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) { answer.Say = Trim(text.Trim(), 90); return answer; }
        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            if (doc.RootElement.TryGetProperty("say", out var say)) answer.Say = Trim(say.GetString() ?? "", 90);
            if (doc.RootElement.TryGetProperty("vote", out var vote)) answer.Vote = vote.GetString() ?? "skip";
        }
        catch (JsonException) { answer.Say = Trim(text.Trim(), 90); }
        return answer;
    }

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..max];
}

// AI に渡す、試合中に起きたこと
public static class AiMemory
{
    private static readonly List<string> chat = new();
    public static IReadOnlyList<string> Chat => chat;

    public static void Reset() => chat.Clear();

    public static void AddChat(PlayerControl source, string text)
    {
        var name = source.Data?.PlayerName ?? "?";
        var clean = text.RemoveHtmlTags().Replace("\n", " ").Trim();
        if (clean.Length == 0) return;
        if (clean.Length > 120) clean = clean[..120];
        chat.Add($"{name.RemoveHtmlTags()}: {clean}");
        if (chat.Count > 30) chat.RemoveAt(0);
    }

    public static string RecentChat(int count) => string.Join("\n", chat.Skip(Math.Max(0, chat.Count - count)));
}
