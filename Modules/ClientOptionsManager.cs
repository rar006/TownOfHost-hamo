using System;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;

using TownOfHost.Attributes;

namespace TownOfHost
{
    public static class ClientOptionsManager
    {
        private static readonly string OPTIONS_FILE_PATH = Main.BaseDirectory + "/options.txt";
        private static readonly string DEFAULT = "WebHookUrl:none\nLog Data:true\nPreset Data:true\nYomiagePort:50080\n\n// Don't Change The Value. / この値を変更しないでください。\nverison:1";
        private static readonly int Version = 1;
        public static string WebhookUrl = "none";
        public static bool LogData = true;
        public static bool PresetData = true;
        public static string YomiagePort = "50080";
        // 試合結果のスレッドに付ける、勝った陣営のフォーラムのタグの ID(空なら付けない)
        public static string TagCrewmate = "";
        public static string TagImpostor = "";
        public static string TagNeutral = "";
        [PluginModuleInitializer]
        public static void Init()
        {
            CreateIfNotExists();
            EnsureWebhookDataOptions();
        }

        public static void CreateIfNotExists()
        {
            if (!File.Exists(OPTIONS_FILE_PATH))
            {
                try
                {
                    if (!Directory.Exists(Main.BaseDirectory)) Directory.CreateDirectory(Main.BaseDirectory);
                    if (File.Exists(Main.BaseDirectory + "/options.txt"))
                    {
                        File.Move(Main.BaseDirectory + "/options.txt", OPTIONS_FILE_PATH);
                    }
                    else
                    {
                        Logger.Info("Among Us.exeと同じフォルダにoptions.txtが見つかりませんでした。新規作成します。", "OptionsManager");
                        File.WriteAllText(OPTIONS_FILE_PATH, DEFAULT);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Exception(ex, "OptionsManager");
                }
            }
        }

        public static void CheckOptions()
        {
            //CreateIfNotExists();
            CheckVersion();
            EnsureWebhookDataOptions();
            LogData = true;
            PresetData = true;
            TagCrewmate = TagImpostor = TagNeutral = "";
            using StreamReader sr = new(OPTIONS_FILE_PATH, Encoding.GetEncoding("UTF-8"));
            string text;
            string[] tmp = Array.Empty<string>();

            while ((text = sr.ReadLine()) != null)
            {
                tmp = text.Split(":");
                if (tmp.Length > 1 && tmp[1] != "")
                {
                    if (tmp[0].ToLower() == "webhookurl")
                    {
                        var none = tmp.Skip(1).Join(delimiter: ":").ToLower() == "none";
                        WebhookUrl = none ? "none" : tmp.Skip(1).Join(delimiter: ":");
                    }
                    var optionName = tmp[0].Replace(" ", "").ToLowerInvariant();
                    var optionValue = tmp.Skip(1).Join(delimiter: ":").Trim();
                    if (optionName == "logdata" && bool.TryParse(optionValue, out var logData))
                        LogData = logData;
                    if (optionName == "presetdata" && bool.TryParse(optionValue, out var presetData))
                        PresetData = presetData;
                    if (tmp[0].ToLower() == "yomiageport") YomiagePort = tmp.Skip(1).Join(delimiter: ":");
                    if (optionName == "tagcrewmate") TagCrewmate = optionValue;
                    if (optionName == "tagimpostor") TagImpostor = optionValue;
                    if (optionName == "tagneutral") TagNeutral = optionValue;
                }
            }
        }

        // options.txt の 1 行を書きかえる(無ければ足す)。ゲームの中の入力画面から使う
        public static void SetValue(string key, string value)
        {
            try
            {
                CreateIfNotExists();
                var name = key.Replace(" ", "").ToLowerInvariant();
                var lines = File.ReadAllLines(OPTIONS_FILE_PATH, Encoding.UTF8).ToList();
                var index = lines.FindIndex(line => line.Split(':')[0].Replace(" ", "").ToLowerInvariant() == name);
                var newLine = $"{key}:{value}";
                if (index >= 0) lines[index] = newLine;
                else
                {
                    // 「この値を変更しないでください」の注意書きより前に足す
                    var note = lines.FindIndex(line => line.TrimStart().StartsWith("//"));
                    if (note >= 0) lines.Insert(note, newLine);
                    else lines.Add(newLine);
                }
                File.WriteAllLines(OPTIONS_FILE_PATH, lines, Encoding.UTF8);
                CheckOptions();
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "OptionsManager");
            }
        }

        private static void EnsureWebhookDataOptions()
        {
            try
            {
                var lines = File.ReadAllLines(OPTIONS_FILE_PATH, Encoding.UTF8);
                var updatedLines = lines
                    .Where(line => !line.TrimStart().StartsWith("//default:", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var hasLogData = updatedLines.Any(line =>
                    line.Split(':')[0].Replace(" ", "").Equals("logdata", StringComparison.OrdinalIgnoreCase));
                var hasPresetData = updatedLines.Any(line =>
                    line.Split(':')[0].Replace(" ", "").Equals("presetdata", StringComparison.OrdinalIgnoreCase));

                if (!hasLogData) updatedLines.Add("Log Data:true");
                if (!hasPresetData) updatedLines.Add("Preset Data:true");

                if (updatedLines.Count != lines.Length || !hasLogData || !hasPresetData)
                    File.WriteAllLines(OPTIONS_FILE_PATH, updatedLines, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "OptionsManager");
            }
        }
        public static void CheckVersion(StreamReader sr = null)
        {
            if (sr == null)
            {
                if (!File.Exists(OPTIONS_FILE_PATH))
                {
                    CreateIfNotExists();
                    return; //options.textがなかったらチェックしない
                }
                sr = new(OPTIONS_FILE_PATH, Encoding.GetEncoding("UTF-8"));
            }
            string text;
            string[] tmp = Array.Empty<string>();

            while ((text = sr.ReadLine()) != null)
            {
                tmp = text.Split(":");
                if (tmp.Length > 1 && tmp[1] != "")
                {
                    if (tmp[0].ToLower() == "verison")
                    {
                        if (tmp.Skip(1).Join(delimiter: ":") == $"{Version}") return;
                        sr.Close();
                        Logger.Info("バージョンが違うからデフォ値で上書きするのだ！", "OptionsManager");
                        try
                        {
                            File.WriteAllText(OPTIONS_FILE_PATH, DEFAULT);
                        }
                        catch (Exception ex)
                        {
                            Logger.Exception(ex, "OptionsManager");
                        }
                        return;

                    }
                }
            }
            sr.Close();
            Logger.Info("バージョン情報ないなら帰れにゃ ﾊﾞｲﾊﾞｲ\\(^^)", "OptionsManager");
            try
            {
                File.WriteAllText(OPTIONS_FILE_PATH, DEFAULT);
            }
            catch (Exception ex)
            {
                Logger.Exception(ex, "OptionsManager");
            }
        }
    }
}
