using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using HarmonyLib;
using InnerNet;
using UnityEngine;

namespace TownOfHost.Modules;

[HarmonyPatch]
public static class SharedBanList
{
    public static OptionItem Enabled;
    private static bool IsOn => Enabled?.GetBool() == true && AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost;

    private static readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static string Url => LobbyChatRemoteConfig.EndpointUrl.Replace("/lobbychat", "/bans").TrimEnd('/');

    private static Dictionary<string, string> bans = new();
    private static float nextFetchAt;
    private static volatile bool fetching;

    private class Response
    {
        public List<Entry> Bans { get; set; } = new();
    }
    private class Entry
    {
        public string Fc { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    private static void FetchIfNeeded(bool force = false)
    {
        if (fetching || (!force && Time.time < nextFetchAt)) return;
        nextFetchAt = Time.time + 120f;
        fetching = true;
        _ = Task.Run(async () =>
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, Url);
                if (!string.IsNullOrEmpty(LobbyChatRemoteConfig.ApiKey))
                    req.Headers.Add("Authorization", $"Bearer {LobbyChatRemoteConfig.ApiKey}");
                using var res = await http.SendAsync(req).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) return;
                var r = JsonSerializer.Deserialize<Response>(await res.Content.ReadAsStringAsync().ConfigureAwait(false), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var next = new Dictionary<string, string>();
                foreach (var e in r?.Bans ?? new()) if (!string.IsNullOrEmpty(e.Fc)) next[e.Fc.ToLowerInvariant()] = e.Reason ?? "";
                bans = next;
            }
            catch (Exception e) { Logger.Warn($"共有ブラックリストを読めませんでした: {e.Message}", "SharedBanList"); }
            finally { fetching = false; }
        });
    }

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Update)), HarmonyPostfix]
    public static void LobbyTick()
    {
        if (IsOn) FetchIfNeeded();
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined)), HarmonyPostfix]
    public static void OnPlayerJoined([HarmonyArgument(0)] ClientData client)
    {
        try
        {
            if (!IsOn || client == null || client.Id == AmongUsClient.Instance.ClientId) return;
            var fc = (client.FriendCode ?? "").ToLowerInvariant();
            if (fc.Length == 0 || !bans.TryGetValue(fc, out var reason)) return;
            var name = client.PlayerName;
            Logger.Info($"共有ブラックリストの {name}({fc}) をキックしました", "SharedBanList");
            AmongUsClient.Instance.KickPlayer(client.Id, false);
            Utils.SendMessage($"{name} さんは共有ブラックリストに入っているため、キックしました。{(string.IsNullOrEmpty(reason) ? "" : $"(理由: {reason})")}",
                PlayerControl.LocalPlayer.PlayerId, "<#ff6666>【ブラックリスト】</color>");
        }
        catch (Exception e) { Logger.Warn($"共有ブラックリストの確認に失敗: {e.Message}", "SharedBanList"); }
    }
}
