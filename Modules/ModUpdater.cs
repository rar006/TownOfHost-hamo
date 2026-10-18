using System;

using System.Collections.Generic;

using System.IO;

using System.Net;

using System.Net.Http;

using System.Linq;

using System.Reflection;

using System.Text.Json;

using System.Text.Json.Serialization;

using System.Threading.Tasks;

using HarmonyLib;

using UnityEngine;

using Newtonsoft.Json.Linq;

using TownOfHost.Templates;

using static TownOfHost.Translator;



namespace TownOfHost

{

    [HarmonyPatch]

    public class ModUpdater

    {

        private static readonly string URL = "https://github.com/rar006/TownOfHost-hamo";
        // リリース情報はAPIのURLから取る(github.com の /releases/latest はWebページ(HTML)を返すので、
        // JSONとして読めずに毎回「MODを構成するファイルが破損しています」扱いになっていた)
        private static readonly string ApiURL = "https://api.github.com/repos/rar006/TownOfHost-hamo";
        /// <summary>起動を早くするため、MOD読み込み直後にリリース情報の取得を始めておく</summary>
        public static void StartPrefetch()
        {
            TownOfHost.Modules.StartupPrefetch.Start(ReleaseUrl(Main.BetaBuildURL.Value != "", false));
        }
        private static string ReleaseUrl(bool beta, bool all)
            => beta ? Main.BetaBuildURL.Value : ApiURL + "/releases" + (all ? "?page=1&per_page=100" : "/latest");

        public static bool hasUpdate = false;

        public static bool isBroken = false;

        public static bool isChecked = false;

        public static bool isSubUpdata = false;

        public static Version latestVersion = null;

        public static string latestTitle = null;

        public static string downloadUrl = null;

        public static GenericPopup InfoPopup;

        public static bool? BlockPublicRoom = null;

        public static string body = "詳細のチェックに失敗しました\nFailed to verify the details.";

        public static List<Release> releases = new();

        public static List<Release> snapshots = new();
        /// <summary>バージョン切り替え画面に並べる最大数(2列×8段)</summary>
        public const int MaxSnapshots = 16;

