using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using HarmonyLib;
using TownOfHost;
using TownOfHost.Attributes;
using TownOfHost.Patches;

namespace TownOfHost.Modules;

// ===== バグ報告システム(複数チケット対応版) =====
//
// 【全体設計】
// バグ報告ボタン→「これまでのバグ報告一覧 + 新規報告」の一覧画面→ウィザード(チャット希望選択→
// 必要ならDiscord ID入力→内容入力)→送信、で Discord Bot側にPOSTしてチケット用チャンネルを
// 自動作成してもらう。1人が複数件のバグ報告(チケット)を並行して持てるようになっている。
// 各チケットはBUGボタンから開ける専用チャットパネル(BugChatPanel)から返信でき、
// Discord側からの返信は自動でポーリングし、未読があればBUGボタンに赤い●が付く。
public static class BugReportSystem
{
    private static readonly string SaveDir = Path.Combine(Main.BaseDirectory, "BugReport");
    private static readonly string StateFilePath = Path.Combine(SaveDir, "bugreport_tickets.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    // ログファイル送信(最大3MB)に時間がかかる場合があるため、タイムアウトを少し長めにしている。
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public class TicketMessage
    {
        public string Id { get; set; } = "";
        public string From { get; set; } = ""; // "discord" or "game"
        public string Author { get; set; } = "";
        public string Text { get; set; } = "";
        public long At { get; set; } = 0;
        public bool Resolved { get; set; } = false; // 「修正完了」ボタンが押されたことを示すシステムメッセージ
    }

    public class TicketInfo
    {
        public string TicketId { get; set; } = "";
        public string Summary { get; set; } = ""; // 一覧表示用の短い説明(バグ内容の先頭部分)
        public long CreatedAtMs { get; set; } = 0;
        public long LastPolledAtMs { get; set; } = 0;
        public bool HasUnread { get; set; } = false;
        /// <summary>モデレーターが /cmd bug で送ったチケット(返信をロビーのモデレーターにも見せる)</summary>
        public bool ViaModerator { get; set; } = false;
        /// <summary>Discord側のステータス(open / in_progress / resolved / read)。一覧でタグとして表示する</summary>
        public string Status { get; set; } = "open";
        public List<TicketMessage> Messages { get; set; } = new();
    }

    private class SaveData
    {
        public List<TicketInfo> Tickets { get; set; } = new();
    }

    private static SaveData _data = new();

    // ロビー・メインメニュー内ポーリングの間隔(秒)。要望により短めの12〜15秒にしてある。
    public const float PollIntervalSeconds = 12f;

    public static IReadOnlyList<TicketInfo> Tickets => _data.Tickets;

    /// <summary>一覧に付けるステータスのタグ(Discordのフォーラムのタグと同じ。絵文字はゲームで出ないので文字で)</summary>
    public static string StatusTag(string status) => status switch
    {
        "in_progress" => "<#4f8ff7>【修正中】</color>",
        "resolved" => "<#3ec46d>【修正済み】</color>",
        "read" => "<#f5c542>【既読】</color>",
        _ => "<#9aa0a6>【未対応】</color>",
    };
    public static bool HasAnyTicket => _data.Tickets.Count > 0;
    public static bool HasAnyUnread => _data.Tickets.Any(t => t.HasUnread);

    [PluginModuleInitializer]
    public static void Init()
    {
        try
        {
            if (!Directory.Exists(SaveDir)) Directory.CreateDirectory(SaveDir);
            _data = ReadJson<SaveData>(StateFilePath) ?? new SaveData();
        }
        catch (Exception ex)
        {
            Logger.Error($"BugReportSystemの初期化に失敗しました: {ex}", "BugReportSystem");
            _data = new SaveData();
        }
    }

    private static T ReadJson<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text)) return null;
            return JsonSerializer.Deserialize<T>(text, JsonOptions);
        }
        catch (Exception ex)
        {
            Logger.Error($"{path}の読み込みに失敗しました: {ex}", "BugReportSystem");
            return null;
        }
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(StateFilePath, JsonSerializer.Serialize(_data, JsonOptions));
        }
        catch (Exception ex)
        {
            Logger.Error($"{StateFilePath}の保存に失敗しました: {ex}", "BugReportSystem");
        }
    }

    public static TicketInfo GetTicket(string ticketId) => _data.Tickets.FirstOrDefault(t => t.TicketId == ticketId);

    /// <summary>
    /// Discord側でチケット(チャンネル)が削除されたことをポーリングで検知した際、
    /// ゲーム側の一覧・保存データ・開いていれば専用チャットからもチケットを消す。
    /// </summary>
    private static void RemoveTicketLocally(string ticketId)
    {
        var removed = _data.Tickets.RemoveAll(t => t.TicketId == ticketId) > 0;
        if (removed) Save();

        // 今まさにこのチケットの専用チャットが開いていたら閉じる。
        BugChatPanel.CloseIfShowing(ticketId);
    }

    /// <summary>指定したチケットを開いたことにする(未読を消す)。チャットパネルを開いたときに呼ぶ。</summary>
    public static void MarkAsRead(string ticketId)
    {
        var ticket = GetTicket(ticketId);
        if (ticket == null || !ticket.HasUnread) return;
        ticket.HasUnread = false;
        Save();
    }

    /// <summary>
    /// バグ報告を新規送信する。discordUserIdは空文字でも良い(白紙=Discordで会話しない/IDが無い)。
    /// 戻り値: (成功したか, ユーザーに見せるメッセージ, 作成されたチケットID)
    /// </summary>
    public static async Task<(bool ok, string message, string ticketId)> SubmitAsync(string discordUserId, string description, string playerName, string moderatorFriendCode = null, string roomCodeOverride = null)
    {
        if (string.IsNullOrEmpty(BugReportRemoteConfig.EndpointUrl))
            return (false, "現在バグ報告サーバーに接続できません。", null);

        if (string.IsNullOrWhiteSpace(description))
            return (false, "バグの内容が入力されていません。", null);

        try
        {
            var payload = new
            {
                discordUserId = discordUserId ?? "",
                description,
                roomCode = roomCodeOverride ?? GetCurrentRoomCode(),
                playerName,
                // モデレーター経由(/cmd bug)の時は、報告者=モデレーター、hostFriendCode=部屋主(この端末)
                friendCode = moderatorFriendCode ?? GetLocalFriendCode(),
                hostFriendCode = moderatorFriendCode != null ? GetLocalFriendCode() : "",
                viaModerator = moderatorFriendCode != null,
                gameVersion = Main.PluginShowVersion ?? "",
            };

            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, BugReportRemoteConfig.EndpointUrl.TrimEnd('/') + "/submit")
            {
                Content = content,
            };
            if (!string.IsNullOrEmpty(BugReportRemoteConfig.ApiKey))
                request.Headers.Add("Authorization", $"Bearer {BugReportRemoteConfig.ApiKey}");

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // 上限到達(HTTP 429)の場合は、詳しい理由(あと何時間で1枠空くか)を
                // レスポンス本文から読み取って分かりやすいメッセージにする。
                if ((int)response.StatusCode == 429)
                {
                    try
                    {
                        var limitJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        using var limitDoc = JsonDocument.Parse(limitJson);
                        var limitRoot = limitDoc.RootElement;
                        var maxTickets = limitRoot.TryGetProperty("maxTickets", out var maxProp) ? maxProp.GetInt32() : 3;

                        if (limitRoot.TryGetProperty("hoursUntilNextSlot", out var hoursProp) && hoursProp.ValueKind == JsonValueKind.Number)
                        {
                            var hours = hoursProp.GetDouble();
                            var hoursText = hours < 1
                                ? $"{Math.Max(1, (int)Math.Ceiling(hours * 60))}分ほど"
                                : $"{Math.Ceiling(hours)}時間ほど";
                            return (false,
                                $"バグ報告は同時に{maxTickets}件までです。現在すべての枠が埋まっています。\n" +
                                $"あと{hoursText}で1枠空く見込みです。", null);
                        }

                        return (false,
                            $"バグ報告は同時に{maxTickets}件までです。現在すべての枠が埋まっています。\n" +
                            "未解決のバグ報告が「修正完了」になると、そこから数時間後に1枠空きます。", null);
                    }
                    catch
                    {
                        return (false, "バグ報告は同時に3件までです。現在すべての枠が埋まっています。", null);
                    }
                }

                Logger.Warn($"バグ報告の送信に失敗しました (HTTP {(int)response.StatusCode})", "BugReportSystem");
                return (false, "送信に失敗しました。少し時間をおいて再度お試しください。", null);
            }

            var respJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(respJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("ok", out var okProp) || !okProp.GetBoolean())
                return (false, "送信に失敗しました。少し時間をおいて再度お試しください。", null);

            var ticketId = root.TryGetProperty("ticketId", out var idProp) ? idProp.GetString() : null;
            if (string.IsNullOrEmpty(ticketId))
                return (false, "送信に失敗しました(チケットIDを取得できませんでした)。", null);

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var summary = description.Length > 30 ? description[..30] + "…" : description;

            var ticket = new TicketInfo
            {
                TicketId = ticketId,
                Summary = summary,
                CreatedAtMs = now,
                LastPolledAtMs = 0,
                HasUnread = false,
                ViaModerator = moderatorFriendCode != null,
                Messages = new List<TicketMessage>
                {
                    new TicketMessage
                    {
                        Id = Guid.NewGuid().ToString(),
                        From = "game",
                        Author = playerName,
                        Text = description,
                        At = now,
                    },
                },
            };
            _data.Tickets.Add(ticket);
            Save();

            // ログファイル(BepInEx/LogOutput.log)もあわせて送っておく。
            var logSent = await UploadLogFileAsync(ticketId).ConfigureAwait(false);

            var logNote = logSent
                ? "フレンドコードとログファイルも一緒にDiscordへ送信しました。"
                : "フレンドコードは送信しましたが、ログファイルの送信には失敗しました(報告自体は受理されています)。";

            return (true,
                "バグ報告を送信しました！ご協力ありがとうございます。\n" +
                logNote + "\n" +
                "運営スタッフからの返信は、BUGボタンから開ける専用チャットに届きます。",
                ticketId);
        }
        catch (Exception ex)
        {
            Logger.Error($"バグ報告の送信で例外が発生しました: {ex}", "BugReportSystem");
            return (false, "送信中にエラーが発生しました。", null);
        }
    }

    /// <summary>チケットへ返信を送信する(専用チャットパネル、またはゲーム内チャット→Discord)。</summary>
    public static async Task<string> ReplyAsync(string ticketId, string text, string playerName)
    {
        var ticket = GetTicket(ticketId);
        if (ticket == null)
            return "対象のバグ報告チケットが見つかりません。";

        if (string.IsNullOrWhiteSpace(text))
            return "メッセージを入力してください。";

        try
        {
            var payload = new { author = playerName, text };
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{BugReportRemoteConfig.EndpointUrl.TrimEnd('/')}/{ticketId}/messages")
            {
                Content = content,
            };
            if (!string.IsNullOrEmpty(BugReportRemoteConfig.ApiKey))
                request.Headers.Add("Authorization", $"Bearer {BugReportRemoteConfig.ApiKey}");

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return "返信の送信に失敗しました。";

            ticket.Messages.Add(new TicketMessage
            {
                Id = Guid.NewGuid().ToString(),
                From = "game",
                Author = playerName,
                Text = text,
                At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            });
            Save();

            return "";
        }
        catch (Exception ex)
        {
            Logger.Error($"バグ報告返信の送信で例外が発生しました: {ex}", "BugReportSystem");
            return "返信の送信中にエラーが発生しました。";
        }
    }

    private class MessagesResponse
    {
        public bool Ok { get; set; }
        public List<TicketMessage> Messages { get; set; } = new();
        public string Status { get; set; } = "";
    }

    /// <summary>
    /// 全チケットの新着メッセージ(Discord側からの返信)を取得する。
    /// 新着があれば未読フラグを立て、ゲーム内チャットにも簡易表示する。
    /// ロビー/メインメニューのポーリングから呼ばれる。
    /// </summary>
    public static async Task PollAllTicketsAsync()
    {
        if (!HasAnyTicket) return;
        if (string.IsNullOrEmpty(BugReportRemoteConfig.EndpointUrl)) return;

        // 複数チケットを順番にポーリングする(件数が多くなりすぎない前提の簡易実装)。
        foreach (var ticket in _data.Tickets.ToList())
        {
            await PollSingleTicketAsync(ticket).ConfigureAwait(false);
        }
    }

    private static async Task PollSingleTicketAsync(TicketInfo ticket)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{BugReportRemoteConfig.EndpointUrl.TrimEnd('/')}/{ticket.TicketId}/messages?after={ticket.LastPolledAtMs}");
            if (!string.IsNullOrEmpty(BugReportRemoteConfig.ApiKey))
                request.Headers.Add("Authorization", $"Bearer {BugReportRemoteConfig.ApiKey}");

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            // Discord側でチケット(チャンネル)が削除されると、Bot側は404を返す。
            // その場合はこちら側の一覧・専用チャットからもチケットを削除して同期させる。
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                RemoveTicketLocally(ticket.TicketId);
                return;
            }

            if (!response.IsSuccessStatusCode) return;

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var parsed = JsonSerializer.Deserialize<MessagesResponse>(json, JsonOptions);
            if (parsed == null) return;

            var changed = false;
            // ステータスが変わっていたら覚える(新しいメッセージが無くても反映する)
            if (!string.IsNullOrEmpty(parsed.Status) && parsed.Status != ticket.Status)
            {
                ticket.Status = parsed.Status;
                changed = true;
            }
            if (parsed.Messages == null || parsed.Messages.Count == 0) { if (changed) Save(); return; }
            foreach (var msg in parsed.Messages)
            {
                if (msg.At > ticket.LastPolledAtMs) ticket.LastPolledAtMs = msg.At;

                // ゲーム側から送った(自分の)メッセージはエコーバックしない。
                if (msg.From == "discord")
                {
                    ticket.Messages.Add(msg);
                    ticket.HasUnread = true;
                    changed = true;
                    if (ticket.ViaModerator) ModeratorBugRelay.Enqueue(ticket.Summary, msg.Author, msg.Text);

                    // 修正完了メッセージも履歴として保持する。修正完了後に
                    // チケットを削除すると、利用者が過去の対応内容を確認できず、
                    // 追加報告の導線も失われるため、一覧からは削除しない。
                    // 要望により、Discordからの返信をロビー内チャットにエコー表示する機能は削除した。
                    // 通知はBUGボタンの未読バッジと、開いたときの専用チャット(BugChatPanel)のみで行う。
                }
            }

            if (changed) Save();
        }
        catch (Exception ex)
        {
            Logger.Warn($"バグ報告メッセージのポーリングに失敗しました(ticket={ticket.TicketId}): {ex.Message}", "BugReportSystem");
        }
    }

    private static string GetLocalFriendCode()
    {
        try
        {
            return PlayerControl.LocalPlayer != null
                ? (PlayerControl.LocalPlayer.GetClient()?.FriendCode ?? "")
                : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>BepInExのログファイルをチケットチャンネルに送信する。成功したかどうかを返す。</summary>
    private static async Task<bool> UploadLogFileAsync(string ticketId)
    {
        try
        {
            var logPath = Path.Combine(Environment.CurrentDirectory, "BepInEx", "LogOutput.log");
            // Androidでは作業フォルダが違うので、BepInExのフォルダから探す
            if (!File.Exists(logPath) && !string.IsNullOrEmpty(BepInEx.Paths.BepInExRootPath))
                logPath = Path.Combine(BepInEx.Paths.BepInExRootPath, "LogOutput.log");
            if (!File.Exists(logPath)) return false;

            // 大きすぎる場合はDiscordの添付上限に配慮して末尾だけ切り出す(3MBまで)。
            const int maxBytes = 3 * 1024 * 1024;
            byte[] logBytes;
            using (var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length > maxBytes)
                {
                    fs.Seek(-maxBytes, SeekOrigin.End);
                }
                using var ms = new MemoryStream();
                await fs.CopyToAsync(ms).ConfigureAwait(false);
                logBytes = ms.ToArray();
            }

            using var fileContent = new ByteArrayContent(logBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{BugReportRemoteConfig.EndpointUrl.TrimEnd('/')}/{ticketId}/log")
            {
                Content = fileContent,
            };
            if (!string.IsNullOrEmpty(BugReportRemoteConfig.ApiKey))
                request.Headers.Add("Authorization", $"Bearer {BugReportRemoteConfig.ApiKey}");

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warn($"ログファイルの送信に失敗しました (HTTP {(int)response.StatusCode})", "BugReportSystem");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"ログファイルの送信中にエラーが発生しました: {ex.Message}", "BugReportSystem");
            return false;
        }
    }

    /// <summary>選択した画像・動画ファイルをチケットチャンネルに送信する。成功したかどうかと、
    /// 失敗時のメッセージを返す。</summary>
    public static async Task<(bool ok, string message)> UploadAttachmentAsync(string ticketId, string filePath)
    {
        if (string.IsNullOrEmpty(ticketId)) return (false, "送信先のチケットが見つかりません。");
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return (false, "ファイルが見つかりません。");

        try
        {
            // Discordの一般的な添付上限(Nitroなし)に合わせ、25MBまでに制限する。
            const long maxBytes = 25L * 1024 * 1024;
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length > maxBytes)
                return (false, "ファイルサイズが大きすぎます(25MBまで)。");

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            var allowedExtensions = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp", ".mp4", ".webm", ".mov", ".avi" };
            if (Array.IndexOf(allowedExtensions, ext) < 0)
                return (false, "画像または動画ファイルを選択してください。");

            byte[] fileBytes;
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var ms = new MemoryStream())
            {
                await fs.CopyToAsync(ms).ConfigureAwait(false);
                fileBytes = ms.ToArray();
            }

            using var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(GetMimeType(ext));

            using var form = new MultipartFormDataContent
            {
                { fileContent, "file", Path.GetFileName(filePath) },
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{BugReportRemoteConfig.EndpointUrl.TrimEnd('/')}/{ticketId}/attachment")
            {
                Content = form,
            };
            if (!string.IsNullOrEmpty(BugReportRemoteConfig.ApiKey))
                request.Headers.Add("Authorization", $"Bearer {BugReportRemoteConfig.ApiKey}");

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Logger.Warn($"添付ファイルの送信に失敗しました (HTTP {(int)response.StatusCode})", "BugReportSystem");
                return (false, "添付ファイルの送信に失敗しました。少し時間をおいて再度お試しください。");
            }
            return (true, "");
        }
        catch (Exception ex)
        {
            Logger.Warn($"添付ファイルの送信中にエラーが発生しました: {ex.Message}", "BugReportSystem");
            return (false, "添付ファイルの送信中にエラーが発生しました。");
        }
    }

    private static string GetMimeType(string extension) => extension switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".avi" => "video/x-msvideo",
        _ => "application/octet-stream",
    };

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

