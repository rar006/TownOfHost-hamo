using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace TownOfHost.Modules;

/// <summary>
/// ロビー投票(便利機能・ホストのみ)。
/// ・プリセット投票: ホストが /vote(全部のプリセット) か /vote 1 3 5(番号を指定) と送ると30秒の投票が始まり、
///   時間になったら一番多いプリセットに切り替える(同じ数ならランダム・誰も投票しなければ変えない)。
/// ・自由な質問の投票: ホストが /poll 質問 選択肢1 選択肢2 … と送ると30秒の投票が始まり、結果を知らせるだけ(設定は変えない)。
/// みんなはチャットに数字(1・2…)か /v 数字 を送って投票する(何回でも送り直せる・最後の1票が有効)。
/// /vote stop か /poll stop で途中でやめる。同時に1つだけ。
/// </summary>
[HarmonyPatch]
public static class LobbyVote
{
    public static OptionItem Enabled;
    private static bool IsOn => Enabled?.GetBool() == true;
    private const float VoteSeconds = 30f;
    private const string PresetTitle = "<#7fd8ff>【プリセット投票】</color>";
    private const string PollTitle = "<#7fd8ff>【投票】</color>";
    private const int MaxPollChoices = 9;

    /// <summary>選択肢の表示名。空なら投票していない</summary>
    private static readonly List<string> labels = new();
    /// <summary>プリセット投票なら、選択肢ごとのプリセット番号(0〜)。自由な質問なら空</summary>
    private static readonly List<int> presets = new();
    private static string question = "";
    private static readonly Dictionary<byte, int> votes = new();
    private static float endAt;
    private static bool warned;
    public static bool IsActive => labels.Count > 0;
    private static bool IsPresetVote => presets.Count > 0;
    private static string Title => IsPresetVote ? PresetTitle : PollTitle;

    private static string PresetName(int preset)
    {
        // プリセットの表示名。GetString は今のプリセットの名前を返すので、名前の設定から直接読む
        var entries = new[] { Main.Preset1, Main.Preset2, Main.Preset3, Main.Preset4, Main.Preset5, Main.Preset6, Main.Preset7 };
        if (preset < 0 || preset >= entries.Length) return $"プリセット{preset + 1}";
        var e = entries[preset];
        return e.Value == (string)e.DefaultValue ? Translator.GetString($"Preset_{preset + 1}") : e.Value;
    }

    /// <summary>タグとして読まれないように &lt; &gt; を全角にする</summary>
    private static string Safe(string s) => (s ?? "").Replace('<', '＜').Replace('>', '＞');

    /// <summary>チャットを処理する。投票のコマンド・投票の数字なら true(hide=true ならチャットに出さない)</summary>
    public static bool TryHandle(string text, PlayerControl player, out bool hide)
    {
        hide = false;
        if (!IsOn || player == null || string.IsNullOrWhiteSpace(text)) return false;
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || !GameStates.IsLobby) return false;
        var tokens = text.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;
        var first = tokens[0].ToLowerInvariant();

        if (first is "/vote" or "/poll")
        {
            hide = true;
            if (!player.AmOwner)
            {
                Utils.SendMessage("投票を始められるのはホストだけです。", player.PlayerId, first == "/vote" ? PresetTitle : PollTitle);
                return true;
            }
            if (tokens.Length >= 2 && tokens[1].ToLowerInvariant() is "stop" or "end" or "やめる")
            {
                if (IsActive) { var t = Title; Clear(); Utils.SendMessage("投票をやめました。", byte.MaxValue, t); }
                return true;
            }
            if (IsActive)
            {
                Utils.SendMessage("今は別の投票をしています。/vote stop でやめてから始めてください。", player.PlayerId, Title);
                return true;
            }
            if (first == "/vote") StartPreset(tokens.Skip(1));
            else StartPoll(tokens.Skip(1).ToList());
            return true;
        }

