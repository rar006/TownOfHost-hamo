using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;

namespace TownOfHost.Modules;

/// <summary>
/// 起動を早くするため、メインメニューで必要になる通信(versions.json・GitHubのリリース情報)を
/// MODの読み込み直後にバックグラウンドで始めておく。
/// ここでは文字列を取ってくるだけ(純粋な .NET の HttpClient なのでワーカースレッドで安全)。
/// JSONの解析などゲームに関わる処理は、従来どおり呼び出し元(メインスレッド)で行う。
/// </summary>
public static class StartupPrefetch
{
    private static readonly HttpClient Client = CreateClient();
    private static readonly ConcurrentDictionary<string, Task<string>> Tasks = new();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        c.DefaultRequestHeaders.Add("User-Agent", "TownOfHost-hamo Updater");
        return c;
    }

    /// <summary>取得を始める(すでに始めていれば何もしない)</summary>
    public static void Start(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        Tasks.GetOrAdd(url, u => Task.Run(async () =>
        {
            using var res = await Client.GetAsync(u).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)res.StatusCode}");
            return await res.Content.ReadAsStringAsync().ConfigureAwait(false);
        }));
    }

    /// <summary>
    /// 結果を受け取る(まだなら最大 timeoutMs 待つ)。失敗・時間切れは null。
    /// 一度受け取った(または失敗した)URLは忘れるので、次に呼ぶと取り直す。
    /// </summary>
    public static string Get(string url, int timeoutMs = 8000)
    {
        Start(url);
        if (!Tasks.TryGetValue(url, out var task)) return null;
        try
        {
            if (!task.Wait(timeoutMs))
            {
                Logger.Warn($"timeout: {url}", nameof(StartupPrefetch));
                return null;
            }
            return task.Result;
        }
        catch (Exception e)
        {
            Logger.Warn($"failed: {url} {e.GetBaseException().Message}", nameof(StartupPrefetch));
            return null;
        }
        finally
        {
            if (task.IsCompleted) Tasks.TryRemove(url, out _);
        }
    }
}
