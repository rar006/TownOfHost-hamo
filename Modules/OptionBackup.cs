using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;

namespace TownOfHost.Modules;

/// <summary>
/// 設定の自動バックアップ(便利機能・ホストのみ)。
/// 設定を変え始める直前の状態(今のプリセットの全設定)を保存しておき、OTHER2の「1つ前の設定に戻す」で戻せる。
/// 続けて変えている間(最後に変えてから10秒以内)は1つにまとめる。最大3つまで、新しいものから戻す。
/// 保存先: TOHhm_DATA/OptionBackups.json
/// </summary>
public static class OptionBackup
{
    public static OptionItem Enabled;
    private const int MaxBackups = 3;
    private const float GroupSeconds = 10f;

    private class Backup
    {
        public long At { get; set; }
        public int Preset { get; set; }
        public Dictionary<int, int> Values { get; set; } = new();
    }
    private static List<Backup> backups;
    private static float lastChangeAt = -999f;
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

    private static void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(Backups)); }
        catch (Exception e) { Logger.Warn($"設定のバックアップを保存できませんでした: {e.Message}", "OptionBackup"); }
    }

    /// <summary>
    /// 設定が変わる直前に呼ぶ(OptionItem.SetValue・まとめて変えるタイルから)。
    /// 前回の変更から10秒以上たっていたら、今の状態を1つ保存する。
    /// </summary>
    public static void BeforeChange()
    {
        try
        {
            if (restoring || Enabled?.GetBool() != true) return;
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            var now = Time.realtimeSinceStartup;
            var startNew = now - lastChangeAt >= GroupSeconds;
            lastChangeAt = now;
            if (!startNew) return;
            var snap = new Backup { At = DateTimeOffset.Now.ToUnixTimeMilliseconds(), Preset = OptionItem.CurrentPreset };
            foreach (var opt in OptionItem.AllOptions)
            {
                if (opt == null || opt is PresetOptionItem || opt is ObjectOptionitem) continue;
                snap.Values[opt.Id] = opt.CurrentValue;
            }
            Backups.Insert(0, snap);
            if (Backups.Count > MaxBackups) Backups.RemoveRange(MaxBackups, Backups.Count - MaxBackups);
            Save();
        }
        catch (Exception e) { Logger.Warn($"設定のバックアップに失敗: {e.Message}", "OptionBackup"); }
    }

    /// <summary>一番新しいバックアップに戻す(戻したものは消す)。戻せたら true</summary>
    public static bool RestoreLatest()
    {
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return false;
        if (Backups.Count == 0) { Utils.SendMessage("戻せる設定のバックアップがありません。", PlayerControl.LocalPlayer.PlayerId); return false; }
        var b = Backups[0];
        if (b.Preset != OptionItem.CurrentPreset)
        {
            Utils.SendMessage($"このバックアップはプリセット{b.Preset + 1}のものです。プリセット{b.Preset + 1}に切り替えてから戻してください。", PlayerControl.LocalPlayer.PlayerId);
            return false;
        }
        restoring = true;
        var changed = 0;
        try
        {
            foreach (var opt in OptionItem.AllOptions)
            {
                if (opt == null || !b.Values.TryGetValue(opt.Id, out var v) || opt.CurrentValue == v) continue;
                opt.SetValue(v, false, false);
                changed++;
            }
        }
        finally { restoring = false; }
        Backups.RemoveAt(0);
        Save();
        if (changed > 0)
        {
            OptionSaver.Save();
            OptionItem.SyncAllOptions();
        }
        var time = DateTimeOffset.FromUnixTimeMilliseconds(b.At).ToLocalTime().ToString("HH:mm");
        Utils.SendMessage($"{time} の設定に戻しました({changed}件)。残りのバックアップ: {Backups.Count}", PlayerControl.LocalPlayer.PlayerId, "<#7fd8ff>【設定のバックアップ】</color>");
        return true;
    }
}
