using System;
using System.Globalization;
using HarmonyLib;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TownOfHost.Modules.ClientOptions;

// デバッグ版の「イントロ撮影」: 試合の最初のイントロ(陣営・役職の画面)に出す文字と色を、自分の画面だけ好きに変える
public static class IntroShotScreen
{
    static readonly (string name, string hex)[] Colors =
    {
        ("シアン", "#00FFFF"), ("白", "#FFFFFF"), ("赤", "#FF1919"), ("オレンジ", "#FF9A2E"), ("黄", "#FFE14D"),
        ("緑", "#3DDC6A"), ("青", "#2E6BFF"), ("紫", "#9B5CFF"), ("ピンク", "#FF6FD8"), ("灰", "#808080"), ("黒", "#000000"),
    };

    private static GameObject root;
    private static TextMeshPro preview;
    private static TextMeshPro status;
    private static ToggleButtonBehaviour enableButton;
    private static ToggleButtonBehaviour textColorButton;
    private static ToggleButtonBehaviour backColorButton;
    private static ToggleButtonBehaviour roleButton;

    public static void ForgetPopup()
    {
        try { if (root != null) Object.Destroy(root); } catch { }
        root = null;
    }

    public static void Hide()
    {
        if (root != null) root.SetActive(false);
    }

    public static void Show()
    {
        var options = OptionsMenuBehaviourStartPatch.Instance;
        if (options == null) return;
        if (root == null && !Build(options)) return;
        status.text = "";
        Refresh();
        root.SetActive(true);
    }

