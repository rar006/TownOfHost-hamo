using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TownOfHost.Roles.Core;
using UnityEngine;
using static TownOfHost.Utils;

namespace TownOfHost.Modules;

// モデレーター(とホスト)の「/cmd setting 探す言葉 値」: ゲームの設定を名前で探して変える。
// 例: /cmd setting シェリフ キルクール 20  /  /cmd setting シェリフ 50  /  /cmd setting 匿名投票 on
// 見つからなければそう伝え、いくつも見つかったら番号付きで一覧を出して「/cmd setting 番号」で選んでもらう。
public static class ModeratorSettingCommand
{
    private const int MaxChoices = 9;
    private const float PendingSeconds = 90f;

    private sealed class Pending
    {
        public List<OptionItem> Candidates;
        public string Value;
        public float At;
    }
    private static readonly Dictionary<byte, Pending> pending = new();

    public static void Handle(PlayerControl player, string[] args)
    {
        var me = player.PlayerId;
        if (Options.OptionCommandModeratorSetting?.GetBool() == true)
        {
            SendMessage("このコマンドは部屋の設定で無効化されています。", me);
            return;
        }
        if (!GameStates.IsLobby)
        {
            SendMessage("/cmd setting はロビーでだけ使えます。", me);
            return;
        }
        var words = args.Skip(1).Where(a => !string.IsNullOrWhiteSpace(a)).ToList();

        // 前に出した候補の一覧から、番号で選んだ時
        if (words.Count == 1 && int.TryParse(words[0], out var choice) && pending.TryGetValue(me, out var p) && Time.realtimeSinceStartup - p.At <= PendingSeconds)
        {
            pending.Remove(me);
            if (choice < 1 || choice > p.Candidates.Count)
            {
                SendMessage($"番号は 1〜{p.Candidates.Count} で答えてください。", me);
                return;
            }
            Apply(player, p.Candidates[choice - 1], p.Value);
            return;
        }
        if (words.Count < 2)
        {
            SendMessage("使い方: /cmd setting 役職や設定の名前 値\n例: /cmd setting シェリフ キルクール 20\n例: /cmd setting シェリフ 50(出現率)\n例: /cmd setting 匿名投票 on", me);
            return;
        }

        var value = words[^1];
        var query = words.Take(words.Count - 1).Select(Normalize).Where(w => w.Length > 0).ToList();
        var found = Search(query);
        if (found.Count == 0)
        {
            SendMessage($"「{string.Join(" ", words.Take(words.Count - 1))}」に当てはまる設定が見つかりませんでした。", me);
            return;
        }
        if (found.Count == 1)
        {
            Apply(player, found[0], value);
            return;
        }

        // いくつも見つかった時: 最後の言葉と設定名がぴったり同じものが1つだけなら、それにする
        var last = query[^1];
        var exact = found.Where(o => Normalize(OwnName(o)) == last).ToList();
        if (exact.Count == 1)
        {
            Apply(player, exact[0], value);
            return;
        }

        var list = (exact.Count > 1 ? exact : found).Take(MaxChoices).ToList();
        pending[me] = new Pending { Candidates = list, Value = value, At = Time.realtimeSinceStartup };
        var sb = new StringBuilder();
        sb.Append($"「{string.Join(" ", words.Take(words.Count - 1))}」で{found.Count}件見つかりました。どれですか？\n");
        for (var i = 0; i < list.Count; i++)
            sb.Append($"{i + 1}: {FullName(list[i])} (今: {Clean(list[i].GetString())})\n");
        if (found.Count > list.Count) sb.Append($"(ほか{found.Count - list.Count}件。言葉を増やすと絞り込めます)\n");
        sb.Append("「/cmd setting 番号」で答えてください。");
        SendMessage(sb.ToString(), me);
    }

    // ===== 探す =====
    private static List<OptionItem> Search(List<string> query)
    {
        var result = new List<OptionItem>();
        foreach (var o in OptionItem.AllOptions)
        {
            if (!IsChangeable(o)) continue;
            var label = Normalize(FullName(o));
            if (query.All(q => label.Contains(q))) result.Add(o);
        }
        return result;
    }

    private static bool IsChangeable(OptionItem o)
        => o != null && !o.IsHidden
           && o is BooleanOptionItem or IntegerOptionItem or FloatOptionItem or StringOptionItem
           && o is not PresetOptionItem;

    private static string OwnName(OptionItem o)
    {
        try { return Clean(o.GetName(disableColor: true)); } catch { return o.Name; }
    }

