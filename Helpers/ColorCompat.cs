using System;
using System.Globalization;
using UnityEngine;

namespace TownOfHost;

/// <summary>
/// UnityEngine.ColorUtility の代替(クラス名は既存の ColorHelper と衝突しないよう ColorCompat)。
/// Android版のゲームでは ColorUtility.TryParseHtmlString などの内部呼び出し(icall)が
/// 削除されており "ICall ... DoTryParseHtmlColor was not resolved" で MOD の読み込みが失敗するため、
/// Android(HAMO_ANDROID)では自前実装を使い、Windowsでは従来どおり ColorUtility を使う。
/// </summary>
public static class ColorCompat
{
    public static bool TryParseHtmlString(string htmlString, out Color color)
    {
#if HAMO_ANDROID
        return TryParseManaged(htmlString, out color);
#else
        return ColorUtility.TryParseHtmlString(htmlString, out color);
#endif
    }

    public static string ToHtmlStringRGB(Color color)
    {
#if HAMO_ANDROID
        return To8(color.r).ToString("X2") + To8(color.g).ToString("X2") + To8(color.b).ToString("X2");
#else
        return ColorUtility.ToHtmlStringRGB(color);
#endif
    }

    public static string ToHtmlStringRGBA(Color color)
    {
#if HAMO_ANDROID
        return ToHtmlStringRGB(color) + To8(color.a).ToString("X2");
#else
        return ColorUtility.ToHtmlStringRGBA(color);
#endif
    }

#if HAMO_ANDROID
    private static byte To8(float v)
    {
        if (float.IsNaN(v)) return 0;
        if (v < 0f) v = 0f;
        if (v > 1f) v = 1f;
        return (byte)Math.Round(v * 255f, MidpointRounding.AwayFromZero);
    }

    private static bool TryParseManaged(string s, out Color color)
    {
        color = default;
        if (string.IsNullOrEmpty(s)) return false;
        s = s.Trim();
        if (s.Length == 0) return false;

        if (s[0] != '#')
        {
            switch (s.ToLowerInvariant())
            {
                case "red": color = new Color(1f, 0f, 0f, 1f); return true;
                case "cyan": case "aqua": color = new Color(0f, 1f, 1f, 1f); return true;
                case "blue": color = new Color(0f, 0f, 1f, 1f); return true;
                case "darkblue": color = new Color32(0, 0, 160, 255); return true;
                case "lightblue": color = new Color32(173, 216, 230, 255); return true;
                case "purple": color = new Color32(128, 0, 128, 255); return true;
                case "yellow": color = new Color(1f, 235f / 255f, 4f / 255f, 1f); return true;
                case "lime": color = new Color(0f, 1f, 0f, 1f); return true;
                case "fuchsia": case "magenta": color = new Color(1f, 0f, 1f, 1f); return true;
                case "white": color = new Color(1f, 1f, 1f, 1f); return true;
                case "silver": color = new Color32(192, 192, 192, 255); return true;
                case "grey": case "gray": color = new Color(0.5f, 0.5f, 0.5f, 1f); return true;
                case "black": color = new Color(0f, 0f, 0f, 1f); return true;
                case "orange": color = new Color32(255, 165, 0, 255); return true;
                case "brown": color = new Color32(165, 42, 42, 255); return true;
                case "maroon": color = new Color32(128, 0, 0, 255); return true;
                case "green": color = new Color32(0, 128, 0, 255); return true;
                case "olive": color = new Color32(128, 128, 0, 255); return true;
                case "navy": color = new Color32(0, 0, 128, 255); return true;
                case "teal": color = new Color32(0, 128, 128, 255); return true;
            }
            return false;
        }

        string hex = s.Substring(1);
        int len = hex.Length;
        if (len != 3 && len != 4 && len != 6 && len != 8) return false;
        foreach (char c in hex)
        {
            if (!Uri.IsHexDigit(c)) return false;
        }

        int r, g, b, a = 255;
        if (len <= 4)
        {
            r = Nibble(hex[0]) * 17;
            g = Nibble(hex[1]) * 17;
            b = Nibble(hex[2]) * 17;
            if (len == 4) a = Nibble(hex[3]) * 17;
        }
        else
        {
            r = Hex2(hex, 0);
            g = Hex2(hex, 2);
            b = Hex2(hex, 4);
            if (len == 8) a = Hex2(hex, 6);
        }
        color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
        return true;
    }

    private static int Nibble(char c) => int.Parse(c.ToString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    private static int Hex2(string s, int i) => int.Parse(s.Substring(i, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
#endif
}