        /// <summary>
        /// GitHubのリリース説明(Markdown)を、ゲーム内で読める文字にする。
        /// 見出しの#や太字の**などを消し、ゲームで表示できない絵文字も取り除く。
        /// </summary>
        public static string CleanReleaseBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return "(説明はありません)";
            const char LF = (char)10, CR = (char)13, VS16 = (char)0xFE0F, ZWJ = (char)0x200D;
            var sb = new System.Text.StringBuilder();
            foreach (var rawLine in body.Replace(CR.ToString(), "").Split(LF))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) { if (sb.Length > 0) sb.Append(LF); continue; }
                var heading = line.StartsWith("#");
                line = line.TrimStart('#').Trim().Replace("**", "").Replace("`", "");
                if (line.StartsWith("- ")) line = "・" + line[2..];
                // 絵文字(サロゲートペアや異体字セレクタ)は表示できないので消す
                var clean = new System.Text.StringBuilder();
                foreach (var c in line)
                {
                    if (char.IsSurrogate(c) || c == VS16 || c == ZWJ) continue;
                    clean.Append(c);
                }
                line = clean.ToString().Replace("<", "＜").Replace(">", "＞").Trim();
                if (line.Length == 0) continue;
                sb.Append(heading ? $"<b><color=#ffd36e>{line}</color></b>" : line).Append(LF);
            }
            return sb.ToString().TrimEnd();
        }



        [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start)), HarmonyPostfix, HarmonyPriority(Priority.LowerThanNormal)]

        public static void StartPostfix()

        {

            // ===== 例外がMainMenuManager.Start全体に波及しないようにする =====

            // CheckRelease内部のJSON解析(Newtonsoft.Json)がIl2Cppの相互運用境界で

            // 例外を起こすことがあり、その場合は内部のtry/catchが効かず、

            // MainMenuManager.Startの残りの処理(ゲーム本体の初期化処理を含む)まで

            // 巻き込んで止めてしまう可能性がある。そのため、このPostfix全体を

            // try/catchで確実に囲み、失敗しても他のStartPostfixパッチの実行や

            // バニラ側の処理を妨げないようにする。

            try

            {

                DeleteOldDLL();

                InfoPopup = UnityEngine.Object.Instantiate(Twitch.TwitchManager.Instance.TwitchPopup);

                InfoPopup.name = "InfoPopup";

                InfoPopup.TextAreaTMP.GetComponent<RectTransform>().sizeDelta = new(2.5f, 2f);

                if (!isChecked)

                {

                    CheckRelease(Main.BetaBuildURL.Value != "").GetAwaiter().GetResult();

                }

                MainMenuManagerPatch.UpdateButton.Button.gameObject.SetActive(hasUpdate);

                MainMenuManagerPatch.UpdateButton.Button.transform.Find("FontPlacer/Text_TMP").GetComponent<TMPro.TMP_Text>().SetText($"{GetString("updateButton")}\n{latestTitle}");

                MainMenuManagerPatch.UpdateButton2.Button.gameObject.SetActive(hasUpdate);

            }

            catch (Exception ex)

            {

                Logger.Error($"アップデートチェック処理中に予期しないエラーが発生しました。\n{ex}", "CheckRelease", false);

            }

        }

        /// <param name="all">1ページ分のリリースをすべて取得し、releasesとsnapshotsを更新します</param>

        /// <param name="forced">allパラメータと同時に使用します / キャッシュを使用せずもう一度取得します</param>

        /// <returns></returns>

        public static Task<bool> CheckRelease(bool beta = false, bool all = false, bool forced = false)

        {

            // NOTE: 呼び出し元は全て .GetAwaiter().GetResult() で同期的に待っているため、

            // 本メソッドは意図的に async/await を使わず同期実行にしている。

            // 以前は await の継続処理（JObject.Parse を含む）が IL2CPP 非対応のスレッドプールスレッド上で

            // 実行されてしまい、ネイティブクラッシュ(0xC0000005)を引き起こしていたための対策。

            return Task.FromResult(CheckReleaseSync(beta, all, forced));

        }



        /// <summary>
        /// リリースのタグ(例: "v4.32.00α", "v4.31.01β", "S4.00.00.10")からバージョンを取り出す。
        /// 以前は new Version(...) に α/β 付きの文字列をそのまま渡していたため例外になり、
        /// 「MODを構成するファイルが破損しています」と誤判定されて公開(マッチメイキング)できなかった。
        /// 数字と '.' 以外は無視し、読めなければ例外ではなく null を返す。
        /// また、タグは「4.32.00」(3つ区切り)、MOD本体は「4.00.32.00」(PluginVersion)と書き方が違うので、
        /// 3つ区切りのタグは PluginVersion と同じ4つ区切りにそろえて比較する。
        ///   4.32.00 → 4.0.32.0 / 4.31.01 → 4.0.31.1 / 4.00.31 → 4.0.31.0 / 4.00.00.10 → そのまま
        /// </summary>
        public static Version ParseTagVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            var digits = new string(tag.SkipWhile(c => !char.IsDigit(c)).TakeWhile(c => char.IsDigit(c) || c == '.').ToArray()).Trim('.');
            var parts = digits.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var nums = new List<int>();
            foreach (var p in parts)
            {
                if (!int.TryParse(p, out var n)) return null;
                nums.Add(n);
            }
            if (nums.Count == 3)
                nums = nums[1] == 0 ? new() { nums[0], 0, nums[2], 0 } : new() { nums[0], 0, nums[1], nums[2] };
            return nums.Count switch
            {
                1 => new Version(nums[0], 0),
                2 => new Version(nums[0], nums[1]),
                3 => new Version(nums[0], nums[1], nums[2]),
                >= 4 => new Version(nums[0], nums[1], nums[2], nums[3]),
                _ => null,
            };
        }

        private static bool CheckReleaseSync(bool beta, bool all, bool forced)

        {

            //bool updateCheck = version != null && version.Update.Version != null;

            string url = ReleaseUrl(beta, all);

            // (?page=1&per_page=100 は ReleaseUrl で付けている)



            //強制オプションが使用されていない & allオプションが使用されている & 既に取得済み

            if (!forced && all && releases.Any()) return true;




            try

            {

                // 通信はバックグラウンド(StartupPrefetch)で行い、ここでは結果を受け取るだけ。
                // JSONの解析はこのまま呼び出し元(メイン)スレッドで行う(以前、解析をワーカースレッドで行ってクラッシュしたため)
                string result = TownOfHost.Modules.StartupPrefetch.Get(url);
                if (result == null)
                {
                    Logger.Error($"リリース情報を取得できませんでした: {url}", "CheckRelease");
                    return false;
                }

                JObject data = all ? null : JObject.Parse(result);

                if (beta)

                {

                    latestTitle = data["name"].ToString();

                    downloadUrl = data["url"].ToString();

                    hasUpdate = latestTitle != ThisAssembly.Git.Commit;

                }

                else if (all)

                {

                    snapshots = new();

                    releases = JsonSerializer.Deserialize<List<Release>>(result);

                    foreach (var release in releases)

                    {

                        var tag = release.TagName;

                        var assets = release.Assets;

                        foreach (var asset in assets)

                        {

                            if (asset.Name == "TownOfHost-hamo_Steam.dll" && Constants.GetPlatformType() == Platforms.StandaloneSteamPC)

                            {

                                release.DownloadUrl = asset.DownloadUrl;

                                break;

                            }

                            if (asset.Name == "TownOfHost-hamo_Epic.dll" && Constants.GetPlatformType() == Platforms.StandaloneEpicPC)

                            {

                                release.DownloadUrl = asset.DownloadUrl;

                                break;

                            }

                            if (asset.Name == "TownOfHost-hamo.dll")

                                release.DownloadUrl = asset.DownloadUrl;

                        }

                        release.OpenURL = $"https://github.com/rar006/TownOfHost-hamo/releases/tag/{tag}";



                        if (tag == null) continue;



                        // v / S / s.

                        var normalizedTag = tag.Trim();

                        if (normalizedTag.StartsWith("v", StringComparison.OrdinalIgnoreCase))

                            normalizedTag = normalizedTag[1..];

                        if (normalizedTag.StartsWith("s", StringComparison.OrdinalIgnoreCase))

                            normalizedTag = normalizedTag[1..];



                        // 3.x / 1.x

                        // hamo のバージョン(4.xx.yy)は、今と違う番号のものも選べるように全部出す(以前は今と同じ番号のものだけだった)
                        if ((!normalizedTag.Contains($"{Main.ModVersion}") && !normalizedTag.StartsWith("4.") && !normalizedTag.StartsWith("3.") && !normalizedTag.StartsWith("1."))

                            || normalizedTag.Contains(".30.1")

                            || normalizedTag.Contains(".30.21")

                            || normalizedTag.Contains(".30.22")

                            || normalizedTag is "51.13.30")

                            continue;



                        if (normalizedTag.StartsWith("5.") || normalizedTag.StartsWith("519."))

                            continue;



                        if (snapshots.Count >= MaxSnapshots) continue; // 新しい順に、画面に並べられる数まで
                        release.Info = CleanReleaseBody(release.body);
                        snapshots.Add(release);

                    }

                }

                else

                {

                    latestVersion = ParseTagVersion(data["tag_name"]?.ToString());

                    latestTitle = $"Ver. {data["tag_name"]?.ToString().TrimStart('v', 'V', 'S', 's')}";

                    // (JArray)data["assets"] は Newtonsoft のバージョン違いで InvalidCastException になっていたので、一覧として読む
                    var assets = (data.GetJ("assets") as System.Collections.Generic.IEnumerable<JToken>)?.ToList() ?? new System.Collections.Generic.List<JToken>();

                    for (int i = 0; i < assets.Count; i++)

                    {

                        if (assets[i].GetJ("name").ToString() == "TownOfHost-hamo_Steam.dll" && Constants.GetPlatformType() == Platforms.StandaloneSteamPC)

                        {

                            downloadUrl = assets[i].GetJ("browser_download_url").ToString();

                            break;

                        }

                        if (assets[i].GetJ("name").ToString() == "TownOfHost-hamo_Epic.dll" && Constants.GetPlatformType() == Platforms.StandaloneEpicPC)

                        {

                            downloadUrl = assets[i].GetJ("browser_download_url").ToString();

                            break;

                        }

                        if (assets[i].GetJ("name").ToString() == "TownOfHost-hamo.dll")

                            downloadUrl = assets[i].GetJ("browser_download_url").ToString();

                    }

                    var body = data["body"].ToString();

                    bool? check = body?.Contains("IsforceUpdate") ?? null;

                    // タグからバージョンを読めなかった場合は「更新なし」扱い(破損扱いにはしない)
                    hasUpdate = latestVersion != null && (latestVersion.CompareTo(Main.version) > 0 ||

                    //最後のアプデのcheckが有効で～最終バージョンと現バージョンが一緒じゃない

                    (check is true && latestVersion.CompareTo(Main.version) is not 0));

                }

                if (all) return true;

                if (downloadUrl == null)

                {

                    Logger.Error("ダウンロードURLを取得できませんでした。", "CheckRelease");

                    return false;

                }

                isChecked = true;

                isBroken = false;

                var ages = data["body"].ToString().Split("## ");

                for (var i = 0; i < ages.Length - 1; i++)

                {

                    if (i == 0)

                    {

                        body = ages[0] + "<size=80%>";

                        continue;

                    }

                    if (i == 1) continue;

                    var ages2 = ages[i].Split("\n");

                    for (var i2 = 0; i2 < ages2.Length; i2++)

                    {

                        if (i2 == 0)

                        {

                            body += $"<b><size=120%>{ages2[i2]}";

                            body += "</b></size>\n";

                            continue;

                        }

                        body += ages2[i2] + "\n";

                    }

                }

            }

            catch (Exception ex)

            {

                // 通信の失敗(タイムアウト・オフライン等)はファイルの破損ではないので、破損扱いにしない
                if (ex is not (HttpRequestException or TaskCanceledException or System.Net.Sockets.SocketException))
                    isBroken = true;

                Logger.Error($"リリースのチェックに失敗しました。\n{ex}", "CheckRelease", false);

                return false;

            }

            return true;

        }

        public static void StartUpdate(string url, string openurl = "")

        {

            ShowPopup(GetString("updatePleaseWait"));

            if (!BackupDLL())

            {

                ShowPopup(GetString("updateManually"), true, openurl);

                return;

            }

            _ = DownloadDLL(url, openurl);

            return;

        }

        public static bool BackupDLL()

        {

            try

            {

                File.Move(Assembly.GetExecutingAssembly().Location, Assembly.GetExecutingAssembly().Location + ".bak");

            }

            catch

            {

                Logger.Error("バックアップに失敗しました", "BackupDLL");

                return false;

            }

            return true;

        }

        public static void DeleteOldDLL()

        {

            try

            {

                foreach (var path in Directory.EnumerateFiles(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "*.bak"))

                {

                    Logger.Info($"{Path.GetFileName(path)}を削除", "DeleteOldDLL");

                    File.Delete(path);

                }

            }

            catch

            {

                Logger.Error("削除に失敗しました", "DeleteOldDLL");

            }

            return;

        }

        public static async Task<bool> DownloadDLL(string url, string openurl)

        {

            try

            {

                using HttpClient client = new();

                using var response = await client.GetAsync(url);

                if (response.IsSuccessStatusCode)

                {

                    using var content = response.Content;

                    using var stream = content.ReadAsStream();

                    using var file = new FileStream("BepInEx/plugins/TownOfHost-hamo.dll", FileMode.Create, FileAccess.Write);

                    stream.CopyTo(file);

                    ShowPopup(GetString("updateRestart"), true, openurl);

                    return true;

                }

            }

            catch (Exception ex)

            {

                Logger.Error($"ダウンロードに失敗しました。\n{ex}", "DownloadDLL", false);

            }

            ShowPopup(GetString("updateManually"), true, openurl);

            return false;

        }

        private static void DownloadCallBack(object sender, DownloadProgressChangedEventArgs e)

        {

            ShowPopup($"{GetString("updateInProgress")}\n{e.BytesReceived}/{e.TotalBytesToReceive}({e.ProgressPercentage}%)");

        }

        private static void ShowPopup(string message, bool showButton = false, string OpenURL = "")

        {

            if (InfoPopup != null)

            {

                InfoPopup.Show(message);

                var button = InfoPopup.transform.FindChild("ExitGame");

                if (button != null)

                {

                    button.gameObject.SetActive(showButton);

                    button.GetComponentInChildren<TextTranslatorTMP>().TargetText = StringNames.QuitLabel;

                    button.GetComponent<PassiveButton>().OnClick = new();

                    button.GetComponent<PassiveButton>().OnClick.AddListener((Action)(() =>

                    {

                        Application.OpenURL(OpenURL == "" ? "https://github.com/rar006/TownOfHost-hamo/releases/latest" : OpenURL);

                        Application.Quit();

                    }));

                }

            }

        }

        public class Release

        {

            [JsonPropertyName("tag_name")]

            public string TagName { get; set; }

            [JsonPropertyName("assets")]

            public List<Asset> Assets { get; set; }

            [JsonPropertyName("body")]

            public string body { get; set; }



            public string DownloadUrl { get; set; }

            public string OpenURL { get; set; }

            public string Info { get; set; }



            public class Asset

            {

                [JsonPropertyName("name")]

                public string Name { get; set; }

                [JsonPropertyName("browser_download_url")]

                public string DownloadUrl { get; set; }

            }

        }

    }

}