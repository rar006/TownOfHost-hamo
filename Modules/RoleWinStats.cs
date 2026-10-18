using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using TownOfHost.Roles.Core;

namespace TownOfHost.Modules;

/// <summary>
/// 役職ごとの勝率を記録する(便利機能・ホストのみ)。
/// 試合が終わるたびに、全員の役職と勝ち負けを TOHhm_DATA/RoleWinStats.json に足していく(廃村は数えない)。
/// 「/stats」(または「/cmd stats」)で勝率の高い順・低い順を表示、「/stats 役職名」でその役職だけ表示。
/// </summary>
[HarmonyPatch]
public static class RoleWinStats
{
    public static OptionItem Enabled;
    private static bool IsOn => Enabled?.GetBool() == true;

    private const int MinGamesForRanking = 3; // 順位に入れる最低試合数
    private const int RankingCount = 5;

    private class Record
    {
        public int Games { get; set; }
        public int Wins { get; set; }
    }
    private static Dictionary<string, Record> data;
    private static string FilePath => Path.Combine(Main.BaseDirectory, "RoleWinStats.json");

    private static Dictionary<string, Record> Data
    {
        get
        {
            if (data != null) return data;
            try
            {
                data = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, Record>>(File.ReadAllText(FilePath)) ?? new()
                    : new();
            }
            catch (Exception e)
            {
                Logger.Warn($"役職の勝率を読み込めませんでした: {e.Message}", "RoleWinStats");
                data = new();
            }
            return data;
        }
    }

    private static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Data)); }
        catch (Exception e) { Logger.Warn($"役職の勝率を保存できませんでした: {e.Message}", "RoleWinStats"); }
    }

    [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnGameEnd)), HarmonyPostfix]
    public static void OnGameEnd()
    {
        try
        {
            if (!IsOn || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            if (!GameModeManager.IsStandardClass()) return;
            if (CustomWinnerHolder.WinnerTeam is CustomWinner.Draw) return; // 廃村は数えない
            var winners = CustomWinnerHolder.WinnerIds ?? new HashSet<byte>();
            var count = 0;
            foreach (var (id, state) in PlayerState.AllPlayerStates)
            {
                if (state == null) continue;
                var role = state.MainRole;
                if (role is CustomRoles.NotAssigned or CustomRoles.GM) continue;
                var key = role.ToString();
                if (!Data.TryGetValue(key, out var r)) Data[key] = r = new Record();
                r.Games++;
                if (winners.Contains(id)) r.Wins++;
                count++;
            }
            if (count > 0) Save();
        }
        catch (Exception e)
        {
            Logger.Warn($"役職の勝率の記録に失敗: {e.Message}", "RoleWinStats");
        }
    }

    /// <summary>「/stats」「/cmd stats」なら表示して true(sendTo: 結果を送る相手)</summary>
    public static bool TryHandle(string text, byte sendTo)
    {
        if (!IsOn || string.IsNullOrWhiteSpace(text)) return false;
        var tokens = text.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;
        int start;
        if (tokens[0].Equals("/stats", StringComparison.OrdinalIgnoreCase)) start = 1;
        else if (tokens.Length >= 2 && tokens[0].Equals("/cmd", StringComparison.OrdinalIgnoreCase) && tokens[1].Equals("stats", StringComparison.OrdinalIgnoreCase)) start = 2;
        else return false;
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return false;
        var query = string.Join(" ", tokens.Skip(start));
        Utils.SendMessage(query.Length > 0 ? RoleText(query) : RankingText(), sendTo, "<#7fd8ff>【役職の勝率】</color>");
        return true;
    }

    private static string Line(string key, Record r)
    {
        var name = Enum.TryParse<CustomRoles>(key, out var role) ? UtilsRoleText.GetRoleName(role) : key;
        return $"{name}: {r.Wins * 100 / Math.Max(1, r.Games)}% ({r.Wins}/{r.Games})";
    }

    private static string RankingText()
    {
        var lf = ((char)10).ToString();
        var list = Data.Where(kv => kv.Value.Games >= MinGamesForRanking).ToList();
        if (list.Count == 0) return $"まだ記録が少ないです({MinGamesForRanking}試合以上遊んだ役職から表示されます)。";
        var sb = new StringBuilder("▼勝率が高い役職");
        foreach (var kv in list.OrderByDescending(kv => (double)kv.Value.Wins / kv.Value.Games).ThenByDescending(kv => kv.Value.Games).Take(RankingCount))
            sb.Append(lf).Append(Line(kv.Key, kv.Value));
        sb.Append(lf).Append(lf).Append("▼勝率が低い役職");
        foreach (var kv in list.OrderBy(kv => (double)kv.Value.Wins / kv.Value.Games).ThenByDescending(kv => kv.Value.Games).Take(RankingCount))
            sb.Append(lf).Append(Line(kv.Key, kv.Value));
        sb.Append(lf).Append(lf).Append($"({MinGamesForRanking}試合以上の役職のみ・「/stats 役職名」で個別に表示)");
        return sb.ToString();
    }

    private static string RoleText(string query)
    {
        foreach (var (key, r) in Data)
        {
            var name = Enum.TryParse<CustomRoles>(key, out var role) ? UtilsRoleText.GetRoleName(role) : key;
            if (name.RemoveHtmlTags() == query || key.Equals(query, StringComparison.OrdinalIgnoreCase)) return Line(key, r);
        }
        return $"「{query}」の記録はまだありません。";
    }
}

/// <summary>ホストが自分で「/stats」を打った時</summary>
[HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
[HarmonyPriority(Priority.First)]
class RoleWinStatsChatPatch
{
    public static bool Prefix(ChatController __instance)
    {
        var textArea = __instance?.freeChatField?.textArea;
        if (textArea == null || PlayerControl.LocalPlayer == null) return true;
        if (!RoleWinStats.TryHandle(textArea.text, PlayerControl.LocalPlayer.PlayerId)) return true;
        textArea.Clear();
        return false;
    }
}
