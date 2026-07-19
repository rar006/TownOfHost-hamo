using System;
using System.Collections.Generic;
using System.Linq;

using TownOfHost.Roles.Core;

namespace TownOfHost;

// 「/cmd co <役職名>」「/cmd aco <属性名>」の可否判定。
// 以前は専用の「許可する」トグルがあったが、/co・/aco自体の禁止トグルと役割が
// 重複していた(禁止トグルがOFF=使える、なので「許可する」を別に持つ必要が無い)ため廃止し、
// コマンド自体の禁止トグルだけで制御するようにした。
public static class CoCommandOption
{
    // 通常役職(メイン役職)のCOが許可されているか
    public static bool IsNormalRoleAllowed(CustomRoles role) => !role.IsAddOn();

    // 属性(アドオン)のCOが許可されているか
    public static bool IsAddonAllowed(CustomRoles role) => role.IsAddOn();
}

// 「/cmd co ベント系」のように、具体的な役職名ではなく系統(カテゴリ)名だけを
// 宣言できるようにするための一覧。該当役職は個別に列挙して管理する。
// (自動判定にすると、能力の副次効果でOnEnterVentを持つだけの役職まで
//  「ベント系」に混ざってしまい、実際のプレイ感覚と一致しないため)
public static class CoCategoryOption
{
    public const string VentCategoryName = "ベント系";
    public const string SeerCategoryName = "占い系";

    private static readonly HashSet<CustomRoles> VentRoles = new()
    {
        CustomRoles.Sleeper,
        CustomRoles.VentMaster,
        CustomRoles.Missioneer,
        CustomRoles.VentOpener,
        CustomRoles.VentHunter,
        CustomRoles.NiceTeleporter,
        CustomRoles.NiceTrapper,
        CustomRoles.Android,
    };

    private static readonly HashSet<CustomRoles> SeerRoles = new()
    {
        CustomRoles.Seer,
        CustomRoles.FortuneTeller,
        CustomRoles.PonkotuTeller,
        CustomRoles.SuspiciousTeller,
        CustomRoles.AmateurTeller,
        CustomRoles.Fortuner,
        CustomRoles.UnFortuner,
        CustomRoles.MadTeller,
        CustomRoles.JackalSeer,
        CustomRoles.EvilTeller,
    };

    // 入力文字列がカテゴリ名(部分一致)なら、そのカテゴリ名自体を返す。該当なければnull。
    public static string FindCategoryName(string input)
    {
        input = input.Trim();
        if (input == "") return null;

        if (VentCategoryName.Contains(input) || input.Contains(VentCategoryName)) return VentCategoryName;
        if (SeerCategoryName.Contains(input) || input.Contains(SeerCategoryName)) return SeerCategoryName;
        return null;
    }

    public static bool IsInCategory(CustomRoles role, string categoryName) => categoryName switch
    {
        VentCategoryName => VentRoles.Contains(role),
        SeerCategoryName => SeerRoles.Contains(role),
        _ => false,
    };
}

// 「/cmd co」「/cmd aco」で入力された名前(日本語表記/英語表記どちらも可)からCustomRolesを検索し、
// CO(役職宣言)の記録・一覧表示を行う。通常役職用と属性用でそれぞれ別に記録する。
public static class CoLog
{
    // 通常役職: 1人につき最新のCOだけを記録する(2回目以降のCOは上書き)
    // 役職名でCOした場合は通常通りの表示、カテゴリ名(ベント系/占い系)でCOした場合は
    // カテゴリ名だけを表示する(具体的な役職は伏せる)。
    private static readonly Dictionary<byte, (string PlayerName, string DisplayText, UnityEngine.Color DisplayColor)> RoleEntries = new();
    // 属性: 1人が複数の属性を同時に持てるため、1人につき複数のCOを記録できるようにする
    private static readonly Dictionary<byte, (string PlayerName, List<CustomRoles> Roles)> AddonEntries = new();

    private static CustomRoles? FindByName(string input, Func<CustomRoles, bool> isAllowed)
    {
        input = input.Trim();
        if (input == "") return null;

        foreach (CustomRoles role in Enum.GetValues(typeof(CustomRoles)))
        {
            if (!isAllowed(role)) continue;

            if (string.Equals(role.ToString(), input, StringComparison.OrdinalIgnoreCase)) return role;

            var jpName = Translator.GetString(role.ToString());
            if (!string.IsNullOrEmpty(jpName) && string.Equals(jpName.RemoveHtmlTags(), input, StringComparison.OrdinalIgnoreCase)) return role;
        }
        return null;
    }

    public static void Reset()
    {
        RoleEntries.Clear();
        AddonEntries.Clear();
    }

