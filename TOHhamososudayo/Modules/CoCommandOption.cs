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

// 「/cmd co」「/cmd aco」で入力された名前(日本語表記/英語表記どちらも可)からCustomRolesを検索し、
// CO(役職宣言)の記録・一覧表示を行う。通常役職用と属性用でそれぞれ別に記録する。
public static class CoLog
{
    // 通常役職: 1人につき最新のCOだけを記録する(2回目以降のCOは上書き)
    private static readonly Dictionary<byte, (string PlayerName, CustomRoles Role)> RoleEntries = new();
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

    // "/cmd co <役職名>" (通常役職のCO。最新の1つだけ記録)
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

        var coRole = FindByName(roleNameInput, CoCommandOption.IsNormalRoleAllowed);
        Logger.Info($"/cmd co 入力=\"{roleNameInput}\" 解決結果={(coRole == null ? "null(該当なし)" : coRole.ToString())}", "CoCommand");
        if (coRole == null) return; // 該当する役職名が無ければ完全無反応

        RoleEntries[player.PlayerId] = (player.Data.PlayerName, coRole.Value);
        Utils.SendMessage(Translator.GetString("CoAnnounce")
            .Replace("%PLAYER%", player.Data.PlayerName)
            .Replace("%ROLE%", Utils.ColorString(UtilsRoleText.GetRoleColor(coRole.Value), UtilsRoleText.GetRoleName(coRole.Value))));
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
            sb.Append($"\n{e.PlayerName} : {Utils.ColorString(UtilsRoleText.GetRoleColor(e.Role), UtilsRoleText.GetRoleName(e.Role))}");
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
