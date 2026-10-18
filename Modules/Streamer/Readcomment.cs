using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace TownOfHost;
//めも
//正常に動作するか
class StreamerInfo
{
    public static string StreamURL = "";
    static string streamkey;
    public static float timer;
    public static Dictionary<int, HopeInfo> Hopeplayers = new();
    public static List<HopeInfo> JoindHopeplayers = new();
    private ChatData chatData = null;
    private readonly HttpClient client;
    static StreamerInfo streamerInfo;
    public static int number;

    /// <summary>画面のコメント枠に出す、最近のコメント(古い順・最大 MaxRecentComments 件)</summary>
    public static readonly List<(string Name, string Text)> RecentComments = new();
    private const int MaxRecentComments = 30;
    /// <summary>コメント枠の内容が変わった回数(表示側が書き換えるかの判断に使う)</summary>
    public static int RecentCommentsVersion;
    /// <summary>配信URLが設定されていて、コメントを取りに行ける状態か</summary>
    public static bool HasStream => StreamURL != "" && !string.IsNullOrEmpty(streamkey) && streamerInfo != null;

    /// <summary>
    /// 配信URLを設定する(メニュー・ゲーム内の設定の両方から使う)。YouTubeのURLでなければ false。
    /// 設定したURLは保存して、次に起動した時も使う。
    /// </summary>
    public static bool TrySetStreamURL(string url)
    {
        url = (url ?? "").Trim();
        if (ExtractVideoId(url).Length == 0 && !TryGetChannelUrl(url, out _)) return false; // YouTubeの動画・チャンネルのURLでない
        StreamURL = url;
        try { SetstreamKey(); }
        catch (System.Exception e)
        {
            Logger.Warn($"配信URLを読み取れませんでした: {e.Message}", "StreamerInfo");
            StreamURL = "";
            return false;
        }
        Main.StreamURLConfig.Value = url;
        return true;
    }

    /// <summary>前回保存した配信URLを読み込む(起動時に1回)</summary>
    private static bool savedLoaded;
    public static void LoadSavedURL()
    {
        if (savedLoaded) return;
        savedLoaded = true;
        var saved = Main.StreamURLConfig?.Value;
        if (!string.IsNullOrEmpty(saved) && StreamURL == "") TrySetStreamURL(saved);
    }

    /// <summary>
    /// ホスト以外(参加者として配信している人)でも、コメント枠のためにコメントを取りに行く。
    /// (ホストは参加希望の処理 FixUpdate で取りに行っているので、ここでは何もしない)
    /// </summary>
    public static void CommentFrameTick(float deltaTime)
    {
        // ホストも参加者も、コメント枠を出している時はここから取りに行く
        // (以前ホストは試合中の処理からしか取りに行っておらず、ロビーではコメントが出なかった)
        if (!Main.ShowStreamComments.Value || !HasStream) return;
        TryStartFetch();
    }

    private static float lastFetchAt = -999f;
    private static volatile bool fetching;
    private static System.DateTime lastErrorLogAt = System.DateTime.MinValue;

    /// <summary>前回から3秒以上たっていて、取得中でなければコメントを取りに行く(参加希望の処理と共通)</summary>
    private static void TryStartFetch()
    {
        if (streamerInfo == null || fetching) return;
        if (Time.time - lastFetchAt < 3f) return;
        lastFetchAt = Time.time;
        fetching = true;
        var info = streamerInfo;
        _ = Task.Run(async () =>
        {
            try { await info.FetchAsync(); }
            catch (System.Exception e)
            {
                // 失敗はTaskの中で消えてしまうので、ログに残す(出しすぎないよう30秒に1回)
                if (System.DateTime.Now - lastErrorLogAt > System.TimeSpan.FromSeconds(30))
                {
                    lastErrorLogAt = System.DateTime.Now;
                    Logger.Warn($"YouTubeのコメント取得に失敗: {e.Message}", "StreamerInfo");
                }
            }
            finally { fetching = false; }
        });
    }

    private static void AddRecentComment(string name, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (RecentComments)
        {
            RecentComments.Add((name, text));
            while (RecentComments.Count > MaxRecentComments) RecentComments.RemoveAt(0);
            RecentCommentsVersion++;
        }
    }

