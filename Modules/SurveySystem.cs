using System;

using System.Collections.Generic;

using System.IO;

using System.Linq;

using System.Net.Http;

using System.Text;

using System.Text.Json;

using System.Threading.Tasks;



using HarmonyLib;

using TownOfHost.Attributes;



namespace TownOfHost.Modules;



// ===== アンケートシステム (/cmd q) =====

//

// 【全体設計】

// アンケートの作成・開始・終了・内容変更・複数投票ON/OFFは、すべてDiscord側

// (マッチメイキング中継Botに統合されたアンケート機能)で行う。Among Us側(このクラス)は

// 「今のアンケート内容が何か」を一切自分では決めない、Discord側の内容を

// そのまま映す「鏡」として動作する。

//

// 【同期方法(ポーリング)】

// ロビーにいる間、1分に1回だけDiscord Bot側のAPIへGETリクエストを送り、

// 現在のアンケート内容(質問・選択肢・複数投票可否・実施中かどうか・終了時刻)を

// 取得してローカルにキャッシュする。ゲーム中や、ロビーを離れている間は

// 通信を行わない(リクエスト数の節約のため)。

//

// 【投票】

// "/cmd q" (誰でも、ロビーのみ) → キャッシュされた内容を表示(一覧のみ、投票はしない)

// "/cmd aq <番号>" (誰でも、ロビーのみ、複数投票可の場合は"/cmd aq <番号1.番号2....>" も可) → 投票をDiscord Bot側に送信

//

// 【終了後48時間以内の結果表示】

// 部屋(ロビー)に入った瞬間、直近のアンケートが「終了してから48時間以内」であれば、

// 結果を自動でチャット表示する。

public static class SurveySystem

{

    private static readonly string SaveDir = Path.Combine(Main.BaseDirectory, "Survey");

    private static readonly string CacheFilePath = Path.Combine(SaveDir, "survey_cache.json");



    private static readonly JsonSerializerOptions JsonOptions = new()

    {

        WriteIndented = true,

        PropertyNameCaseInsensitive = true,

    };



    // ===== 結果表示の有効期限 =====

    // 「終了後48時間以内なら部屋に入った時に結果表示」の48時間はここで一元管理する。

    public static readonly TimeSpan ResultVisibleDuration = TimeSpan.FromHours(48);



    // ===== ポーリング間隔 =====

    // Discord Bot側の負荷を抑えるため、ロビー内では1分に1回のみ問い合わせる。

    public const float PollIntervalSeconds = 60f;



    public class SurveyOption

    {

        public int Index { get; set; }

        public string Text { get; set; } = "";

        public int Count { get; set; } = 0; // 現在の得票数(Discord側が集計して返してくる)

    }



    // Discord側から取得する「今のアンケート状態」をそのまま保持する入れ物。

    // Among Us側はこの中身を書き換えない(常にDiscordから来た値で上書きされる)。

    public class SurveyCache

    {

        public string Id { get; set; } = "";

        public string Question { get; set; } = "";

        public List<SurveyOption> Options { get; set; } = new();

        public bool IsActive { get; set; } = false;

        public bool AllowMultipleVotes { get; set; } = false;

        public DateTime? EndedAtUtc { get; set; } = null;

        public int TotalVotes { get; set; } = 0;

        public DateTime FetchedAtUtc { get; set; } = DateTime.UtcNow;



        // このキャッシュを、部屋に入った時に結果表示すべきかどうか。

        // 「終了している」かつ「終了してからResultVisibleDuration以内」の場合のみtrue。

        public bool ShouldShowResultOnJoin =>

            !IsActive

            && EndedAtUtc.HasValue

            && (DateTime.UtcNow - EndedAtUtc.Value) <= ResultVisibleDuration

            && !string.IsNullOrEmpty(Question);

    }



    private static SurveyCache _cache = new();

    // このロビー滞在中、既に結果表示を1回見せたかどうか(何度も表示させないため)

    private static bool _resultShownThisLobby = false;

    // 外部アンケートAPIの接続失敗はロビー中に毎分繰り返され得るため、

    // 同じ停止状態では最初の1回だけ警告し、成功時に通知可能な状態へ戻す。

    private static bool _pollFailureLogged = false;



    private static void WarnPollFailureOnce(string message)

    {

        if (_pollFailureLogged) return;

        _pollFailureLogged = true;

        Logger.Warn(message, "SurveySystem");

    }



