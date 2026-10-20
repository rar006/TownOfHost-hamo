using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HarmonyLib;
using UnityEngine;

namespace TownOfHost.Modules;

// 設定のバックアップ(ホストのみ)。
// 設定を変える前に毎回、その時のプリセットの全設定を残しておき、一覧から選んで戻せる。
// ・同じ設定を続けて押している間(最後に押してから3秒以内)は1つにまとめる(＋を10回押しても1件)
// ・プリセットを切り替える前にも残すので、間違えて切り替えた時も戻せる
// ・戻す前にも今の状態を残すので、戻したのを取り消すこともできる
// ・最大30件(古いものから消える)。保存先: TOHhm_DATA/OptionBackups.json
// 使い方: ゲーム設定の上の「バックアップ」(またはOTHER2の「バックアップの一覧を見る」)で一覧を開き、選んで戻す
public static class OptionBackup
{
    public static OptionItem Enabled;
    private const int MaxBackups = 30;
    private const float GroupSeconds = 3f;
    private const string Title = "<#7fd8ff>【設定のバックアップ】</color>";

    private class Backup
    {
        public long At { get; set; }
        public int Preset { get; set; }
        public string Label { get; set; } = "";
        public Dictionary<int, int> Values { get; set; } = new();
    }
    private static List<Backup> backups;
    private static float lastChangeAt = -999f;
    private static int lastChangedId = int.MinValue;
    private static bool restoring;

    private static string FilePath => Path.Combine(Main.BaseDirectory, "OptionBackups.json");

    private static List<Backup> Backups
    {
        get
        {
            if (backups != null) return backups;
            try { backups = File.Exists(FilePath) ? JsonSerializer.Deserialize<List<Backup>>(File.ReadAllText(FilePath)) ?? new() : new(); }
            catch { backups = new(); }
            return backups;
        }
    }

    public static int Count => Backups.Count;
    private static bool IsOn => Enabled?.GetBool() == true;
    private static bool IsHost => AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost;

    private static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Backups)); }
        catch (Exception e) { Logger.Warn($"設定のバックアップを保存できませんでした: {e.Message}", "OptionBackup"); }
    }

    private static string PresetLabel(int preset)
    {
        var entries = new[]
        {
            Main.Preset1, Main.Preset2, Main.Preset3, Main.Preset4, Main.Preset5, Main.Preset6, Main.Preset7, Main.Preset8,
            Main.Preset9, Main.Preset10, Main.Preset11, Main.Preset12, Main.Preset13, Main.Preset14, Main.Preset15, Main.Preset16,
        };
        if (preset < 0 || preset >= entries.Length) return $"プリセット{preset + 1}";
        var e = entries[preset];
        return e.Value == (string)e.DefaultValue ? Translator.GetString($"Preset_{preset + 1}") : e.Value;
    }

    public static void BeforeChange(OptionItem option = null, string label = null)
    {
        try
        {
            if (restoring || !IsOn || !IsHost) return;
            var now = Time.realtimeSinceStartup;
            var id = option?.Id ?? int.MinValue + 1;
            var sameAsLast = id == lastChangedId && now - lastChangeAt < GroupSeconds;
            lastChangeAt = now;
            lastChangedId = id;
            if (sameAsLast) return;
            string what;
            if (label != null) what = label;
            else if (option is PresetOptionItem) what = $"プリセット切り替え前(「{PresetLabel(OptionItem.CurrentPreset)}」)";
            else if (option != null)
            {
                string name;
                try { name = option.GetName(true).RemoveHtmlTags().Trim(); } catch { name = ""; }
                if (string.IsNullOrEmpty(name)) try { name = Translator.GetString(option.Name).RemoveHtmlTags().Trim(); } catch { }
                if (string.IsNullOrEmpty(name)) name = option.Name;
                what = $"「{name}」の変更前";
            }
            else what = "設定の変更前";
            Take(what);
        }
        catch (Exception e) { Logger.Warn($"設定のバックアップに失敗: {e.Message}", "OptionBackup"); }
    }

    private static void Take(string label)
    {
        var snap = new Backup { At = DateTimeOffset.Now.ToUnixTimeMilliseconds(), Preset = OptionItem.CurrentPreset, Label = label };
        foreach (var opt in OptionItem.AllOptions)
        {
            if (opt == null || opt is PresetOptionItem || opt is ObjectOptionitem) continue;
            snap.Values[opt.Id] = opt.CurrentValue;
        }
        Backups.Insert(0, snap);
        if (Backups.Count > MaxBackups) Backups.RemoveRange(MaxBackups, Backups.Count - MaxBackups);
        Save();
    }

    public static bool IsEnabled => IsOn;

    // 一覧の1行分の文字(index は 0 = 一番新しい)
    public static string EntryText(int index)
    {
        if (index < 0 || index >= Backups.Count) return "";
        var b = Backups[index];
        var time = DateTimeOffset.FromUnixTimeMilliseconds(b.At).ToLocalTime().ToString("HH:mm:ss");
        return $"<color=#9a9aaa>{time}</color>  <color=#ffd36e>{PresetLabel(b.Preset)}</color>  {b.Label}";
    }

    public static bool RestoreLatest() => Restore(1);

    public static bool Restore(int number)
    {
        if (!IsHost) return false;
        var me = PlayerControl.LocalPlayer?.PlayerId ?? byte.MaxValue;
        if (Backups.Count == 0) { Utils.SendMessage("復元できるバックアップがありません。", me, Title); return false; }
        if (number < 1 || number > Backups.Count) { return false; }
        var b = Backups[number - 1];
        // 今の状態を残してから戻す(戻したのを取り消せるように)
        var time = DateTimeOffset.FromUnixTimeMilliseconds(b.At).ToLocalTime().ToString("HH:mm:ss");
        Take($"復元前({time}の状態に戻す前)");
        restoring = true;
        var changed = 0;
        try
        {
            if (b.Preset != OptionItem.CurrentPreset && PresetOptionItem.Preset != null)
                PresetOptionItem.Preset.SetValue(b.Preset, true, false);
            foreach (var opt in OptionItem.AllOptions)
            {
                if (opt == null || opt is PresetOptionItem || !b.Values.TryGetValue(opt.Id, out var v) || opt.CurrentValue == v) continue;
                opt.SetValue(v, false, false);
                changed++;
            }
        }
        finally { restoring = false; }
        OptionSaver.Save();
        OptionItem.SyncAllOptions();
        Utils.SendMessage($"{time}の状態に戻しました({PresetLabel(b.Preset)}・{changed}項目)。{((char)10)}戻す前の設定は一覧の1番に保存してあるので、そこからやり直せます。", me, Title);
        return true;
    }
}
