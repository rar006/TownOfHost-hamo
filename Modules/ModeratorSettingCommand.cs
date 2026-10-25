using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AmongUs.GameOptions;
using TownOfHost.Roles.Core;
using UnityEngine;
using static TownOfHost.Utils;

namespace TownOfHost.Modules;

// モデレーター(とホスト)の「/cmd setting 探す言葉 値」: ゲームの設定を名前で探して変える。
// 例: /cmd setting シェリフ キルクール 20  /  /cmd setting シェリフ 50  /  /cmd setting 匿名投票 on
// MOD の設定に加えて、キルクール・視界・会議の時間・タスクの数・マップなど、バニラのゲーム設定も変えられる。
// 見つからなければそう伝え、いくつも見つかったら番号付きで一覧を出して「/cmd setting 番号」で選んでもらう。
public static class ModeratorSettingCommand
{
    private const int MaxChoices = 9;
    private const float PendingSeconds = 90f;

    // MOD の設定(OptionItem)か、バニラの設定(VanillaSetting)のどちらか
    private sealed class Target
    {
        public OptionItem Option;
        public VanillaSetting Vanilla;
        public string FullName => Option != null ? ModeratorSettingCommand.FullName(Option) : Vanilla.FullName;
        public string OwnName => Option != null ? ModeratorSettingCommand.OwnName(Option) : Vanilla.Name;
        public string Current => Option != null ? Clean(Option.GetString()) : Vanilla.Current();
    }

    private sealed class Pending
    {
        public List<Target> Candidates;
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
            SendMessage("使い方: /cmd setting 役職や設定の名前 値\n例: /cmd setting シェリフ キルクール 20\n例: /cmd setting シェリフ 50(出現率)\n例: /cmd setting 匿名投票 on\n例: /cmd setting キルクール 25(バニラの設定も変えられます)", me);
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
        var exact = found.Where(o => Normalize(o.OwnName) == last || (o.Vanilla?.Aliases.Any(a => Normalize(a) == last) ?? false)).ToList();
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
            sb.Append($"{i + 1}: {list[i].FullName} (今: {list[i].Current})\n");
        if (found.Count > list.Count) sb.Append($"(ほか{found.Count - list.Count}件。言葉を増やすと絞り込めます)\n");
        sb.Append("「/cmd setting 番号」で答えてください。");
        SendMessage(sb.ToString(), me);
    }

