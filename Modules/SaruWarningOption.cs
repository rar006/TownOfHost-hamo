using System.Collections.Generic;

using TownOfHost.Roles.Core;

namespace TownOfHost;

// 「猿(根拠の薄いキル/勘に頼ったキル)禁止」の警告文を、対象役職の開始時説明に追加するための共通ヘルパー。
// 各役職(Sheriff, Guesser系, Seer系, Dictatorなど)のSetupOptionItemから
// SaruWarningOption.AddTo(RoleInfo) を呼ぶことで、その役職専用のON/OFFオプションが追加される。
//
// 【重要】以前は roleInfo.ConfigId + 手動オフセット という方式だったが、
// Sheriffのようにゲーム内で動的にオプションIDを消費し続ける役職(ニュートラル役職の数だけ
// IDを消費するループ)があり、役職自身の100番ブロックからはみ出して別の役職と衝突する
// 事故が続いたため、完全に独立したグローバルな絶対ID帯(900000番台)を自前で管理する方式に変更した。
public static class SaruWarningOption
{
    // 900000番台を専有(他のどの役職・属性の採番方式とも重ならない完全に独立した帯)
    private const int BaseId = 900000;
    private static int _nextId = BaseId;

    private static readonly Dictionary<CustomRoles, OptionItem> PerRoleOption = new();

    public static OptionItem AddTo(SimpleRoleInfo roleInfo)
    {
        var id = _nextId++;
        var opt = BooleanOptionItem.Create(id, "WarnNoSaru", false, roleInfo.Tab, false);
        opt.SetParent(roleInfo.RoleOption);
        opt.SetParentRole(roleInfo.RoleName);
        PerRoleOption[roleInfo.RoleName] = opt;
        return opt;
    }

    public static bool ShouldWarn(CustomRoles role)
        => PerRoleOption.TryGetValue(role, out var opt) && opt.GetBool();
}
