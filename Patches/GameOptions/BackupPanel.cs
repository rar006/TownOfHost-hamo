using System;
using TMPro;
using TownOfHost.Modules;
using TownOfHost.Templates;
using UnityEngine;

namespace TownOfHost;

// ゲーム設定の「設定のバックアップ」の行(プリセットの下)から開く、設定のバックアップの一覧。
// 行を押して選び、「この設定に戻す」で戻す(押し間違えですぐ戻らないように2段階にしている)。
// 1ページ8件・前へ/次へでめくる。ホストだけ使える。
public static class BackupPanel
{
    private const int RowsPerPage = 8;
    private const int Order = 5000; // プリセット名の変更と同じく、設定画面より確実に手前

    private static Transform parent;
    private static GameObject root;
    private static TextMeshPro titleText;
    private static TextMeshPro infoText;
    private static readonly SimpleButton[] rows = new SimpleButton[RowsPerPage];
    private static readonly SpriteRenderer[] rowBgs = new SpriteRenderer[RowsPerPage];
    private static Sprite rectSprite;
    private static SimpleButton prevButton, nextButton, restoreButton, closeButton;
    private static int page;
    private static int selected = -1; // 選んでいる行(一覧全体での番号 0〜)

    public static bool IsOpen => root != null && root.activeSelf;

    // 設定画面ができた時に、バックアップを出す場所(設定画面の上の段と同じ入れ物)を覚える
    public static void SetParent(Transform t)
    {
        var menu = GameSettingMenu.Instance != null ? GameSettingMenu.Instance.transform : t;
        if (parent != menu && root != null) { UnityEngine.Object.Destroy(root); root = null; }
        parent = menu;
    }