    // ===== 探す =====
    private static List<Target> Search(List<string> query)
    {
        var result = new List<Target>();
        foreach (var v in VanillaSettings)
        {
            var labels = v.Aliases.Append(v.FullName).Select(Normalize).ToList();
            var all = string.Concat(labels);
            if (query.All(q => all.Contains(q))) result.Add(new Target { Vanilla = v });
        }
        foreach (var o in OptionItem.AllOptions)
        {
            if (!IsChangeable(o)) continue;
            var label = Normalize(FullName(o));
            if (query.All(q => label.Contains(q))) result.Add(new Target { Option = o });
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
    private static void Apply(PlayerControl player, Target t, string raw)
    {
        if (t.Vanilla != null) { ApplyVanilla(player, t.Vanilla, raw); return; }
        var o = t.Option;
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

    // ===== バニラの設定 =====
    private enum VKind { Float, Int, Bool, Choice, Map }

    private sealed class VanillaSetting
    {
        public string Name;
        public string[] Aliases;
        public VKind Kind;
        public FloatOptionNames FloatName;
        public Int32OptionNames IntName;
        public BoolOptionNames BoolName;
        public float Min, Max, Step;
        public string Unit = "";
        // Choice / Map の選択肢(値と名前)
        public (int Value, string Name)[] Choices;

        public string FullName => "バニラ設定 > " + Name;

        public string Current()
        {
            var opt = Main.NormalOptions;
            if (opt == null) return "?";
            return Kind switch
            {
                VKind.Float => $"{opt.GetFloat(FloatName)}{Unit}",
                VKind.Int => $"{opt.GetInt(IntName)}{Unit}",
                VKind.Bool => opt.GetBool(BoolName) ? "ON" : "OFF",
                VKind.Choice => ChoiceName(opt.GetInt(IntName)),
                VKind.Map => ChoiceName(opt.MapId),
                _ => "?",
            };
        }

        public string ChoiceName(int value) => Choices.FirstOrDefault(c => c.Value == value).Name ?? value.ToString();
    }

    private static VanillaSetting F(string name, FloatOptionNames n, float min, float max, float step, string unit, params string[] aliases)
        => new() { Name = name, Aliases = aliases, Kind = VKind.Float, FloatName = n, Min = min, Max = max, Step = step, Unit = unit };
    private static VanillaSetting I(string name, Int32OptionNames n, int min, int max, int step, string unit, params string[] aliases)
        => new() { Name = name, Aliases = aliases, Kind = VKind.Int, IntName = n, Min = min, Max = max, Step = step, Unit = unit };
    private static VanillaSetting B(string name, BoolOptionNames n, params string[] aliases)
        => new() { Name = name, Aliases = aliases, Kind = VKind.Bool, BoolName = n };

    private static readonly VanillaSetting[] VanillaSettings =
    {
        F("キルクールダウン", FloatOptionNames.KillCooldown, 2.5f, 60f, 2.5f, "秒", "キルクール", "キルクル", "kc"),
        F("移動速度", FloatOptionNames.PlayerSpeedMod, 0.25f, 3f, 0.25f, "x", "スピード", "速度"),
        F("クルーの視界", FloatOptionNames.CrewLightMod, 0.25f, 5f, 0.25f, "x", "クルー視界"),
        F("インポスターの視界", FloatOptionNames.ImpostorLightMod, 0.25f, 5f, 0.25f, "x", "インポスター視界", "インポ視界"),
        I("インポスターの数", Int32OptionNames.NumImpostors, 1, 3, 1, "人", "インポ数", "インポスター数"),
        I("緊急会議の回数", Int32OptionNames.NumEmergencyMeetings, 0, 9, 1, "回", "会議の回数", "ボタンの回数"),
        I("緊急会議のクールダウン", Int32OptionNames.EmergencyCooldown, 0, 60, 5, "秒", "会議クール", "ボタンクール"),
        I("会議時間", Int32OptionNames.DiscussionTime, 0, 300, 15, "秒", "議論時間", "話し合いの時間"),
        I("投票時間", Int32OptionNames.VotingTime, 0, 300, 15, "秒"),
        I("通常タスクの数", Int32OptionNames.NumCommonTasks, 0, 99, 1, "", "通常タスク", "コモンタスク"),
        I("ロングタスクの数", Int32OptionNames.NumLongTasks, 0, 99, 1, "", "ロングタスク"),
        I("ショートタスクの数", Int32OptionNames.NumShortTasks, 0, 99, 1, "", "ショートタスク"),
        new() { Name = "キル距離", Aliases = new[] { "キルの距離" }, Kind = VKind.Choice, IntName = Int32OptionNames.KillDistance,
            Choices = new[] { (0, "短い"), (1, "普通"), (2, "長い") } },
        new() { Name = "タスクバーの更新", Aliases = new[] { "タスクバー" }, Kind = VKind.Choice, IntName = Int32OptionNames.TaskBarMode,
            Choices = new[] { (0, "常に"), (1, "会議の時"), (2, "表示しない") } },
        B("追放の確認", BoolOptionNames.ConfirmImpostor, "追放確認", "役職の確認"),
        B("匿名投票", BoolOptionNames.AnonymousVotes),
        B("タスクの見た目", BoolOptionNames.VisualTasks, "視覚タスク", "ビジュアルタスク"),
        new() { Name = "マップ", Aliases = new[] { "map", "ステージ" }, Kind = VKind.Map,
            Choices = new[] { (0, "スケルド"), (1, "ミラHQ"), (2, "ポーラス"), (4, "エアシップ"), (5, "ファングル") } },
    };

    private static void ApplyVanilla(PlayerControl player, VanillaSetting v, string raw)
    {
        var me = player.PlayerId;
        var opt = Main.NormalOptions;
        if (opt == null)
        {
            SendMessage("今はバニラの設定を変えられません。", me);
            return;
        }
        var before = v.Current();
        var text = (raw ?? "").Trim();
        switch (v.Kind)
        {
            case VKind.Float:
            case VKind.Int:
                if (!TryNumber(text, out var n)) { SendMessage($"{v.FullName}: 数字で指定してください。", me); return; }
                if (n < v.Min - 0.0001f || n > v.Max + 0.0001f) { SendMessage($"{v.FullName}: {v.Min}〜{v.Max} の間で指定してください。", me); return; }
                n = Mathf.Clamp(v.Min + Mathf.Round((n - v.Min) / v.Step) * v.Step, v.Min, v.Max);
                if (v.Kind == VKind.Float) opt.SetFloat(v.FloatName, n);
                else opt.SetInt(v.IntName, Mathf.RoundToInt(n));
                break;
            case VKind.Bool:
                var lower = text.ToLowerInvariant();
                if (OnWords.Contains(lower)) opt.SetBool(v.BoolName, true);
                else if (OffWords.Contains(lower)) opt.SetBool(v.BoolName, false);
                else { SendMessage($"{v.FullName}: ON/OFF の設定です。on か off で指定してください。", me); return; }
                break;
            case VKind.Choice:
            case VKind.Map:
                var nv = Normalize(text);
                var hits = v.Choices.Where(c => Normalize(c.Name) == nv).ToList();
                if (hits.Count == 0) hits = v.Choices.Where(c => Normalize(c.Name).Contains(nv)).ToList();
                if (hits.Count == 0 && int.TryParse(text, out var no) && no >= 1 && no <= v.Choices.Length) hits.Add(v.Choices[no - 1]);
                if (hits.Count != 1)
                {
                    SendMessage($"{v.FullName}: 選べるのは: " + string.Join(" / ", v.Choices.Select((c, i) => $"{i + 1}:{c.Name}")) + "(番号でも指定できます)", me);
                    return;
                }
                if (v.Kind == VKind.Map) opt.MapId = (byte)hits[0].Value;
                else opt.SetInt(v.IntName, hits[0].Value);
                break;
        }
        GameOptionsSender.RpcSendOptions();
        var after = v.Current();
        Logger.Info($"{player.Data?.GetLogPlayerName()} が /cmd setting で「{v.FullName}」を {before} → {after} に変更", "ModeratorSetting");
        SendMessage(before == after ? $"「{v.FullName}」は すでに {after} です。" : $"「{v.FullName}」を {before} → {after} に変更しました。", me);
    }

    // 「20秒」「50%」のような単位付きでも数字だけを読む
    private static bool TryNumber(string s, out float value)
    {
        var m = Regex.Match(s ?? "", @"-?\d+(\.\d+)?");
        value = 0f;
        return m.Success && float.TryParse(m.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}
