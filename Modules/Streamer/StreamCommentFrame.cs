using System.Linq;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace TownOfHost;

/// <summary>
/// YouTubeのライブのコメントを、画面左下の枠に表示する(自分の画面だけ)。
/// 配信URLは「Stream」メニューか、ゲーム内の設定(クライアント設定の2ページ目)で設定する。
/// 枠はマウスでドラッグして好きな場所に動かせる(位置は保存される)。
/// </summary>
[HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
public static class StreamCommentFrame
{
    private const int ShowCount = 8;          // 枠に出す件数(新しい順に下から)
    private const float Width = 3.6f;         // 枠の大きさ(ワールド単位)
    private const float Height = 2.3f;
    private const int MaxTextLength = 60;     // 1件の最大文字数(長いコメントは省略)

    private static TextMeshPro text;
    private static SpriteRenderer background;
    private static AspectPosition aspect;
    private static int shownVersion = -1;
    private static int shownVoteVersion = -1;
    private static bool shownVoteOn;
    private static bool dragging;
    private static Vector3 lastMouse;
    private const float DefaultX = 0.35f, DefaultY = 1.25f;

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
            HandleDrag();
            var voteOn = Modules.YouTubeMapVote.IsOn;
            if (shownVersion == StreamerInfo.RecentCommentsVersion && shownVoteVersion == Modules.YouTubeMapVote.Version && shownVoteOn == voteOn) return;
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

    private static void Build(HudManager hud)
    {
        text = Templates.TMPTemplate.Create("StreamCommentFrame", "", Color.white, 1.4f, TextAlignmentOptions.BottomLeft, true, hud.transform);
        var font = hud.TaskPanel?.taskText?.font;
        if (font != null) text.font = font; // 日本語が出るフォント
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Truncate;
        text.rectTransform.pivot = new Vector2(0f, 0f);
        text.rectTransform.sizeDelta = new Vector2(Width - 0.2f, Height - 0.15f);
        text.SetOutlineColor(Color.black);
        text.SetOutlineThickness(0.15f);

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
        bgObj.transform.localPosition = new Vector3(-0.1f, -0.08f, 0.1f);
        bgObj.transform.localScale = new Vector3(Width, Height, 1f);
        var textRenderer = text.GetComponent<Renderer>();
        if (textRenderer != null) background.sortingOrder = textRenderer.sortingOrder - 1;
    }

    /// <summary>枠を左クリックでつかんで動かす。離した時に位置を保存する</summary>
    private static void HandleDrag()
    {
        var cam = Camera.main;
        if (cam == null || background == null || aspect == null) return;
        var mouse = cam.ScreenToWorldPoint(Input.mousePosition);
        if (!dragging)
        {
            if (!Input.GetMouseButtonDown(0)) return;
            var b = background.bounds;
            if (mouse.x < b.min.x || mouse.x > b.max.x || mouse.y < b.min.y || mouse.y > b.max.y) return;
            dragging = true;
            lastMouse = mouse;
            return;
        }
        if (Input.GetMouseButton(0))
        {
            var d = mouse - lastMouse;
            lastMouse = mouse;
            // 画面の外に出ないようにする
            var screenH = cam.orthographicSize * 2f;
            var screenW = screenH * cam.aspect;
            var pos = aspect.DistanceFromEdge + new Vector3(d.x, d.y, 0f);
            pos.x = Mathf.Clamp(pos.x, 0f, Mathf.Max(0f, screenW - Width));
            pos.y = Mathf.Clamp(pos.y, 0f, Mathf.Max(0f, screenH - Height));
            aspect.DistanceFromEdge = pos;
            aspect.AdjustPosition();
            return;
        }
        dragging = false;
        Main.StreamCommentPosX.Value = aspect.DistanceFromEdge.x;
        Main.StreamCommentPosY.Value = aspect.DistanceFromEdge.y;
    }

    /// <summary>枠の位置を最初の場所(左下)に戻す(ゲーム内の設定のボタンから)</summary>
    public static void ResetPosition()
    {
        Main.StreamCommentPosX.Value = DefaultX;
        Main.StreamCommentPosY.Value = DefaultY;
        if (aspect != null)
        {
            aspect.DistanceFromEdge = new Vector3(DefaultX, DefaultY, -20f);
            aspect.AdjustPosition();
        }
    }

    private static string BuildText()
    {
        (string Name, string Text)[] items;
        lock (StreamerInfo.RecentComments)
            items = StreamerInfo.RecentComments.Skip(System.Math.Max(0, StreamerInfo.RecentComments.Count - ShowCount)).ToArray();
        if (items.Length == 0) return "<color=#aaaaaa>YouTubeのコメントを待っています…</color>";
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
