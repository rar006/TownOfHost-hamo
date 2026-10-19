using System;
using TownOfHost.Roles.Core;
using TownOfHost.Modules;

namespace TownOfHost;

public static class AllRolesChance
{
    public static int Percent { get; private set; } = 0;

    public static void Next() => Percent = Percent >= 100 ? 0 : Percent + 10;

    public static void Randomize()
    {
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
        OptionBackup.BeforeChange(); // まとめて変える前の状態を残す(自動バックアップがONの時)
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
        OptionBackup.BeforeChange(); // まとめて変える前の状態を残す(自動バックアップがONの時)
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
