using System.Globalization;

using System.Text;

using HarmonyLib;

using TMPro;

using UnityEngine;



using TownOfHost.Modules;

using TownOfHost.Roles.Core;

using TownOfHost.Templates;

using static TownOfHost.Translator;

using System.Linq;

using System.Collections.Generic;



namespace TownOfHost

{

    [HarmonyPatch]

    public static class CredentialsPatch

    {

        public static SpriteRenderer TOHhmLogo { get; private set; }

        private static TextMeshPro pingTrackerCredential = null;

        private static AspectPosition pingTrackerCredentialAspectPos = null;

        private static float deltaTime = 0.0f;

        public static bool a = false;

        public static List<float> fpss = new();



        [HarmonyPatch(typeof(PingTracker), nameof(PingTracker.Update))]

        class PingTrackerUpdatePatch

        {

            static StringBuilder sb = new();

            static StringBuilder exSb = new();

            static void Postfix(PingTracker __instance)

            {

                // 要望により、バグ報告パネルを開いている間はPING表示を消す。
                // (SetActiveでGameObjectごと消すと、このUpdateパッチ自体が呼ばれなくなり
                //  閉じても復活しなくなるため、テキストの透明化のみで対応する)
                if (__instance.text != null)
                {
                    var isBugReportOpen = TownOfHost.Patches.BugReportWizard.Step != TownOfHost.Patches.BugReportWizardStep.Hidden;
                    var c = __instance.text.color;
                    __instance.text.color = new Color(c.r, c.g, c.b, isBugReportOpen ? 0f : 1f);
                }

                if (pingTrackerCredential == null)

                {

                    var uselessPingTracker = Object.Instantiate(__instance, __instance.transform.parent);

                    pingTrackerCredential = uselessPingTracker.GetComponent<TextMeshPro>();

                    Object.Destroy(uselessPingTracker);

                    pingTrackerCredential.alignment = TextAlignmentOptions.TopRight;

                    pingTrackerCredential.rectTransform.pivot = new(1f, 0.7f);  // 中心を右上角に設定

                    pingTrackerCredentialAspectPos = pingTrackerCredential.GetComponent<AspectPosition>();

                    pingTrackerCredentialAspectPos.Alignment = AspectPosition.EdgeAlignments.RightTop;

                    pingTrackerCredential.gameObject.name = "CredentialText";



                    if (GameStates.IsFreePlay && !uselessPingTracker.gameObject.active)

                        uselessPingTracker.gameObject.SetActive(true);

                }

                if (pingTrackerCredentialAspectPos)

                {

                    bool isChatButtonVisible = DestroyableSingleton<HudManager>.InstanceExists

                        && DestroyableSingleton<HudManager>.Instance.Chat.chatButton.gameObject.activeInHierarchy;

                    // 要望によりさらに左へ移動(以前は2.5f/1.8f)。
                    // その後、BUGボタンと同じ方向(右)へ少しずらす要望が複数回あったため、
                    // 右端からの距離をさらに詰める。
                    float rightOffset = isChatButtonVisible

                        ? 2.74f

                        : 2.04f;

                    // 役職ガイドボタンの左側へ上部表示を収める。

                    if (RoleGuideButtonPatch.HasGuideButton) rightOffset += 0.72f;

                    // 要望により、HELPの真横に並んだBUGボタンの分もさらに詰めて、上部表示と重ならないようにする。
                    if (TownOfHost.Patches.BugReportHudButtonPatch.HasBugButton) rightOffset += 0.72f;

                    // HELP/BUGボタンの実際の位置を見て、上部表示(バージョン・カスタムスポーンエディタ等)が
                    // それらと重ならないよう右端からの距離を広げる(フリープレイ等で文字が被る不具合の対策)。
                    try
                    {
                        if (DestroyableSingleton<HudManager>.InstanceExists)
                        {
                            var hudForOffset = DestroyableSingleton<HudManager>.Instance;
                            var uiCam = hudForOffset.UICamera != null ? hudForOffset.UICamera : Camera.main;
                            if (uiCam != null)
                            {
                                float edgeX = uiCam.ViewportToWorldPoint(new Vector3(1f, 0.5f, 0f)).x;
                                float leftmost = float.MaxValue;
                                var helpTf = hudForOffset.transform.Find("RoleGuideButton");
                                if (helpTf != null && helpTf.gameObject.activeInHierarchy)
                                    leftmost = Mathf.Min(leftmost, helpTf.position.x);
                                var bugTf = hudForOffset.transform.Find("BugReportHudButton");
                                if (bugTf != null && bugTf.gameObject.activeInHierarchy)
                                    leftmost = Mathf.Min(leftmost, bugTf.position.x);
                                if (leftmost < float.MaxValue)
                                {
                                    // ボタン半幅(約0.32)+余白
                                    rightOffset = Mathf.Max(rightOffset, edgeX - leftmost + 0.45f);
                                }
                            }
                        }
                    }
                    catch (System.Exception) { }
                    pingTrackerCredentialAspectPos.DistanceFromEdge = new(rightOffset, 0, 0);

                }



                if ((FixedUpdatePatch.timer is 1 && GameStates.InGame) || GameStates.IsLobby || GameStates.IsFreePlay)

                {

                    sb.Clear();



                    sb.Append("\r\n").Append($"<{Main.ModColor}>{Main.ModName}</color> v{Main.PluginShowVersion}");

                    if (Main.DebugVersion) sb.Append($"<{Main.ModColor}>☆Debug☆</color>");



                    if (!GameStates.IsFreePlay)

                    {

                        if ((Options.NoGameEnd.OptionMeGetBool() && GameStates.IsLobby) || (Main.DontGameSet && !GameStates.IsLobby)) sb.Append($"\r\n").Append(Utils.ColorString(Color.red, GetString("NoGameEnd")));



                        switch (Options.CurrentGameMode)

                        {

                            case CustomGameMode.StandardHAS: sb.Append($"\r\n").Append(Utils.ColorString(Color.yellow, GetString("StandardHAS"))); break;

                            case CustomGameMode.HideAndSeek: sb.Append($"\r\n").Append(Utils.ColorString(Color.red, GetString("HideAndSeek"))); break;

                            case CustomGameMode.TaskBattle: sb.Append($"\r\n").Append(Utils.ColorString(Color.cyan, GetString("TaskBattle"))); break;

                            case CustomGameMode.SuddenDeath: sb.Append("\r\n").Append(Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.Comebacker), GetString("SuddenDeathMode"))); break;

                            case CustomGameMode.MurderMystery: sb.Append("\r\n").Append($"<#1a389c>{GetString("MurderMystery")}"); break;

                        }

                        if (Options.EnableGM.OptionMeGetBool()) sb.Append($"\r\n").Append(Utils.ColorString(UtilsRoleText.GetRoleColor(CustomRoles.GM), GetString("GM")));

                        if (!GameStates.IsModHost) sb.Append($"\r\n").Append(Utils.ColorString(Color.red, GetString("Warning.NoModHost")));

                        if (DebugModeManager.IsDebugMode)

                        {

                            sb.Append("\r\n");

                            sb.Append(DebugModeManager.EnableTOHhmDebugMode.OptionMeGetBool() ? "<#0066de>DebugMode</color>" : Utils.ColorString(Color.green, "デバッグモード"));

                        }



                        exSb.Clear();



                        // #ffef39

                        if (Options.ExHideChatCommand.GetBool())

                            exSb.Append($"<#ffdfaf>Ⓗ</color> ");

                        if (Options.ExAftermeetingflash.GetBool())

                            exSb.Append($"<#d62c12>Ⓚ</color> ");

                        if (Options.FixSpawnPacketSize.GetBool())

                            exSb.Append($"<#ffef39>Ⓟ</color> ");

                        if (Options.ExRpcWeightR.GetBool())

                            exSb.Append($"<#3d83c5>Ⓡ</color> ");



                        if (exSb.Length > 0)

                        {

                            sb.Append("\r\n<size=50%>").Append(exSb).Append("</size>");

                        }

                    }