    [PluginModuleInitializer]

    public static void Init()

    {

        try

        {

            if (!Directory.Exists(SaveDir)) Directory.CreateDirectory(SaveDir);

            _cache = ReadJson<SurveyCache>(CacheFilePath) ?? new SurveyCache();

        }

        catch (Exception ex)

        {

            Logger.Error($"SurveySystemの初期化に失敗しました: {ex}", "SurveySystem");

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

            Logger.Error($"{path}の読み込みに失敗しました: {ex}", "SurveySystem");

            return null;

        }

    }



    private static void WriteJson<T>(string path, T data)

    {

        try

        {

            File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptions));

        }

        catch (Exception ex)

        {

            Logger.Error($"{path}の保存に失敗しました: {ex}", "SurveySystem");

        }

    }



    public static bool HasActiveSurvey => _cache is { IsActive: true };

    public static SurveyCache Current => _cache;



    /// <summary>

    /// 現在のアンケートの一覧を表示用テキストとして組み立てる。「/cmd q」用。

    /// </summary>

    public static string BuildSurveyListText()

    {

        if (_cache == null || !_cache.IsActive || string.IsNullOrEmpty(_cache.Question))

            return GetString("Survey.NoActiveSurvey");



        var sb = new StringBuilder();

        sb.Append($"<size=90%><color=#00c1ff>📋 {_cache.Question}</color>\n");

        foreach (var opt in _cache.Options.OrderBy(o => o.Index))

        {

            sb.Append($"{opt.Index + 1}. {opt.Text}\n");

        }

        sb.Append(_cache.AllowMultipleVotes ? GetString("Survey.HowToVoteMultiple") : GetString("Survey.HowToVote"));

        sb.Append("</size>");

        return sb.ToString();

    }



    /// <summary>

    /// 部屋に入った時、結果を自動表示すべき場合のテキストを返す。不要ならnull。

    /// </summary>

    public static string BuildJoinResultTextIfNeeded()

    {

        if (_resultShownThisLobby) return null;

        if (_cache == null || !_cache.ShouldShowResultOnJoin) return null;



        _resultShownThisLobby = true;

        return BuildResultText();

    }



    /// <summary>アンケート結果を表示用テキストとして組み立てる。「/cmd q result」用にも使う。</summary>

    public static string BuildResultText()

    {

        if (_cache == null || string.IsNullOrEmpty(_cache.Question))

            return GetString("Survey.NoActiveSurvey");



        var sb = new StringBuilder();

        sb.Append($"<size=90%><color=#00c1ff>📊 {_cache.Question} - {GetString("Survey.ResultTitle")}</color>\n");

        foreach (var opt in _cache.Options.OrderBy(o => o.Index))

        {

            double percent = _cache.TotalVotes == 0 ? 0 : (opt.Count * 100.0 / _cache.TotalVotes);

            sb.Append($"{opt.Index + 1}. {opt.Text} : {opt.Count}{GetString("Survey.VotesUnit")} ({percent:0.0}%)\n");

        }

        sb.Append($"{GetString("Survey.TotalVotes")}: {_cache.TotalVotes}{GetString("Survey.VotesUnit")}");

        sb.Append("</size>");

        return sb.ToString();

    }



    /// <summary>

    /// 指定したプレイヤーの投票をDiscord Bot側へ送信する。optionNumbersは1始まり。

    /// 複数投票が無効な場合、2個以上指定されたらエラーメッセージを返す。

    /// 戻り値: 投票結果メッセージ(本人にだけ表示する用)

    /// </summary>

    public static async Task<string> VoteAsync(PlayerControl player, List<int> optionNumbers)

    {

        if (_cache == null || !_cache.IsActive)

            return GetString("Survey.NoActiveSurvey");



        if (optionNumbers == null || optionNumbers.Count == 0)

            return GetString("Survey.InvalidOptionFormat");



        if (!_cache.AllowMultipleVotes && optionNumbers.Count > 1)

            return GetString("Survey.MultipleVotesNotAllowed");



        var maxOption = _cache.Options.Count;

        foreach (var n in optionNumbers)

        {

            if (n < 1 || n > maxOption)

                return string.Format(GetString("Survey.InvalidOption"), maxOption);

        }



        if (string.IsNullOrEmpty(SurveyRemoteConfig.EndpointUrl))

        {

            // Discord Bot側がまだ用意されていない場合、投票を送る先が無いためその旨を伝える。

            return GetString("Survey.RemoteNotConfigured");

        }



        var voterKey = GetVoterKey(player);

        var payload = new

        {

            surveyId = _cache.Id,

            voterKey,

            voterName = player.GetNameWithRole().RemoveHtmlTags(),

            optionIndexes = optionNumbers.Select(n => n - 1).ToList(),

        };



        var (ok, errorMessage) = await PostAsync("/vote", payload).ConfigureAwait(false);

        if (!ok)

            return errorMessage ?? GetString("Survey.VoteFailed");



        // 投票直後、表示を最新化するためにもう一度取得しておく(次のポーリングを待たずに済むように)。

        _ = PollNowAsync();



        var chosenTexts = string.Join("、", optionNumbers

            .Select(n => _cache.Options.FirstOrDefault(o => o.Index == n - 1)?.Text ?? $"#{n}"));

        return string.Format(GetString("Survey.VoteAccepted"), chosenTexts);

    }



    private static string GetVoterKey(PlayerControl player)

    {

        var puid = player?.GetClient()?.ProductUserId;

        if (!string.IsNullOrEmpty(puid)) return puid;

        return $"name:{player?.Data?.GetLogPlayerName() ?? "???"}";

    }



    private static string GetString(string key)

    {

        try

        {

            var s = Translator.GetString(key);

            if (!string.IsNullOrEmpty(s) && !s.StartsWith("<INVALID:")) return s;

        }

        catch { /* Translatorで例外が出た場合は下のフォールバックを使う */ }



        return key switch

        {

            "Survey.NoActiveSurvey" => "現在実施中のアンケートはありません。",

            "Survey.HowToVote" => "\n投票するには「/cmd aq (番号)」と送信してください。例: /cmd aq 1",

            "Survey.HowToVoteMultiple" => "\n投票するには「/cmd aq (番号)」と送信してください。複数選ぶ場合は「/cmd aq 1.2」のようにピリオド区切りで指定できます。",

            "Survey.InvalidOption" => "番号が正しくありません。1〜{0}の数字を指定してください。",

            "Survey.InvalidOptionFormat" => "使い方: 「/cmd aq (番号)」、複数投票可の場合は「/cmd aq (番号1.番号2)」",

            "Survey.MultipleVotesNotAllowed" => "このアンケートは複数投票できません。番号を1つだけ指定してください。",

            "Survey.VoteAccepted" => "「{0}」に投票しました！",

            "Survey.VoteFailed" => "投票の送信に失敗しました。少し時間をおいて再度お試しください。",

            "Survey.RemoteNotConfigured" => "現在アンケートサーバーに接続できません。",

            "Survey.ResultTitle" => "結果発表",

            "Survey.VotesUnit" => "票",

            "Survey.TotalVotes" => "合計投票数",

            _ => key,

        };

    }



    // ===================================================================

    // ===== Discord Bot側APIとの通信 =====

    // ===================================================================

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };



    private static async Task<(bool ok, string errorMessage)> PostAsync(string path, object payload)

    {

        try

        {

            if (string.IsNullOrEmpty(SurveyRemoteConfig.EndpointUrl)) return (false, GetString("Survey.RemoteNotConfigured"));



            var json = JsonSerializer.Serialize(payload, JsonOptions);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var request = new HttpRequestMessage(HttpMethod.Post, SurveyRemoteConfig.EndpointUrl.TrimEnd('/') + path)

            {

                Content = content,

            };

            if (!string.IsNullOrEmpty(SurveyRemoteConfig.ApiKey))

                request.Headers.Add("Authorization", $"Bearer {SurveyRemoteConfig.ApiKey}");



            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)

            {

                Logger.Warn($"アンケートサーバーへの送信に失敗しました (HTTP {(int)response.StatusCode})", "SurveySystem");

                return (false, null);

            }

            return (true, null);

        }

        catch (Exception ex)

        {

            Logger.Warn($"アンケートサーバーへの送信中に例外が発生しました: {ex.Message}", "SurveySystem");

            return (false, null);

        }

    }



    /// <summary>

    /// Discord Bot側から現在のアンケート状態を取得してキャッシュを更新する。

    /// 失敗した場合は何もせず、直前のキャッシュをそのまま維持する

    /// (通信が一時的に途切れても、部屋の中では前回取得した内容が見え続ける)。

    /// </summary>

    public static async Task PollNowAsync()

    {

        try

        {

            if (string.IsNullOrEmpty(SurveyRemoteConfig.EndpointUrl)) return;



            using var request = new HttpRequestMessage(HttpMethod.Get, SurveyRemoteConfig.EndpointUrl.TrimEnd('/') + "/current");

            if (!string.IsNullOrEmpty(SurveyRemoteConfig.ApiKey))

                request.Headers.Add("Authorization", $"Bearer {SurveyRemoteConfig.ApiKey}");



            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode) return;



            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

            var fetched = JsonSerializer.Deserialize<SurveyCache>(json, JsonOptions);

            if (fetched == null) return;



            fetched.FetchedAtUtc = DateTime.UtcNow;



            // アンケートのIdが変わった(=新しいアンケートに切り替わった)場合、

            // このロビーでの「結果表示済みフラグ」をリセットする。

            if (_cache?.Id != fetched.Id)

            {

                _resultShownThisLobby = false;

            }



            _cache = fetched;

            _pollFailureLogged = false;

            WriteJson(CacheFilePath, _cache);

        }

        catch (Exception ex)

        {

            // 通信に失敗しても直前のキャッシュを使い続ける。

            // ポーリングは1分ごとなので、同一障害中は最初の1回だけ記録する。

            WarnPollFailureOnce($"アンケート取得中に例外が発生しました: {ex.Message}");

        }

    }

}



