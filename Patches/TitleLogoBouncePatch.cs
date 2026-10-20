using System;
using HarmonyLib;
using TMPro;
using TownOfHost.Templates;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TownOfHost;

// タイトルのロゴを Town / Of / Host / hamo に分けて、1つずつ斜め上から飛んできて2回バウンドさせる。
// hamo だけ着地でズレて、あわてて(やべやべ)元の位置に戻る。
// 演出中にメインメニューのボタン(プレイなど)を押したら、すぐ止めて普通のロゴに戻す。
public static class TitleLogoBounce
{
    private static readonly string[] PartNames = { "town", "of", "host", "hamo" };
    private const float Ppu = 175f;
    private const float DropHeight = 1.6f;
    // 画面が出てくるのを待ってから始める(早すぎると最初の Town が見えない)
    private const float FirstDelay = 0.7f;
    private const float Interval = 0.32f;
    private const float DropTime = 1.1f;
    // それぞれが飛んでくる向き(横のずれ)と、飛んでいる間の傾き
    private static readonly float[] StartX = { -2.6f, -1.4f, 2.6f, -2.2f };
    private static readonly float[] StartAngle = { 25f, 18f, -25f, 20f };
    // hamo の着地後
    private const float PanicTime = 0.75f;
    private const float ReturnTime = 0.45f;
    private static readonly Vector3 HamoMiss = new(0.42f, -0.06f, 0f);
    private const float HamoMissAngle = -11f;

    private static SpriteRenderer logo;
    private static Sprite logoSprite;
    private static readonly SpriteRenderer[] parts = new SpriteRenderer[4];
    private static TextMeshPro panicText;
    private static float time;
    private static bool playing;

    // 起動してから最初にメインメニューを開いた時だけ再生する(試合から戻った時などは出さない)
    private static bool playedThisLaunch;

    public static void Play(SpriteRenderer target)
    {
        Finish();
        if (target == null || playedThisLaunch) return;
        playedThisLaunch = true;
        try
        {
            logo = target;
            logoSprite = target.sprite;
            for (var i = 0; i < parts.Length; i++)
            {
                var sprite = UtilsSprite.LoadSprite($"TownOfHost.Resources.TownOfHost-hamo-logo-{PartNames[i]}.png", Ppu);
                if (sprite == null) { Finish(); return; }
                var go = new GameObject($"titleLogoPart_{PartNames[i]}");
                go.layer = target.gameObject.layer;
                go.transform.SetParent(target.transform, false);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite;
                sr.sortingLayerID = target.sortingLayerID;
                sr.sortingOrder = target.sortingOrder + 1 + i;
                sr.color = new Color(1f, 1f, 1f, 0f);
                parts[i] = sr;
            }
            target.enabled = false;
            time = 0f;
            playing = true;
            Logger.Info("タイトルロゴの演出を開始", "TitleLogoBounce");
            Apply();
        }
        catch (Exception e)
        {
            Logger.Warn($"ロゴの演出を始められませんでした: {e.Message}", "TitleLogoBounce");
            Finish();
        }
    }

