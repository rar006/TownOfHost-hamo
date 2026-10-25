using System.Linq;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace TownOfHost;

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
    private static TextMeshPro lockButton;
    // ロビーのボタンや部屋の情報の枠より手前に出す
    private const int FrontOrder = 500;
    private const float FrontZ = -150f;
    private static TextMeshPro grip;
    private static SpriteRenderer background;
    private static Modules.UiSkin.Panel panel;
    // 配信の上に重ねるので、ほかの枠より少し透かす
    private static readonly Color BackgroundColor = new(0.035f, 0.05f, 0.085f, 0.62f);
    private const string LiveMark = "<color=#ff4d4d>●</color> ";
    private static string HeaderTitle => LiveMark + Modules.UiSkin.Title("YouTube") + $" <size=75%><color={Modules.UiSkin.DimHex}>LIVE CHAT</color></size>";
    private static AspectPosition aspect;
    private static int shownVersion = -1;
    private static int shownVoteVersion = -1;
    private static bool shownVoteOn;
    private static string shownStatus = "";
    private static int minimizedAtVersion;

    private enum Mode { None, Drag, Resize }
    private static Mode mode;
    private static bool pressedButton;
    private static bool pressedLock;
    private static Vector3 lastMouse;

    private static float W => Main.StreamCommentMinimized.Value ? MinimizedWidth : Mathf.Clamp(Main.StreamCommentWidth.Value, MinWidth, MaxWidth);
    private static float H => Main.StreamCommentMinimized.Value ? HeaderHeight + 0.04f : Mathf.Clamp(Main.StreamCommentHeight.Value, MinHeight, MaxHeight);
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
                header.text = hasNew ? LiveMark + Modules.UiSkin.Title("YouTube") + " <color=#ffd36e>● 新着</color>" : HeaderTitle;
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
        aspect.DistanceFromEdge = new Vector3(Main.StreamCommentPosX.Value, Main.StreamCommentPosY.Value, FrontZ);
        aspect.AdjustPosition();

        // 角の丸い、半透明の暗い枠(ほかの枠と同じ見た目)
        panel = new Modules.UiSkin.Panel("StreamCommentFrame", text.transform, BackgroundColor);
        background = panel.Background;
        var textRenderer = text.GetComponent<Renderer>();
        if (textRenderer != null) panel.SetOrder(textRenderer.sortingOrder - 3);

        header = CreateText("StreamCommentHeader", hud, text.transform, 1.3f, TextAlignmentOptions.MidlineLeft);
        header.text = HeaderTitle;
        button = CreateText("StreamCommentMinButton", hud, text.transform, 1.8f, TextAlignmentOptions.Center);
        grip = CreateText("StreamCommentGrip", hud, text.transform, 1.3f, TextAlignmentOptions.BottomRight);
        grip.text = $"<color={Modules.UiSkin.AccentHex}99>◢</color>";
        lockButton = CreateText("StreamCommentLockButton", hud, text.transform, 1.1f, TextAlignmentOptions.Center);
        // 全部の部品を、ほかの UI より手前に描く(重なり方は枠の中で今まで通り)
        foreach (var r in text.GetComponentsInChildren<Renderer>(true))
            if (r != null) r.sortingOrder += FrontOrder;
        Layout();
    }

    // 「固定」ボタンの場所(最小化ボタンの左)
    private const float LockWidth = 0.5f;
    private static bool InLock(Vector2 p, float w, float h) => InRect(p, w - ButtonSize - LockWidth - 0.08f, h - ButtonSize - 0.05f, LockWidth + 0.05f, ButtonSize + 0.05f);

    private static void Layout()
    {
        if (text == null) return;
        var w = W;
        var h = H;
        var minimized = Main.StreamCommentMinimized.Value;
        panel.Layout(-PadX, -PadY, w, h, HeaderHeight);
        // コメント本文はタイトルの帯の下に入れる
        text.rectTransform.sizeDelta = new Vector2(w - 0.2f, Mathf.Max(0.1f, h - HeaderHeight - 0.15f));
        if (minimized) text.text = "";
        var top = h - PadY; // 背景の上の端(文字の位置から見た高さ)
        // 帯の左の印の分だけ、見出しを右へずらす
        header.rectTransform.sizeDelta = new Vector2(w - ButtonSize - LockWidth - 0.3f, HeaderHeight);
        header.transform.localPosition = new Vector3(0.08f, top - HeaderHeight, -0.01f);
        if (!minimized) header.text = HeaderTitle;
        button.rectTransform.sizeDelta = new Vector2(ButtonSize, ButtonSize);
        button.transform.localPosition = new Vector3(w - PadX - ButtonSize, top - ButtonSize, -0.01f);
        button.text = $"<b><color={Modules.UiSkin.ButtonHex}>{(minimized ? "＋" : "－")}</color></b>";
        lockButton.rectTransform.sizeDelta = new Vector2(LockWidth, ButtonSize);
        lockButton.transform.localPosition = new Vector3(w - PadX - ButtonSize - LockWidth - 0.03f, top - ButtonSize, -0.01f);
        lockButton.text = Main.StreamCommentLocked.Value
            ? $"<b><color=#ffd36e>固定中</color></b>"
            : $"<color={Modules.UiSkin.DimHex}>固定</color>";
        grip.gameObject.SetActive(!minimized && !Main.StreamCommentLocked.Value);
        grip.rectTransform.sizeDelta = new Vector2(GripSize, GripSize);
        grip.transform.localPosition = new Vector3(w - PadX - GripSize, -PadY, -0.01f);
        shownVersion = -1; // 件数が変わるので書き直す
    }

    private static Vector2 ToFrame(Vector3 world)
    {
        var p = text.transform.InverseTransformPoint(world);
        return new Vector2(p.x + PadX, p.y + PadY);
    }

    private static bool InRect(Vector2 p, float x, float y, float w, float h) => p.x >= x && p.x <= x + w && p.y >= y && p.y <= y + h;

    private static void HandleMouse()
    {
        var cam = Camera.main;
        if (cam == null || background == null || aspect == null) return;
        var mouse = cam.ScreenToWorldPoint(Input.mousePosition);
        var w = W;
        var h = H;
        if (mode == Mode.None && !pressedButton && !pressedLock)
        {
            if (!Input.GetMouseButtonDown(0)) return;
            var p = ToFrame(mouse);
            if (!InRect(p, 0f, 0f, w, h)) return;
            lastMouse = mouse;
            if (InRect(p, w - ButtonSize - 0.05f, h - ButtonSize - 0.05f, ButtonSize + 0.05f, ButtonSize + 0.05f)) { pressedButton = true; return; }
            if (InLock(p, w, h)) { pressedLock = true; return; }
            // 固定中は、動かしたり大きさを変えたりしない
            if (Main.StreamCommentLocked.Value) return;
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
        if (pressedLock)
        {
            if (Input.GetMouseButton(0)) return;
            pressedLock = false;
            if (InLock(ToFrame(mouse), w, h))
            {
                Main.StreamCommentLocked.Value = !Main.StreamCommentLocked.Value;
                Layout();
            }
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

    public static void ResetPosition()
    {
        Main.StreamCommentPosX.Value = DefaultX;
        Main.StreamCommentPosY.Value = DefaultY;
        Main.StreamCommentWidth.Value = DefaultWidth;
        Main.StreamCommentHeight.Value = DefaultHeight;
        Main.StreamCommentMinimized.Value = false;
        if (aspect != null)
        {
            aspect.DistanceFromEdge = new Vector3(DefaultX, DefaultY, FrontZ);
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
            sb.Append("<color=#8fe3ff>").Append(Clean(name)).Append($"</color><color={Modules.UiSkin.DimHex}> : </color>").Append(t);
        }
        return sb.ToString();
    }

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