/// <summary>

/// アンケートを取得・送信する際のDiscord Bot側API接続設定。

/// EndpointUrlとApiKeyは、Discord Bot側(.envのSURVEY_API_SECRET)と対になっている。

/// ApiKeyは推測されにくいランダム文字列にしてあるので、

/// URL自体が漏れても、このキーを知らない第三者はアンケートの取得・投票ができない

/// (Bot側でAuthorizationヘッダーの一致を必ず確認するため)。

/// </summary>

public static class SurveyRemoteConfig

{

    /// <summary>Discord Bot側のAPIベースURL</summary>

    public static string EndpointUrl = "https://townofhost-hamo.haru87245.workers.dev/api/survey";



    /// <summary>

    /// MOD↔Bot間の認証キー。Bot側の.env(SURVEY_API_SECRET)と必ず同じ値にすること。

    /// このキーが漏れると荒らし対策の意味が無くなるため、MODファイルの配布方法には注意すること

    /// (逆コンパイルされればこの文字列自体は読めてしまう点は完全には防げないが、

    ///  少なくとも「URLだけを知っている」第三者からは保護できる)。

    /// </summary>

    public static string ApiKey = "dgsaq4F_lucbOtfcbeIHmJDZ5KNZA2xgVm6OMCDc0qI";

}