                    else if (CustomSpawnEditor.ActiveEditMode)

                    {

                        sb.Append($"\r\n").Append(Utils.ColorString(Color.cyan, GetString("ED.CSE")));

                    }



                    if (GameStates.IsLobby)

                    {

                        if (Options.IsStandardHAS && !CustomRoles.Sheriff.IsEnable() && !CustomRoles.SerialKiller.IsEnable() && CustomRoles.Egoist.IsEnable())

                            sb.Append($"\r\n").Append(Utils.ColorString(Color.red, GetString("Warning.EgoistCannotWin")));

                    }



                    pingTrackerCredential.text = sb.ToString();

                }

#if DEBUG

                if (Main.ViewPingDetails.Value)

                {

                    var serverName = GameStates.IsOnlineGame ? (Main.IsCs() ? ServerManager.Instance.CurrentRegion.Name : GetString(ServerManager.Instance.CurrentRegion.TranslateName)) : "ローカル";

                    deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;

                    float fps = 1.0f / deltaTime;

                    __instance.text.text += $"({AmongUsClient.Instance.Ping / 1000f}秒/{serverName}/FPS: {Mathf.Ceil(fps)})\n";

                    __instance.text.alignment = TextAlignmentOptions.Top;

                    if (a) fpss.Add(fps);

                }