    public static void SetstreamKey()
    {
        var oldkey = streamkey;
        if (StreamURL is not "")
        {
            streamkey = ExtractVideoId(StreamURL);
            // チャンネルのURLなら、配信中の動画をあとで探す(配信のたびにURLを貼り直さなくてよい)
            if (streamkey.Length == 0 && TryGetChannelUrl(StreamURL, out var channelUrl)) streamkey = ChannelPrefix + channelUrl;
            if (oldkey != streamkey)
            {
                StatusText = "";
                retryAt = System.DateTime.MinValue;
                streamerInfo = new StreamerInfo();
                lock (RecentComments) { RecentComments.Clear(); RecentCommentsVersion++; }
                Logger.Info($"配信の動画ID: {streamkey}", "StreamerInfo");
            }
        }
    }

    /// <summary>
    /// YouTubeのURLから動画IDを取り出す。次の形に対応する:
    /// youtube.com/watch?v=ID / youtube.com/live/ID / youtube.com/shorts/ID / youtube.com/embed/ID / youtu.be/ID
    /// (www. や m. 付き、?si= などの付け足しがあってもよい)
    /// 以前は「https://www.youtube.com/live/ID?si=…」の形を読めず、si の値を動画IDとして使っていたため、コメントが一切取れなかった。
    /// </summary>
    /// <summary>streamkey がチャンネルを表す時の印(この後ろにチャンネルのURL)</summary>
    private const string ChannelPrefix = "channel:";

    /// <summary>YouTubeのチャンネルのURL(@名前・/channel/ID・/c/名前・/user/名前)なら、その形に整えて返す</summary>
    public static bool TryGetChannelUrl(string url, out string channelUrl)
    {
        channelUrl = "";
        if (string.IsNullOrWhiteSpace(url) || !System.Uri.TryCreate(url.Trim(), System.UriKind.Absolute, out var uri)) return false;
        if (!uri.Host.ToLowerInvariant().EndsWith("youtube.com")) return false;
        var seg = uri.AbsolutePath.Trim('/').Split('/');
        if (seg.Length >= 1 && seg[0].StartsWith("@") && seg[0].Length > 1) channelUrl = $"https://www.youtube.com/{seg[0]}";
        else if (seg.Length >= 2 && seg[0] is "channel" or "c" or "user") channelUrl = $"https://www.youtube.com/{seg[0]}/{seg[1]}";
        return channelUrl.Length > 0;
    }