    public static void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    public static void Open()
    {
        if (GameSettingMenu.Instance != null) SetParent(GameSettingMenu.Instance.transform);
        if (parent == null) return;
        if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost)
        {
            Utils.SendMessage("設定のバックアップはホストのみ使用できます。", PlayerControl.LocalPlayer?.PlayerId ?? byte.MaxValue);
            return;
        }
        if (root == null) Build();
        if (root == null) return;
        page = 0;
        selected = -1;
        ApplySorting();
        root.SetActive(true);
        Refresh();
    }

    public static void Close()
    {
        if (root != null) root.SetActive(false);
        selected = -1;
    }

    private static void Build()
    {
        try
        {
            root = new GameObject("TOHBackupPanel");
            root.layer = LayerMask.NameToLayer("UI");
            root.transform.SetParent(parent, false);
            // 設定画面の真ん中に出す
            root.transform.localPosition = new Vector3(0f, 0f, -600f);

            // 背景(黒の半透明・ピンクの縁)
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            rectSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            SpriteRenderer MakeRect(string name, Vector2 size, Color color, float z, int order) => Rect(root.transform, name, size, color, new Vector3(0f, 0f, z));
            // 後ろの設定画面を暗くして、一覧を見やすくする
            MakeRect("Dim", new Vector2(60f, 40f), new Color(0f, 0f, 0f, 0.75f), 0.5f, Order - 3);
            MakeRect("Border", new Vector2(6.56f, 5.36f), new Color(1f, 0.55f, 0.8f, 1f), 0.2f, Order - 2);
            MakeRect("Background", new Vector2(6.5f, 5.3f), new Color(0.05f, 0.05f, 0.08f, 0.97f), 0.1f, Order - 1);

            titleText = MakeText("BackupTitle", "<color=#ff8fc8>設定のバックアップ</color>", 2.6f, new Vector3(0f, 2.28f, -1f), new Vector2(6f, 0.5f), TextAlignmentOptions.Center);
            infoText = MakeText("BackupInfo", "", 1.35f, new Vector3(0f, 1.86f, -1f), new Vector2(6.1f, 0.4f), TextAlignmentOptions.Center);
            // 見出しと一覧の区切り線
            Rect(root.transform, "Line", new Vector2(6.1f, 0.025f), new Color(1f, 0.55f, 0.8f, 0.6f), new Vector3(0f, 1.64f, -0.5f));

            for (var i = 0; i < RowsPerPage; i++)
            {
                var index = i;
                rows[i] = MakeButton($"BackupRow{i}", new Vector3(0f, 1.33f - i * 0.47f, -1f), new Vector2(6.1f, 0.41f),
                    new Color32(38, 38, 52, 255), new Color32(66, 66, 92, 255), () => OnRowClicked(index), "", 1.45f, out rowBgs[i]);
                rows[i].Label.alignment = TextAlignmentOptions.MidlineLeft;
                rows[i].Label.rectTransform.sizeDelta = new Vector2(5.8f, 0.41f);
            }

            prevButton = MakeButton("BackupPrev", new Vector3(-2.35f, -2.22f, -1f), new Vector2(1.2f, 0.45f),
                new Color32(60, 90, 130, 255), new Color32(90, 125, 170, 255), () => { page--; selected = -1; Refresh(); }, "< 前へ", 1.6f, out _);
            nextButton = MakeButton("BackupNext", new Vector3(-1.05f, -2.22f, -1f), new Vector2(1.2f, 0.45f),
                new Color32(60, 90, 130, 255), new Color32(90, 125, 170, 255), () => { page++; selected = -1; Refresh(); }, "次へ >", 1.6f, out _);
            restoreButton = MakeButton("BackupRestore", new Vector3(1.75f, -2.22f, -1f), new Vector2(2.6f, 0.45f),
                new Color32(190, 60, 60, 255), new Color32(225, 85, 85, 255), OnRestoreClicked, "この状態に戻す", 1.7f, out _);
            closeButton = MakeButton("BackupClose", new Vector3(2.92f, 2.28f, -1f), new Vector2(0.45f, 0.45f),
                new Color32(150, 40, 40, 255), new Color32(200, 60, 60, 255), Close, "X", 2.2f, out _);

            root.SetActive(false);
        }
        catch (Exception e)
        {
            Logger.Warn($"バックアップの一覧を作れませんでした: {e.Message}", "BackupPanel");
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
        }
    }

    // 設定画面のどのUIよりも手前に描く(sortingLayer を一番手前のものに揃え、マスクで切られないようにする)
    private static void ApplySorting()
    {
        var layerId = 0;
        try
        {
            var highest = int.MinValue;
            foreach (var r in parent.root.GetComponentsInChildren<Renderer>(true))
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
            r.sortingOrder = r.GetComponent<TMP_Text>() != null ? Order + 10
                : n == "Dim" ? Order
                : n == "Border" ? Order + 1
                : n == "Background" ? Order + 2
                : n is "Line" or "BtnBg" ? Order + 3
                : Order + 4;
            if (r is SpriteRenderer sr) sr.maskInteraction = SpriteMaskInteraction.None;
        }
        foreach (var tmp in root.GetComponentsInChildren<TextMeshPro>(true))
        {
            tmp.fontMaterial?.SetFloat("_StencilComp", 8f);
        }
    }

    private static SpriteRenderer Rect(Transform t, string name, Vector2 size, Color color, Vector3 pos)
    {
        var go = new GameObject(name);
        go.layer = root.layer;
        go.transform.SetParent(t, false);
        go.transform.localPosition = pos;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = rectSprite;
        sr.drawMode = SpriteDrawMode.Sliced;
        sr.size = size;
        sr.color = color;
        return sr;
    }

    // ボタンの見た目は自分で描いた板にする(元のボタンの絵は設定画面のマスクで消えてしまうため透明にする)
    private static SimpleButton MakeButton(string name, Vector3 pos, Vector2 size, Color32 normal, Color32 hover, Action action, string label, float fontSize, out SpriteRenderer bg)
    {
        var b = new SimpleButton(root.transform, name, pos, new Color32(0, 0, 0, 0), new Color32(0, 0, 0, 0), action, label);
        b.Scale = size;
        b.FontSize = fontSize;
        var plate = Rect(b.Button.transform, "BtnBg", size, normal, new Vector3(0f, 0f, 0.05f));
        b.Button.OnMouseOver.AddListener((Action)(() => { if (plate != null && !IsSelectedPlate(plate)) plate.color = hover; }));
        b.Button.OnMouseOut.AddListener((Action)(() => { if (plate != null && !IsSelectedPlate(plate)) plate.color = normal; }));
        plateNormal[plate.GetInstanceID()] = normal;
        bg = plate;
        return b;
    }

    private static readonly System.Collections.Generic.Dictionary<int, Color32> plateNormal = new();
    private static readonly Color32 SelectedColor = new(120, 92, 30, 255);
    private static bool IsSelectedPlate(SpriteRenderer plate)
    {
        var row = Array.IndexOf(rowBgs, plate);
        return row >= 0 && page * RowsPerPage + row == selected;
    }

    private static TextMeshPro MakeText(string name, string text, float size, Vector3 pos, Vector2 box, TextAlignmentOptions align)
    {
        var tmp = TMPTemplate.Create(name, text, Color.white, size, align, true, root.transform);
        tmp.transform.localPosition = pos;
        tmp.rectTransform.sizeDelta = box;
        tmp.enableWordWrapping = false;
        return tmp;
    }

    private static void Refresh()
    {
        if (root == null) return;
        var count = OptionBackup.Count;
        var pages = Math.Max(1, (count + RowsPerPage - 1) / RowsPerPage);
        page = Math.Clamp(page, 0, pages - 1);
        if (count == 0)
            infoText.text = OptionBackup.IsEnabled ? "バックアップはまだありません。設定を変更すると、変更前の状態が自動で保存されます" : "自動バックアップはOFFです(OTHER2の「設定の自動バックアップ」でONにできます)";
        else if (selected < 0)
            infoText.text = $"全{count}件(新しい順)　復元したいバックアップを選んでください　<color=#aaaaaa>{page + 1}/{pages}ページ</color>";
        else
            infoText.text = $"<color=#ffd36e>{selected + 1}番を選択中　「この状態に戻す」を押すと復元します</color>　<color=#aaaaaa>{page + 1}/{pages}ページ</color>";
        for (var i = 0; i < RowsPerPage; i++)
        {
            var index = page * RowsPerPage + i;
            var b = rows[i];
            var show = index < count;
            b.Button.gameObject.SetActive(show);
            if (!show) continue;
            b.Label.text = (index == selected ? "<color=#ffd36e>▶</color> " : "") + $"<color=#ff8fc8>{index + 1}.</color> {OptionBackup.EntryText(index)}";
            if (rowBgs[i] != null && plateNormal.TryGetValue(rowBgs[i].GetInstanceID(), out var normal))
                rowBgs[i].color = index == selected ? SelectedColor : normal;
        }
        prevButton.Button.gameObject.SetActive(page > 0);
        nextButton.Button.gameObject.SetActive(page < pages - 1);
        restoreButton.Button.gameObject.SetActive(selected >= 0 && selected < count);
    }

    private static void OnRowClicked(int row)
    {
        var index = page * RowsPerPage + row;
        selected = selected == index ? -1 : index; // もう一度押すと選ぶのをやめる
        Refresh();
    }

    private static void OnRestoreClicked()
    {
        if (selected < 0) return;
        OptionBackup.Restore(selected + 1);
        // 戻す前の状態が1番に入るので、一覧の1ページ目を見せる
        selected = -1;
        page = 0;
        Refresh();
    }
}
