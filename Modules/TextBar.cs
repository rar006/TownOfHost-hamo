using System.Collections.Generic;
using UnityEngine;

namespace TownOfHost.Modules;

// ダミー(CustomNetObject)の名前の文字で、どの角度にも回せる「棒」を描くための道具。
// ゲームに入っている、ふち(黒い外枠)の無いフォントの横棒の文字を、大きくして回す。MOD を入れていない人にも同じに見える。
// 大きさやずれは、ホストの画面で実際の文字を測って決める。
public static class TextBar
{
    private static readonly string[] Fonts = { "Barlow-Black SDF", "Barlow-ExtraBold SDF", "Barlow-Bold SDF", "Barlow-Regular SDF" };
    // 実際の画面(録画)で測った補正。棒は計算より 1.4 倍くらい長く見え、太さの 1/8 くらい下に出ていた
    private const float LengthFix = 1.4f;
    private const float RaisePerThickness = 0.12f;

    private static bool measured;
    // 文字 1 文字(1em)が、100% の時にワールドでどれくらいの長さか
    private static float emAt100 = 0.162f;
    // 名前の文字の行の真ん中が、ダミーの体からどれだけずれているか
    private static Vector2 nameOffset = new(0.01f, 0.56f);
    private static string font;
    // 文字ごとの形(1em あたりの幅・高さ、行の真ん中からの文字の真ん中のずれ)
    private static readonly Dictionary<char, (float w, float h, Vector2 center)> shapes = new();

    public static bool Ready => font != null;

    // ホストの画面で 1 回だけ測る。ダミーができた時に呼ぶ
    public static void Measure(PlayerControl dummy, params char[] chars)
    {
        if (measured || dummy == null) return;
        measured = true;
        try
        {
            var text = dummy.cosmetics.nameText;
            var scale = Mathf.Abs(text.transform.lossyScale.x);
            var width = text.GetPreferredValues("<mspace=1em>■■■■■■■■■■").x * scale;
            if (width > 0.01f) emAt100 = width / 10f;
            nameOffset = (Vector2)text.transform.position - (Vector2)dummy.transform.position;
        }
        catch (System.Exception e) { Logger.Warn($"文字の大きさを測れませんでした: {e.Message}", "TextBar"); }
        FindFont(chars);
    }

    private static void FindFont(char[] chars)
    {
        var basePath = "Fonts & Materials/";
        try
        {
            var path = TMPro.TMP_Settings.defaultFontAssetPath;
            if (!string.IsNullOrEmpty(path)) basePath = path;
        }
        catch { }
        foreach (var name in Fonts)
        {
            try
            {
                var asset = Resources.Load(basePath + name, Il2CppInterop.Runtime.Il2CppType.Of<TMPro.TMP_FontAsset>())?.TryCast<TMPro.TMP_FontAsset>();
                if (asset == null) continue;
                var mat = asset.material;
                if (mat != null && mat.HasProperty("_OutlineWidth") && mat.GetFloat("_OutlineWidth") > 0.01f) continue;
                var face = asset.faceInfo;
                var unit = face.scale / face.pointSize;
                var lineCenter = (face.ascentLine + face.descentLine) * 0.5f * unit;
                shapes.Clear();
                foreach (var c in chars)
                {
                    if (!asset.characterLookupTable.TryGetValue(c, out var ch) || ch?.glyph == null) continue;
                    var m = ch.glyph.metrics;
                    if (m.width <= 0f || m.height <= 0f) continue;
                    var center = new Vector2(
                        (m.horizontalBearingX + m.width * 0.5f - m.horizontalAdvance * 0.5f) * unit,
                        (m.horizontalBearingY - m.height * 0.5f) * unit - lineCenter);
                    shapes[c] = (m.width * unit, m.height * unit, center);
                }
                if (shapes.Count == 0) continue;
                font = name;
                Logger.Info($"棒の文字: {name} 1em={emAt100:F3} 名前のずれ={nameOffset} 使える文字={string.Concat(shapes.Keys)}", "TextBar");
                return;
            }
            catch (System.Exception e) { Logger.Warn($"{name} を使えませんでした: {e.Message}", "TextBar"); }
        }
        Logger.Info("ふちの無いフォントが見つかりませんでした", "TextBar");
    }

    public static bool Has(char c) => Ready && shapes.ContainsKey(c);

    // 太さ thickness の棒の文字を作る。length に棒の長さ、offset に「ダミーの位置 = 棒の真ん中 - offset」のずれを返す
    public static string Build(char c, float thickness, float angle, string color, out float length, out Vector2 offset)
    {
        var (w, h, center) = shapes[c];
        var size = Mathf.Clamp(Mathf.RoundToInt(thickness / h / emAt100 * 100f), 20, 20000);
        var em = emAt100 * size / 100f;
        length = w * em * LengthFix;
        offset = nameOffset + center * em - new Vector2(0f, thickness * RaisePerThickness);
        var rot = Mathf.RoundToInt(Mathf.DeltaAngle(0f, angle));
        return $"<font=\"{font}\"><size={size}%><rotate={rot}><{color}>{c}</color></rotate></size></font>";
    }
}