        if (!IsActive) return false;
        string numText = null;
        if (first is "/v" && tokens.Length >= 2) { numText = tokens[1]; hide = true; }
        else if (tokens.Length == 1) numText = tokens[0];
        if (numText == null) return false;
        numText = numText.Normalize(System.Text.NormalizationForm.FormKC); // 全角の数字も読む
        if (!int.TryParse(numText, out var n) || n < 1 || n > labels.Count) return hide;
        votes[player.PlayerId] = n - 1;
        if (hide) Utils.SendMessage($"{n}: {labels[n - 1]} に投票しました。", player.PlayerId, Title);
        return true;
    }

    private static void StartPreset(IEnumerable<string> args)
    {
        Clear();
        foreach (var a in args)
        {
            if (!int.TryParse(a.Normalize(System.Text.NormalizationForm.FormKC), out var n)) continue;
            if (n < 1 || n > OptionItem.NumPresets || presets.Contains(n - 1)) continue;
            presets.Add(n - 1);
        }
        if (presets.Count == 0) presets.AddRange(Enumerable.Range(0, OptionItem.NumPresets));
        if (presets.Count < 2)
        {
            Clear();
            Utils.SendMessage("選択肢は2つ以上にしてください(例: /vote 1 3 5)。", PlayerControl.LocalPlayer.PlayerId, PresetTitle);
            return;
        }
        labels.AddRange(presets.Select(PresetName));
        Begin("次のプリセットを投票で決めます！");
    }

    private static void StartPoll(List<string> args)
    {
        Clear();
        if (args.Count < 3)
        {
            Utils.SendMessage("使い方: /poll 質問 選択肢1 選択肢2 …(スペースで区切る・選択肢は2〜9個)" + ((char)10) + "例: /poll 次のマップは? スケルド ポーラス エアシップ",
                PlayerControl.LocalPlayer.PlayerId, PollTitle);
            return;
        }
        question = Safe(args[0]);
        labels.AddRange(args.Skip(1).Take(MaxPollChoices).Select(Safe));
        Begin($"「{question}」");
    }

    private static void Begin(string head)
    {
        endAt = Time.time + VoteSeconds;
        warned = false;
        var lf = ((char)10).ToString();
        var list = string.Join(lf, labels.Select((l, i) => $"{i + 1}: {l}"));
        Utils.SendMessage($"{head}{(int)VoteSeconds}秒以内に、番号をチャットで送ってください。{lf}{list}", byte.MaxValue, Title);
    }

    private static void Clear()
    {
        labels.Clear();
        presets.Clear();
        question = "";
        votes.Clear();
    }

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Update)), HarmonyPostfix]
    public static void Tick()
    {
        try
        {
            if (!IsActive) return;
            if (!IsOn || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) { Clear(); return; }
            var remaining = endAt - Time.time;
            if (!warned && remaining <= 10f)
            {
                warned = true;
                Utils.SendMessage($"投票はあと10秒です。(今 {votes.Count}票)", byte.MaxValue, Title);
            }
            if (remaining > 0f) return;
            Finish();
        }
        catch (Exception e)
        {
            Logger.Warn($"ロビー投票の処理に失敗: {e.Message}", "LobbyVote");
            Clear();
        }
    }

    private static void Finish()
    {
        var lf = ((char)10).ToString();
        var title = Title;
        if (votes.Count == 0)
        {
            var wasPreset = IsPresetVote;
            Clear();
            Utils.SendMessage(wasPreset ? "誰も投票しなかったので、プリセットはそのままです。" : "誰も投票しませんでした。", byte.MaxValue, title);
            return;
        }
        var counts = votes.Values.GroupBy(v => v).ToDictionary(g => g.Key, g => g.Count());
        var max = counts.Values.Max();
        var top = counts.Where(kv => kv.Value == max).Select(kv => kv.Key).OrderBy(i => i).ToList();
        var result = string.Join(lf, labels.Select((l, i) => $"{i + 1}: {l} … {(counts.TryGetValue(i, out var c) ? c : 0)}票"));
        if (IsPresetVote)
        {
            var winIndex = top[IRandom.Instance.Next(top.Count)];
            var preset = presets[winIndex];
            Clear();
            AutoPresetByCount.SuppressOnce();
            PresetOptionItem.Preset.SetValue(preset);
            Utils.SendMessage($"{result}{lf}{lf}「{PresetName(preset)}」に決まりました！{(top.Count > 1 ? "(同じ票数だったのでランダムで選びました)" : "")}", byte.MaxValue, title);
            return;
        }
        // 自由な質問は結果を知らせるだけ(同じ票数なら全部を1位として出す)
        var q = question;
        var winners = string.Join("・", top.Select(i => labels[i]));
        Clear();
        Utils.SendMessage($"「{q}」の結果{lf}{result}{lf}{lf}1位: {winners}({max}票)", byte.MaxValue, title);
    }
}

