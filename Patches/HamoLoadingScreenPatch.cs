using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP.Utils;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace TownOfHost.Patches
{
    [HarmonyPriority(Priority.HigherThanNormal)]
    [HarmonyPatch(typeof(SplashManager))]
    internal static class HamoLoadingScreenPatch
    {
        private static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("TOHhamoLoadingScreen");

        private static bool started = false;
        private static bool finished = false;
        private static bool cachedDoneLoadingRefData = false;

        public static string CurrentStatusText { get; set; } = "";

        [HarmonyPrefix]
        [HarmonyPatch(nameof(SplashManager.Update))]
        public static bool PrefixUpdate(SplashManager __instance)
        {
            if (DebugModeManager.AmDebugger) return true;

            if (finished) return true;

            cachedDoneLoadingRefData |= __instance.doneLoadingRefdata;
            __instance.doneLoadingRefdata = false;

            if (!started)
            {
                started = true;
                __instance.StartCoroutine(CoShowHamoLoadingScreen(__instance).WrapToIl2Cpp());
                __instance.StartCoroutine(CoLoadingWatchdog(__instance).WrapToIl2Cpp());
            }

            return finished;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(SplashManager.Update))]
        public static void PostfixUpdate(SplashManager __instance)
        {
            if (DebugModeManager.AmDebugger) return;
            if (finished) return;
            __instance.doneLoadingRefdata = cachedDoneLoadingRefData;
        }

        private static IEnumerator CoShowHamoLoadingScreen(SplashManager __instance)
        {
            foreach (var name in new[] { "TOHhamoLogo", "TOHhamoLoadingBar", "TOHhamoBubble0", "TOHhamoBubble1", "TOHhamoBubble2", "TOHhamoBubble3", "TOHhm_LoadingLogo" })
            {
                var existing = GameObject.Find(name);
                if (existing != null) GameObject.Destroy(existing);
            }

            LoadingBarManager[] loadingBarManagers = Array.Empty<LoadingBarManager>();
            try
            {
                loadingBarManagers = UnityEngine.Object.FindObjectsByType<LoadingBarManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var lbm in loadingBarManagers)
                {
                    if (lbm != null) lbm.gameObject.SetActive(false);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"LoadingBarManagerの非表示化に失敗しました(致命的ではありません): {ex.Message}");
            }
            Canvas[] hiddenCanvases = Array.Empty<Canvas>();
            Animator[] hiddenAnimators = Array.Empty<Animator>();
            PlayerControl[] hiddenPlayers = Array.Empty<PlayerControl>();
            try
            {
                hiddenCanvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var c in hiddenCanvases)
                {
                    if (c != null) c.enabled = false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Canvasの非表示化に失敗しました(致命的ではありません): {ex.Message}");
            }
            try
            {
                hiddenAnimators = UnityEngine.Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var anim in hiddenAnimators)
                {
                    if (anim != null) anim.enabled = false;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Animatorの停止に失敗しました(致命的ではありません): {ex.Message}");
            }
            try
            {
                hiddenPlayers = UnityEngine.Object.FindObjectsByType<PlayerControl>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var p in hiddenPlayers)
                {
                    if (p != null) p.gameObject.SetActive(false);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"PlayerControlの非表示化に失敗しました(致命的ではありません): {ex.Message}");
            }

            const float overlayWidth = 60f;
            const float overlayHeight = 40f;
            var overlayColor = new Color(0.06f, 0.03f, 0.07f, 1f);
            var overlay = CreateBar(new Vector3(-overlayWidth / 2f, 0f, -12f), overlayWidth, overlayHeight, new Color(overlayColor.r, overlayColor.g, overlayColor.b, 0f), sortingOrder: 50);

            const float logoY = 1.95f;
            Sprite logoSprite = UtilsSprite.LoadSprite("TownOfHost.Resources.TownOfHost-hamo-logo.png", 170f);

            GameObject logoObj = null;
            if (logoSprite != null)
            {
                logoObj = new GameObject("TOHhamoLogo");
                var sr = logoObj.AddComponent<SpriteRenderer>();
                sr.sprite = logoSprite;
                sr.sortingOrder = 100;
                logoObj.transform.position = new Vector3(0f, logoY, -11f);
                sr.color = new Color(1f, 1f, 1f, 0f);
            }
            else
            {
                Logger.LogWarning("TOHhamo logo resource stream was null.");
            }

            float instantFade = 0f;
            while (instantFade < 1f)
            {
                instantFade += Time.deltaTime * 8f;
                var oc = new Color(overlayColor.r, overlayColor.g, overlayColor.b, overlayColor.a * Mathf.Clamp01(instantFade));
                overlay.GetComponent<SpriteRenderer>().color = oc;
                if (logoObj != null) logoObj.GetComponent<SpriteRenderer>().color = new Color(1f, 1f, 1f, Mathf.Clamp01(instantFade));
                yield return null;
            }

            var safetyFrames = 0;
            while ((__instance.errorPopup == null || __instance.errorPopup.InfoText == null) && safetyFrames < 300)
            {
                safetyFrames++;
                yield return null;
            }


            var loadText = GameObject.Instantiate(__instance.errorPopup.InfoText, null);
            loadText.transform.localPosition = new Vector3(0f, logoY - 1.5f, -10f);
            loadText.fontStyle = FontStyles.Bold;
            loadText.text = "";
            loadText.color = new Color(1f, 1f, 1f, 0f);
            loadText.fontSize = 3.6f;
            loadText.alignment = TextAlignmentOptions.Top;
            loadText.sortingOrder = 150;

            // --- 進捗バー ---
            const float barWidth = 3.6f;
            const float barHeight = 0.24f;
            const float barY = logoY - 2.15f;
            var barBgColor = new Color(0f, 0f, 0f, 0.45f);
            var barFillColor = new Color(1f, 0.52f, 0.85f, 0.95f);
            var barBg = CreateBar(new Vector3(-barWidth / 2f, barY, -10f), barWidth, barHeight, new Color(barBgColor.r, barBgColor.g, barBgColor.b, 0f), sortingOrder: 98);
            var barFill = CreateBar(new Vector3(-barWidth / 2f, barY, -10.5f), 0f, barHeight, new Color(barFillColor.r, barFillColor.g, barFillColor.b, 0f), sortingOrder: 99);

            var bubbles = new List<Transform>();
            var bubbleColors = new[]
            {
                new Color(1f, 0.52f, 0.85f, 0.8f),
                new Color(0.4f, 0.85f, 0.85f, 0.8f),
                new Color(1f, 0.75f, 0.85f, 0.8f),
                new Color(0.55f, 0.9f, 0.9f, 0.8f),
            };
            for (int i = 0; i < bubbleColors.Length; i++)
            {
                var bubble = new GameObject($"TOHhamoBubble{i}");
                var sr = bubble.AddComponent<SpriteRenderer>();
                sr.sprite = CreateCircleSprite(bubbleColors[i]);
                sr.sortingOrder = 97;
                var scale = UnityEngine.Random.Range(0.16f, 0.3f);
                bubble.transform.localScale = new Vector3(scale, scale, 1f);
                bubble.transform.position = new Vector3((i - (bubbleColors.Length - 1) / 2f) * 0.5f, barY - 0.9f, -10f);
                bubbles.Add(bubble.transform);
            }
            var bubbleAnim = __instance.StartCoroutine(CoAnimateBubbles(bubbles, barY - 0.9f).WrapToIl2Cpp());

            float fade = 0f;
            while (fade < 1f)
            {
                fade += Time.deltaTime * 3.5f;
                var c = new Color(1f, 1f, 1f, Mathf.Clamp01(fade));
                loadText.color = c;
                barBg.GetComponent<SpriteRenderer>().color = SetAlpha(barBgColor, barBgColor.a * Mathf.Clamp01(fade));
                barFill.GetComponent<SpriteRenderer>().color = SetAlpha(barFillColor, barFillColor.a * Mathf.Clamp01(fade));
                foreach (var b in bubbles)
                    if (b != null) b.GetComponent<SpriteRenderer>().color = SetAlpha(b.GetComponent<SpriteRenderer>().color, Mathf.Clamp01(fade));
                yield return null;
            }

            var imagePaths = UtilsSprite.GetAllEmbeddedImagePaths();
            var total = Math.Max(imagePaths.Length, 1);
            var loaded = 0;
            const int perFrame = 8;

            foreach (var path in imagePaths)
            {
                UtilsSprite.GetOrLoadTexture(path);
                loaded++;
                CurrentStatusText = $"アセットを読み込み中… ({loaded}/{total})";
                loadText.text = BuildLoadingText(CurrentStatusText);

                var percent = Mathf.Clamp01(loaded / (float)total);
                SetBarFill(barFill, barWidth, percent);

                if (loaded % perFrame == 0) yield return null;
            }
            SetBarFill(barFill, barWidth, 1f);

            CurrentStatusText = "サウンドを読み込み中…";
            loadText.text = BuildLoadingText(CurrentStatusText);
            yield return null;
            AudioClip[] audioClips = null;
            try
            {
                var allClips = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<AudioClip>());
                var list = new List<AudioClip>(allClips.Length);
                foreach (var obj in allClips)
                {
                    var clip = obj.TryCast<AudioClip>();
                    if (clip != null) list.Add(clip);
                }
                audioClips = list.ToArray();
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"音声アセット一覧の取得に失敗しました(致命的ではありません): {ex.Message}");
            }

            if (audioClips != null)
            {
                var clipCount = 0;
                foreach (var clip in audioClips)
                {
                    try
                    {
                        if (clip.loadState == AudioDataLoadState.Unloaded)
                        {
                            clip.LoadAudioData();
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"音声クリップ({clip?.name})の事前読み込みに失敗しました(致命的ではありません): {ex.Message}");
                    }
                    clipCount++;
                    if (clipCount % perFrame == 0) yield return null;
                }
            }
            const float maxInitWaitSeconds = 60f;
            var initWaitElapsed = 0f;
            while (!Main.IsLoaded && initWaitElapsed < maxInitWaitSeconds)
            {
                CurrentStatusText = "Modの初期化を待っています…";
                loadText.text = BuildLoadingText(CurrentStatusText);
                initWaitElapsed += Time.deltaTime;
                yield return null;
            }
            if (!Main.IsLoaded)
            {
                Logger.LogError($"{maxInitWaitSeconds}秒待ってもMain.IsLoadedがtrueになりませんでした。安全のため続行します。");
            }

            const float finalCheckSeconds = 5f;
            var finalCheckElapsed = 0f;
            var unloadOp = Resources.UnloadUnusedAssets();
            var gcDone = false;
            while (finalCheckElapsed < finalCheckSeconds)
            {
                if (!gcDone && (unloadOp == null || unloadOp.isDone))
                {
                    GC.Collect();
                    gcDone = true;
                }
                CurrentStatusText = $"最終確認中… ({Mathf.CeilToInt(finalCheckSeconds - finalCheckElapsed)})";
                loadText.text = BuildLoadingText(CurrentStatusText);
                finalCheckElapsed += Time.deltaTime;
                yield return null;
            }

            var completeMessages = new[]
            {
                "船内に潜む影に注意せよ……",
                "準備OK！ タスクをこなして生き残れ！",
                "hamoの世界を楽しんで！",
                "hamoを楽しんでー！",
                "ロード完了！誰も信じるな...",
            };
            loadText.text = BuildLoadingText(completeMessages[UnityEngine.Random.Range(0, completeMessages.Length)], isFinal: true);

            for (int i = 0; i < 3; i++)
            {
                yield return new WaitForSeconds(0.1f);
                loadText.alpha = 0f;
                yield return new WaitForSeconds(0.1f);
                loadText.alpha = 1f;
            }
            yield return new WaitForSeconds(0.3f);

            fade = 1f;
            while (fade > 0f)
            {
                fade -= Time.deltaTime * 1.4f;
                var c = new Color(1f, 1f, 1f, Mathf.Clamp01(fade));
                if (logoObj != null) logoObj.GetComponent<SpriteRenderer>().color = c;
                loadText.color = c;
                overlay.GetComponent<SpriteRenderer>().color = SetAlpha(overlayColor, overlayColor.a * Mathf.Clamp01(fade));
                barBg.GetComponent<SpriteRenderer>().color = SetAlpha(barBgColor, barBgColor.a * Mathf.Clamp01(fade));
                barFill.GetComponent<SpriteRenderer>().color = SetAlpha(barFillColor, barFillColor.a * Mathf.Clamp01(fade));
                foreach (var b in bubbles)
                    if (b != null) b.GetComponent<SpriteRenderer>().color = SetAlpha(b.GetComponent<SpriteRenderer>().color, Mathf.Clamp01(fade));
                yield return null;
            }

            if (bubbleAnim != null) __instance.StopCoroutine(bubbleAnim);

            if (logoObj != null) GameObject.Destroy(logoObj);
            GameObject.Destroy(loadText.gameObject);
            GameObject.Destroy(overlay.gameObject);
            GameObject.Destroy(barBg.gameObject);
            GameObject.Destroy(barFill.gameObject);
            foreach (var b in bubbles)
                if (b != null) GameObject.Destroy(b.gameObject);

            foreach (var c in hiddenCanvases) if (c != null) c.enabled = true;
            foreach (var anim in hiddenAnimators) if (anim != null) anim.enabled = true;
            foreach (var lbm in loadingBarManagers) if (lbm != null) lbm.gameObject.SetActive(true);
            foreach (var p in hiddenPlayers) if (p != null) p.gameObject.SetActive(true);

            __instance.doneLoadingRefdata = cachedDoneLoadingRefData;
            __instance.startTime = Time.time;
            finished = true;

            Logger.LogInfo($"TOHhamo loading screen finished. Preloaded {UtilsSprite.PreloadedTextureCount} embedded images.");
        }

        private static string BuildLoadingText(string status, bool isFinal = false)
        {
            return $"<size=70%><color=#c0c0c0>{status}</color></size>";
        }

        private static IEnumerator CoLoadingWatchdog(SplashManager __instance)
        {
            const float timeoutSeconds = 75f;
            var elapsed = 0f;
            while (!finished && elapsed < timeoutSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (!finished)
            {
                Logger.LogError("HamoLoadingScreen timed out. Forcing scene transition to avoid a boot hang.");
                try { __instance.sceneChanger.AllowFinishLoadingScene(); } catch { }
                __instance.startedSceneLoad = true;
                finished = true;
            }
        }

        private static IEnumerator CoAnimateBubbles(List<Transform> bubbles, float baseY)
        {
            var phase = new float[bubbles.Count];
            for (int i = 0; i < phase.Length; i++) phase[i] = UnityEngine.Random.Range(0f, Mathf.PI * 2f);

            while (true)
            {
                for (int i = 0; i < bubbles.Count; i++)
                {
                    var t = bubbles[i];
                    if (t == null) continue;
                    phase[i] += Time.deltaTime * 1.1f;
                    var bob = Mathf.Sin(phase[i]) * 0.12f;
                    var rise = (phase[i] % (Mathf.PI * 2f)) / (Mathf.PI * 2f) * 0.35f;
                    var pos = t.position;
                    pos.y = baseY + bob + rise;
                    t.position = pos;
                }
                yield return null;
            }
        }

        private static GameObject CreateBar(Vector3 leftEdgePosition, float width, float height, Color color, int sortingOrder)
        {
            var obj = new GameObject("TOHhamoLoadingBar");
            var sr = obj.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(GetSolidTexture(), new Rect(0, 0, 4, 4), new Vector2(0f, 0.5f), 4f);
            sr.sortingOrder = sortingOrder;
            sr.color = color;
            obj.transform.position = leftEdgePosition;
            obj.transform.localScale = new Vector3(Mathf.Max(width, 0f), height, 1f);
            return obj;
        }

        private static void SetBarFill(GameObject barFill, float fullWidth, float percent)
        {
            var scale = barFill.transform.localScale;
            scale.x = fullWidth * Mathf.Clamp01(percent);
            barFill.transform.localScale = scale;
        }

        private static Texture2D _solidTexture;
        private static Texture2D GetSolidTexture()
        {
            if (_solidTexture != null) return _solidTexture;
            _solidTexture = new Texture2D(4, 4, TextureFormat.ARGB32, false);
            var pixels = new Color32[4 * 4];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            _solidTexture.SetPixels32(pixels);
            _solidTexture.Apply();
            return _solidTexture;
        }

        private static readonly Dictionary<Color, Sprite> _circleSpriteCache = new();
        private static Sprite CreateCircleSprite(Color color)
        {
            if (_circleSpriteCache.TryGetValue(color, out var cached) && cached != null) return cached;

            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var center = new Vector2(size / 2f, size / 2f);
            var radius = size / 2f - 1f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    var dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center);
                    var alpha = Mathf.Clamp01(1f - (dist - (radius - 2f)) / 2f);
                    var c = color;
                    c.a *= alpha;
                    pixels[y * size + x] = c;
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            _circleSpriteCache[color] = sprite;
            return sprite;
        }

        private static Color SetAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}