using System;
using System.Linq;
using TMPro;
using TownOfHost.Templates;
using UnityEngine;

namespace TownOfHost;

/// <summary>
/// プリセット行の「名前」ボタンから開く、画面中央のプリセット名入力フォーム。
/// (以前の「上部のプリセット名編集欄」の代わり)
/// </summary>
public static class PresetNameDialog
{
    private static GameObject root;
    private static SimpleTextBox nameBox;
    private static Sprite whiteSprite;
    private static TextMeshPro previewText;
    private static bool ticking;

    private static Sprite WhiteSprite()
    {
        if (whiteSprite != null) return whiteSprite;
        var tex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return whiteSprite;
    }

    private static SpriteRenderer MakeRect(string name, Transform parent, Vector2 size, Vector3 pos, Color color)
    {
        var obj = new GameObject(name);
        obj.layer = LayerMask.NameToLayer("UI"); // Defaultレイヤーのままだとカメラに映らないことがあるためUIレイヤーにする
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = pos;
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = WhiteSprite();
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = size;
        sr.color = color;
        return sr;
    }

    private static string GetCurrentPresetName()
    {
        var preset = OptionItem.AllOptions.FirstOrDefault(op => op.Id == 0);
        if (preset == null) return "";
        var names = new[]
        {
            Main.Preset1, Main.Preset2, Main.Preset3, Main.Preset4, Main.Preset5, Main.Preset6, Main.Preset7, Main.Preset8,
            Main.Preset9, Main.Preset10, Main.Preset11, Main.Preset12, Main.Preset13, Main.Preset14, Main.Preset15, Main.Preset16,
        };
        var index = preset.CurrentValue;
        return index >= 0 && index < names.Length ? names[index].Value : "";
    }

    private static void SetCurrentPresetName(string value)
    {
        var preset = OptionItem.AllOptions.FirstOrDefault(op => op.Id == 0);
        if (preset == null) return;
        var names = new[]
        {
            Main.Preset1, Main.Preset2, Main.Preset3, Main.Preset4, Main.Preset5, Main.Preset6, Main.Preset7, Main.Preset8,
            Main.Preset9, Main.Preset10, Main.Preset11, Main.Preset12, Main.Preset13, Main.Preset14, Main.Preset15, Main.Preset16,
        };
        var index = preset.CurrentValue;
        if (index >= 0 && index < names.Length) names[index].Value = value;
    }

    public static void Open()
    {
        try
        {
            var menu = GameSettingMenu.Instance;
            if (menu == null) return;
            if (root == null || root.transform.parent != menu.transform)
            {
                Build(menu.transform);
            }
            ApplySorting(menu.transform);
            root.SetActive(true);
            nameBox.SetText(GetCurrentPresetName());
            nameBox.TextBox.GiveFocus();
            Tick();
        }
        catch (Exception e)
        {
            Logger.Warn($"PresetNameDialog.Open: {e.Message}", "PresetNameDialog");
        }
    }

/// <summary>プレビュー用テキストを入力内容に合わせて更新する(フォーカス中は点滅カーソル付き)</summary>
    private static void Tick()
    {
        if (ticking) return;
        ticking = true;
        void Loop()
        {
            _ = new LateTask(() =>
            {
                try
                {
                    if (root == null || !root.activeSelf || previewText == null || nameBox == null)
                    {
                        ticking = false;
                        return;
                    }
                    var caret = nameBox.TextBox != null && nameBox.TextBox.hasFocus && (int)(Time.realtimeSinceStartup * 2f) % 2 == 0;
                    previewText.text = (nameBox.Text ?? "") + (caret ? "|" : "");
                    Loop();
                }
                catch { ticking = false; }
            }, 0.1f, "PresetNameDialogTick", true);
        }
        Loop();
    }

    public static void Close()
    {
        if (root != null) root.SetActive(false);
    }

    private static void Apply()
    {
        var text = nameBox?.Text ?? "";
        if (!string.IsNullOrWhiteSpace(text)) SetCurrentPresetName(text.Trim());
        Close();
    }