                else __instance.text.alignment = TextAlignmentOptions.TopLeft;

#endif

            }

        }

        public static string Subver;

        // メインメニュー上部バーに表示する、現在読み込まれているMODモード。
        private static TextMeshPro ModeStatusText;

        [HarmonyPatch(typeof(VersionShower), nameof(VersionShower.Start))]

        class VersionShowerStartPatch

        {

            static TextMeshPro SpecialEventText;

            static void Postfix(VersionShower __instance)

            {

                if (!__instance) return;

                // 画面下のバニラのバージョン表記の隣に、hamoのバージョンを追記する
                if (__instance.text != null && !__instance.text.text.Contains(Main.ModName))
                {
                    string text = __instance.text.text;
                    int last = text.IndexOf('(');
                    if (last != -1) text = text.Substring(0, last);

                    __instance.text.text = $"AU {text}, <{Main.ModColor}>{Main.ModName}</color> v{Main.PluginShowVersion}";
                }

                TMPTemplate.SetBase(__instance.text);



                CreateText();



                ErrorText.Create(__instance.text);

                if (Main.hasArgumentException && ErrorText.Instance != null)

                {

                    ErrorText.Instance.AddError(ErrorCode.Main_DictionaryError);

                }



                VersionChecker.Check();

#if DEBUG

                if (OptionItem.IdDuplicated)

                {

                    ErrorText.Instance.AddError(ErrorCode.OptionIDDuplicate);

                }

#endif



                if (SpecialEventText == null && TOHhmLogo != null)

                {

                    SpecialEventText = TMPTemplate.Create(

                        "SpecialEventText",

                        "",

                        Color.white,

                        alignment: TextAlignmentOptions.Center,

                        parent: TOHhmLogo.transform);

                    SpecialEventText.name = "SpecialEventText";

                    SpecialEventText.fontSizeMin = 3f;

                    SpecialEventText.transform.localPosition = new Vector3(0f, 0.8f, 0f);

                    SpecialEventText?.gameObject?.SetActive(true);

                }

                if (!SpecialEventText) return;

                SpecialEventText.enabled = TitleLogoPatch.amongUsLogo != null;

                if (Event.IsInitialRelease)

                {

                    SpecialEventText.text = $"Happy Birthday to {Main.ModName}!";

                    if (ColorUtility.TryParseHtmlString(Main.ModColor, out var col))

                    {

                        SpecialEventText.color = col;

                    }

                }

                if (Event.IsChristmas && CultureInfo.CurrentCulture.Name == "ja-JP")

                {

                    //このソースコ―ドを見た人へ。口外しないでもらえると嬉しいです...

                    //To anyone who has seen this source code. I would appreciate it if you would keep your mouth shut...

                    SpecialEventText.text = $"何とは言いませんが、特別な日ですね。\n<size=15%>\n\n末永く爆発しろ</size>";

                    SpecialEventText.color = UtilsRoleText.GetRoleColor(CustomRoles.Lovers);

                }

                MainMenuManagerPatch.Statistics_TMP = TMPTemplate.Create(

                "Statistisc",

                "",

                Color.white,

                3f,

                TextAlignmentOptions.TopLeft,

                false,

                null

                );

                {

                    MainMenuManagerPatch.Statistics_TMP.transform.localPosition = new Vector3(0.8f, 1.7f);

                }

                CreateStreameMenu.CreateText();

            }

        }



        public static TextMeshPro CreateText()

        {

            var Debugver = "";

            if (Main.DebugVersion) Debugver = $"<{Main.ModColor}>☆Debug☆</color>";

            Subver = "";

            // 要望により「ホスト専用MOD」/「参加者専用MOD」のバッジ表示を削除。
            Main.credentialsText = $"<{Main.ModColor}>{Main.ModName}</color> v{Main.PluginShowVersion}" + Debugver;

#if DEBUG

            if (!GameStates.InGame) Main.credentialsText += $"\n<{Main.ModColor}>{ThisAssembly.Git.Branch}({ThisAssembly.Git.Commit})</color>";

#endif

            var credentials = TMPTemplate.Create(

                "TOHCredentialsText",

                Main.credentialsText,

                fontSize: 2f,

                alignment: TextAlignmentOptions.Right,

                setActive: true);

            // 要望により元の位置に戻した(以前 x=1.1f に変更していたが、実際にロビーで見えていた
            // 表示は上部のpingTrackerCredential側だったため、こちらは元の値に戻す)。
            // その後、BUGボタンと同じ量(+0.12f)だけ右にずらす要望があったため反映。
            credentials.transform.position = new Vector3(2.4619f, 2.29f, -5f);

            // ユーザー名横の追加モード表示は使用しない。過去の表示オブジェクトが残っていても隠す。
            if (ModeStatusText != null)
                ModeStatusText.gameObject.SetActive(false);

#if DEBUG

            if (!GameStates.InGame) credentials.transform.position -= new Vector3(0f, 0.1218f, 0f);

#endif

            if (FindAGameManager._instance)

            {

                credentials.transform.position = new Vector3(2.5f, -2.858f, 5f);

#if DEBUG

                credentials.transform.position += new Vector3(0, 0.185f);

#endif

            }

            return credentials;

        }



        [HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.Start))]

        class TitleLogoPatch

        {

            public static GameObject amongUsLogo;



            [HarmonyPriority(Priority.VeryHigh)]

            static void Postfix(MainMenuManager __instance)

            {

                // 左上はバニラのAMONG USロゴを維持する。
                amongUsLogo = GameObject.Find("LOGO-AU");
                if (amongUsLogo != null)
                    amongUsLogo.SetActive(true);

                // 初期hamo版と同じ右パネル（YouTube/GitHub等の追加ボタン領域）へ
                // 未加工のTown Of Host-hamoロゴ画像を表示する。左上のAMONG USロゴは置き換えない。
                var rightPanel = __instance.gameModeButtons?.transform?.parent ?? __instance.transform;
                if (TOHhmLogo == null || TOHhmLogo.transform.parent != rightPanel)
                {
                    if (TOHhmLogo != null) Object.Destroy(TOHhmLogo.gameObject);

                    var logoObject = new GameObject("titleLogo_TOHhm");
                    logoObject.transform.parent = rightPanel;
                    logoObject.transform.localPosition = new Vector3(0f, 0.15f, 1f);
                    TOHhmLogo = logoObject.AddComponent<SpriteRenderer>();
                    TOHhmLogo.sprite = UtilsSprite.LoadSprite(
                        Event.April || Event.Special
                            ? "TownOfHost.Resources.TownOfHost-hamo-logo2.png"
                            : "TownOfHost.Resources.TownOfHost-hamo-logo.png",
                        175f);
                }
                TOHhmLogo.gameObject.SetActive(true);

            }

        }

        [HarmonyPatch(typeof(ModManager), nameof(ModManager.LateUpdate))]

        class ModManagerLateUpdatePatch

        {

            static int oldcount;

            static float olddeltimer;

            static float timer = 0;

            public static void Prefix(ModManager __instance)

            {

                __instance.ShowModStamp();



                LateTask.Update(Time.deltaTime);

                CheckMurderPatch.Update();



                if (Main.MegCount > 49)

                {

                    timer += Time.deltaTime;



                    if (timer > 1)

                    {

                        timer = 0;

                        olddeltimer = 0;

                        Main.MegCount = 0;

                    }

                }

                else if (Main.MegCount == oldcount)

                {

                    olddeltimer += Time.deltaTime;



                    if (olddeltimer > 1.3f)

                    {

                        timer = 0;

                        olddeltimer = 0;

                        Main.MegCount = 0;

                    }

                }



                oldcount = Main.MegCount;

            }

            public static void Postfix(ModManager __instance)

            {
                // 赤い✘ボタンのサイズ・位置を毎フレーム固定(チカチカ防止)
                MainMenuManagerPatch.ApplyRedButtonLayout();

                // 参加者専用版は会議HUD上部の他テキストと重ならないよう、
                // MODスタンプを小さくして右上から少し下へ配置する。
                var offset_y = HudManager.InstanceExists ? (Main.IsNonHostClient ? 2.15f : 1.6f) : 0.9f;
                // MODスタンプが目立ちすぎて邪魔なため、全体的に縮小する。
                __instance.ModStamp.transform.localScale = Main.IsNonHostClient ? Vector3.one * 0.42f : Vector3.one * 0.52f;

                __instance.ModStamp.transform.position = AspectPosition.ComputeWorldPosition(

                    __instance.localCamera, AspectPosition.EdgeAlignments.RightTop,

                    new Vector3(Main.IsNonHostClient ? 0.62f : 0.4f, offset_y, __instance.localCamera.nearClipPlane + 0.1f));

            }

        }

    }

}