    public static string ExtractVideoId(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";
        if (!System.Uri.TryCreate(url.Trim(), System.UriKind.Absolute, out var uri)) return "";
        var host = uri.Host.ToLowerInvariant();
        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (host.EndsWith("youtu.be")) return segments.Length > 0 ? segments[0] : "";
        if (!host.EndsWith("youtube.com")) return "";
        if (segments.Length >= 2 && segments[0] is "live" or "shorts" or "embed") return segments[1];
        foreach (var part in uri.Query.TrimStart('?').Split('&'))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && kv[0] == "v") return kv[1];
        }
        return "";
    }

    public static void JoinGame()//ホスト入室時に参加済みリストを削除
    {
        if (AmongUsClient.Instance?.AmHost is false or null) return;
        timer = 0;
    }
    public static void DisconnectInternal()
    {
        JoindHopeplayers.Clear();
    }
    public static bool JoinPlayer(InnerNet.ClientData player)//参加可能かのチェック
    {
        if (AmongUsClient.Instance?.AmHost is false or null) return true;
        // 参加希望制は設定(OTHER2のYouTubeコメント枠)でONにした時だけ。
        // (以前は配信URLを入れるだけで有効になり、コメント枠のためにURLを入れると、参加希望を書いていない人が入れなかった)
        if (TownOfHost.Modules.ExtraFeatureOptions.JoinByCommentOnly?.GetBool() != true) return true;
        if (StreamURL == "" || streamkey == "") return true;

        foreach (var info in Hopeplayers.Values)
        {
            if (player.PlayerName == info.PlayerName) return true;
        }
        foreach (var joindinfo in JoindHopeplayers)//ロビーに帰ってきた人も入室させる
        {
            if (joindinfo.PlayerName == player.PlayerName) return true;
        }
        return false;
    }
    public static void LeftPlayer(NetworkedPlayerInfo info)//参加済みでロビーから退出した人を参加済みリストから外す処理
    {
        if (AmongUsClient.Instance?.AmHost is false or null) return;
        var dis = JoindHopeplayers.Where(data => data.PlayerName == info.PlayerName).FirstOrDefault();

        if (dis == null) return;
        JoindHopeplayers.Remove(dis);
    }
    public static void ChangeList(List<string> playernames)//ゲーム開始時に、参加済みリストに追加する
    {
        if (AmongUsClient.Instance?.AmHost is false or null) return;
        var dellist = new List<int>();
        JoindHopeplayers.Clear();
        for (var i = 0; Hopeplayers.Count > i; i++)
        {
            var hope = Hopeplayers.OrderBy(x => x.Key).ToList()[i];
            if (playernames.Contains(hope.Value.PlayerName))
            {
                dellist.Add(hope.Key);
                JoindHopeplayers.Add(hope.Value);
            }
        }
        dellist.Do(x => Hopeplayers.Remove(x));
    }
    public static void FixUpdate()//参加希望の収集
    {
        if (AmongUsClient.Instance?.AmHost is false or null) return;
        if (StreamURL == "" || streamkey == "" || streamerInfo == null) return;

        TryStartFetch(); //3秒に1度の処理にする(コメント枠と共通)
    }

    public async Task<HopeInfo[]> FetchAsync()
    {
        if (chatData == null)
        {
            if (System.DateTime.Now < retryAt) return null; // 見つからなかった直後は、少し待ってから探し直す
            await FirstFetch();
            if (chatData == null) return null;
        }

        HttpResponseMessage chat = await FetchChat();
        if (chat == null) return null;
        string response = chat.Content.ReadAsStringAsync().Result;

        List<HopeInfo> comments = Parse(response);

        chatData.UpdateContinuation(response);

        foreach (var comment in comments)
        {
            Logger.Info($"{comment.AccountName}/{comment.Comment}", "Hope");
            // 追加済み
            if (Hopeplayers.ContainsValue(comment) || JoindHopeplayers.Contains(comment)) continue;

            //更新のチェック
            for (var i = 0; i < Hopeplayers.Count; i++)
            {
                var hope = Hopeplayers[i];
                if (hope.IsUpdate(comment))
                {
                    Hopeplayers[i] = comment;
                }
            }

            // 新規参加希望
            Hopeplayers.Add(number, comment);
            comment.SetId(number++);
        }

        return comments.ToArray();
    }
    private List<HopeInfo> Parse(string response)
    {
        List<HopeInfo> comments = new();

        var node = JsonNode.Parse(response);
        var a = node?["continuationContents"]?["liveChatContinuation"]?["actions"];
        // ↓ 取得してないコメントがある場合、ここれreturnされる。
        if (a == null) return comments;

        foreach (var item in a.AsArray())
        {
            JsonNode chats = item["addChatItemAction"]?["item"]?["liveChatTextMessageRenderer"];

            string message = "";
            string commentId = chats?["id"]?.ToString() ?? "";
            string userName = chats?["authorName"]?["simpleText"]?.ToString() ?? "";
            string userId = chats?["authorExternalChannelId"]?.ToString() ?? "";

            if (chats == null) continue;
            foreach (var chat in chats["message"]?["runs"]?.AsArray())
            {
                if (chat?["text"] == null) continue;
                message += chat["text"].ToString();
            }
            AddRecentComment(userName, message); // コメント枠用(参加希望かどうかに関係なく全部)
            TownOfHost.Modules.YouTubeMapVote.OnComment(userId, message); // マップ投票(便利機能)

            var hope = HopeInfo.TryCrateHopeInfo(message, userId, userName);
            if (hope is not null)
            {
                comments.Add(hope);
            }
        }

        return comments;
    }
    /// <summary>ライブチャットのページに入っている最近のコメント(ytInitialData)を枠に追加する</summary>
    private static void AddInitialComments(string html)
    {
        try
        {
            var start = html.IndexOf("ytInitialData");
            if (start < 0) return;
            start = html.IndexOf('{', start);
            var end = html.IndexOf("</script>", start);
            if (start < 0 || end < 0) return;
            var json = html.Substring(start, end - start);
            json = json.Substring(0, json.LastIndexOf('}') + 1);
            var actions = JsonNode.Parse(json)?["contents"]?["liveChatRenderer"]?["actions"];
            if (actions == null) return;
            foreach (var item in actions.AsArray())
            {
                var chat = item?["addChatItemAction"]?["item"]?["liveChatTextMessageRenderer"];
                if (chat == null) continue;
                var name = chat["authorName"]?["simpleText"]?.ToString() ?? "";
                var message = "";
                var runs = chat["message"]?["runs"];
                if (runs != null)
                    foreach (var run in runs.AsArray())
                        if (run?["text"] != null) message += run["text"].ToString();
                AddRecentComment(name, message);
            }
        }
        catch (System.Exception e)
        {
            Logger.Warn($"最近のコメントを読み取れませんでした: {e.Message}", "StreamerInfo");
        }
    }

    private async Task<HttpResponseMessage> FetchChat()
    {
        var param = new Dictionary<string, string>()
        {
            ["key"] = chatData.key
        };

        var response = await client.PostAsync(
            "https://www.youtube.com/youtubei/v1/live_chat/get_live_chat?" + "key=" + param["key"],
            new StringContent(chatData.Build()));

        // OKじゃない場合はnullを返す(配信が終わった時など。次は最初から探し直す)
        if (response.StatusCode != System.Net.HttpStatusCode.OK)
        {
            StatusText = "コメントを取得できませんでした(配信が終わったかもしれません)";
            chatData = null;
            retryAt = System.DateTime.Now.AddSeconds(30);
            return null;
        }
        return response;
    }
    /// <summary>コメント枠に出す状態(取れない時の理由など)。空なら問題なし</summary>
    public static string StatusText = "";
    /// <summary>チャットが見つからなかった時、次に探し直す時刻(何度も取りに行かないように)</summary>
    private static System.DateTime retryAt = System.DateTime.MinValue;
    private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/129.0.0.0 Safari/537.36";

    /// <summary>
    /// チャンネルのURL(@名前・/channel/…・/c/…)なら、今そのチャンネルで配信中の動画IDを探す。
    /// 「チャンネルURL/live」を開くと、配信中ならその動画のページになるので、ページの canonical から動画IDを取る。
    /// </summary>
    private async Task<string> ResolveVideoIdAsync()
    {
        if (!streamkey.StartsWith(ChannelPrefix)) return streamkey;
        var channelUrl = streamkey.Substring(ChannelPrefix.Length).TrimEnd('/');
        var html = await client.GetStringAsync(channelUrl + "/live");
        var m = Regex.Match(html, "<link rel=\"canonical\" href=\"https://www\\.youtube\\.com/watch\\?v=([\\w-]{11})\"");
        return m.Success ? m.Groups[1].Value : "";
    }

    private async Task FirstFetch()
    {
        var videoId = await ResolveVideoIdAsync();
        if (videoId.Length == 0)
        {
            StatusText = "このチャンネルは今配信していないようです(配信が始まると自動でコメントを読みます)";
            retryAt = System.DateTime.Now.AddSeconds(30);
            return;
        }

        var param = new Dictionary<string, string>()
        {
            ["v"] = videoId
        };
        var content = await new FormUrlEncodedContent(param).ReadAsStringAsync();
        var result = await client.GetAsync("https://www.youtube.com/live_chat?" + content);
        var resultContent = await result.Content.ReadAsStringAsync();

        Match matchedKey = Regex.Match(resultContent, "\"INNERTUBE_API_KEY\":\"(.+?)\"");
        Match matchedContinuation = Regex.Match(resultContent, "\"continuation\":\"(.+?)\"");
        Match matchedVisitor = Regex.Match(resultContent, "\"visitorData\":\"(.+?)\"");
        Match matchedClient = Regex.Match(resultContent, "\"clientVersion\":\"(.+?)\"");
        // 取りに行く前に書かれていた最近のコメントも、ページに入っているので枠に出す
        // (続きの取得では「新しく書かれたコメント」しか返ってこないため、最初は枠が空のままになっていた)
        AddInitialComments(resultContent);

        // 続きを取るための情報が無い = 配信が終わっている・まだ始まっていない・URLが違う
        // (以前はここで何も出さずに失敗し続けていたため、コメント枠が「待っています」のままになっていた)
        if (!matchedKey.Success || !matchedContinuation.Success)
        {
            StatusText = "この配信のチャットが見つかりません(配信が終わっているか、URLが違います。チャンネルのURLにすると、配信中の動画を自動で探します)";
            retryAt = System.DateTime.Now.AddSeconds(30);
            Logger.Warn($"YouTubeのチャットが見つかりません(動画ID: {videoId})", "StreamerInfo");
            return;
        }

        StatusText = "";
        chatData = new ChatData(
            matchedKey.Groups[1].Value,
            matchedContinuation.Groups[1].Value,
            matchedVisitor.Groups[1].Value,
            matchedClient.Groups[1].Value);
    }
    public StreamerInfo()
    {
        client = new HttpClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ja");
    }
}