// ===== Discord Bot側APIの接続先設定 =====
// SurveyRemoteConfigと同じBotに相乗りするため、ホスト部分は同じ設定にすること。
public static class BugReportRemoteConfig
{
    /// <summary>Discord Bot側のバグ報告APIベースURL</summary>
    public static string EndpointUrl = "https://hmbot.nkzk.win/api/bugreport";

    /// <summary>修正完了後も追加のバグ報告ボタンを表示するか。Discord運用に合わせて切り替える。</summary>
    public static bool AllowAdditionalReports = true;

    /// <summary>同時に保持できるバグ報告数。サーバー側の上限3件と一致させる。</summary>
    public const int MaxTickets = 3;

    public static bool CanSubmitAdditionalReport => AllowAdditionalReports && BugReportSystem.Tickets.Count < MaxTickets;

    /// <summary>MOD↔Bot間の認証キー。Bot側の.env(SURVEY_API_SECRET)と必ず同じ値にすること。</summary>
    public static string ApiKey = "e14ow2cvdpx5";
}

// ===== ロビー内でのポーリング制御 =====
// ロビーにいる間だけ、12秒に1回チケットの新着メッセージ・ロビーチャットアナウンスを取得する。
[HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
public static class BugReportPollLobbyStartPatch
{
    private static float _pollTimer = 0f;

    public static void Postfix()
    {
        _pollTimer = 0f;
        // ロビーに入った瞬間にも1回取得しておく。
        _ = BugReportSystem.PollAllTicketsAsync();
        _ = LobbyChatRelay.PollAsync();
    }

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Update))]
    public static class UpdatePatch
    {
        public static void Postfix()
        {
            _pollTimer += UnityEngine.Time.deltaTime;
            if (_pollTimer < BugReportSystem.PollIntervalSeconds) return;
            _pollTimer = 0f;
            _ = BugReportSystem.PollAllTicketsAsync();
            _ = LobbyChatRelay.PollAsync();
        }
    }
}


