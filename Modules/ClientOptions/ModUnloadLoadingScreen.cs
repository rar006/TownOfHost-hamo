using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using TMPro;
using TownOfHost.Patches;
using TownOfHost.Templates;
using UnityEngine;

namespace TownOfHost.Modules.ClientOptions;

/// <summary>
/// 「Modを無効化」を押した後に表示するロード画面。
/// 起動時のhamoロード画面(HamoLoadingScreenPatch)と同じ見た目(ロゴ・進捗バー・泡・ステータス文字)で、
/// 無効化処理を段階的に進めながら進捗を見せる。
/// </summary>
public static class ModUnloadLoadingScreen
{
    private static bool running;
    public static bool IsRunning => running;

    // 描画順。UIより手前に出すため大きめの値を使う。
    private const int OrderOverlay = 30000;
    private const int OrderBarBg = 30001;
    private const int OrderBarFill = 30002;
    private const int OrderBubble = 30003;
    private const int OrderLogo = 30004;
    private const int OrderText = 30010;

    private const float MinStepSeconds = 0.45f;
    private const float MaxGcWaitSeconds = 5f;

    /// <summary>ロード画面を出しながらMODを無効化する。</summary>
    public static void Begin()
    {
        if (running) return;

        var host = AmongUsClient.Instance;
        if (host == null)
        {
            // コルーチンを動かす土台が無い場合は従来通り即時に無効化する
            ModUnloaderScreen.Unload();
            return;
        }

        running = true;
        host.StartCoroutine(CoUnloadWithLoadingScreen().WrapToIl2Cpp());
    }

