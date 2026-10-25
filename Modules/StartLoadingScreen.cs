using System;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace TownOfHost.Modules;

// ゲーム開始後の読み込み中に出す TOH-hamo のロード画面(自分の画面だけ。通信はしない)。
// TOH-hm オプションの「開始時のロード画面」で、ふつうの読み込みバーと切り替えられる
public static class StartLoadingScreen
{
    private const float MaxShowTime = 40f; // 何かで閉じられなかった時の保険
    private static readonly string[] Tips =
    {
        "会議中は /cmd n r I で、入っている役職を確かめられます",
        "Shift+F3 で通信モニターを出せます",
        "役職の説明は、タスクの欄からいつでも見られます",
        "ゲーム設定の「有効なMAP設定」で、今使える設定だけを表示できます",
        "暗転したら、少し待つと戻ることがあります",
    };

    private static GameObject root;
    private static SpriteRenderer fill;
    private static TextMeshPro status;
    private static TextMeshPro dots;
    private static TextMeshPro tip;
    private static float shownAt;
    private static float targetPercent;
    private static float shownPercent;
    private static string statusText = "";
    private static bool active;

    public static bool IsShowing => active;
    public static bool Enabled => Main.CustomStartLoading?.Value ?? true;

    public static void Show()
    {
        if (!Enabled || active) return;
        var hud = HudManager.Instance;
        if (hud == null) return;
        try
        {
            Build(hud);
            active = true;
            shownAt = Time.unscaledTime;
            targetPercent = shownPercent = 0f;
            tip.text = $"<color={UiSkin.DimHex}>TIPS</color>  {Tips[IRandom.Instance.Next(Tips.Length)]}";
            root.SetActive(true);
        }
        catch (Exception e) { Logger.Warn($"ロード画面を出せませんでした: {e.Message}", "StartLoadingScreen"); Hide(); }
    }

    public static void Hide()
    {
        active = false;
        if (root != null) root.SetActive(false);
    }

    public static void SetProgress(float percent, string text)
    {
        targetPercent = Mathf.Clamp01(percent / 100f);
        if (!string.IsNullOrEmpty(text)) statusText = text;
    }

    public static void Tick()
    {
        if (!active || root == null) return;
        // イントロが始まったり、タスクが始まったりしたら閉じる
        if (IntroCutscene.Instance != null || GameStates.IsInTask || GameStates.IsMeeting || Time.unscaledTime - shownAt > MaxShowTime)
        {
            Hide();
            return;
        }
        shownPercent = Mathf.MoveTowards(shownPercent, Mathf.Max(targetPercent, 0.05f), Time.unscaledDeltaTime * 0.8f);
        fill.transform.localScale = new Vector3(BarWidth * shownPercent, BarHeight, 1f);
        fill.transform.localPosition = new Vector3(-BarWidth / 2f + BarWidth * shownPercent / 2f, BarY, -0.2f);
        status.text = statusText;
        dots.text = new string('.', 1 + (int)(Time.unscaledTime * 3f) % 3);
    }

    private const float BarWidth = 4.2f, BarHeight = 0.12f, BarY = -0.9f;

    private static Sprite white;
    private static SpriteRenderer Rect(string name, Transform parent, Color color, Vector3 pos, Vector2 size, int order)
    {
        white ??= Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
        var go = new GameObject(name);
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = white;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    private static TextMeshPro Text(string name, HudManager hud, Transform parent, float size, Vector3 pos, int order)
    {
        var t = Templates.TMPTemplate.Create(name, "", Color.white, size, TextAlignmentOptions.Center, true, parent);
        var font = hud.TaskPanel?.taskText?.font;
        if (font != null) t.font = font;
        t.rectTransform.sizeDelta = new Vector2(9f, 0.6f);
        t.transform.localPosition = pos;
        t.enableWordWrapping = false;
        t.sortingOrder = order;
        return t;
    }

    private static void Build(HudManager hud)
    {
        if (root != null) return;
        root = new GameObject("TOHhamoStartLoading");
        root.layer = LayerMask.NameToLayer("UI");
        root.transform.SetParent(hud.transform, false);
        // 画面の真ん中・いちばん手前
        root.transform.localPosition = new Vector3(0f, 0f, -600f);

        const int order = 2000;
        Rect("Bg", root.transform, new Color(0.035f, 0.04f, 0.07f, 1f), Vector3.zero, new Vector2(40f, 30f), order);
        var logo = UtilsSprite.LoadSprite("TownOfHost.Resources.TownOfHost-hamo-logo.png", 170f);
        if (logo != null)
        {
            var go = new GameObject("Logo");
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.9f, -0.1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = logo;
            sr.sortingOrder = order + 1;
        }
        Rect("BarBg", root.transform, new Color(1f, 1f, 1f, 0.12f), new Vector3(0f, BarY, -0.1f), new Vector2(BarWidth, BarHeight), order + 1);
        fill = Rect("BarFill", root.transform, new Color(1f, 0.52f, 0.85f, 1f), new Vector3(-BarWidth / 2f, BarY, -0.2f), new Vector2(0f, BarHeight), order + 2);
        status = Text("Status", hud, root.transform, 2.2f, new Vector3(0f, -0.45f, -0.3f), order + 3);
        dots = Text("Dots", hud, root.transform, 2.2f, new Vector3(0f, -1.3f, -0.3f), order + 3);
        tip = Text("Tip", hud, root.transform, 1.6f, new Vector3(0f, -2.1f, -0.3f), order + 3);
        root.SetActive(false);
    }
}

[HarmonyPatch(typeof(LoadingBarManager))]
static class StartLoadingScreenPatch
{
    // ゲーム開始の読み込みの時だけ、ふつうのバーの代わりに出す(起動時の読み込みは別の画面)
    [HarmonyPatch(nameof(LoadingBarManager.ToggleLoadingBar)), HarmonyPostfix]
    static void Toggle(LoadingBarManager __instance, [HarmonyArgument(0)] bool on)
    {
        try
        {
            if (!on) { StartLoadingScreen.Hide(); return; }
            if (!StartLoadingScreen.Enabled || HudManager.Instance == null || AmongUsClient.Instance == null || !AmongUsClient.Instance.IsGameStarted) return;
            StartLoadingScreen.Show();
            if (__instance?.loadingBar != null) __instance.loadingBar.gameObject.SetActive(false);
        }
        catch { }
    }

    [HarmonyPatch(nameof(LoadingBarManager.SetLoadingPercent)), HarmonyPostfix]
    static void Percent(LoadingBarManager __instance, [HarmonyArgument(0)] float percent, [HarmonyArgument(1)] StringNames loadText)
    {
        try
        {
            // 「ほかの人を待っています」はゲーム開始の読み込みなので、まだ出ていなければここで出す
            if (loadText == StringNames.LoadingBarGameStartWaitingPlayers && StartLoadingScreen.Enabled) StartLoadingScreen.Show();
            StartLoadingScreen.SetProgress(percent, __instance?.loadingBar?.loadingText?.text);
            if (StartLoadingScreen.IsShowing && __instance?.loadingBar != null) __instance.loadingBar.gameObject.SetActive(false);
        }
        catch { }
    }
}

[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
static class StartLoadingScreenTickPatch
{
    static void Postfix()
    {
        try { StartLoadingScreen.Tick(); } catch { }
    }
}
