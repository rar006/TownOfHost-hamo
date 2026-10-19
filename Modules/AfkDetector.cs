using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace TownOfHost.Modules;

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
public static class AfkDetector
{
    public static OptionItem Enabled;
    public static OptionItem Seconds;
    public static OptionItem KickInLobby;

    private class Track
    {
        public Vector2 Position;
        public float LastMoveAt;
        public bool Notified;
    }
    private static readonly Dictionary<byte, Track> tracks = new();
    private static float checkTimer;
    private static bool wasCounting;

    public static void Postfix()
    {
        try
        {
            if (Enabled?.GetBool() != true || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost)
            {
                tracks.Clear();
                return;
            }
            // 会議中・イントロ中などは数えない。再開した時は全員「今動いた」扱いにする(会議で全員がワープするため)
            var counting = GameStates.IsLobby || (GameStates.IsInTask && !GameStates.Intro && !GameStates.CalledMeeting);
            if (!counting) { wasCounting = false; return; }
            if (!wasCounting) { tracks.Clear(); wasCounting = true; }

            checkTimer += Time.deltaTime;
            if (checkTimer < 1f) return; // 1秒に1回で十分
            checkTimer = 0f;

            var limit = Seconds?.GetInt() ?? 120;
            var now = Time.time;
            foreach (var pc in PlayerControl.AllPlayerControls)
            {
                if (pc == null || pc.Data == null || pc.AmOwner || pc.Data.Disconnected) continue;
                if (!GameStates.IsLobby && !pc.IsAlive()) { tracks.Remove(pc.PlayerId); continue; }
                var pos = pc.GetTruePosition();
                if (!tracks.TryGetValue(pc.PlayerId, out var t))
                {
                    tracks[pc.PlayerId] = new Track { Position = pos, LastMoveAt = now };
                    continue;
                }
                if (Vector2.Distance(pos, t.Position) > 0.05f)
                {
                    t.Position = pos;
                    t.LastMoveAt = now;
                    t.Notified = false;
                    continue;
                }
                if (t.Notified || now - t.LastMoveAt < limit) continue;
                t.Notified = true;
                var name = pc.Data.GetLogPlayerName();
                if (GameStates.IsLobby && KickInLobby?.GetBool() == true)
                {
                    var clientId = pc.GetClientId();
                    Logger.Info($"{name} が{limit}秒動いていないためキックしました", "AfkDetector");
                    Utils.SendMessage($"{name} さんは{limit}秒動いていなかったため、キックしました。", PlayerControl.LocalPlayer.PlayerId);
                    if (clientId != -1) AmongUsClient.Instance.KickPlayer(clientId, false);
                    tracks.Remove(pc.PlayerId);
                }
                else
                {
                    Logger.Info($"{name} が{limit}秒動いていません", "AfkDetector");
                    Utils.SendMessage($"{name} さんが{limit}秒動いていません(放置しているかもしれません)。", PlayerControl.LocalPlayer.PlayerId);
                }
            }
        }
        catch (System.Exception e)
        {
            Logger.Warn($"放置の確認に失敗: {e.Message}", "AfkDetector");
        }
    }
}
