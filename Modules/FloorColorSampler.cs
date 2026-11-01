using System.Collections.Generic;
using UnityEngine;

namespace TownOfHost.Modules;

public static class FloorColorSampler
{
    private const int Size = 16;
    private static readonly string[] SkipLayers = { "Players", "Ghost", "UI", "Shadow", "KillAnimation" };

    public static Color Sample(Vector2 center, float radius = 0.8f)
    {
        var fallback = MapFallback();
        GameObject go = null;
        RenderTexture rt = null;
        Texture2D tex = null;
        var prevActive = RenderTexture.active;
        try
        {
            var mask = ~0;
            foreach (var name in SkipLayers)
            {
                var layer = LayerMask.NameToLayer(name);
                if (layer >= 0) mask &= ~(1 << layer);
            }
            go = new GameObject("FloorColorSampler");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = radius;
            cam.transform.position = new Vector3(center.x, center.y, -100f);
            cam.cullingMask = mask;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            rt = new RenderTexture(Size, Size, 16);
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            cam.targetTexture = null;

            var r = new List<float>();
            var g = new List<float>();
            var b = new List<float>();
            for (var x = 0; x < Size; x++)
                for (var y = 0; y < Size; y++)
                {
                    var c = tex.GetPixel(x, y);
                    if (c.a < 0.9f) continue;
                    r.Add(c.r);
                    g.Add(c.g);
                    b.Add(c.b);
                }
            if (r.Count < Size * Size / 4)
            {
                Logger.Info($"床の色を読めなかったので、マップの目安の色を使います ({r.Count}点)", "FloorColorSampler");
                return fallback;
            }
            var color = new Color(Median(r), Median(g), Median(b), 1f);
            Logger.Info($"床の色: #{ColorUtility.ToHtmlStringRGB(color)} ({r.Count}点)", "FloorColorSampler");
            return color;
        }
        catch (System.Exception e)
        {
            Logger.Warn($"床の色を読めませんでした: {e.Message}", "FloorColorSampler");
            return fallback;
        }
        finally
        {
            RenderTexture.active = prevActive;
            if (tex != null) Object.Destroy(tex);
            if (rt != null) { rt.Release(); Object.Destroy(rt); }
            if (go != null) Object.Destroy(go);
        }
    }

    private static float Median(List<float> values)
    {
        values.Sort();
        return values[values.Count / 2];
    }

    private static Color MapFallback()
    {
        var hex = Main.NormalOptions.MapId switch
        {
            0 or 3 => "#7C8494",
            1 => "#A9B0BA",
            2 => "#6E6863",
            4 => "#7D746A",
            5 => "#5F6A45",
            _ => "#808080",
        };
        return ColorUtility.TryParseHtmlString(hex, out var c) ? c : Color.gray;
    }
}
