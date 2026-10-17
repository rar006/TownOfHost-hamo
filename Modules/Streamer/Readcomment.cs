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
    private static float commentTimer;

    /// <summary>
    /// 配信URLを設定する(メニュー・ゲーム内の設定の両方から使う)。YouTubeのURLでなければ false。
    /// 設定したURLは保存して、次に起動した時も使う。
    /// </summary>
    public static bool TrySetStreamURL(string url)
    {
        url = (url ?? "").Trim();
        if (!(url.StartsWith("https://youtube") || url.StartsWith("https://www.youtube.com/") || url.StartsWith("https://youtu.be"))) return false;
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
        if (AmongUsClient.Instance?.AmHost is true) return;
        if (!Main.ShowStreamComments.Value || !HasStream) return;
        commentTimer += deltaTime;
        if (commentTimer < 3f) return;
        commentTimer = 0f;
        _ = Task.Run(streamerInfo.FetchAsync);
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
            if (StreamURL.StartsWith("https://youtu.be"))
            {
                var nonq = StreamURL.RemoveDeltext("https://youtu.be/");
                if (nonq.Contains("?"))
                {
                    streamkey = nonq.Split("?")[0];
                }
                else streamkey = nonq;
            }
            else if (StreamURL.StartsWith("https://youtube.com/live/"))
            {

                var nonq = StreamURL.RemoveDeltext("https://youtube.com/live/").Split("?")[0];
                if (nonq.Contains("?"))
                {
                    streamkey = nonq.Split("?")[0];
                }
                else streamkey = nonq;
            }
            else
            {
                streamkey = StreamURL.Split("=")[1];
            }
            if (streamkey.Contains("&t"))
            {
                streamkey = streamkey.Split("&")[0];
            }

            if (oldkey != streamkey)
            {
                streamerInfo = new StreamerInfo();
            }
        }
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

        timer += Time.fixedDeltaTime;

        if (timer > 3)//3秒に1度の処理にする
        {
            _ = Task.Run(streamerInfo.FetchAsync);
            timer = 0;
        }
    }

    public async Task<HopeInfo[]> FetchAsync()
    {
        if (chatData == null) await FirstFetch();

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

            var hope = HopeInfo.TryCrateHopeInfo(message, userId, userName);
            if (hope is not null)
            {
                comments.Add(hope);
            }
        }

        return comments;
    }
    private async Task<HttpResponseMessage> FetchChat()
    {
        if (Main.IsAndroid()) return null;
        var param = new Dictionary<string, string>()
        {
            ["key"] = chatData.key
        };

        var response = await client.PostAsync(
            "https://www.youtube.com/youtubei/v1/live_chat/get_live_chat?" + "key=" + param["key"],
            new StringContent(chatData.Build()));

        // OKじゃない場合はnullを返す
        if (response.StatusCode != System.Net.HttpStatusCode.OK) return null;
        return response;
    }
    private async Task FirstFetch()
    {
        client.DefaultRequestHeaders.Add(
    "User-Agent",
    "Mozilla/5.0 (Windows NT 6.3; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/86.0.4240.111 Safari/537.36");

        var param = new Dictionary<string, string>()
        {
            ["v"] = streamkey
        };
        var content = await new FormUrlEncodedContent(param).ReadAsStringAsync();
        var result = await client.GetAsync("https://www.youtube.com/live_chat?" + content);
        var resultContent = await result.Content.ReadAsStringAsync();

        Match matchedKey = Regex.Match(resultContent, "\"INNERTUBE_API_KEY\":\"(.+?)\"");
        Match matchedContinuation = Regex.Match(resultContent, "\"continuation\":\"(.+?)\"");
        Match matchedVisitor = Regex.Match(resultContent, "\"visitorData\":\"(.+?)\"");
        Match matchedClient = Regex.Match(resultContent, "\"clientVersion\":\"(.+?)\"");

        chatData = new ChatData(
            matchedKey.Groups[1].Value,
            matchedContinuation.Groups[1].Value,
            matchedVisitor.Groups[1].Value,
            matchedClient.Groups[1].Value);
    }
    public StreamerInfo()
    {
        client = new HttpClient();
    }
}