    // "/cmd co <役職名 または ベント系/占い系>" (通常役職のCO。最新の1つだけ記録)
    public static void HandleCoCommand(PlayerControl player, string roleNameInput)
    {
        if (Options.OptionCommandCo.GetBool())
        {
            Utils.SendMessage("<color=#ff0000>現在このコマンドはホストによって無効化されています。</color>", player.PlayerId);
            return;
        }

        if (player?.Data == null || player.Data.IsDead)
        {
            Utils.SendMessage("<color=#ff0000>死亡しているとCOできません。</color>", player.PlayerId);
            return;
        }

        // まず具体的な役職名として解決を試み、ダメならカテゴリ名(ベント系/占い系)として解決する。
        var coRole = FindByName(roleNameInput, CoCommandOption.IsNormalRoleAllowed);
        if (coRole != null)
        {
            Logger.Info($"/cmd co 入力=\"{roleNameInput}\" 解決結果={coRole}", "CoCommand");
            RoleEntries[player.PlayerId] = (player.Data.PlayerName, UtilsRoleText.GetRoleName(coRole.Value), UtilsRoleText.GetRoleColor(coRole.Value));
            Utils.SendMessage(Translator.GetString("CoAnnounce")
                .Replace("%PLAYER%", player.Data.PlayerName)
                .Replace("%ROLE%", Utils.ColorString(UtilsRoleText.GetRoleColor(coRole.Value), UtilsRoleText.GetRoleName(coRole.Value))));
            return;
        }

        var categoryName = CoCategoryOption.FindCategoryName(roleNameInput);
        Logger.Info($"/cmd co 入力=\"{roleNameInput}\" 解決結果={(categoryName == null ? "null(該当なし)" : $"カテゴリ:{categoryName}")}", "CoCommand");
        if (categoryName == null) return; // 該当する役職名・カテゴリ名が無ければ完全無反応

        var categoryColor = UnityEngine.Color.white;
        RoleEntries[player.PlayerId] = (player.Data.PlayerName, categoryName, categoryColor);
        Utils.SendMessage(Translator.GetString("CoAnnounce")
            .Replace("%PLAYER%", player.Data.PlayerName)
            .Replace("%ROLE%", Utils.ColorString(categoryColor, categoryName)));
    }

    // "/cmd colist" "/cmd cl" (通常役職のCO一覧)
    public static void HandleColistCommand(PlayerControl player)
    {
        if (Options.OptionCommandColist.GetBool())
        {
            Utils.SendMessage("<color=#ff0000>現在このコマンドはホストによって無効化されています。</color>", player.PlayerId);
            return;
        }

        if (RoleEntries.Count == 0)
        {
            Utils.SendMessage(Translator.GetString("CoListEmpty"), player.PlayerId);
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append(Translator.GetString("CoListHeader"));
        foreach (var e in RoleEntries.Values)
        {
            sb.Append($"\n{e.PlayerName} : {Utils.ColorString(e.DisplayColor, e.DisplayText)}");
        }
        Utils.SendMessage(sb.ToString(), player.PlayerId);
    }

    // "/cmd aco <属性名>" (属性のCO。1人が複数の属性をCOできる。同じ属性を再度打っても増えない)
    public static void HandleAcoCommand(PlayerControl player, string addonNameInput)
    {
        if (Options.OptionCommandAco.GetBool())
        {
            Utils.SendMessage("<color=#ff0000>現在このコマンドはホストによって無効化されています。</color>", player.PlayerId);
            return;
        }

        if (player?.Data == null || player.Data.IsDead)
        {
            Utils.SendMessage("<color=#ff0000>死亡しているとCOできません。</color>", player.PlayerId);
            return;
        }

        var coRole = FindByName(addonNameInput, CoCommandOption.IsAddonAllowed);
        Logger.Info($"/cmd aco 入力=\"{addonNameInput}\" 解決結果={(coRole == null ? "null(該当なし)" : coRole.ToString())}", "CoCommand");
        if (coRole == null) return; // 該当する属性名が無ければ完全無反応

        if (!AddonEntries.TryGetValue(player.PlayerId, out var entry))
        {
            entry = (player.Data.PlayerName, new List<CustomRoles>());
            AddonEntries[player.PlayerId] = entry;
        }
        if (!entry.Roles.Contains(coRole.Value)) entry.Roles.Add(coRole.Value);

        Utils.SendMessage(Translator.GetString("CoAnnounce")
            .Replace("%PLAYER%", player.Data.PlayerName)
            .Replace("%ROLE%", Utils.ColorString(UtilsRoleText.GetRoleColor(coRole.Value), UtilsRoleText.GetRoleName(coRole.Value))));
    }

    // "/cmd acl" (属性のCO一覧。1人につき複数の属性が並ぶ)
    public static void HandleAclCommand(PlayerControl player)
    {
        if (Options.OptionCommandAcl.GetBool())
        {
            Utils.SendMessage("<color=#ff0000>現在このコマンドはホストによって無効化されています。</color>", player.PlayerId);
            return;
        }

        if (AddonEntries.Count == 0)
        {
            Utils.SendMessage(Translator.GetString("CoListEmpty"), player.PlayerId);
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.Append(Translator.GetString("CoListHeader"));
        foreach (var e in AddonEntries.Values)
        {
            var rolesText = string.Join(", ", e.Roles.Select(r => Utils.ColorString(UtilsRoleText.GetRoleColor(r), UtilsRoleText.GetRoleName(r))));
            sb.Append($"\n{e.PlayerName} : {rolesText}");
        }
        Utils.SendMessage(sb.ToString(), player.PlayerId);
    }
}
