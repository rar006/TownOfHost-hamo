using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace TownOfHost.Modules;

// 配信のコメントで参加希望を書いた人がまだ入れていないのに、部屋が満員(設定した人数以上)の時に、ホストに知らせる。
// 設定で、そのまま部屋を自動で立て直すこともできる(立て直す前に、全員にお知らせして少し待つ)。
[HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
public static class HopeFullWatch
{
    public static OptionItem Warn;
    public static OptionItem Threshold;
    public static OptionItem AutoRehost;

    // 同じ状態で何度も知らせないように、次に知らせてよい時刻
    private const float WarnInterval = 60f;
    // 立て直す前に待つ秒数(この間に人数が減ったり、希望者がいなくなったりしたらやめる)
    private const float RehostDelay = 30f;
    private static float nextWarnAt;
    private static float nextCheckAt;
    private static float rehostAt = -1f;
    private static bool wasFull;

    public static void Postfix()
    {
        if (Time.time < nextCheckAt) return;
        nextCheckAt = Time.time + 1f;
        if (Warn?.GetBool() != true) { Cancel(); return; }
        var client = AmongUsClient.Instance;
        if (client == null || !client.AmHost || !GameStates.IsLobby || GameStates.IsInGame || !GameStates.IsOnlineGame) { Cancel(); return; }

        var players = PlayerCatch.AllPlayerControls.Where(p => p != null && p.Data != null && !p.Data.Disconnected).ToArray();
        var waiting = WaitingHopes(players);
        var full = players.Length >= Threshold.GetInt() && waiting > 0;
        if (!full)
        {
            if (rehostAt >= 0f) Utils.SendMessage("満員ではなくなったので、部屋の立て直しをやめました。", byte.MaxValue, "<#ff8080>【参加希望】</color>");
            Cancel();
            return;
        }

        if (!wasFull || Time.time >= nextWarnAt)
        {
            wasFull = true;
            nextWarnAt = Time.time + WarnInterval;
            var text = $"部屋が{players.Length}人で満員ですが、コメントで参加希望を書いた人がまだ{waiting}人入れていません。";
            if (AutoRehost.GetBool() && rehostAt < 0f)
            {
                rehostAt = Time.time + RehostDelay;
                Utils.SendMessage($"部屋が満員のため、{RehostDelay:F0}秒後に部屋を立て直します。立て直した後、もう一度入り直してください。", byte.MaxValue, "<#ff8080>【参加希望】</color>");
            }
            Utils.SendMessage(text, PlayerControl.LocalPlayer.PlayerId, "<#ff8080>【ホストだけに表示】</color>");
            Logger.Info(text, "HopeFullWatch");
        }

        if (rehostAt >= 0f && Time.time >= rehostAt)
        {
            rehostAt = -1f;
            Logger.Info("参加希望の人が入れないので、部屋を立て直します", "HopeFullWatch");
            Modules.AutoRehost.TriggerHopeRehost();
        }
    }

    // コメントで参加希望を書いたのに、まだ部屋にいない人の数
    private static int WaitingHopes(PlayerControl[] players)
    {
        if (!StreamerInfo.HasStream) return 0;
        var names = players.Select(p => p.Data.PlayerName).ToHashSet();
        return StreamerInfo.Hopeplayers.Values.Count(h => h != null && !string.IsNullOrEmpty(h.PlayerName) && !names.Contains(h.PlayerName));
    }

    private static void Cancel()
    {
        wasFull = false;
        rehostAt = -1f;
    }
}