    public static bool TryParseColor(string hex, out Color color)
    {
        color = Color.white;
        if (string.IsNullOrEmpty(hex)) return false;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6 && hex.Length != 8) return false;
        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return false;
        if (hex.Length == 6) v = (v << 8) | 0xFF;
        color = new Color32((byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Color TextColor => TryParseColor(Main.IntroShotTextColor.Value, out var c) ? c : Color.cyan;
    public static Color BackColor => TryParseColor(Main.IntroShotBackColor.Value, out var c) ? c : Color.blue;

    private static ToggleButtonBehaviour Button(OptionsMenuBehaviour options, string name, string text, Vector3 pos, Vector2 scale, Action onClick)
    {
        var b = Object.Instantiate(options.DisableMouseMovement, root.transform);
        b.name = name;
        b.transform.localPosition = pos;
        b.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        b.Text.text = text;
        b.Background.color = Palette.DisabledGrey;
        var pb = b.GetComponent<PassiveButton>();
        pb.OnClick = new();
        pb.OnClick.AddListener((Action)(() => { onClick(); Refresh(); }));
        b.gameObject.SetActive(true);
        return b;
    }

    private static TextMeshPro Label(OptionsMenuBehaviour options, string name, Vector3 pos, float size, float width)
    {
        var t = Object.Instantiate(options.DisableMouseMovement.Text, root.transform);
        t.name = name;
        t.DestroyTranslator();
        t.enableAutoSizing = false;
        t.fontSize = size;
        t.alignment = TextAlignmentOptions.Center;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.rectTransform.sizeDelta = new Vector2(width, 0.5f);
        t.transform.localPosition = pos;
        t.transform.localScale = Vector3.one;
        t.text = "";
        t.gameObject.SetActive(true);
        return t;
    }

    private static string Clipboard()
    {
        try { return (GUIUtility.systemCopyBuffer ?? "").Replace("\r", "").Trim(); } catch { return ""; }
    }

    private static void SetFromClipboard(Action<string> set, string what)
    {
        var text = Clipboard();
        if (text == "") { status.text = "<color=#ff8080>コピーした文字がありません</color>"; return; }
        if (text.Length > 120) text = text.Substring(0, 120);
        set(text);
        status.text = $"<color=#8cffff>{what}を変えました</color>";
    }

    private static void ColorFromClipboard(bool back)
    {
        var text = Clipboard();
        if (!TryParseColor(text, out _)) { status.text = "<color=#ff8080>コピーした文字が色コード(例: #FF6FD8)ではありません</color>"; return; }
        var hex = "#" + text.Trim().TrimStart('#').ToUpperInvariant();
        if (back) Main.IntroShotBackColor.Value = hex; else Main.IntroShotTextColor.Value = hex;
        status.text = $"<color=#8cffff>{(back ? "背景" : "文字")}の色を {hex} にしました</color>";
    }

    private static string NextColor(string now)
    {
        var i = Array.FindIndex(Colors, c => string.Equals(c.hex, now, StringComparison.OrdinalIgnoreCase));
        return Colors[(i + 1) % Colors.Length].hex;
    }

    private static string ColorName(string hex)
    {
        foreach (var c in Colors) if (string.Equals(c.hex, hex, StringComparison.OrdinalIgnoreCase)) return c.name;
        return hex;
    }

    private static bool Build(OptionsMenuBehaviour options)
    {
        if (options.DisableMouseMovement == null || options.Background == null) return false;
        var parent = ClientActionItem.CustomBackground?.transform?.parent ?? options.transform;
        root = new GameObject("IntroShotPopup");
        root.layer = LayerMask.NameToLayer("UI");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = new Vector3(0f, 0f, -20f);

        var bg = Object.Instantiate(options.Background, root.transform);
        bg.name = "Bg";
        bg.transform.localPosition = Vector3.zero;
        bg.transform.localScale = new Vector3(1.3f, 1f, 1f);
        bg.gameObject.SetActive(true);

        var title = Label(options, "Title", new Vector3(0f, 2.35f, -1f), 2.4f, 7f);
        title.text = "イントロ撮影 <size=70%><color=#a0a0a0>(デバッグ版)</color></size>";

        enableButton = Button(options, "Enable", "", new Vector3(0f, 1.8f, -2f), new Vector2(0.7f, 0.6f), () =>
            Main.IntroShot.Value = !Main.IntroShot.Value);

        preview = Label(options, "Preview", new Vector3(0f, 1.15f, -1f), 2.6f, 7.2f);
        preview.enableWordWrapping = true;
        preview.rectTransform.sizeDelta = new Vector2(7.2f, 0.9f);

        Button(options, "TitleFromClip", "コピーした文字をタイトルに", new Vector3(-1.4f, 0.35f, -2f), new Vector2(0.55f, 0.6f),
            () => SetFromClipboard(v => Main.IntroShotTitle.Value = v, "タイトル"));
        Button(options, "SubFromClip", "コピーした文字を下の文字に", new Vector3(1.4f, 0.35f, -2f), new Vector2(0.55f, 0.6f),
            () => SetFromClipboard(v => Main.IntroShotSub.Value = v, "下の文字"));
        Button(options, "SubClear", "下の文字を消す", new Vector3(1.4f, -0.2f, -2f), new Vector2(0.55f, 0.6f),
            () => { Main.IntroShotSub.Value = ""; status.text = "<color=#8cffff>下の文字を消しました</color>"; });
        Button(options, "TitleReset", "タイトルを元に戻す", new Vector3(-1.4f, -0.2f, -2f), new Vector2(0.55f, 0.6f),
            () => { Main.IntroShotTitle.Value = Main.ModName; status.text = "<color=#8cffff>タイトルを元に戻しました</color>"; });

        textColorButton = Button(options, "TextColor", "", new Vector3(-2.3f, -0.85f, -2f), new Vector2(0.5f, 0.6f), () =>
            Main.IntroShotTextColor.Value = NextColor(Main.IntroShotTextColor.Value));
        Button(options, "TextColorClip", "コピーした色を文字に", new Vector3(-0.75f, -0.85f, -2f), new Vector2(0.45f, 0.6f), () => ColorFromClipboard(false));
        backColorButton = Button(options, "BackColor", "", new Vector3(0.75f, -0.85f, -2f), new Vector2(0.45f, 0.6f), () =>
            Main.IntroShotBackColor.Value = NextColor(Main.IntroShotBackColor.Value));
        Button(options, "BackColorClip", "コピーした色を背景に", new Vector3(2.3f, -0.85f, -2f), new Vector2(0.5f, 0.6f), () => ColorFromClipboard(true));

        roleButton = Button(options, "Role", "", new Vector3(0f, -1.45f, -2f), new Vector2(0.9f, 0.6f), () =>
            Main.IntroShotRole.Value = !Main.IntroShotRole.Value);

        status = Label(options, "Status", new Vector3(0f, -1.95f, -1f), 1.3f, 7f);
        var note = Label(options, "Note", new Vector3(-1.2f, -2.4f, -1f), 1.1f, 4.4f);
        note.text = "<color=#a0a0a0>文字はチャット欄などに打ってコピーしてから押してください</color>";
        Button(options, "Close", Translator.GetString("Close"), new Vector3(2.3f, -2.4f, -2f), new Vector2(0.5f, 0.6f), Hide);

        root.SetActive(false);
        return true;
    }

    private static void Refresh()
    {
        if (root == null) return;
        var on = Main.IntroShot.Value;
        enableButton.Text.text = $"イントロ撮影: {(on ? "ON" : "OFF")}";
        enableButton.Background.color = on ? new Color(0f, 0.8f, 0.4f) : Palette.DisabledGrey;
        var sub = Main.IntroShotSub.Value;
        preview.text = $"<color={ColorUtilityHex(TextColor)}>{Main.IntroShotTitle.Value}</color>"
            + (sub == "" ? "" : $"\n<size=55%>{sub}</size>");
        textColorButton.Text.text = $"文字の色: {ColorName(Main.IntroShotTextColor.Value)}";
        textColorButton.Background.color = TextColor;
        backColorButton.Text.text = $"背景の色: {ColorName(Main.IntroShotBackColor.Value)}";
        backColorButton.Background.color = BackColor;
        roleButton.Text.text = $"役職の画面も変える: {(Main.IntroShotRole.Value ? "ON" : "OFF")}";
        roleButton.Background.color = Main.IntroShotRole.Value ? new Color(0f, 0.8f, 0.4f) : Palette.DisabledGrey;
    }

    private static string ColorUtilityHex(Color c)
    {
        Color32 c32 = c;
        return $"#{c32.r:X2}{c32.g:X2}{c32.b:X2}";
    }
}

// イントロが出ている間、毎フレーム文字と色を上書きする(ほかの処理が後から書き換えても負けないように)
[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
static class IntroShotPatch
{
    static void Postfix()
    {
        if (!DebugModeManager.AmDebugger || !Main.IntroShot.Value) return;
        var intro = IntroCutscene.Instance;
        if (intro == null) return;
        try
        {
            var title = Main.IntroShotTitle.Value;
            var sub = Main.IntroShotSub.Value;
            var textColor = IntroShotScreen.TextColor;
            if (intro.TeamTitle != null && intro.TeamTitle.gameObject.activeInHierarchy)
            {
                // 長いタイトルでも画面からはみ出さないように少し小さくする
                var teamText = title.Length > 12 ? $"<size=70%>{title}</size>" : title;
                if (intro.TeamTitle.text != teamText) intro.TeamTitle.text = teamText;
                intro.TeamTitle.color = textColor;
                if (intro.ImpostorText != null)
                {
                    if (intro.ImpostorText.gameObject.activeSelf != (sub != "")) intro.ImpostorText.gameObject.SetActive(sub != "");
                    if (intro.ImpostorText.text != sub) intro.ImpostorText.text = sub;
                }
                if (intro.BackgroundBar != null) intro.BackgroundBar.material.color = IntroShotScreen.BackColor;
            }
            if (Main.IntroShotRole.Value && intro.RoleText != null && intro.RoleText.gameObject.activeInHierarchy)
            {
                if (intro.RoleText.text != title) intro.RoleText.text = title;
                intro.RoleText.color = textColor;
                if (intro.YouAreText != null) intro.YouAreText.color = textColor;
                if (intro.RoleBlurbText != null)
                {
                    if (intro.RoleBlurbText.text != sub) intro.RoleBlurbText.text = sub;
                    intro.RoleBlurbText.color = textColor;
                }
            }
        }
        catch (Exception e) { Logger.Warn($"イントロ撮影の上書きに失敗: {e.Message}", "IntroShot"); }
    }
}