/// <summary>
/// モデレーター経由(/cmd bug)のバグ報告に付いたDiscordからの返信を、ロビーにいるモデレーターだけに個別チャットで見せる。
/// 試合中は溜めておき、次のロビーで送る。アンチチート対策として、まとめて送り・送信間隔を空け・1回の量を制限する。
/// </summary>
public static class ModeratorBugRelay
{
    private static readonly System.Collections.Concurrent.ConcurrentQueue<string> Pending = new();
    private const int MaxPending = 30;          // 溜めすぎたら古いものから捨てる
    private const int MaxLinesPerMessage = 4;    // 1回のチャットにまとめる件数
    private const float SendIntervalSeconds = 4f; // チャット送信の間隔
    private const float LobbyWarmupSeconds = 5f;  // ロビーに入ってすぐは送らない(参加処理との重なりを避ける)
    private static float _nextSendAt;
    private static float _lobbySince = -1f;

    public static void Enqueue(string summary, string author, string text)
    {
        var line = $"<#ff6b6b>[BUG:{Trim(summary, 12)}]</color> {Trim(author, 16)}: {Trim(text, 120)}";
        Pending.Enqueue(line);
        while (Pending.Count > MaxPending && Pending.TryDequeue(out _)) { }
    }

    private static string Trim(string s, int max)
    {
        s = (s ?? "").Replace("\n", " ").RemoveHtmlTags();
        return s.Length > max ? s[..max] + "…" : s;
    }

