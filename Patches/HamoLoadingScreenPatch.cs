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
    /// <summary>
    /// TOHhamo専用のロード画面。
    ///
    /// これまでTOHhamoにはAmong Us起動直後の「起動ロゴ画面(SplashManager)」用の
    /// 独自演出が無く、素通しでメインメニューまで進んでいた。
    /// そのため、実際に重い処理(埋め込み画像の初回デコードなど)が
    /// メインメニューに入った後に散発的に発生し、そこでカクつきが出ていた。
    ///
    /// このパッチでは、TownOfHost-Fun側の「クルーメイトが歩くロード画面」とは
    /// 見た目・演出を分けたTOHhamo独自のロード画面(ロゴ+気泡アニメーション+
    /// 進捗バー)をSplashManagerの上に表示しつつ、その待ち時間を使って
    /// Modに埋め込まれている画像を全てあらかじめデコードしてキャッシュに
    /// 乗せておく(= UtilsSprite.GetOrLoadTexture の事前実行)。
    /// 多少時間がかかっても、メインメニューに入った後の「軽さ」を優先する。
    /// </summary>
    [HarmonyPriority(Priority.HigherThanNormal)]
    [HarmonyPatch(typeof(SplashManager))]
    internal static class HamoLoadingScreenPatch
    {
        private static readonly ManualLogSource Logger = BepInEx.Logging.Logger.CreateLogSource("TOHhamoLoadingScreen");

        private static bool started = false;
        private static bool finished = false;
        private static bool cachedDoneLoadingRefData = false;

        /// <summary>ロード画面の下部に表示する現在の状態テキスト。</summary>
        public static string CurrentStatusText { get; set; } = "";

        [HarmonyPrefix]
        [HarmonyPatch(nameof(SplashManager.Update))]
        public static bool PrefixUpdate(SplashManager __instance)
        {
            // デバッグ実行時は開発効率を優先し、従来通りロード演出をスキップする
            // (ClientPatch.SplashLogoAnimatorPatchが即座にシーン遷移を許可する)
            if (DebugModeManager.AmDebugger) return true;

            cachedDoneLoadingRefData |= __instance.doneLoadingRefdata;
            __instance.doneLoadingRefdata = false;

            // 以前は「バニラ側の下準備(doneLoadingRefdata)が終わってから」独自画面を
            // 出していたため、それまでの数秒はAmong Us本体側のスプラッシュ演出が
            // そのまま見えてしまい、「ロード画面が2回出る」ように見えていた。
            // → Amoung Us起動直後の最初のUpdateから即座に自前の画面(背景オーバーレイ+ロゴ)を
            //   出して、バニラ側の演出ごと覆い隠すようにする。
            if (!started)
            {
                started = true;
                __instance.StartCoroutine(CoShowHamoLoadingScreen(__instance).WrapToIl2Cpp());
                // 万一ロード画面のコルーチン内で例外が起きて途中で止まっても
                // メインメニューに進めなくなる("起動不能")事故を避けるための保険
                __instance.StartCoroutine(CoLoadingWatchdog(__instance).WrapToIl2Cpp());
            }

            // 独自ロード画面の処理が終わるまでは、バニラのシーン遷移処理そのものを止めておく
            return finished;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(SplashManager.Update))]
        public static void PostfixUpdate(SplashManager __instance)
        {
            if (DebugModeManager.AmDebugger) return;
            __instance.doneLoadingRefdata = cachedDoneLoadingRefData;
        }

        private static IEnumerator CoShowHamoLoadingScreen(SplashManager __instance)
        {
            // ===== 既存の同名オブジェクトが残っていたら先に片付ける =====
            // (何らかの理由でパッチが二重に走った場合の保険。これが無いとロゴやテキストが
            // 重なって表示されてしまう)
            foreach (var name in new[] { "TOHhamoLogo", "TOHhamoLoadingBar", "TOHhamoBubble0", "TOHhamoBubble1", "TOHhamoBubble2", "TOHhamoBubble3", "TOHhm_LoadingLogo" })
            {
                var existing = GameObject.Find(name);
                if (existing != null) GameObject.Destroy(existing);
            }

            // ===== 起動時に裏で歩いている緑のクルーメイト(バニラのLoadingBarManager)を非表示に =====
            // 不要とのことなので、ロード画面の間ずっと隠しておく。
            try
            {
                var loadingBarManagers = UnityEngine.Object.FindObjectsByType<LoadingBarManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var lbm in loadingBarManagers)
                {
                    if (lbm != null) lbm.gameObject.SetActive(false);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"LoadingBarManagerの非表示化に失敗しました(致命的ではありません): {ex.Message}");
            }

            // ===== 起動時に裏で歩いている緑のクルーメイトや、あもあすロゴ演出を止める =====
            // これらはバニラのCanvas UI/Animatorで動いているため、SpriteRendererの
            // オーバーレイを上に重ねるだけでは(Canvasは別描画パスで最前面に出るため)
            // 隠しきれないことがある。なので描画自体を止めてしまう。
            // ただし「TOHhamoのロード画面が終わったらバニラのロゴ演出を出す」という
            // 要望もあるため、ここで止めたものは後でちゃんと元に戻す。
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

            // --- 背景オーバーレイ ---
            // Among Us本体のスプラッシュ演出(ロゴ点滅など)を覆い隠して、
            // 「起動した瞬間からTOHhamoのロード画面だけが見えている」状態にするための土台。
            // カメラの表示範囲全体を覆えるよう、余裕を持った大きさにしている。
            const float overlayWidth = 60f;
            const float overlayHeight = 40f;
            var overlayColor = new Color(0.06f, 0.03f, 0.07f, 1f); // 完全不透明にして裏が見えないようにする
            var overlay = CreateBar(new Vector3(-overlayWidth / 2f, 0f, -12f), overlayWidth, overlayHeight, new Color(overlayColor.r, overlayColor.g, overlayColor.b, 0f), sortingOrder: 50);

            // --- hamoロゴ ---
            // TownOfHost-Fun側 (y = 0.6) よりはっきり上に配置してほしいという要望を反映
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

            // 背景オーバーレイとロゴは、起動した瞬間に見せたいのでここで先に一瞬だけフェードインさせる
            // (errorPopup等の準備を待たずに出す。数フレームで済むごく短いフェードなので「一瞬で表示された」ように見える)
            float instantFade = 0f;
            while (instantFade < 1f)
            {
                instantFade += Time.deltaTime * 8f;
                var oc = new Color(overlayColor.r, overlayColor.g, overlayColor.b, overlayColor.a * Mathf.Clamp01(instantFade));
                overlay.GetComponent<SpriteRenderer>().color = oc;
                if (logoObj != null) logoObj.GetComponent<SpriteRenderer>().color = new Color(1f, 1f, 1f, Mathf.Clamp01(instantFade));
                yield return null;
            }

            // errorPopup(テキスト複製元)がまだシーン上で準備できていない場合に備えて少しだけ待つ
            // (通常はシーンロード時点で既に存在しているため、ほとんどのケースで待たずに通る)
            var safetyFrames = 0;
            while ((__instance.errorPopup == null || __instance.errorPopup.InfoText == null) && safetyFrames < 300)
            {
                safetyFrames++;
                yield return null;
            }

            // --- ローディングテキスト (ロゴのすぐ下) ---
            var loadText = GameObject.Instantiate(__instance.errorPopup.InfoText, null);
            loadText.transform.localPosition = new Vector3(0f, logoY - 1.9f, -10f);
            loadText.fontStyle = FontStyles.Bold;
            loadText.text = "";
            loadText.color = new Color(1f, 1f, 1f, 0f);
            loadText.fontSize = 3.6f;
            loadText.alignment = TextAlignmentOptions.Top;
            loadText.sortingOrder = 150; // 背景オーバーレイ(50)・ロゴ(100)より確実に手前に描画する

            // --- 進捗バー ---
            const float barWidth = 3.6f;
            const float barHeight = 0.24f;
            const float barY = logoY - 2.15f;
            var barBgColor = new Color(0f, 0f, 0f, 0.45f);
            var barFillColor = new Color(1f, 0.52f, 0.85f, 0.95f);
            var barBg = CreateBar(new Vector3(-barWidth / 2f, barY, -10f), barWidth, barHeight, new Color(barBgColor.r, barBgColor.g, barBgColor.b, 0f), sortingOrder: 98);
            var barFill = CreateBar(new Vector3(-barWidth / 2f, barY, -10.5f), 0f, barHeight, new Color(barFillColor.r, barFillColor.g, barFillColor.b, 0f), sortingOrder: 99);

            // --- 気泡アニメーション (Funのクルーメイトとは別の、hamoロゴの泡モチーフを使った独自演出) ---
            var bubbles = new List<Transform>();
            var bubbleColors = new[]
            {
                new Color(1f, 0.52f, 0.85f, 0.8f),   // hamoロゴのピンク
                new Color(0.4f, 0.85f, 0.85f, 0.8f), // hamoロゴのターコイズ
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

            // --- フェードイン (テキスト・進捗バー・気泡。背景とロゴは既に表示済み) ---
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

            // --- 画像アセットの事前読み込み ("軽量化"のための本体処理) ---
            var imagePaths = UtilsSprite.GetAllEmbeddedImagePaths();
            var total = Math.Max(imagePaths.Length, 1);
            var loaded = 0;
            const int perFrame = 3; // 1フレームで読みすぎてカクつかない程度の枚数に抑える

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

            // --- 音声アセットの事前読み込み ---
            // 画像だけでなく、シーン上にロード済みのオーディオクリップも
            // ここでまとめて実データ展開(LoadAudioData)しておくことで、
            // 「これ以上ロードできるものが無い」状態までここで済ませる。
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
            // --- MOD本体の初期化(Main.IsLoaded)完了を待つ ---
            // 画像・音声のプリロードが終わっていても、Modの初期化処理自体が
            // まだ終わっていない可能性があるため、要望通り「すべて」が終わるまでここで待つ。
            const float maxInitWaitSeconds = 60f; // フリーズ防止の保険
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

            // --- 不要になった一時リソースの解放(このタイミングで前払いしておく) ---
            CurrentStatusText = "最終確認中…";
            loadText.text = BuildLoadingText(CurrentStatusText);
            var unloadOp = Resources.UnloadUnusedAssets();
            while (unloadOp != null && !unloadOp.isDone) yield return null;
            GC.Collect();
            yield return new WaitForSeconds(0.2f);

            // --- 完了メッセージ ---
            var completeMessages = new[]
            {
                "準備完了！",
                "お待たせしました！",
                "たのしい時間の始まりです！",
                "hamoの世界へようこそ！",
                "さあ、はじめましょう！",
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

            // --- フェードアウト ---
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

            // ===== 隠していたバニラ側の演出を元に戻す =====
            // ここで戻してから finished = true にすることで、この直後の
            // 通常のSplashManager.Updateで「あもあす」のロゴ演出が正しく再生される。
            // ただし、裏を走っていた緑のクルーメイト(PlayerControl)は「不要」とのことなので
            // これだけは元に戻さず、消したままにしておく。
            foreach (var c in hiddenCanvases) if (c != null) c.enabled = true;
            foreach (var anim in hiddenAnimators) if (anim != null) anim.enabled = true;

            // ここで自前でシーン遷移を確定させると、バニラの「あもあす」ロゴ演出が
            // 一切表示されないまま次のシーンへ飛んでしまう。
            // 要望により「TOHhamoのロード画面が終わったら、その後にバニラのロゴ演出が
            // 出るように」したいので、ここでは強制的にシーン遷移させず、
            // startTime をリセットしてバニラのタイマーを仕切り直したうえで
            // finished = true にして、以降は通常のSplashManager.Updateに処理を戻す。
            __instance.startTime = Time.time;
            finished = true;

            Logger.LogInfo($"TOHhamo loading screen finished. Preloaded {UtilsSprite.PreloadedTextureCount} embedded images.");        }

        private static string BuildLoadingText(string status, bool isFinal = false)
        {
            // ロゴ画像自体に既に「TownOfHost hamo」の文字が入っているため、
            // ここでタイトルを重ねて出すと二重表示に見えてしまう。
            // なのでここでは進捗ステータスの1行だけを表示する。
            return $"<size=70%><color=#c0c0c0>{status}</color></size>";
        }

        /// <summary>
        /// CoShowHamoLoadingScreen内で予期せぬ例外が起きて処理が止まった場合の保険。
        /// 一定時間経ってもロードが終わらない場合は、演出を諦めて強制的に
        /// メインメニューへ進める(=起動不能になる事故を防ぐ)。
        /// </summary>
        private static IEnumerator CoLoadingWatchdog(SplashManager __instance)
        {
            const float timeoutSeconds = 25f;
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
