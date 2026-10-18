using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TownOfHost.Modules;

/// <summary>
/// YouTubeのコメントで次のマップを投票する(便利機能・ホストのみ)。
/// 「!skeld」「!mira」「!polus」「!airship」「!fungle」(日本語・全角の！も可)と書くと投票。1人1票で、書き直すと変わる。
/// 試合開始時に一番多いマップにして、投票はリセットする。
/// </summary>
public static class YouTubeMapVote
{
    public static OptionItem Enabled;
    public static bool IsOn => Enabled?.GetBool() == true;

    /// <summary>投票者(YouTubeのチャンネルID) → マップID</summary>
    private static readonly Dictionary<string, byte> votes = new();
    /// <summary>表示を書き直すかの判断用</summary>
    public static int Version { get; private set; }

    private static readonly (string[] Words, byte MapId, string Name)[] Maps =
    {
        (new[] { "skeld", "スケルド" }, 0, "Skeld"),
        (new[] { "mira", "mirahq", "ミラ" }, 1, "MIRA"),
        (new[] { "polus", "ポーラス" }, 2, "Polus"),
        (new[] { "airship", "エアシップ" }, 4, "Airship"),
        (new[] { "fungle", "ファングル" }, 5, "Fungle"),
    };

    /// <summary>コメントを受け取った時に呼ぶ(コメント取得のスレッドから呼ばれる)</summary>
    public static void OnComment(string voterId, string text)
    {
        if (!IsOn || string.IsNullOrWhiteSpace(text)) return;
        var t = text.Trim().Replace('！', '!').ToLowerInvariant();
        if (!t.StartsWith("!")) return;
        var word = t.Substring(1).Split(' ', '　')[0];
        foreach (var (words, mapId, _) in Maps)
        {
            if (!words.Contains(word)) continue;
            var key = string.IsNullOrEmpty(voterId) ? "?" : voterId;
            lock (votes) { votes[key] = mapId; Version++; }
            return;
        }
    }

    /// <summary>コメント枠の一番上に出す集計(投票が無ければ使い方)</summary>
    public static string Summary()
    {
        Dictionary<byte, int> counts;
        lock (votes) counts = votes.Values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        var sb = new StringBuilder("<color=#ff8080>マップ投票</color> ");
        if (counts.Count == 0) return sb.Append("<color=#aaaaaa>「!skeld」「!polus」などで投票</color>").ToString();
        sb.Append(string.Join(" / ", Maps.Where(m => counts.ContainsKey(m.MapId)).OrderByDescending(m => counts[m.MapId]).Select(m => $"{m.Name} {counts[m.MapId]}")));
        return sb.ToString();
    }

    /// <summary>試合開始時(ホスト): 一番多いマップにして、投票をリセットする。同数ならその中からランダム</summary>
    public static void ApplyAtGameStart()
    {
        if (!IsOn || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
        Dictionary<byte, int> counts;
        lock (votes) { counts = votes.Values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count()); votes.Clear(); Version++; }
        if (counts.Count == 0) return;
        var max = counts.Values.Max();
        var top = counts.Where(c => c.Value == max).Select(c => c.Key).ToList();
        var mapId = top[IRandom.Instance.Next(top.Count)];
        Main.NormalOptions.MapId = mapId;
        Logger.Info($"YouTubeのマップ投票で {Maps.First(m => m.MapId == mapId).Name} に決定({max}票)", "YouTubeMapVote");
    }
}