    [HarmonyLib.HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    private static class TickPatch
    {
        public static void Postfix()
        {
            try
            {
                if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || !GameStates.IsLobby || GameStates.IsCountDown)
                {
                    _lobbySince = -1f;
                    return;
                }
                var now = UnityEngine.Time.time;
                if (_lobbySince < 0f) _lobbySince = now;
                if (Pending.IsEmpty || now - _lobbySince < LobbyWarmupSeconds || now < _nextSendAt) return;

                var mods = PlayerControl.AllPlayerControls.ToArray()
                    .Where(p => p != null && !p.AmOwner && TownOfHost.Modules.Moderator.IsModerator(p)).ToList();
                if (mods.Count == 0) return; // モデレーターが来るまで溜めておく

                var lines = new List<string>();
                while (lines.Count < MaxLinesPerMessage && Pending.TryDequeue(out var l)) lines.Add(l);
                var body = "<#ff6b6b>【BUGチャット(モデレーターのみ)】</color>\n" + string.Join("\n", lines);
                foreach (var m in mods) Utils.SendMessage(body, m.PlayerId);
                // 送った人数が多いほど間隔を空ける
                _nextSendAt = now + SendIntervalSeconds * Math.Max(1, mods.Count);
            }
            catch (Exception e) { Logger.Warn($"ModeratorBugRelay: {e.Message}", "BugReportSystem"); }
        }
    }
}