    /// <summary>設定画面など他のUIより確実に手前へ描画する(sortingLayerも最前面のものに揃える)</summary>
    private static void ApplySorting(Transform menuTransform)
    {
        var layerId = 0;
        try
        {
            var highest = int.MinValue;
            foreach (var r in menuTransform.root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.transform.IsChildOf(root.transform)) continue;
                var value = SortingLayer.GetLayerValueFromID(r.sortingLayerID);
                if (value > highest) { highest = value; layerId = r.sortingLayerID; }
            }
        }
        catch { }
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            r.sortingLayerID = layerId;
            var n = r.gameObject.name;
            r.sortingOrder = r.GetComponent<TMP_Text>() != null ? 5010
                : n == "DialogDim" ? 5000
                : n == "DialogBorder" ? 5001
                : n == "DialogBackground" ? 5002
                : n == "NameBoxBackground" ? 5003
                : 5004; // ボタンなど
        }
    }

    private static void Build(Transform menuTransform)
    {
        root = new GameObject("PresetNameDialog");
        root.layer = LayerMask.NameToLayer("UI");
        root.transform.SetParent(menuTransform, false);
        // 画面中央(設定画面の中心)に出す
        root.transform.localPosition = new Vector3(0f, 0f, -600f);

        // 画面全体を暗くする板(後ろの設定画面を見えにくくして、フォームを見やすくする)
        MakeRect("DialogDim", root.transform, new Vector2(60f, 40f), new Vector3(0f, 0f, 0.5f), new Color(0f, 0f, 0f, 0.8f));
        // 背景 + 縁(ピンク)
        MakeRect("DialogBorder", root.transform, new Vector2(5.2f, 2.5f), new Vector3(0f, 0f, 0.1f), new Color(1f, 0.45f, 0.75f, 1f));
        MakeRect("DialogBackground", root.transform, new Vector2(5.1f, 2.4f), Vector3.zero, new Color(0f, 0f, 0f, 0.95f));

        var title = TMPTemplate.Create(
            name: "PresetNameDialogTitle",
            text: "プリセット名を変更",
            color: Color.white,
            fontSize: 2.2f,
            alignment: TextAlignmentOptions.Center,
            setActive: true,
            parent: root.transform);
        title.rectTransform.localPosition = new Vector3(0f, 0.8f, -1f);
        title.rectTransform.sizeDelta = new Vector2(4.8f, 0.5f);

        // 入力欄の背景(入力欄の本体は透明のため、暗い板を敷く)
        MakeRect("NameBoxBackground", root.transform, new Vector2(4.2f, 0.5f), new Vector3(0f, 0.05f, -0.5f), new Color(0.12f, 0.12f, 0.12f, 1f));
        nameBox = new SimpleTextBox(
            parent: root.transform,
            name: "PresetNameDialogBox",
            localPosition: new Vector3(-2.0f, 0.05f, -1f),
            width: 4.0f,
            height: 0.4f,
            characterLimit: 30,
            placeholder: "プリセット名",
            onEnter: Apply);

        var ok = new SimpleButton(
            parent: root.transform,
            name: "PresetNameDialogOk",
            localPosition: new Vector3(-1.1f, -0.8f, -1f),
            normalColor: new Color32(60, 150, 90, 255),
            hoverColor: new Color32(80, 190, 115, 255),
            action: Apply,
            label: "決定");
        ok.Scale = new Vector2(1.7f, 0.5f);
        ok.FontSize = 1.8f;
        var cancel = new SimpleButton(
            parent: root.transform,
            name: "PresetNameDialogCancel",
            localPosition: new Vector3(1.1f, -0.8f, -1f),
            normalColor: new Color32(120, 120, 120, 255),
            hoverColor: new Color32(160, 160, 160, 255),
            action: Close,
            label: "キャンセル");
        cancel.Scale = new Vector2(1.7f, 0.5f);
        cancel.FontSize = 1.8f;

        // 入力文字が見えない問題の対策: タイトルと同じ(日本語対応の)フォント・マテリアルを使い、白色で最前面に描画する
        if (nameBox.DisplayText != null)
        {
            nameBox.DisplayText.font = title.font;
            nameBox.DisplayText.fontSharedMaterial = title.fontSharedMaterial;
            nameBox.DisplayText.color = Color.white;
            nameBox.DisplayText.fontSize = 2.2f;
            nameBox.DisplayText.fontSizeMin = nameBox.DisplayText.fontSizeMax = 2.2f;
        }
        // 入力欄の文字が「フォーカスが外れている間だけ」見えない問題の対策:
        // 入力欄本来の表示は隠し、入力内容を毎回コピーして表示する専用のテキストを重ねる。
        if (nameBox.DisplayText != null) nameBox.DisplayText.gameObject.SetActive(false);
        previewText = TMPTemplate.Create(
            name: "PresetNameDialogPreview",
            text: "",
            color: Color.white,
            fontSize: 2.2f,
            alignment: TextAlignmentOptions.Left,
            setActive: true,
            parent: root.transform);
        previewText.rectTransform.localPosition = new Vector3(0f, 0.05f, -2f); // 中心は入力欄の中心(左揃えなので文字は左端から始まる)
        previewText.rectTransform.sizeDelta = new Vector2(3.9f, 0.45f);
        previewText.enableWordWrapping = false;
        ApplySorting(menuTransform);
        root.SetActive(false);
    }
}
