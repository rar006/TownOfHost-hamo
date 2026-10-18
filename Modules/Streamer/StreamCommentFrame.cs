using System.Linq;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace TownOfHost;

/// <summary>
/// YouTubeのライブのコメントを、画面左下の枠に表示する(自分の画面だけ)。
/// 配信URLは「Stream」メニューか、ゲーム内の設定(クライアント設定の2ページ目)で設定する。
/// ・枠はマウスでドラッグして好きな場所に動かせる
/// ・右下の「◢」をドラッグすると大きさを変えられる
/// ・右上の「－」で最小化(タイトルだけの細い枠になる)、「＋」で元に戻す
/// 位置・大きさ・最小化はすべて保存される。
/// </summary>
[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
public static class StreamCommentFrame
{
    private const int MaxTextLength = 60;     // 1件の最大文字数(長いコメントは省略)
    private const float DefaultX = 0.35f, DefaultY = 1.25f;
    private const float DefaultWidth = 3.6f, DefaultHeight = 2.3f;
    private const float MinWidth = 2.2f, MaxWidth = 9f;
    private const float MinHeight = 1.1f, MaxHeight = 6f;
    private const float HeaderHeight = 0.32f; // 上のタイトルの帯の高さ
    private const float ButtonSize = 0.34f;   // 「－」「＋」のボタンの大きさ
    private const float GripSize = 0.3f;      // 右下の「◢」の大きさ
    private const float MinimizedWidth = 2.1f;
    private const float LineHeight = 0.23f;   // コメント1行の高さ(表示する件数の計算用)
    // 背景は文字の位置から少し左下にずらして置く
    private const float PadX = 0.1f, PadY = 0.08f;

    private static TextMeshPro text;
    private static TextMeshPro header;
    private static TextMeshPro button;
    private static TextMeshPro grip;
    private static SpriteRenderer background;
    private static AspectPosition aspect;
    private static int shownVersion = -1;
    private static int shownVoteVersion = -1;
    private static bool shownVoteOn;
    private static string shownStatus = "";
    private static int minimizedAtVersion;

    private enum Mode { None, Drag, Resize }
    private static Mode mode;
    private static bool pressedButton;
    private static Vector3 lastMouse;

    private static float W => Main.StreamCommentMinimized.Value ? MinimizedWidth : Mathf.Clamp(Main.StreamCommentWidth.Value, MinWidth, MaxWidth);
    private static float H => Main.StreamCommentMinimized.Value ? HeaderHeight + 0.04f : Mathf.Clamp(Main.StreamCommentHeight.Value, MinHeight, MaxHeight);
    /// <summary>枠の大きさに合わせて、表示するコメントの件数を決める</summary>
    private static int ShowCount => Mathf.Max(1, (int)((H - HeaderHeight - 0.15f) / LineHeight));

    public static void Postfix(HudManager __instance)
    {
        try
        {
            StreamerInfo.LoadSavedURL();
            StreamerInfo.CommentFrameTick(Time.deltaTime);
            var visible = Main.ShowStreamComments.Value && StreamerInfo.HasStream;
            if (!visible)
            {
                if (text != null) text.gameObject.SetActive(false);
                return;
            }
            if (text == null) Build(__instance);
            if (text == null) return;
            if (!text.gameObject.activeSelf) { text.gameObject.SetActive(true); shownVersion = -1; }
            HandleMouse();

            if (Main.StreamCommentMinimized.Value)
            {
                // 最小化中は、新しいコメントが来たらタイトルに印を出すだけ
                var hasNew = StreamerInfo.RecentCommentsVersion != minimizedAtVersion;
                header.text = hasNew ? "<b>YouTube</b> <color=#ffd36e>● 新着</color>" : "<b>YouTube</b>";
                return;
            }
            var voteOn = Modules.YouTubeMapVote.IsOn;
            if (shownVersion == StreamerInfo.RecentCommentsVersion && shownVoteVersion == Modules.YouTubeMapVote.Version && shownVoteOn == voteOn && shownStatus == StreamerInfo.StatusText) return;
            shownStatus = StreamerInfo.StatusText;
            shownVersion = StreamerInfo.RecentCommentsVersion;
            shownVoteVersion = Modules.YouTubeMapVote.Version;
            shownVoteOn = voteOn;
            // マップ投票(便利機能)がONなら、一番上に集計を出す
            text.text = voteOn ? Modules.YouTubeMapVote.Summary() + ((char)10).ToString() + BuildText() : BuildText();
        }
        catch (System.Exception e)
        {
            Logger.Warn($"コメント枠の更新に失敗: {e.Message}", "StreamCommentFrame");
        }
    }

    private static TextMeshPro CreateText(string name, HudManager hud, Transform parent, float size, TextAlignmentOptions align)
    {
        var t = Templates.TMPTemplate.Create(name, "", Color.white, size, align, true, parent);
        var font = hud.TaskPanel?.taskText?.font;
        if (font != null) t.font = font; // 日本語が出るフォント
        t.rectTransform.pivot = new Vector2(0f, 0f);
        t.SetOutlineColor(Color.black);
        t.SetOutlineThickness(0.15f);
        return t;
    }

    private static void Build(HudManager hud)
    {
        text = CreateText("StreamCommentFrame", hud, hud.transform, 1.4f, TextAlignmentOptions.BottomLeft);
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Truncate;

        // 画面の左下に固定する(画面の縦横比が変わっても位置がずれないように)
        aspect = text.gameObject.AddComponent<AspectPosition>();
        aspect.Alignment = AspectPosition.EdgeAlignments.LeftBottom;
        aspect.DistanceFromEdge = new Vector3(Main.StreamCommentPosX.Value, Main.StreamCommentPosY.Value, -20f);
        aspect.AdjustPosition();

        // 半透明の黒い背景
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        var bgObj = new GameObject("StreamCommentFrameBg");
        bgObj.layer = text.gameObject.layer;
        bgObj.transform.SetParent(text.transform, false);
        background = bgObj.AddComponent<SpriteRenderer>();
        background.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0f, 0f), 1f);
        background.color = new Color(0f, 0f, 0f, 0.45f);
        var textRenderer = text.GetComponent<Renderer>();
        if (textRenderer != null) background.sortingOrder = textRenderer.sortingOrder - 1;

        header = CreateText("StreamCommentHeader", hud, text.transform, 1.3f, TextAlignmentOptions.MidlineLeft);
        header.text = "<b>YouTube</b>";
        button = CreateText("StreamCommentMinButton", hud, text.transform, 1.8f, TextAlignmentOptions.Center);
        grip = CreateText("StreamCommentGrip", hud, text.transform, 1.3f, TextAlignmentOptions.BottomRight);
        grip.text = "<color=#bbbbbb>◢</color>";
        Layout();
    }

    /// <summary>今の大きさ・最小化に合わせて、背景・文字・ボタンを置き直す</summary>
    private static void Layout()
    {
        if (text == null) return;
        var w = W;
        var h = H;
        var minimized = Main.StreamCommentMinimized.Value;
        background.transform.localPosition = new Vector3(-PadX, -PadY, 0.1f);
        background.transform.localScale = new Vector3(w, h, 1f);
        // コメント本文はタイトルの帯の下に入れる
        text.rectTransform.sizeDelta = new Vector2(w - 0.2f, Mathf.Max(0.1f, h - HeaderHeight - 0.15f));
        if (minimized) text.text = "";
        var top = h - PadY; // 背景の上の端(文字の位置から見た高さ)
        header.rectTransform.sizeDelta = new Vector2(w - ButtonSize - 0.2f, HeaderHeight);
        header.transform.localPosition = new Vector3(0f, top - HeaderHeight, -0.01f);
        if (!minimized) header.text = "<b>YouTube</b>";
        button.rectTransform.sizeDelta = new Vector2(ButtonSize, ButtonSize);
        button.transform.localPosition = new Vector3(w - PadX - ButtonSize, top - ButtonSize, -0.01f);
        button.text = minimized ? "<b>＋</b>" : "<b>－</b>";
        grip.gameObject.SetActive(!minimized);
        grip.rectTransform.sizeDelta = new Vector2(GripSize, GripSize);
        grip.transform.localPosition = new Vector3(w - PadX - GripSize, -PadY, -0.01f);
        shownVersion = -1; // 件数が変わるので書き直す
    }

    /// <summary>マウスの位置を、枠の左下を(0,0)とした座標にする</summary>
    private static Vector2 ToFrame(Vector3 world)
    {
        var p = text.transform.InverseTransformPoint(world);
        return new Vector2(p.x + PadX, p.y + PadY);
    }

    private static bool InRect(Vector2 p, float x, float y, float w, float h) => p.x >= x && p.x <= x + w && p.y >= y && p.y <= y + h;

    /// <summary>
    /// 左クリックで: 「－」「＋」なら最小化の切り替え、右下の「◢」なら大きさを変える、それ以外の枠の中ならつかんで動かす。
    /// 離した時に位置・大きさを保存する。
    /// </summary>
    private static void HandleMouse()
    {
        var cam = Camera.main;
        if (cam == null || background == null || aspect == null) return;
        var mouse = cam.ScreenToWorldPoint(Input.mousePosition);
        var w = W;
        var h = H;
        if (mode == Mode.None && !pressedButton)
        {
            if (!Input.GetMouseButtonDown(0)) return;
            var p = ToFrame(mouse);
            if (!InRect(p, 0f, 0f, w, h)) return;
            lastMouse = mouse;
            if (InRect(p, w - ButtonSize - 0.05f, h - ButtonSize - 0.05f, ButtonSize + 0.05f, ButtonSize + 0.05f)) { pressedButton = true; return; }
            mode = !Main.StreamCommentMinimized.Value && InRect(p, w - GripSize - 0.05f, 0f, GripSize + 0.05f, GripSize + 0.05f) ? Mode.Resize : Mode.Drag;
            return;
        }
        if (pressedButton)
        {
            if (Input.GetMouseButton(0)) return;
            pressedButton = false;
            // 押したまま枠の外に出て離した時は何もしない
            if (InRect(ToFrame(mouse), w - ButtonSize - 0.05f, h - ButtonSize - 0.05f, ButtonSize + 0.05f, ButtonSize + 0.05f)) ToggleMinimize();
            return;
        }
        var screenH = cam.orthographicSize * 2f;
        var screenW = screenH * cam.aspect;
        if (Input.GetMouseButton(0))
        {
            var d = mouse - lastMouse;
            lastMouse = mouse;
            var pos = aspect.DistanceFromEdge;
            if (mode == Mode.Drag)
            {
                pos += new Vector3(d.x, d.y, 0f);
            }
            else
            {
                // 右下をつかんでいるので、右に動かすと横に広がり、下に動かすと縦に広がる(枠の上の端はそのまま)
                var newW = Mathf.Clamp(w + d.x, MinWidth, Mathf.Min(MaxWidth, screenW - pos.x));
                var newH = Mathf.Clamp(h - d.y, MinHeight, Mathf.Min(MaxHeight, pos.y + h));
                pos.y -= newH - h;
                Main.StreamCommentWidth.Value = newW;
                Main.StreamCommentHeight.Value = newH;
                Layout();
                w = newW;
                h = newH;
            }
            // 画面の外に出ないようにする
            pos.x = Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, screenW - w));
            pos.y = Mathf.Clamp(pos.y, 0f, Mathf.Max(0f, screenH - h));
            aspect.DistanceFromEdge = pos;
            aspect.AdjustPosition();
            return;
        }
        mode = Mode.None;
        Main.StreamCommentPosX.Value = aspect.DistanceFromEdge.x;
        Main.StreamCommentPosY.Value = aspect.DistanceFromEdge.y;
    }

    /// <summary>最小化 ⇔ 元の大きさ。枠の上の端の位置は変えない(上に向かって縮む・伸びる)</summary>
    private static void ToggleMinimize()
    {
        var oldH = H;
        Main.StreamCommentMinimized.Value = !Main.StreamCommentMinimized.Value;
        if (Main.StreamCommentMinimized.Value) minimizedAtVersion = StreamerInfo.RecentCommentsVersion;
        var pos = aspect.DistanceFromEdge;
        pos.y = Mathf.Max(0f, pos.y + oldH - H);
        aspect.DistanceFromEdge = pos;
        aspect.AdjustPosition();
        Main.StreamCommentPosY.Value = pos.y;
        Layout();
    }

    /// <summary>枠の位置・大きさを最初の状態(左下)に戻す(ゲーム内の設定・Streamメニューのボタンから)</summary>
    public static void ResetPosition()
    {
        Main.StreamCommentPosX.Value = DefaultX;
        Main.StreamCommentPosY.Value = DefaultY;
        Main.StreamCommentWidth.Value = DefaultWidth;
        Main.StreamCommentHeight.Value = DefaultHeight;
        Main.StreamCommentMinimized.Value = false;
        if (aspect != null)
        {
            aspect.DistanceFromEdge = new Vector3(DefaultX, DefaultY, -20f);
            aspect.AdjustPosition();
        }
        Layout();
    }

    private static string BuildText()
    {
        (string Name, string Text)[] items;
        var count = ShowCount;
        lock (StreamerInfo.RecentComments)
            items = StreamerInfo.RecentComments.Skip(System.Math.Max(0, StreamerInfo.RecentComments.Count - count)).ToArray();
        if (items.Length == 0) return $"<color=#aaaaaa>{(string.IsNullOrEmpty(StreamerInfo.StatusText) ? "YouTubeのコメントを待っています…" : StreamerInfo.StatusText)}</color>";
        var lf = ((char)10).ToString();
        var sb = new StringBuilder();
        foreach (var (name, body) in items)
        {
            if (sb.Length > 0) sb.Append(lf);
            var t = Clean(body);
            if (t.Length > MaxTextLength) t = t.Substring(0, MaxTextLength) + "…";
            sb.Append("<color=#ffd36e>").Append(Clean(name)).Append("</color>: ").Append(t);
        }
        return sb.ToString();
    }

    /// <summary>タグとして読まれないように &lt; &gt; を全角に、表示できない絵文字を消す</summary>
    private static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsSurrogate(c) || c == (char)0xFE0F || c == (char)0x200D || char.IsControl(c)) continue;
            sb.Append(c == '<' ? '＜' : c == '>' ? '＞' : c);
        }
        return sb.ToString().Trim();
    }
}