// ===== ロビー内でのポーリング制御 =====

// ロビーにいる間だけ、1分に1回SurveySystem.PollNowAsync()を呼ぶ。

// ゲーム中・ロビー以外では一切通信しない(リクエスト数節約のため)。

[HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]

public static class SurveyPollLobbyStartPatch

{

    private static float _pollTimer = 0f;

    // ロビー入室直後の取得(非同期)が終わるまで少し待ってから結果表示を試みるためのタイマー。

    private static float _joinCheckTimer = -1f; // -1は「チェック不要」を表す

    private const float JoinCheckDelaySeconds = 2f;



    public static void Postfix()

    {

        _pollTimer = 0f;

        _joinCheckTimer = JoinCheckDelaySeconds;

        // ロビーに入った瞬間に1回取得しておく(即座に一覧・結果が見えるように)。

        _ = SurveySystem.PollNowAsync();

    }



    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Update))]

    public static class UpdatePatch

    {

        public static void Postfix()

        {

            if (_joinCheckTimer >= 0f)

            {

                _joinCheckTimer -= UnityEngine.Time.deltaTime;

                if (_joinCheckTimer <= 0f)

                {

                    _joinCheckTimer = -1f;

                    var joinResultText = SurveySystem.BuildJoinResultTextIfNeeded();

                    if (joinResultText != null)

                    {

                        Utils.SendMessage(joinResultText, byte.MaxValue);

                    }

                }

            }



            _pollTimer += UnityEngine.Time.deltaTime;

            if (_pollTimer < SurveySystem.PollIntervalSeconds) return;

            _pollTimer = 0f;

            _ = SurveySystem.PollNowAsync();

        }

    }

}