    // 演出をやめて、元の1枚のロゴに戻す
    public static void Finish()
    {
        if (playing) Logger.Info($"タイトルロゴの演出を終了 ({time:0.00}秒)", "TitleLogoBounce");
        playing = false;
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i] != null) Object.Destroy(parts[i].gameObject);
            parts[i] = null;
        }
        if (panicText != null) Object.Destroy(panicText.gameObject);
        panicText = null;
        if (logo != null) logo.enabled = true;
    }

    public static bool IsPlaying => playing;

    public static void Update()
    {
        if (!playing) return;
        if (logo == null || logo.sprite != logoSprite)
        {
            Finish();
            return;
        }
        // ロゴが隠れている間(サブ画面など)は進めない。読み込みで1フレームが長くなっても飛ばさない
        if (!logo.gameObject.activeInHierarchy) return;
        time += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
        Apply();
        if (time >= EndTime) Finish();
    }

    private static float StartOf(int i) => FirstDelay + Interval * i;
    private static float EndTime => StartOf(3) + DropTime + PanicTime + ReturnTime;

    private static void Apply()
    {
        for (var i = 0; i < parts.Length; i++)
        {
            var sr = parts[i];
            if (sr == null) continue;
            var t = sr.transform;
            var u = Mathf.Clamp01((time - StartOf(i)) / DropTime);
            var y = TwoBounce(u) * DropHeight;
            var fly = 1f - EaseOut(u);
            var x = StartX[i] * fly;
            var angle = StartAngle[i] * fly;
            sr.color = new Color(1f, 1f, 1f, Mathf.Clamp01(u * 5f));
            // 地面に着いた瞬間だけ少しつぶれる
            var squash = y < 0.05f && u is > 0f and < 1f ? 0.09f * (1f - u) : 0f;
            t.localScale = new Vector3(1f + squash, 1f - squash, 1f);

            if (i < 3)
            {
                t.localPosition = new Vector3(x, y, -0.01f * i);
                t.localRotation = Quaternion.Euler(0f, 0f, angle);
                continue;
            }

            // hamo: 落ちながら横にズレていき、ズレた所に着地する
            var after = time - StartOf(i) - DropTime;
            if (after < 0f)
            {
                var miss = Vector3.Lerp(Vector3.zero, HamoMiss, EaseOut(u));
                t.localPosition = new Vector3(miss.x + x, miss.y + y, -0.03f);
                t.localRotation = Quaternion.Euler(0f, 0f, HamoMissAngle * EaseOut(u) + angle);
                continue;
            }
            if (after < PanicTime)
            {
                // あわててプルプルする(やべやべ)
                var p = after / PanicTime;
                var shake = Mathf.Sin(after * 55f) * (1f - p * 0.4f);
                var hop = Mathf.Abs(Mathf.Sin(after * 18f)) * 0.05f;
                t.localPosition = new Vector3(HamoMiss.x + shake * 0.035f, HamoMiss.y + hop, -0.03f);
                t.localRotation = Quaternion.Euler(0f, 0f, HamoMissAngle + shake * 5f);
                ShowPanic(true, t, after);
                continue;
            }
            // 急いで元の位置に戻る(少し行き過ぎてから止まる)
            var r = Mathf.Clamp01((after - PanicTime) / ReturnTime);
            var back = 1f - EaseOutBack(r);
            t.localPosition = new Vector3(HamoMiss.x * back, HamoMiss.y * back + Mathf.Sin(r * Mathf.PI) * 0.12f, -0.03f);
            t.localRotation = Quaternion.Euler(0f, 0f, HamoMissAngle * back);
            ShowPanic(r < 0.5f, t, after);
        }
    }

    private static void ShowPanic(bool show, Transform hamo, float after)
    {
        if (!show)
        {
            if (panicText != null) panicText.gameObject.SetActive(false);
            return;
        }
        if (panicText == null)
        {
            panicText = TMPTemplate.Create("titleLogoPanic", "!?", new Color32(255, 230, 90, 255), 4f, TextAlignmentOptions.Center, true, logo.transform);
            panicText.fontStyle = FontStyles.Bold;
            panicText.outlineWidth = 0.2f;
            panicText.outlineColor = Color.black;
            panicText.sortingLayerID = logo.sortingLayerID;
            panicText.sortingOrder = logo.sortingOrder + 10;
        }
        panicText.gameObject.SetActive(true);
        var lp = hamo.localPosition;
        panicText.transform.localPosition = new Vector3(lp.x + 2.25f, lp.y - 0.3f + Mathf.Abs(Mathf.Sin(after * 12f)) * 0.08f, -0.1f);
        panicText.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(after * 30f) * 8f);
    }

    private static float EaseOut(float x) => 1f - (1f - x) * (1f - x);

    private static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
    }

    // 高さ(1=飛んできた所, 0=着地)。落ちてから2回はねて止まる
    private const float Bounce1 = 0.3f;
    private const float Bounce2 = 0.09f;
    private static float TwoBounce(float u)
    {
        var b1 = Mathf.Sqrt(Bounce1);
        var b2 = Mathf.Sqrt(Bounce2);
        var total = 1f + 2f * b1 + 2f * b2;
        var t = u * total;
        if (t < 1f) return 1f - t * t;
        t -= 1f;
        if (t < 2f * b1) return Bounce1 - (t - b1) * (t - b1);
        t -= 2f * b1;
        if (t < 2f * b2) return Bounce2 - (t - b2) * (t - b2);
        return 0f;
    }
}

[HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.LateUpdate))]
static class TitleLogoBounceUpdatePatch
{
    public static void Postfix() => TitleLogoBounce.Update();
}

// メインメニューのボタン(プレイ・オンラインなど)を押したら演出を止める。お知らせなどのポップアップのボタンでは止めない
[HarmonyPatch(typeof(PassiveButton), nameof(PassiveButton.ReceiveClickDown))]
static class TitleLogoBounceClickPatch
{
    public static void Prefix(PassiveButton __instance)
    {
        if (!TitleLogoBounce.IsPlaying || __instance == null) return;
        var menu = Object.FindObjectOfType<MainMenuManager>();
        if (menu == null || !__instance.transform.IsChildOf(menu.transform)) return;
        for (var t = __instance.transform; t != null && t != menu.transform; t = t.parent)
        {
            var n = t.name.ToLowerInvariant();
            if (n.Contains("popup") || n.Contains("announcement")) return;
        }
        TitleLogoBounce.Finish();
    }
}