    // 「役職 > 親の設定 > この設定」の形の名前
    private static string FullName(OptionItem o)
    {
        var parts = new List<string>();
        for (var x = o; x != null; x = x.Parent) parts.Insert(0, OwnName(x));
        var role = o.CustomRole is not CustomRoles.NotAssigned ? o.CustomRole : o.ParentRole;
        if (role is not CustomRoles.NotAssigned)
        {
            var roleName = Clean(UtilsRoleText.GetRoleName(role));
            if (!string.IsNullOrEmpty(roleName) && !parts.Any(p => p.Contains(roleName))) parts.Insert(0, roleName);
        }
        return string.Join(" > ", parts.Where(p => p.Length > 0));
    }

    private static string Clean(string s) => (s ?? "").RemoveHtmlTags().Replace("\n", " ").Trim();

    // 比べる時の形: 小文字・空白なし・ひらがなはカタカナにする
    private static string Normalize(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.RemoveHtmlTags().ToLowerInvariant())
        {
            if (char.IsWhiteSpace(ch) || ch is '>' or '・' or '　') continue;
            sb.Append(ch is >= 'ぁ' and <= 'ゖ' ? (char)(ch + 0x60) : ch);
        }
        return sb.ToString();
    }

    // ===== 変える =====
    private static void Apply(PlayerControl player, OptionItem o, string raw)
    {
        var me = player.PlayerId;
        var before = Clean(o.GetString());
        if (!TryGetIndex(o, raw, out var index, out var error))
        {
            SendMessage($"{FullName(o)}: {error}", me);
            return;
        }
        o.SetValue(index);
        var after = Clean(o.GetString());
        Logger.Info($"{player.Data?.GetLogPlayerName()} が /cmd setting で「{FullName(o)}」を {before} → {after} に変更", "ModeratorSetting");
        SendMessage(before == after ? $"「{FullName(o)}」は すでに {after} です。" : $"「{FullName(o)}」を {before} → {after} に変更しました。", me);
    }

    private static readonly string[] OnWords = { "on", "true", "1", "yes", "y", "はい", "オン", "おん", "有効", "○", "o" };
    private static readonly string[] OffWords = { "off", "false", "0", "no", "n", "いいえ", "オフ", "おふ", "無効", "×", "x" };

    private static bool TryGetIndex(OptionItem o, string raw, out int index, out string error)
    {
        index = 0;
        error = "";
        var v = (raw ?? "").Trim();
        switch (o)
        {
            case BooleanOptionItem:
                var lower = v.ToLowerInvariant();
                if (OnWords.Contains(lower)) { index = 1; return true; }
                if (OffWords.Contains(lower)) { index = 0; return true; }
                error = "ON/OFF の設定です。on か off で指定してください。";
                return false;
            case IntegerOptionItem io:
                if (!TryNumber(v, out var n)) { error = "数字で指定してください。"; return false; }
                if (n < io.Rule.MinValue || n > io.Rule.MaxValue) { error = $"{io.Rule.MinValue}〜{io.Rule.MaxValue} の間で指定してください。"; return false; }
                index = io.Rule.GetNearestIndex((int)Math.Round(n));
                return true;
            case FloatOptionItem fo:
                if (!TryNumber(v, out var f)) { error = "数字で指定してください。"; return false; }
                if (f < fo.Rule.MinValue - 0.0001f || f > fo.Rule.MaxValue + 0.0001f) { error = $"{fo.Rule.MinValue}〜{fo.Rule.MaxValue} の間で指定してください。"; return false; }
                index = fo.Rule.GetNearestIndex(f);
                return true;
            case StringOptionItem so:
                var names = Enumerable.Range(0, so.Selections.Length).Select(i => Clean(so.GetString(i))).ToList();
                if (int.TryParse(v, out var no) && no >= 1 && no <= names.Count && !names.Any(x => x == v)) { index = no - 1; return true; }
                var nv = Normalize(v);
                var hits = Enumerable.Range(0, names.Count).Where(i => Normalize(names[i]) == nv).ToList();
                if (hits.Count == 0) hits = Enumerable.Range(0, names.Count).Where(i => Normalize(names[i]).Contains(nv)).ToList();
                if (hits.Count == 1) { index = hits[0]; return true; }
                error = (hits.Count == 0 ? "その選択肢はありません。" : "選択肢がいくつも当てはまります。") + "選べるのは: "
                    + string.Join(" / ", names.Select((x, i) => $"{i + 1}:{x}")) + "(番号でも指定できます)";
                return false;
        }
        error = "この設定はコマンドでは変えられません。";
        return false;
    }

    // 「20秒」「50%」のような単位付きでも数字だけを読む
    private static bool TryNumber(string s, out float value)
    {
        var m = Regex.Match(s ?? "", @"-?\d+(\.\d+)?");
        value = 0f;
        return m.Success && float.TryParse(m.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
