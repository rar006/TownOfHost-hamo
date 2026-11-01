using System;
using System.Linq;
using TMPro;
using TownOfHost.Templates;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TownOfHost.Modules;

// 試合結果を送る Discord の Webhook を、ゲームの中(設定 → MOD の設定)で入力する画面。AI の設定画面と同じ作り。
// 入力したものは options.txt に保存する(今までどおり、ファイルを直接書きかえてもよい)
public static class WebhookSettingDialog
{
    public enum Mode { Url, Tags }

    private static GameObject root;
    private static SimpleTextBox box;
    private static TextMeshPro titleText;
    private static TextMeshPro noteText;
    private static Sprite whiteSprite;
    private static Mode mode;

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

    public static void Open(Mode target)
    {
        try
        {
            var menu = Object.FindObjectOfType<OptionsMenuBehaviour>();
            if (menu == null) return;
            mode = target;
            if (root == null || root.transform.parent != menu.transform || box?.TextBox == null)
            {
                if (root != null) Object.Destroy(root);
                Build(menu.transform);
            }
            ClientOptionsManager.CheckOptions();
            if (mode == Mode.Url)
            {
                titleText.text = "試合結果を送る Discord の Webhook URL";
                noteText.text = "Discord のチャンネル(フォーラム)の設定 → 連携サービス → ウェブフック で作った URL を貼り付けます。\n空にして保存すると送らなくなります。配信中は画面に映らないよう注意してください。";
                box.SetText("");
            }
            else
            {
                titleText.text = "勝った陣営のタグの ID";
                noteText.text = "フォーラムのタグの ID を「クルー,インポスター,ニュートラル」の順にカンマで区切って入れます(分からない所は空でOK)。\nID が無くても、スレッドの名前の頭に【クルー勝利】のように付きます。";
                box.SetText(string.Join(",", ClientOptionsManager.TagCrewmate, ClientOptionsManager.TagImpostor, ClientOptionsManager.TagNeutral));
            }
            root.SetActive(true);
            SafeFocus.Give(box.TextBox);
        }
        catch (Exception e) { Logger.Warn($"WebhookSettingDialog.Open: {e.Message}", "Webhook"); }
    }

    public static void Close()
    {
        if (root != null) root.SetActive(false);
    }

    private static void Apply()
    {
        var text = (box?.Text ?? "").Trim();
        if (mode == Mode.Url)
        {
            if (text.Length == 0)
            {
                ClientOptionsManager.SetValue("WebHookUrl", "none");
                Logger.seeingame("Webhook を消しました。");
            }
            else if (text.StartsWith("https://discord.com/api/webhooks/") || text.StartsWith("https://discordapp.com/api/webhooks/") || text.StartsWith("https://ptb.discord.com/api/webhooks/") || text.StartsWith("https://canary.discord.com/api/webhooks/"))
            {
                ClientOptionsManager.SetValue("WebHookUrl", text);
                if (!Main.UseWebHook.Value) Main.UseWebHook.Value = true;
                Logger.seeingame("Webhook を保存しました。試合が終わると結果を送ります。");
            }
            else
            {
                Logger.seeingame("Discord の Webhook URL(https://discord.com/api/webhooks/ から始まるもの)を入れてください。");
                return;
            }
        }
        else
        {
            var ids = text.Split(',', '、', ' ').Select(s => s.Trim()).ToArray();
            string Id(int i) => i < ids.Length && ids[i].All(char.IsDigit) ? ids[i] : "";
            ClientOptionsManager.SetValue("Tag Crewmate", Id(0));
            ClientOptionsManager.SetValue("Tag Impostor", Id(1));
            ClientOptionsManager.SetValue("Tag Neutral", Id(2));
            Logger.seeingame("勝った陣営のタグを保存しました。");
        }
        box?.SetText("");
        Close();
    }

    private static void Build(Transform menuTransform)
    {
        root = new GameObject("WebhookSettingDialog") { layer = LayerMask.NameToLayer("UI") };
        root.transform.SetParent(menuTransform, false);
        root.transform.localPosition = new Vector3(0f, 0f, -600f);
        MakeRect("DialogDim", root.transform, new Vector2(60f, 40f), new Vector3(0f, 0f, 0.5f), new Color(0f, 0f, 0f, 0.8f), 5000);
        MakeRect("DialogBorder", root.transform, new Vector2(6.6f, 3.1f), new Vector3(0f, 0f, 0.1f), new Color(0.45f, 0.53f, 0.95f, 1f), 5001);
        MakeRect("DialogBackground", root.transform, new Vector2(6.5f, 3.0f), Vector3.zero, new Color(0f, 0f, 0f, 0.95f), 5002);
        titleText = TMPTemplate.Create(name: "WebhookDialogTitle", text: "", color: Color.white, fontSize: 2.2f,
            alignment: TextAlignmentOptions.Center, setActive: true, parent: root.transform);
        titleText.rectTransform.localPosition = new Vector3(0f, 1.1f, -1f);
        titleText.rectTransform.sizeDelta = new Vector2(6.2f, 0.5f);
        titleText.sortingOrder = 5010;
        noteText = TMPTemplate.Create(name: "WebhookDialogNote", text: "", color: new Color(0.8f, 0.8f, 0.8f), fontSize: 1.2f,
            alignment: TextAlignmentOptions.Center, setActive: true, parent: root.transform);
        noteText.rectTransform.localPosition = new Vector3(0f, 0.55f, -1f);
        noteText.rectTransform.sizeDelta = new Vector2(6.2f, 0.7f);
        noteText.enableWordWrapping = true;
        noteText.sortingOrder = 5010;
        MakeRect("BoxBackground", root.transform, new Vector2(5.6f, 0.5f), new Vector3(0f, -0.15f, -0.5f), new Color(0.12f, 0.12f, 0.12f, 1f), 5003);
        box = new SimpleTextBox(parent: root.transform, name: "WebhookDialogBox", localPosition: new Vector3(-2.7f, -0.15f, -1f),
            width: 5.4f, height: 0.4f, characterLimit: 300, placeholder: "ここに入力(Ctrl+Vで貼り付け)", onEnter: Apply);
        var ok = new SimpleButton(parent: root.transform, name: "WebhookDialogOk", localPosition: new Vector3(-1.1f, -1.05f, -1f),
            normalColor: new Color32(60, 150, 90, 255), hoverColor: new Color32(80, 190, 115, 255), action: Apply, label: "保存");
        ok.Scale = new Vector2(1.7f, 0.5f);
        ok.FontSize = 1.8f;
        var cancel = new SimpleButton(parent: root.transform, name: "WebhookDialogCancel", localPosition: new Vector3(1.1f, -1.05f, -1f),
            normalColor: new Color32(120, 120, 120, 255), hoverColor: new Color32(160, 160, 160, 255), action: Close, label: "戻る");
        cancel.Scale = new Vector2(1.7f, 0.5f);
        cancel.FontSize = 1.8f;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (r != null && r.sortingOrder < 5000) r.sortingOrder = 5004;
        root.SetActive(false);
    }
}
