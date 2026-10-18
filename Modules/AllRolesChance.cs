using System;
using TownOfHost.Roles.Core;
using TownOfHost.Modules;

namespace TownOfHost;

/// <summary>
/// 設定画面の上の列のタイル「全役職 〇%」「まとめて適用」で、すべての役職の出現率を一度に変える。
/// (ホストのみ。全役職を書き換えてから、保存と同期は最後に1回だけ行う)
/// </summary>
public static class AllRolesChance
{
    /// <summary>次に適用する出現率(%)</summary>
    public static int Percent { get; private set; } = 0;

    /// <summary>0 → 10 → … → 100 → 0 と切り替える</summary>
    public static void Next() => Percent = Percent >= 100 ? 0 : Percent + 10;

    /// <summary>すべての役職の出現率をランダムにする(0〜100%・10%きざみ)。「おまかせ」タイル</summary>
    public static void Randomize()
    {
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
        if (Options.CustomRoleSpawnChances == null) return;
        var rand = IRandom.Instance;
        var changed = 0;
        foreach (var (role, option) in Options.CustomRoleSpawnChances)
        {
            try
            {
                if (option == null || role is CustomRoles.NotAssigned) continue;
                var index = option.Rule.GetNearestIndex(rand.Next(0, 11) * 10);
                if (option.CurrentValue == index) continue;
                option.SetValue(index, false, false);
                changed++;
            }
            catch (Exception e)
            {
                Logger.Warn($"{role}の出現率を変更できませんでした: {e.Message}", "AllRolesChance");
            }
        }
        if (changed > 0)
        {
            OptionSaver.Save();
            OptionItem.SyncAllOptions();
        }
        Logger.Info($"すべての役職の出現率をランダムにしました({changed}件変更)", "AllRolesChance");
    }

    public static void Apply()
    {
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
        if (Options.CustomRoleSpawnChances == null) return;
        var changed = 0;
        foreach (var (role, option) in Options.CustomRoleSpawnChances)
        {
            try
            {
                if (option == null || role is CustomRoles.NotAssigned) continue;
                var index = option.Rule.GetNearestIndex(Percent);
                if (option.CurrentValue == index) continue;
                option.SetValue(index, false, false); // 保存と同期は最後にまとめて
                changed++;
            }
            catch (Exception e)
            {
                Logger.Warn($"{role}の出現率を変更できませんでした: {e.Message}", "AllRolesChance");
            }
        }
        if (changed > 0)
        {
            OptionSaver.Save();
            OptionItem.SyncAllOptions();
        }
        Logger.Info($"すべての役職の出現率を{Percent}%にしました({changed}件変更)", "AllRolesChance");
    }
}
