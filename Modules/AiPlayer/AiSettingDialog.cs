using System;
using TMPro;
using TownOfHost.Templates;
using UnityEngine;

namespace TownOfHost.Modules.AiPlayer;

// AI の設定(API キー / ローカル AI のモデル名)を、ゲームの中で入力する画面。プリセット名の入力画面と同じ作り。
// 入力したものは TOHhm_DATA/AIPlayer.json に保存する(ゲームの設定には入れないので、部屋の参加者には送られない)。
public static class AiSettingDialog
{
    private static GameObject root;
    private static SimpleTextBox box;
    private static TextMeshPro titleText;
    private static TextMeshPro noteText;
    private static Sprite whiteSprite;
    private static AiProvider provider;
    private static bool forApi => provider != AiProvider.Local;

    private static Sprite WhiteSprite()
    {
        if (whiteSprite != null) return whiteSprite;
        var tex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        whiteSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        return whiteSprite;
    }

    private static void MakeRect(string name, Transform parent, Vector2 size, Vector3 pos, Color color, int order)
    {
        var obj = new GameObject(name) { layer = LayerMask.NameToLayer("UI") };
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = pos;
        var sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = WhiteSprite();
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = size;
        sr.color = color;
        sr.sortingOrder = order;
    }

    // API を使う AI なら API キー、ローカル AI ならモデル名を入力する
    public static void Open(AiProvider target)
    {
        try
        {
            var menu = GameSettingMenu.Instance;
            if (menu == null)
            {
                Utils.SendMessage("設定画面を開いてから、もう一度選んでください。", PlayerControl.LocalPlayer.PlayerId, "<#8cffb4>【AIプレイヤー】</color>");
                return;
            }
            provider = target;
            var api = forApi;
            if (root == null || root.transform.parent != menu.transform || box?.TextBox == null)
            {
                if (root != null) UnityEngine.Object.Destroy(root);
                Build(menu.transform);
            }
            titleText.text = api ? $"{AiPlayers.ProviderName(target)} の API キーを入力" : "ローカルAI(Ollama)のモデル名を入力";
            noteText.text = api
                ? "作った API キーを貼り付けてください。\n配信中は画面に映らないよう注意してください。"
                : "例: qwen2.5:7b(ollama pull で入れたモデルの名前)";
            var cfg = AiConfig.Load();
            box.SetText(api ? "" : cfg.localModel);
            root.SetActive(true);
            SafeFocus.Give(box.TextBox);
        }
        catch (Exception e) { Logger.Warn($"AiSettingDialog.Open: {e.Message}", "AiSettingDialog"); }
    }

    public static void Close()
    {
        if (root != null) root.SetActive(false);
    }

    private static void Apply()
    {
        var text = (box?.Text ?? "").Trim();
        if (text.Length > 0 && !text.StartsWith("/"))
        {
            var cfg = AiConfig.Load();
            switch (provider)
            {
                case AiProvider.Claude: cfg.apiKey = text; break;
                case AiProvider.ChatGpt: cfg.openaiKey = text; break;
                case AiProvider.Gemini: cfg.geminiKey = text; break;
                default: cfg.localModel = text; break;
            }
            AiConfig.Save(cfg);
            Utils.SendMessage(forApi ? "APIキーを保存しました。" : $"ローカルAIのモデルを「{text}」にしました。", PlayerControl.LocalPlayer.PlayerId, "<#8cffb4>【AIプレイヤー】</color>");
        }
        box?.SetText("");
        Close();
    }

    private static void Build(Transform menuTransform)
    {
        root = new GameObject("AiSettingDialog") { layer = LayerMask.NameToLayer("UI") };
        root.transform.SetParent(menuTransform, false);
        root.transform.localPosition = new Vector3(0f, 0f, -600f);
        MakeRect("DialogDim", root.transform, new Vector2(60f, 40f), new Vector3(0f, 0f, 0.5f), new Color(0f, 0f, 0f, 0.8f), 5000);
        MakeRect("DialogBorder", root.transform, new Vector2(6.2f, 2.9f), new Vector3(0f, 0f, 0.1f), new Color(0.55f, 1f, 0.7f, 1f), 5001);
        MakeRect("DialogBackground", root.transform, new Vector2(6.1f, 2.8f), Vector3.zero, new Color(0f, 0f, 0f, 0.95f), 5002);
        titleText = TMPTemplate.Create(name: "AiSettingDialogTitle", text: "", color: Color.white, fontSize: 2.2f,
            alignment: TextAlignmentOptions.Center, setActive: true, parent: root.transform);
        titleText.rectTransform.localPosition = new Vector3(0f, 1.0f, -1f);
        titleText.rectTransform.sizeDelta = new Vector2(5.8f, 0.5f);
        titleText.sortingOrder = 5010;
        noteText = TMPTemplate.Create(name: "AiSettingDialogNote", text: "", color: new Color(0.8f, 0.8f, 0.8f), fontSize: 1.3f,
            alignment: TextAlignmentOptions.Center, setActive: true, parent: root.transform);
        noteText.rectTransform.localPosition = new Vector3(0f, 0.5f, -1f);
        noteText.rectTransform.sizeDelta = new Vector2(5.8f, 0.6f);
        noteText.sortingOrder = 5010;
        MakeRect("BoxBackground", root.transform, new Vector2(5.2f, 0.5f), new Vector3(0f, -0.1f, -0.5f), new Color(0.12f, 0.12f, 0.12f, 1f), 5003);
        box = new SimpleTextBox(parent: root.transform, name: "AiSettingDialogBox", localPosition: new Vector3(-2.5f, -0.1f, -1f),
            width: 5.0f, height: 0.4f, characterLimit: 200, placeholder: "ここに入力(Ctrl+Vで貼り付け)", onEnter: Apply);
        var ok = new SimpleButton(parent: root.transform, name: "AiSettingDialogOk", localPosition: new Vector3(-1.1f, -0.95f, -1f),
            normalColor: new Color32(60, 150, 90, 255), hoverColor: new Color32(80, 190, 115, 255), action: Apply, label: "保存");
        ok.Scale = new Vector2(1.7f, 0.5f);
        ok.FontSize = 1.8f;
        var cancel = new SimpleButton(parent: root.transform, name: "AiSettingDialogCancel", localPosition: new Vector3(1.1f, -0.95f, -1f),
            normalColor: new Color32(120, 120, 120, 255), hoverColor: new Color32(160, 160, 160, 255), action: Close, label: "戻る");
        cancel.Scale = new Vector2(1.7f, 0.5f);
        cancel.FontSize = 1.8f;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (r != null && r.sortingOrder < 5000) r.sortingOrder = 5004;
        root.SetActive(false);
    }
}
