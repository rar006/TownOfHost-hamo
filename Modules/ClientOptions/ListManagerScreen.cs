using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TownOfHost.Modules.ClientOptions;

// TOH-hm オプションの「名簿の管理」: BANリスト・ホワイトリスト・モデレーターを、画面で追加・解除する。
// ファイル(BanList.txt / WhiteList.txt / moderator.txt)を書き換えるだけなので、通信はしない
public static class ListManagerScreen
{
    enum Tab { Ban, White, Moderator }

    private const int Rows = 7;
    private const float RowTop = 1.25f, RowStep = 0.42f;

    private static GameObject root;
    private static TextMeshPro title;
    private static TextMeshPro status;
    private static TextMeshPro pageText;
    private static readonly ToggleButtonBehaviour[] tabButtons = new ToggleButtonBehaviour[3];
    private static ToggleButtonBehaviour roomButton;
    private static readonly TextMeshPro[] rowText = new TextMeshPro[Rows];
    private static readonly ToggleButtonBehaviour[] rowButton = new ToggleButtonBehaviour[Rows];
    private static readonly Action[] rowAction = new Action[Rows];

    private static Tab tab = Tab.Ban;
    private static bool roomMode;
    private static int page;

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
        roomMode = false;
        page = 0;
        status.text = "";
        Refresh();
        root.SetActive(true);
    }

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
        pb.OnClick.AddListener(onClick);
        b.gameObject.SetActive(true);
        return b;
    }

    private static TextMeshPro Label(OptionsMenuBehaviour options, string name, Vector3 pos, float size, TextAlignmentOptions align, float width)
    {
        var t = Object.Instantiate(options.DisableMouseMovement.Text, root.transform);
        t.name = name;
        t.DestroyTranslator();
        t.enableAutoSizing = false;
        t.fontSize = size;
        t.alignment = align;
        t.enableWordWrapping = false;
        t.overflowMode = TextOverflowModes.Ellipsis;
        t.rectTransform.sizeDelta = new Vector2(width, 0.4f);
        // 左寄せの文字は、左の端を pos に合わせる(真ん中を合わせると、長い名前が枠の外にはみ出す)
        t.rectTransform.pivot = new Vector2(align == TextAlignmentOptions.Left ? 0f : 0.5f, 0.5f);
        t.transform.localPosition = pos;
        t.transform.localScale = Vector3.one;
        t.text = "";
        t.gameObject.SetActive(true);
        return t;
    }

    private static bool Build(OptionsMenuBehaviour options)
    {
        if (options.DisableMouseMovement == null || options.Background == null) return false;
        var parent = ClientActionItem.CustomBackground?.transform?.parent ?? options.transform;
        root = new GameObject("ListManagerPopup");
        root.layer = LayerMask.NameToLayer("UI");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = new Vector3(0f, 0f, -20f);

        var bg = Object.Instantiate(options.Background, root.transform);
        bg.name = "Bg";
        bg.transform.localPosition = Vector3.zero;
        bg.transform.localScale = new Vector3(1.3f, 1f, 1f);
        bg.gameObject.SetActive(true);

        title = Label(options, "Title", new Vector3(0f, 2.35f, -1f), 2.4f, TextAlignmentOptions.Center, 7f);
        title.text = "名簿の管理";

        string[] tabNames = { "BANリスト", "ホワイトリスト", "モデレーター" };
        for (var i = 0; i < 3; i++)
        {
            var t = (Tab)i;
            tabButtons[i] = Button(options, $"Tab{i}", tabNames[i], new Vector3(-2.3f + i * 2.3f, 1.85f, -2f), new Vector2(0.5f, 0.6f), () =>
            {
                tab = t;
                roomMode = false;
                page = 0;
                status.text = "";
                Refresh();
            });
        }

        for (var i = 0; i < Rows; i++)
        {
            var index = i;
            var y = RowTop - i * RowStep;
            rowText[i] = Label(options, $"Row{i}", new Vector3(-3.1f, y, -1f), 1.5f, TextAlignmentOptions.Left, 5.1f);
            rowButton[i] = Button(options, $"RowButton{i}", "", new Vector3(2.75f, y, -2f), new Vector2(0.4f, 0.5f), () => rowAction[index]?.Invoke());
        }

        Button(options, "Prev", "<", new Vector3(-0.9f, -1.8f, -2f), new Vector2(0.25f, 0.55f), () => { page--; Refresh(); });
        Button(options, "Next", ">", new Vector3(0.9f, -1.8f, -2f), new Vector2(0.25f, 0.55f), () => { page++; Refresh(); });
        pageText = Label(options, "Page", new Vector3(0f, -1.8f, -1f), 1.5f, TextAlignmentOptions.Center, 1.2f);

        roomButton = Button(options, "Room", "", new Vector3(-2.3f, -2.35f, -2f), new Vector2(0.5f, 0.6f), () =>
        {
            roomMode = !roomMode;
            page = 0;
            status.text = "";
            Refresh();
        });
        Button(options, "Clipboard", "コピーしたFCを追加", new Vector3(0f, -2.35f, -2f), new Vector2(0.5f, 0.6f), AddFromClipboard);
        Button(options, "Close", Translator.GetString("Close"), new Vector3(2.3f, -2.35f, -2f), new Vector2(0.5f, 0.6f), Hide);

        status = Label(options, "Status", new Vector3(0f, -1.35f, -1f), 1.4f, TextAlignmentOptions.Center, 7f);
        root.SetActive(false);
        return true;
    }

    // 1件 = { フレンドコード, PUID, 名前 }
    private static List<string[]> Entries() => tab switch
    {
        Tab.Ban => BanManager.ReadEntries(BanManager.ListKind.Ban),
        Tab.White => BanManager.ReadEntries(BanManager.ListKind.White),
        _ => Moderator.ListEntries(),
    };

    private static bool Has(string fc, string puid)
    {
        foreach (var e in Entries())
            if ((fc != "" && string.Equals(e[0], fc, StringComparison.OrdinalIgnoreCase)) || (puid != "" && string.Equals(e[1], puid, StringComparison.OrdinalIgnoreCase)))
                return true;
        return false;
    }

    private static string TabName => tab switch { Tab.Ban => "BANリスト", Tab.White => "ホワイトリスト", _ => "モデレーター" };

    private static bool Add(string fc, string puid, string name)
    {
        if (tab == Tab.White && BanManager.HasEntry(BanManager.ListKind.Ban, fc, puid))
        {
            status.text = $"<color=#ff8080>{name} はBANリストにいるので、ホワイトリストに入れられません</color>";
            return false;
        }
        var added = tab switch
        {
            Tab.Ban => BanManager.AddEntry(BanManager.ListKind.Ban, fc, puid, name),
            Tab.White => BanManager.AddEntry(BanManager.ListKind.White, fc, puid, name),
            _ => Moderator.AddEntry(fc, puid, name),
        };
        status.text = added
            ? $"<color=#8cffff>{name} を{TabName}に追加しました</color>"
            : $"<color=#ffd080>{name} はもう{TabName}に入っています</color>";
        return added;
    }

    private static void Remove(string fc, string puid, string name)
    {
        var removed = tab switch
        {
            Tab.Ban => BanManager.RemoveEntry(BanManager.ListKind.Ban, fc, puid),
            Tab.White => BanManager.RemoveEntry(BanManager.ListKind.White, fc, puid),
            _ => Moderator.RemoveEntry(fc, puid),
        };
        status.text = removed ? $"<color=#8cffff>{name} を{TabName}から外しました</color>" : $"<color=#ff8080>{name} が見つかりませんでした</color>";
    }

    private static void AddFromClipboard()
    {
        var text = "";
        try { text = GUIUtility.systemCopyBuffer ?? ""; } catch { }
        // フレンドコードは「英字#数字4桁」
        var m = Regex.Match(text, @"[A-Za-z]+[#:]\d{4}");
        if (!m.Success)
        {
            status.text = "<color=#ff8080>コピーした文字にフレンドコード(例: abcdef#1234)がありません</color>";
            return;
        }
        var fc = m.Value.Replace(':', '#').ToLowerInvariant();
        Add(fc, "", fc);
        roomMode = false;
        Refresh();
    }

    // 部屋にいる人(自分以外)。PUID かフレンドコードのどちらかがある人だけ
    private static List<string[]> RoomPlayers()
    {
        var list = new List<string[]>();
        var client = AmongUsClient.Instance;
        if (client?.allClients == null) return list;
        foreach (var c in client.allClients)
        {
            if (c == null || c.Id == client.ClientId) continue;
            var fc = c.FriendCode ?? "";
            var puid = c.ProductUserId ?? "";
            if (fc == "" && puid == "") continue;
            list.Add(new[] { fc, puid, (c.PlayerName ?? "").RemoveHtmlTags() });
        }
        return list;
    }

    private static void Refresh()
    {
        for (var i = 0; i < 3; i++)
            tabButtons[i].Background.color = (int)tab == i ? new Color(1f, 0.52f, 0.85f) : Palette.DisabledGrey;
        title.text = $"名簿の管理 - {TabName}";
        roomButton.Text.text = roomMode ? "名簿にもどる" : "部屋の人から追加";

        var items = roomMode ? RoomPlayers() : Entries();
        var pages = Mathf.Max(1, (items.Count + Rows - 1) / Rows);
        page = Mathf.Clamp(page, 0, pages - 1);
        pageText.text = $"{page + 1}/{pages}";

        for (var i = 0; i < Rows; i++)
        {
            var n = page * Rows + i;
            var on = n < items.Count;
            rowText[i].gameObject.SetActive(on);
            rowButton[i].gameObject.SetActive(on);
            rowAction[i] = null;
            if (!on) continue;
            var e = items[n];
            var fc = e[0];
            var puid = e[1];
            var name = e[2] == "" ? fc : e[2];
            rowText[i].text = $"{name}  <color=#a0a0a0>{(fc == "" ? "(FCなし)" : fc)}</color>";
            if (roomMode)
            {
                var already = Has(fc, puid);
                rowButton[i].Text.text = already ? "追加済み" : "追加";
                if (!already) rowAction[i] = () => { Add(fc, puid, name); Refresh(); };
            }
            else
            {
                rowButton[i].Text.text = "解除";
                rowAction[i] = () => { Remove(fc, puid, name); Refresh(); };
            }
        }
        if (items.Count == 0)
            status.text = status.text != "" ? status.text : roomMode ? "部屋にほかの人がいません" : $"{TabName}にはまだ誰もいません";
    }
}
