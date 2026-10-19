using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using HarmonyLib;
using TownOfHost.Roles.Core;

namespace TownOfHost.Modules;

public static class OptionSchemaExporter
{
    static bool done;
    public static string FilePath => $"{Main.BaseDirectory}/OptionOutputs/OptionSchema_{Main.PluginVersion}.json";

    [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start)), HarmonyPostfix]
    public static void MainMenuStartPostfix()
    {
        if (done) return;
        done = true;
        _ = new LateTask(() =>
        {
            try { Export(force: false); }
            catch (Exception e) { Logger.Warn($"OptionSchema の書き出しに失敗: {e.Message}", "OptionSchema"); }
        }, 2f, "OptionSchemaExport", true);
    }

    public static string Export(bool force)
    {
        var path = FilePath;
        if (!force && File.Exists(path)) return path;
        Directory.CreateDirectory(Path.GetDirectoryName(path));

        var options = new List<Dictionary<string, object>>();
        foreach (var o in OptionItem.AllOptions.Where(o => o is not PresetOptionItem).OrderBy(o => o.Id))
        {
            var d = new Dictionary<string, object>
            {
                ["id"] = o.Id,
                ["name"] = SafeName(o),
                ["tab"] = o.Tab.ToString(),
                ["parent"] = o.Parent?.Id ?? -1,
                ["default"] = o.DefaultValue,
                ["hidden"] = o.IsHidden,
            };
            if (o.CustomRole is not CustomRoles.NotAssigned) d["role"] = o.CustomRole.ToString();
            var (type, values) = Describe(o);
            d["type"] = type;
            if (values != null) d["values"] = values;
            options.Add(d);
        }

        string vanilla = "";
        try { vanilla = OptionSerializer.GenerateVanillaOptionsString(); } catch { }

        var root = new Dictionary<string, object>
        {
            ["format"] = 1,
            ["modVersion"] = Main.PluginShowVersion,
            ["optionVersion"] = OptionSaver.Version,
            ["header"] = "%TOH-hamoOptions%",
            ["footer"] = "%End%",
            ["vanilla"] = vanilla,
            ["options"] = options,
        };
        File.WriteAllText(path, JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = false }));
        Logger.Info($"OptionSchema を書き出しました: {path} ({options.Count}個)", "OptionSchema");
        return path;
    }

    static string SafeName(OptionItem o)
    {
        try { return o.GetName(true).RemoveHtmlTags(); }
        catch { return o.Name; }
    }

    static (string type, List<string> values) Describe(OptionItem o)
    {
        try
        {
            switch (o)
            {
                case BooleanOptionItem:
                    return ("bool", new() { Translator.GetString("ColoredOff").RemoveHtmlTags(), Translator.GetString("ColoredOn").RemoveHtmlTags() });
                case StringOptionItem s:
                    return ("select", Enumerable.Range(0, s.Selections.Length).Select(i => s.GetString(i).RemoveHtmlTags()).ToList());
                case IntegerOptionItem n:
                {
                    var r = n.Rule;
                    var count = r.Step <= 0 ? 1 : (r.MaxValue - r.MinValue) / r.Step + 1;
                    return ("int", Enumerable.Range(0, Math.Clamp(count, 1, 5000)).Select(i => n.ApplyFormat(r.GetValueByIndex(i).ToString()).RemoveHtmlTags()).ToList());
                }
                case FloatOptionItem f:
                {
                    var r = f.Rule;
                    var count = r.Step <= 0 ? 1 : (int)Math.Round((r.MaxValue - r.MinValue) / r.Step) + 1;
                    return ("float", Enumerable.Range(0, Math.Clamp(count, 1, 5000)).Select(i => f.ApplyFormat(r.GetValueByIndex(i).ToString()).RemoveHtmlTags()).ToList());
                }
            }
        }
        catch { }
        // 役職の割り当て設定など、特殊な形式は数値のまま扱う
        return (o.GetType().Name, null);
    }
}