/// <summary>
/// 人数でプリセットを自動で切り替える(便利機能・ホストのみ)。
/// 「〜〇人」「〜△人」「それより多い」の3段階ごとにプリセットを決めておくと、
/// ロビーの人数が変わって、その段階に入った時に切り替える(0 = 変えない)。
/// 人数が変わった時だけ切り替えるので、ホストが手で変えたプリセットは、次に人数が変わるまでそのまま。
/// </summary>
[HarmonyPatch]
public static class AutoPresetByCount
{
    public static OptionItem Enabled;
    public static OptionItem SmallMax;
    public static OptionItem MiddleMax;
    public static OptionItem SmallPreset;
    public static OptionItem MiddlePreset;
    public static OptionItem LargePreset;

    private static int lastCount = -1;
    private static int lastTier = -1;
    private static float changedAt;
    private static bool pending;
    private static bool suppress;
    private static float nextCheck;
    /// <summary>人が続けて出入りしている間は待つ秒数</summary>
    private const float SettleSeconds = 3f;

    /// <summary>投票などで手で切り替えた直後は、今の人数を基準にし直す(すぐ戻さない)</summary>
    public static void SuppressOnce() => suppress = true;

    private static int CountPlayers()
    {
        var count = GameData.Instance?.PlayerCount ?? 0;
        if (Options.EnableGM?.GetBool() == true) count -= 1; // GMは配役されないので数えない
        return Math.Max(0, count);
    }

    private static int Tier(int count)
    {
        var small = SmallMax.GetInt();
        var middle = Math.Max(small, MiddleMax.GetInt());
        return count <= small ? 0 : count <= middle ? 1 : 2;
    }

    [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Update)), HarmonyPostfix]
    public static void Tick()
    {
        try
        {
            if (Time.time < nextCheck) return;
            nextCheck = Time.time + 0.5f;
            if (Enabled?.GetBool() != true || AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || !GameStates.IsLobby)
            {
                lastCount = -1; lastTier = -1; pending = false;
                return;
            }
            var count = CountPlayers();
            if (suppress || lastCount < 0)
            {
                // 最初・手で変えた直後は今の状態を基準にする(部屋を作っただけで切り替わらないように)
                suppress = false;
                lastCount = count;
                lastTier = Tier(count);
                pending = false;
                return;
            }
            if (count != lastCount)
            {
                lastCount = count;
                changedAt = Time.time;
                pending = true;
                return;
            }
            if (!pending || Time.time - changedAt < SettleSeconds || LobbyVote.IsActive) return;
            pending = false;
            var tier = Tier(count);
            if (tier == lastTier) return;
            lastTier = tier;
            var option = tier switch { 0 => SmallPreset, 1 => MiddlePreset, _ => LargePreset };
            var preset = option.GetInt() - 1;
            if (preset < 0 || preset >= OptionItem.NumPresets || preset == OptionItem.CurrentPreset) return;
            PresetOptionItem.Preset.SetValue(preset);
            var name = PresetOptionItem.Preset.GetString().Split('\n')[0].RemoveHtmlTags();
            Utils.SendMessage($"{count}人になったので、プリセットを「{name}」に切り替えました。", byte.MaxValue, "<#7fd8ff>【プリセット自動切り替え】</color>");
            Logger.Info($"{count}人 → プリセット{preset + 1}", "AutoPresetByCount");
        }
        catch (Exception e)
        {
            Logger.Warn($"プリセットの自動切り替えに失敗: {e.Message}", "AutoPresetByCount");
        }
    }
}

/// <summary>ホストが自分で /vote や投票の数字を打った時(ホストのチャットは OnReceiveChat を通らないため)</summary>
[HarmonyPatch(typeof(ChatController), nameof(ChatController.SendChat))]
[HarmonyPriority(Priority.First)]
class LobbyVoteChatPatch
{
    public static bool Prefix(ChatController __instance)
    {
        var textArea = __instance?.freeChatField?.textArea;
        if (textArea == null || PlayerControl.LocalPlayer == null) return true;
        if (!LobbyVote.TryHandle(textArea.text, PlayerControl.LocalPlayer, out var hide)) return true;
        if (!hide) return true; // 数字の投票は普通にチャットに出す
        textArea.Clear();
        return false;
    }
}
