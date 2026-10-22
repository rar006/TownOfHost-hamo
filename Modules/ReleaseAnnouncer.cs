using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

namespace TownOfHost.Modules;

// 新しいバージョンがリリースされたら、Among Us の中で知らせる(OTHER2 の設定で ON/OFF)。
// ロビーならすぐ、試合中は会議の時だけ出す(会議でない時は、次の会議で出す)。
// GitHub の最新リリースを数分おきに確かめる。起動した時に分かっていたものは知らせない(起動後に出たものだけ)。
[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
public static class ReleaseAnnouncer
{
    public static OptionItem Enabled;

    private const string LatestUrl = "https://api.github.com/repos/rar006/TownOfHost-hamo/releases/latest";
    private const float CheckIntervalSeconds = 600f;
    private const float FirstCheckDelaySeconds = 120f;
    private static readonly HttpClient http = CreateClient();

    private static Version knownVersion;      // ここまでのバージョンは知らせない
    private static string pendingTitle;       // 知らせる予定のバージョン(まだ出していない)
    private static float nextCheckAt = -1f;
    private static bool checking;

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.Add("User-Agent", "TownOfHost-hamo");
        return c;
    }

    public static void Postfix()
    {
        try
        {
            if (Enabled?.GetBool() != true) return;
            var now = Time.realtimeSinceStartup;
            if (nextCheckAt < 0f) nextCheckAt = now + FirstCheckDelaySeconds;
            if (!checking && now >= nextCheckAt)
            {
                nextCheckAt = now + CheckIntervalSeconds;
                StartCheck();
            }
            if (pendingTitle != null && CanShowNow()) Show();
        }
        catch (Exception e)
        {
            Logger.Warn($"リリースのお知らせに失敗: {e.Message}", "ReleaseAnnouncer");
        }
    }

    // ロビーにいる時、または試合中の会議の時だけ出す
    private static bool CanShowNow()
    {
        if (AmongUsClient.Instance == null || PlayerControl.LocalPlayer == null) return false;
        if (GameStates.IsLobby) return true;
        return GameStates.InGame && GameStates.IsMeeting && MeetingHud.Instance != null;
    }

    private static void StartCheck()
    {
        checking = true;
        // 起動時に分かっていた最新版(と今のバージョン)は知らせない
        if (knownVersion == null)
        {
            knownVersion = Main.version;
            if (ModUpdater.latestVersion != null && ModUpdater.latestVersion.CompareTo(knownVersion) > 0) knownVersion = ModUpdater.latestVersion;
        }
        _ = Task.Run(async () =>
        {
            string tag = null;
            try
            {
                var json = await http.GetStringAsync(LatestUrl).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("tag_name", out var t)) tag = t.GetString();
            }
            catch (Exception e)
            {
                MainThreadDispatcher.Enqueue(() => Logger.Info($"最新リリースを確かめられませんでした: {e.Message}", "ReleaseAnnouncer"));
            }
            MainThreadDispatcher.Enqueue(() =>
            {
                checking = false;
                if (string.IsNullOrEmpty(tag)) return;
                var v = ModUpdater.ParseTagVersion(tag);
                if (v == null || v.CompareTo(knownVersion) <= 0) return;
                knownVersion = v;
                pendingTitle = tag.TrimStart('v', 'V');
                Logger.Info($"新しいバージョン {tag} がリリースされました(試合中なら次の会議で知らせます)", "ReleaseAnnouncer");
            });
        });
    }

    private static void Show()
    {
        var title = pendingTitle;
        pendingTitle = null;
        var text = $"<size=130%><color=#ff8fc8><b>★ TOH-hamo v{title} が出ました ★</b></color></size>\n"
                   + "<size=90%>試合が終わったら、メインメニューの「アップデート」から更新できます。</size>";
        const string header = "<#ff8fc8>【アップデートのお知らせ】</color>";
        if (AmongUsClient.Instance.AmHost)
        {
            Utils.SendMessage(text, byte.MaxValue, header);
        }
        else
        {
            // 参加者は自分の画面にだけ出す(参加者はチャットを送れないため)
            DestroyableSingleton<HudManager>.Instance?.Chat?.AddChat(PlayerControl.LocalPlayer, $"{header}\n{text}");
        }
        Logger.Info($"新しいバージョン v{title} のリリースを知らせました", "ReleaseAnnouncer");
    }
}