    private static bool IsJapanese()
    {
        try
        {
            if (Main.ForceJapanese != null && Main.ForceJapanese.Value) return true;
            return TranslationController.InstanceExists
                && TranslationController.Instance.currentLanguage.languageID == SupportedLangs.Japanese;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>指定レイヤーを描画しているカメラのうち、最も手前のものを返す。</summary>
    private static Camera FindCamera(int layer)
    {
        Camera best = null;
        try
        {
            foreach (var cam in Camera.allCameras)
            {
                if (cam == null || !cam.enabled) continue;
                if ((cam.cullingMask & (1 << layer)) == 0) continue;
                if (best == null || cam.depth > best.depth) best = cam;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"カメラの検索に失敗しました: {ex.Message}", nameof(ModUnloadLoadingScreen));
        }
        return best != null ? best : Camera.main;
    }

    private static TextMeshPro CreateText(Transform parent)
    {
        TextMeshPro text = null;
        try
        {
            text = TMPTemplate.Create("TOHhamoUnloadText", "", Color.white, 3.6f, TextAlignmentOptions.Top, true, parent);
        }
        catch (Exception)
        {
            // TMPTemplateの初期化前など。下のフォールバックを使う。
        }
        if (text == null && ModUnloaderScreen.WarnText != null)
        {
            text = UnityEngine.Object.Instantiate(ModUnloaderScreen.WarnText, parent);
            text.name = "TOHhamoUnloadText";
            text.fontSize = 3.6f;
            text.alignment = TextAlignmentOptions.Top;
            text.gameObject.SetActive(true);
        }
        return text;
    }

    private static string BuildText(string status) => $"<size=70%><color=#c0c0c0>{status}</color></size>";

    private static IEnumerator CoUnloadWithLoadingScreen()
    {
        var ja = IsJapanese();
        string Tr(string jp, string en) => ja ? jp : en;

        var layer = ModUnloaderScreen.Popup != null ? ModUnloaderScreen.Popup.gameObject.layer : LayerMask.NameToLayer("UI");
        var cam = FindCamera(layer);

        // 全画面を覆っているCanvasは一時的に隠す(起動時ロード画面と同じ)
        Canvas[] hiddenCanvases = Array.Empty<Canvas>();
        try
        {
            hiddenCanvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (var c in hiddenCanvases)
                if (c != null) c.enabled = false;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Canvasの非表示化に失敗しました(致命的ではありません): {ex.Message}", nameof(ModUnloadLoadingScreen));
        }

        // カメラの正面に置く。カメラの大きさに合わせて全体をスケールさせる。
        var root = new GameObject("TOHhamoUnloadRoot");
        root.layer = layer;
        if (cam != null)
        {
            root.transform.SetPositionAndRotation(cam.transform.position + cam.transform.forward * 5f, cam.transform.rotation);
            if (cam.orthographic) root.transform.localScale = Vector3.one * (cam.orthographicSize / 3f);
        }

        GameObject Attach(GameObject obj, Vector3 localPos)
        {
            obj.layer = layer;
            obj.transform.SetParent(root.transform, false);
            obj.transform.localPosition = localPos;
            return obj;
        }

        const float logoY = 1.95f;
        const float barWidth = 3.6f;
        const float barHeight = 0.24f;
        const float barY = logoY - 2.15f;
        var overlayColor = new Color(0.06f, 0.03f, 0.07f, 1f);
        var barBgColor = new Color(0f, 0f, 0f, 0.45f);
        var barFillColor = new Color(1f, 0.52f, 0.85f, 0.95f);

        // 背景(画面全体を覆う)
        const float overlayWidth = 60f;
        const float overlayHeight = 40f;
        var overlayPos = new Vector3(-overlayWidth / 2f, 0f, 0f);
        var overlay = Attach(HamoLoadingScreenPatch.CreateBar(overlayPos, overlayWidth, overlayHeight, HamoLoadingScreenPatch.SetAlpha(overlayColor, 0f), OrderOverlay), overlayPos);
        overlay.name = "TOHhamoUnloadOverlay";
        var overlaySr = overlay.GetComponent<SpriteRenderer>();

        // ロゴ
        GameObject logoObj = null;
        SpriteRenderer logoSr = null;
        var logoSprite = UtilsSprite.LoadSprite("TownOfHost.Resources.TownOfHost-hamo-logo.png", 170f);
        if (logoSprite != null)
        {
            logoObj = Attach(new GameObject("TOHhamoUnloadLogo"), new Vector3(0f, logoY, -1f));
            logoSr = logoObj.AddComponent<SpriteRenderer>();
            logoSr.sprite = logoSprite;
            logoSr.sortingOrder = OrderLogo;
            logoSr.color = new Color(1f, 1f, 1f, 0f);
        }

        // ステータス文字
        var loadText = CreateText(root.transform);
        if (loadText != null)
        {
            loadText.gameObject.layer = layer;
            loadText.transform.localPosition = new Vector3(0f, logoY - 1.5f, -2f);
            loadText.transform.localScale = Vector3.one;
            loadText.fontStyle = FontStyles.Bold;
            loadText.enableWordWrapping = false;
            loadText.text = "";
            loadText.color = new Color(1f, 1f, 1f, 0f);
            loadText.sortingOrder = OrderText;
        }

        // 進捗バー
        var barBgPos = new Vector3(-barWidth / 2f, barY, -1.5f);
        var barBg = Attach(HamoLoadingScreenPatch.CreateBar(barBgPos, barWidth, barHeight, HamoLoadingScreenPatch.SetAlpha(barBgColor, 0f), OrderBarBg), barBgPos);
        barBg.name = "TOHhamoUnloadBarBg";
        var barFillPos = new Vector3(-barWidth / 2f, barY, -1.6f);
        var barFill = Attach(HamoLoadingScreenPatch.CreateBar(barFillPos, 0f, barHeight, HamoLoadingScreenPatch.SetAlpha(barFillColor, 0f), OrderBarFill), barFillPos);
        barFill.name = "TOHhamoUnloadBarFill";
        var barBgSr = barBg.GetComponent<SpriteRenderer>();
        var barFillSr = barFill.GetComponent<SpriteRenderer>();

        // 泡
        var bubbles = new List<Transform>();
        var bubbleSrs = new List<SpriteRenderer>();
        var bubbleColors = new[]
        {
            new Color(1f, 0.52f, 0.85f, 0.8f),
            new Color(0.4f, 0.85f, 0.85f, 0.8f),
            new Color(1f, 0.75f, 0.85f, 0.8f),
            new Color(0.55f, 0.9f, 0.9f, 0.8f),
        };
        for (var i = 0; i < bubbleColors.Length; i++)
        {
            var bubble = Attach(new GameObject($"TOHhamoUnloadBubble{i}"), new Vector3((i - (bubbleColors.Length - 1) / 2f) * 0.5f, barY - 0.9f, -1.7f));
            var sr = bubble.AddComponent<SpriteRenderer>();
            sr.sprite = HamoLoadingScreenPatch.CreateCircleSprite(bubbleColors[i]);
            sr.sortingOrder = OrderBubble;
            var scale = UnityEngine.Random.Range(0.16f, 0.3f);
            bubble.transform.localScale = new Vector3(scale, scale, 1f);
            sr.color = HamoLoadingScreenPatch.SetAlpha(bubbleColors[i], 0f);
            bubbles.Add(bubble.transform);
            bubbleSrs.Add(sr);
        }
        // 泡のアニメはrootの子のローカルY(=baseY)で動かす
        var bubbleAnim = AmongUsClient.Instance.StartCoroutine(CoAnimateBubblesLocal(bubbles, barY - 0.9f).WrapToIl2Cpp());

        // 全体のフェード(0〜1)
        void SetFade(float a)
        {
            a = Mathf.Clamp01(a);
            overlaySr.color = HamoLoadingScreenPatch.SetAlpha(overlayColor, overlayColor.a * a);
            if (logoSr != null) logoSr.color = new Color(1f, 1f, 1f, a);
            if (loadText != null) loadText.color = new Color(1f, 1f, 1f, a);
            barBgSr.color = HamoLoadingScreenPatch.SetAlpha(barBgColor, barBgColor.a * a);
            barFillSr.color = HamoLoadingScreenPatch.SetAlpha(barFillColor, barFillColor.a * a);
            for (var i = 0; i < bubbleSrs.Count; i++)
                if (bubbleSrs[i] != null) bubbleSrs[i].color = HamoLoadingScreenPatch.SetAlpha(bubbleColors[i], bubbleColors[i].a * a);
        }

        var shownProgress = 0f;
        var targetProgress = 0f;
        void TickBar()
        {
            shownProgress = Mathf.MoveTowards(shownProgress, targetProgress, Time.deltaTime * 1.2f);
            HamoLoadingScreenPatch.SetBarFill(barFill, barWidth, shownProgress);
        }
        void SetStatus(string status)
        {
            if (loadText != null) loadText.text = BuildText(status);
        }

        try
        {
            // フェードイン
            var fade = 0f;
            while (fade < 1f)
            {
                fade += Time.deltaTime * 6f;
                SetFade(fade);
                yield return null;
            }
            SetFade(1f);

            // 無効化の各ステップ
            var steps = new (string text, float target, Action action)[]
            {
                (Tr("Modの通信を切り離しています…", "Detaching mod connection…"), 0.25f, ModUnloaderScreen.NotifyUnloadToHost),
                (Tr("パッチを解除しています…", "Removing patches…"), 0.55f, () => Harmony.UnpatchAll()),
                (Tr("Modをアンロードしています…", "Unloading the mod…"), 0.8f, () => Main.Instance.Unload()),
            };
            foreach (var step in steps)
            {
                SetStatus(step.text);
                targetProgress = step.target;
                yield return null; // 文字を一度描画してから処理する
                try
                {
                    step.action?.Invoke();
                }
                catch (Exception ex)
                {
                    Logger.Error($"無効化ステップの実行に失敗しました({step.text}): {ex}", nameof(ModUnloadLoadingScreen));
                }
                var t = 0f;
                while (t < MinStepSeconds)
                {
                    t += Time.deltaTime;
                    TickBar();
                    yield return null;
                }
            }

            // メモリ解放
            SetStatus(Tr("メモリを解放しています…", "Freeing memory…"));
            targetProgress = 1f;
            AsyncOperation unloadOp = null;
            try { unloadOp = Resources.UnloadUnusedAssets(); }
            catch (Exception ex) { Logger.Warn($"UnloadUnusedAssetsに失敗しました: {ex.Message}", nameof(ModUnloadLoadingScreen)); }
            var gcWait = 0f;
            while ((unloadOp != null && !unloadOp.isDone && gcWait < MaxGcWaitSeconds) || shownProgress < 1f)
            {
                gcWait += Time.deltaTime;
                TickBar();
                yield return null;
            }
            GC.Collect();

            // 完了メッセージ(点滅)
            var completeMessages = new[]
            {
                Tr("Modを無効化しました！バニラで楽しんでね", "Mod disabled! Enjoy vanilla."),
                Tr("無効化完了！再度使うにはゲームの再起動が必要です", "Done! Restart the game to enable the mod again."),
            };
            SetStatus(completeMessages[UnityEngine.Random.Range(0, completeMessages.Length)]);
            for (var i = 0; i < 3; i++)
            {
                yield return new WaitForSeconds(0.1f);
                if (loadText != null) loadText.alpha = 0f;
                yield return new WaitForSeconds(0.1f);
                if (loadText != null) loadText.alpha = 1f;
            }
            yield return new WaitForSeconds(0.6f);

            // フェードアウト
            fade = 1f;
            while (fade > 0f)
            {
                fade -= Time.deltaTime * 1.4f;
                SetFade(fade);
                yield return null;
            }
        }
        finally
        {
            try { if (bubbleAnim != null) AmongUsClient.Instance.StopCoroutine(bubbleAnim); } catch { }
            if (root != null) UnityEngine.Object.Destroy(root);
            foreach (var c in hiddenCanvases)
                if (c != null) c.enabled = true;
            running = false;
        }
    }

    /// <summary>
    /// 泡のふわふわ動作。rootの子として動かすので、baseYはローカル座標で扱う。
    /// </summary>
    private static IEnumerator CoAnimateBubblesLocal(List<Transform> bubbles, float baseY)
    {
        var phase = new float[bubbles.Count];
        for (var i = 0; i < phase.Length; i++) phase[i] = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

        while (true)
        {
            for (var i = 0; i < bubbles.Count; i++)
            {
                var t = bubbles[i];
                if (t == null) continue;
                phase[i] += Time.deltaTime * 1.1f;
                var bob = Mathf.Sin(phase[i]) * 0.12f;
                var rise = (phase[i] % (Mathf.PI * 2f)) / (Mathf.PI * 2f) * 0.35f;
                var pos = t.localPosition;
                pos.y = baseY + bob + rise;
                t.localPosition = pos;
            }
            yield return null;
        }
    }
}
