using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using TownOfHost.Roles.Core;

namespace TownOfHost.Modules;

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
public static class SettingChangeNotice
{
    public static OptionItem Enabled;

    private const float QuietSeconds = 5f;
    private const int MaxLines = 8;

    private class Change
    {
        public OptionItem Option;
        public string Before;
        public string After;
    }
    private static readonly Dictionary<int, Change> pending = new();
    private static float lastChangeAt = -1f;

    public static void OnChanged(OptionItem option, string before, string after)
    {
        try
        {
            if (Enabled?.GetBool() != true || option == null || option == Enabled) return;
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost || !GameStates.IsLobby) return;
            if (option is ObjectOptionitem || option is PresetOptionItem) return;
            if (!pending.TryGetValue(option.Id, out var c))
                pending[option.Id] = c = new Change { Option = option, Before = before };
            c.After = after;
            lastChangeAt = Time.time;
        }
        catch { }
    }

    public static void Postfix()
    {
        if (pending.Count == 0 || lastChangeAt < 0f || Time.time - lastChangeAt < QuietSeconds) return;
        try
        {
            if (Enabled?.GetBool() == true && GameStates.IsLobby && AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost)
            {
                var changes = pending.Values.Where(c => c.Before != c.After).ToList(); // 元に戻したものは出さない
                if (changes.Count > 0)
                {
                    var lf = ((char)10).ToString();
                    var sb = new StringBuilder();
                    foreach (var c in changes.Take(MaxLines))
                    {
                        if (sb.Length > 0) sb.Append(lf);
                        sb.Append("・").Append(DisplayName(c.Option)).Append(": ")
                          .Append(Clean(c.Before)).Append(" → ").Append(Clean(c.After));
                    }
                    if (changes.Count > MaxLines) sb.Append(lf).Append($"ほか{changes.Count - MaxLines}件");
                    Utils.SendMessage(sb.ToString(), byte.MaxValue, "<#7fd8ff>【設定が変わりました】</color>");
                }
            }
        }
        catch (System.Exception e)
        {
            Logger.Warn($"設定変更のお知らせに失敗: {e.Message}", "SettingChangeNotice");
        }
        pending.Clear();
        lastChangeAt = -1f;
    }

    private static string Clean(string s) => (s ?? "").RemoveHtmlTags().Trim();
    // 役職の設定は「アブダクターの人数」のように役職名を付ける(「人数」だけだとどの役職か分からなかった)
    private static string DisplayName(OptionItem option)
    {
        var name = Clean(option.GetName(disableColor: true));
        for (var o = option; o != null; o = o.Parent)
        {
            if (o.CustomRole is not CustomRoles.NotAssigned && o == option) return name; // 役職そのものの出現率
            var role = o.CustomRole is not CustomRoles.NotAssigned ? o.CustomRole : o.ParentRole;
            if (role is CustomRoles.NotAssigned) continue;
            var roleName = Clean(UtilsRoleText.GetRoleName(role));
            return string.IsNullOrEmpty(roleName) || name.Contains(roleName) ? name : $"{roleName}の{name}";
        }
        return name;
    }
}
