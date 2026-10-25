using UnityEngine;

namespace TownOfHost.Modules;

// 画面に出す枠(通信モニター・デバッグの枠・YouTubeのコメント枠)の共通の見た目。
// 角の丸い暗いパネルに、水色の細いふちと、見出しの帯の下の光る線を付ける。
// 角の丸みは 9 分割(Sliced)の絵なので、枠の大きさを変えても角はつぶれない。
public static class UiSkin
{
    public static readonly Color PanelColor = new(0.035f, 0.05f, 0.085f, 0.88f);
    public static readonly Color HeaderColor = new(0.075f, 0.105f, 0.17f, 0.97f);
    public static readonly Color BorderColor = new(0.3f, 0.8f, 1f, 0.5f);
    public static readonly Color AccentColor = new(0.3f, 0.85f, 1f, 1f);
    public static readonly Color AccentLineColor = new(0.3f, 0.85f, 1f, 0.75f);
    public const string AccentHex = "#4dd9ff";
    public const string DimHex = "#7f8ea3";
    public const string ButtonHex = "#9fb3c8";

    private const int TexSize = 32;
    private const int Radius = 10;
    private const float PixelsPerUnit = 110f;
    // 9 分割の絵は、角(Radius+1 ピクセル)の 2 つ分より小さくできない
    private const float MinSliced = (Radius + 1) * 2f / PixelsPerUnit;

    private static Sprite rounded, frame, solid;

    public static Sprite Rounded { get { if (rounded == null) rounded = MakeRounded(false); return rounded; } }
    public static Sprite Frame { get { if (frame == null) frame = MakeRounded(true); return frame; } }
    public static Sprite Solid { get { if (solid == null) solid = MakeSolid(); return solid; } }

    public static string Title(string title) => $"<b><cspace=0.03em>{title}</cspace></b>";

    // 角の丸い四角(ring が true なら、ふちだけ)
    private static Sprite MakeRounded(bool ring)
    {
        var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        for (var y = 0; y < TexSize; y++)
            for (var x = 0; x < TexSize; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                var core = new Vector2(Mathf.Clamp(p.x, Radius, TexSize - Radius), Mathf.Clamp(p.y, Radius, TexSize - Radius));
                var outside = Vector2.Distance(p, core) - Radius;
                var a = Mathf.Clamp01(0.5f - outside);
                if (ring) a -= Mathf.Clamp01(0.5f - (outside + 1.6f));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        var b = Radius + 1;
        var sprite = Sprite.Create(tex, new Rect(0, 0, TexSize, TexSize), Vector2.zero, PixelsPerUnit, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static Sprite MakeSolid()
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        tex.hideFlags = HideFlags.HideAndDontSave;
        var sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero, 1f);
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    public static SpriteRenderer Rect(string name, Transform parent, Sprite sprite, Color color, float z)
    {
        var go = new GameObject(name);
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 0f, z);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        if (sprite != Solid) sr.drawMode = SpriteDrawMode.Sliced;
        return sr;
    }

    // 左下を (x, y) にして、幅 w・高さ h に置く
    public static void Place(SpriteRenderer sr, float x, float y, float w, float h)
    {
        if (sr == null) return;
        sr.transform.localPosition = new Vector3(x, y, sr.transform.localPosition.z);
        if (sr.drawMode == SpriteDrawMode.Sliced)
        {
            sr.transform.localScale = Vector3.one;
            sr.size = new Vector2(Mathf.Max(w, MinSliced), Mathf.Max(h, MinSliced));
        }
        else sr.transform.localScale = new Vector3(w, h, 1f);
    }

    // 枠ひとそろい(背景・見出しの帯・ふち・光る線・帯の左の印)
    public sealed class Panel
    {
        public SpriteRenderer Background, Header, HeaderFill, Border, Line, Pill;

        public Panel(string name, Transform parent, Color background)
        {
            Background = Rect(name + "Bg", parent, Rounded, background, 0.1f);
            Header = Rect(name + "Header", parent, Rounded, HeaderColor, 0.07f);
            HeaderFill = Rect(name + "HeaderFill", parent, Solid, HeaderColor, 0.07f);
            Border = Rect(name + "Border", parent, Frame, BorderColor, 0.05f);
            Line = Rect(name + "Line", parent, Solid, AccentLineColor, 0.05f);
            Pill = Rect(name + "Pill", parent, Solid, AccentColor, 0.05f);
        }

        // 左下 (x, y) から幅 w・高さ h の枠。見出しの帯の高さは headerHeight
        public void Layout(float x, float y, float w, float h, float headerHeight)
        {
            Place(Background, x, y, w, h);
            Place(Border, x, y, w, h);
            var top = y + h;
            Place(Header, x, top - headerHeight, w, headerHeight);
            // 帯の下側の角は丸めない(本文とつながって見えるように)
            Place(HeaderFill, x, top - headerHeight, w, headerHeight * 0.5f);
            var showLine = h > headerHeight + 0.08f;
            Line.gameObject.SetActive(showLine);
            Place(Line, x + 0.03f, top - headerHeight - 0.012f, Mathf.Max(0.01f, w - 0.06f), 0.012f);
            Place(Pill, x + 0.07f, top - headerHeight + 0.08f, 0.035f, Mathf.Max(0.02f, headerHeight - 0.16f));
        }

        // 背景を一番後ろ、その上に帯、ふちと線、文字の順で描く
        public void SetOrder(int baseOrder)
        {
            Background.sortingOrder = baseOrder;
            Header.sortingOrder = baseOrder + 1;
            HeaderFill.sortingOrder = baseOrder + 1;
            Border.sortingOrder = baseOrder + 2;
            Line.sortingOrder = baseOrder + 2;
            Pill.sortingOrder = baseOrder + 2;
        }
    }
}
