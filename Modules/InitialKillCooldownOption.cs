using System.Collections.Generic;
using System.Linq;
using TownOfHost.Roles.Core;
using TownOfHost.Roles.Core.Interfaces;

namespace TownOfHost;

public static class InitialKillCooldownOption
{
    private const int IdBase = 8_000_000;
    private static readonly Dictionary<CustomRoles, OptionItem> Options = new();
    private static readonly HashSet<int> UsedIds = new();

    public static void Create(SimpleRoleInfo roleInfo)
    {
        // ここで失敗しても、ゲーム設定の読み込み全体は止めない
        try
        {
            if (roleInfo?.ClassType == null || !typeof(IKiller).IsAssignableFrom(roleInfo.ClassType)) return;
            if (Options.ContainsKey(roleInfo.RoleName)) return;
            // 役職の設定欄(出現率の項目)が無い役職(バニラのインポスター等)には付けない。
            // (以前はここでエラーになり、ゲーム設定の読み込みが止まって部屋を立てられなくなっていた)
            if (TownOfHost.Options.CustomRoleSpawnChances == null || !TownOfHost.Options.CustomRoleSpawnChances.TryGetValue(roleInfo.RoleName, out var parent)) return;
            var id = IdBase + roleInfo.ConfigId;
            // 同じConfigIdを使っている役職がある(例: 同じ番号の役職が2つ)ので、IDが重なる時は付けない
            if (!UsedIds.Add(id) || OptionItem.AllOptions.Any(o => o.Id == id))
            {
                Logger.Warn($"{roleInfo.RoleName}の初期キルクールはIDが重なるため作りません(id={id})", "InitialKillCooldown");
                return;
            }
            Options[roleInfo.RoleName] = FloatOptionItem.Create(id, "InitialKillCooldown", new(0f, 180f, 0.5f), 0f, roleInfo.Tab, false)
                .SetParent(parent)
                .SetParentRole(roleInfo.RoleName)
                .SetZeroNotation(OptionZeroNotation.Off)
                .SetValueFormat(OptionFormat.Seconds)
                .SetOptionName(() => "初期キルクール");
        }
        catch (System.Exception e)
        {
            Logger.Warn($"{roleInfo?.RoleName}の初期キルクールを作れませんでした: {e.Message}", "InitialKillCooldown");
        }
    }

    public static bool TryGet(PlayerControl pc, out float seconds)
    {
        seconds = 0f;
        if (pc == null) return false;
        if (!Options.TryGetValue(pc.GetCustomRole(), out var option)) return false;
        seconds = option.GetFloat();
        return seconds > 0f;
    }

    public static float Resolve(PlayerControl pc, float fallback) => TryGet(pc, out var s) ? s : fallback;
}
