using System;
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace TownOfHost.Modules;

// ホーム画面で別の画面を開いた時に、いきなり切り替わらず、少し右から滑り込みながら浮かび上がるようにする。
// 画面ごとにボタンの処理を書き換えず、見張っている画面が「見えていない→見えた」になった瞬間に動きを付ける。
// 後ろを暗くする幕(screenTint)も、ぱっと出さずにゆっくり暗くする。
[HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.LateUpdate))]
public static class MenuTransition
{
    private const float Duration = 0.34f;
    private const float SlideX = 0.5f;
    private const float TintDuration = 0.28f;

    private sealed class Anim
    {
        public Transform Target;
        public Vector3 Base;
        public float Start;
        public readonly List<(SpriteRenderer r, float a)> Sprites = new();
        public readonly List<(TMP_Text t, float a)> Texts = new();
    }

    private static readonly Dictionary<int, bool> wasActive = new();
    private static readonly Dictionary<int, Anim> anims = new();
    private static readonly List<int> finished = new();
    private static readonly GameObject[] watched = new GameObject[9];
    private static int lastMenuId;
    private static bool tintWasOn;
    private static float tintStart = -1f;
    private static float tintAlpha = 1f;

    public static void Postfix(MainMenuManager __instance)
    {
        try { Tick(__instance); }
        catch (Exception e)
        {
            Logger.Warn($"画面の切り替えの動きでエラー: {e.Message}", nameof(MenuTransition));
            Reset();
        }
    }

    private static void Tick(MainMenuManager mm)
    {
        if (mm == null) return;
        // ホーム画面が作り直されたら、前の記録は使えない
        var id = mm.GetInstanceID();
        if (id != lastMenuId) { Reset(); lastMenuId = id; }

        var n = 0;
        watched[n++] = mm.onlineButtons != null ? mm.onlineButtons.gameObject : null;
        watched[n++] = mm.gameModeButtons != null ? mm.gameModeButtons.gameObject : null;
        watched[n++] = mm.accountButtons != null ? mm.accountButtons.gameObject : null;
        watched[n++] = mm.creditsScreen != null ? mm.creditsScreen.gameObject : null;
        watched[n++] = MainMenuManagerPatch.StatisticsPanel;
        watched[n++] = MainMenuManagerPatch.VersionMenu;
        watched[n++] = MainMenuManagerPatch.betaVersionMenu;
        watched[n++] = MainMenuManagerPatch.updatea != null ? MainMenuManagerPatch.updatea.gameObject : null;

        var now = Time.unscaledTime;
        for (var i = 0; i < n; i++)
        {
            var go = watched[i];
            if (go == null) continue;
            var key = go.GetInstanceID();
            var active = go.activeInHierarchy;
            if (wasActive.TryGetValue(key, out var before) && !before && active) Begin(go.transform, key, now);
            wasActive[key] = active;
        }

        Animate(now);
        AnimateTint(mm.screenTint, now);
    }

    private static void Begin(Transform t, int key, float now)
    {
        // 外側の画面がもう動いているなら、そちらに任せる(二重に透かすと消えたままになる)
        foreach (var other in anims.Values)
            if (other.Target != null && other.Target != t && t.IsChildOf(other.Target)) return;
        // 内側の画面や、動いている途中の同じ画面は、いったん元どおりにしてから取り直す
        // (途中の透明度を元の値として覚えると、薄いまま残ってしまう)
        finished.Clear();
        foreach (var (k, other) in anims)
            if (other.Target != null && other.Target.IsChildOf(t)) { Apply(other, 1f); if (k != key) finished.Add(k); }
        foreach (var k in finished) anims.Remove(k);
        var anim = anims.TryGetValue(key, out var running) ? running : new Anim { Target = t, Base = t.localPosition };
        anim.Start = now;
        anim.Sprites.Clear();
        anim.Texts.Clear();
        foreach (var r in t.GetComponentsInChildren<SpriteRenderer>(false))
            if (r != null) anim.Sprites.Add((r, r.color.a));
        foreach (var x in t.GetComponentsInChildren<TMP_Text>(false))
            if (x != null) anim.Texts.Add((x, x.color.a));
        anims[key] = anim;
        Apply(anim, 0f);
    }

    private static void Animate(float now)
    {
        if (anims.Count == 0) return;
        finished.Clear();
        foreach (var (key, anim) in anims)
        {
            if (anim.Target == null) { finished.Add(key); continue; }
            var k = Mathf.Clamp01((now - anim.Start) / Duration);
            Apply(anim, EaseOut(k));
            if (k >= 1f) finished.Add(key);
        }
        foreach (var key in finished) anims.Remove(key);
    }

    // e = 0 で消えて右にずれた状態、1 で元どおり。色は透明度だけ変える(マウスを乗せた時の色はそのまま)
    private static void Apply(Anim anim, float e)
    {
        anim.Target.localPosition = anim.Base + new Vector3(SlideX * (1f - e), 0f, 0f);
        foreach (var (r, a) in anim.Sprites)
        {
            if (r == null) continue;
            var c = r.color;
            c.a = a * e;
            r.color = c;
        }
        foreach (var (t, a) in anim.Texts)
        {
            if (t == null) continue;
            var c = t.color;
            c.a = a * e;
            t.color = c;
        }
    }

    private static void AnimateTint(SpriteRenderer tint, float now)
    {
        if (tint == null) return;
        var on = tint.enabled && tint.gameObject.activeInHierarchy;
        if (on && !tintWasOn)
        {
            tintAlpha = tintStart >= 0f ? tintAlpha : tint.color.a;
            tintStart = now;
        }
        tintWasOn = on;
        if (tintStart < 0f) return;
        var k = Mathf.Clamp01((now - tintStart) / TintDuration);
        var c = tint.color;
        c.a = tintAlpha * EaseOut(k);
        tint.color = c;
        if (k >= 1f) tintStart = -1f;
    }

    private static float EaseOut(float k) => 1f - Mathf.Pow(1f - k, 3f);

    private static void Reset()
    {
        foreach (var anim in anims.Values)
            if (anim.Target != null) Apply(anim, 1f);
        anims.Clear();
        wasActive.Clear();
        tintWasOn = false;
        tintStart = -1f;
    }
}
