using System;
using System.Diagnostics;
using System.IO;
using TMPro;
using TownOfHost.Modules.Recorder;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TownOfHost.Modules.ClientOptions;

// TOH-hm オプションの「録画」: 録画の開始・停止と、画質・入れる音の設定
public static class RecorderScreen
{
    private static GameObject root;
    private static TextMeshPro status;
    private static ToggleButtonBehaviour startButton;
    private static ToggleButtonBehaviour qualityButton;
    private static ToggleButtonBehaviour fpsButton;
    private static ToggleButtonBehaviour gameButton;
    private static ToggleButtonBehaviour otherButton;
    private static ToggleButtonBehaviour micButton;
    private static ToggleButtonBehaviour hideButton;

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
        Refresh();
        root.SetActive(true);
    }

    private static ToggleButtonBehaviour Button(OptionsMenuBehaviour options, string name, Vector3 pos, Vector2 scale, Action onClick)
    {
        var b = Object.Instantiate(options.DisableMouseMovement, root.transform);
        b.name = name;
        b.transform.localPosition = pos;
        b.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        b.Text.text = "";
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
        t.overflowMode = TextOverflowModes.Overflow;
        t.rectTransform.sizeDelta = new Vector2(width, 0.4f);
        t.transform.localPosition = pos;
        t.transform.localScale = Vector3.one;
        t.text = "";
        t.gameObject.SetActive(true);
        return t;
    }

    private static int NextVolume(int v)
    {
        var steps = GameRecorder.VolumeSteps;
        var i = Array.IndexOf(steps, v);
        return steps[(i + 1) % steps.Length];
    }

    private static bool Build(OptionsMenuBehaviour options)
    {
        if (options.DisableMouseMovement == null || options.Background == null) return false;
        var parent = ClientActionItem.CustomBackground?.transform?.parent ?? options.transform;
        root = new GameObject("RecorderPopup");
        root.layer = LayerMask.NameToLayer("UI");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = new Vector3(0f, 0f, -20f);

        var bg = Object.Instantiate(options.Background, root.transform);
        bg.name = "Bg";
        bg.transform.localPosition = Vector3.zero;
        bg.transform.localScale = new Vector3(1.3f, 1f, 1f);
        bg.gameObject.SetActive(true);

        var title = Label(options, "Title", new Vector3(0f, 2.35f, -1f), 2.4f, 7f);
        title.text = "録画";

        startButton = Button(options, "Start", new Vector3(0f, 1.75f, -2f), new Vector2(0.7f, 0.7f), GameRecorder.Toggle);
        status = Label(options, "Status", new Vector3(0f, 1.2f, -1f), 1.4f, 7f);

        qualityButton = Button(options, "Quality", new Vector3(-1.4f, 0.65f, -2f), new Vector2(0.55f, 0.6f), () =>
            Main.RecordQuality.Value = (Main.RecordQuality.Value + 1) % GameRecorder.QualityNames.Length);
        fpsButton = Button(options, "Fps", new Vector3(1.4f, 0.65f, -2f), new Vector2(0.55f, 0.6f), () =>
            Main.RecordFps.Value = Main.RecordFps.Value >= 60 ? 30 : 60);

        gameButton = Button(options, "GameVolume", new Vector3(-2.3f, -0.05f, -2f), new Vector2(0.5f, 0.6f), () =>
            Main.RecordGameVolume.Value = NextVolume(Main.RecordGameVolume.Value));
        otherButton = Button(options, "OtherVolume", new Vector3(0f, -0.05f, -2f), new Vector2(0.5f, 0.6f), () =>
            Main.RecordOtherVolume.Value = NextVolume(Main.RecordOtherVolume.Value));
        micButton = Button(options, "MicVolume", new Vector3(2.3f, -0.05f, -2f), new Vector2(0.5f, 0.6f), () =>
            Main.RecordMicVolume.Value = NextVolume(Main.RecordMicVolume.Value));

        hideButton = Button(options, "HideStreamUi", new Vector3(0f, -0.75f, -2f), new Vector2(0.9f, 0.6f), () =>
            Main.RecordHideStreamUi.Value = !Main.RecordHideStreamUi.Value);

        var note = Label(options, "Note", new Vector3(0f, -1.35f, -1f), 1.3f, 7f);
        note.text = "<color=#a0a0a0>Shift+F9でも開始・停止できます / 音・画質は止めている時だけ変えられます</color>";

        var folder = Button(options, "OpenFolder", new Vector3(-1.4f, -2.35f, -2f), new Vector2(0.55f, 0.6f), OpenFolder);
        folder.Text.text = "保存先を開く";
        var close = Button(options, "Close", new Vector3(1.4f, -2.35f, -2f), new Vector2(0.55f, 0.6f), Hide);
        close.Text.text = Translator.GetString("Close");

        root.SetActive(false);
        return true;
    }

    private static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(GameRecorder.Folder);
            Process.Start(new ProcessStartInfo { FileName = GameRecorder.Folder, UseShellExecute = true });
        }
        catch (Exception e) { Logger.Warn($"録画の保存先を開けませんでした: {e.Message}", "Recorder"); }
    }

    private static string Volume(int v) => v <= 0 ? "OFF" : $"{v}%";

    private static void Refresh()
    {
        if (root == null) return;
        var rec = GameRecorder.IsRecording;
        startButton.Text.text = rec ? "録画を止める" : "録画を始める";
        startButton.Background.color = rec ? new Color(1f, 0.3f, 0.3f) : new Color(1f, 0.52f, 0.85f);
        status.text = rec
            ? "<color=#ff6b6b>● 録画中</color>"
            : GameRecorder.LastSavedPath != null ? $"<color=#8cffff>前の録画: {Path.GetFileName(GameRecorder.LastSavedPath)}</color>" : "<color=#a0a0a0>録画していません</color>";

        var quality = Mathf.Clamp(Main.RecordQuality.Value, 0, GameRecorder.QualityNames.Length - 1);
        qualityButton.Text.text = $"画質: {GameRecorder.QualityNames[quality]}";
        fpsButton.Text.text = $"なめらかさ: {(Main.RecordFps.Value >= 60 ? 60 : 30)}fps";
        gameButton.Text.text = $"ゲームの音: {Volume(Main.RecordGameVolume.Value)}";
        otherButton.Text.text = $"PCのほかの音: {Volume(Main.RecordOtherVolume.Value)}";
        micButton.Text.text = $"マイク: {Volume(Main.RecordMicVolume.Value)}";
        hideButton.Text.text = $"配信用の表示を録画に映さない: {(Main.RecordHideStreamUi.Value ? "ON" : "OFF")}";
        hideButton.Background.color = Main.RecordHideStreamUi.Value ? new Color(0f, 0.8f, 0.4f) : Palette.DisabledGrey;

        // 録画中は、途中で変えると困る設定を押せなくする
        foreach (var b in new[] { qualityButton, fpsButton, gameButton, otherButton, micButton })
        {
            b.GetComponent<PassiveButton>().enabled = !rec;
            b.Background.color = rec ? new Color(0.35f, 0.35f, 0.35f) : Palette.DisabledGrey;
        }
    }
}
