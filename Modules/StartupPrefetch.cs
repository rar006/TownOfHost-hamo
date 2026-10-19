using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Threading.Tasks;

namespace TownOfHost.Modules;

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
