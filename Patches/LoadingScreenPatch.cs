using HarmonyLib;
using TMPro;
using UnityEngine;

using TownOfHost.Templates;
using Object = UnityEngine.Object;

namespace TownOfHost.Patches
{
    /// <summary>
    /// MOD起動後、最初にメインメニューが表示される瞬間にロード画面を一枚重ねて表示する。
    /// 実際の役職/実績データの初期化はプラグインロード時(main.cs Load())の安全なタイミングで
    /// 既に完了しているため、ここでの「進行状況」は演出だが、
    /// ・起動直後にいきなり大量のボタンやテキストが並んだメニューが出てきて重く感じる
    /// ・何が起きているか分からないまま数秒待たされる
    /// という体感の重さを緩和する狙いがある。
    /// (実処理としては、後述のTargetArrow/役職表示更新間隔の見直しなど別途軽量化を行っている)
    /// </summary>
    [HarmonyPatch(typeof(MainMenuManager))]
    public static class LoadingScreenPatch
    {
        private static bool _shown = false;
        private static GameObject _root;
        private static TextMeshPro _statusText;
        private static SpriteRenderer _bg;
        private static SpriteRenderer _logo;

        // 演出用のステップ(実処理は既に終わっているため、ここは表示のみを進める)
        private static readonly string[] StepLabels = new[]
        {
            "役職データを確認中...",
            "実績データを確認中...",
            "オプションを準備中...",
            "設定を復元中...",
            "起動中...",
        };

        [HarmonyPatch(nameof(MainMenuManager.Start)), HarmonyPostfix, HarmonyPriority(Priority.First)]
        public static void ShowOnFirstMainMenu(MainMenuManager __instance)
        {
            // 一度表示したら、その後メインメニューに戻ってきても再表示しない
            // (以後の起動は既に初期化済みなので、毎回出すと逆に煩わしい)
            if (_shown) return;
            _shown = true;

            try
            {
                Build(__instance);
            }
            catch (System.Exception ex)
            {
                Logger.Error($"ロード画面の表示に失敗しました(致命的ではないため続行します): {ex}", "LoadingScreen");
                Cleanup();
            }
        }

        private static void Build(MainMenuManager mainMenuManager)
        {
            _root = new GameObject("TOHhm_LoadingScreen");
            Object.DontDestroyOnLoad(_root);

            // 画面のどこでも常に中央に表示されるよう、カメラから見た画面中央のワールド座標を
            // 計算して配置する(BugReportUIPatchと同じ手法。解像度やシーンに依らず中央に来る)。
            var camera = Camera.main;
            var centerWorldPos = new Vector3(0f, 0f, -20f);
            if (camera != null)
            {
                centerWorldPos = AspectPosition.ComputeWorldPosition(camera, AspectPosition.EdgeAlignments.Center, Vector3.zero);
                centerWorldPos.z = camera.transform.position.z + 5f; // 手前に来るようZだけ調整
            }
            _root.transform.position = centerWorldPos;

            // ===== 背景(画面全体を覆う半透明の黒) =====
            _bg = _root.AddComponent<SpriteRenderer>();
            _bg.sprite = UtilsSprite.LoadSprite("TownOfHost.Resources.background.png", 100f);
            if (_bg.sprite == null)
            {
                // 専用の単色背景画像が無い場合に備え、1x1の白テクスチャから即席で生成する。
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _bg.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            }
            _bg.drawMode = SpriteDrawMode.Sliced;
            _bg.size = new Vector2(40f, 24f); // 画面全体を十分覆う大きさ
            _bg.color = new Color(0.05f, 0.05f, 0.08f, 0.97f);
            _bg.sortingOrder = 32000; // 他のどのUIよりも手前に出す

            // ===== ロゴ(TOHhamoロゴを上側に表示) =====
            var logoObj = new GameObject("TOHhm_LoadingLogo");
            logoObj.transform.parent = _root.transform;
            logoObj.transform.localPosition = new Vector3(0f, 2.3f, -1f);
            _logo = logoObj.AddComponent<SpriteRenderer>();
            _logo.sprite = UtilsSprite.LoadSprite(
                Event.April || Event.Special
                    ? "TownOfHost.Resources.TownOfHost-hamo-logo2.png"
                    : "TownOfHost.Resources.TownOfHost-hamo-logo.png",
                150f);
            _logo.sortingOrder = 32001;

            // ===== 進行状況テキスト =====
            // TMPTemplateはVersionShower.Startで初期化されるため、MainMenuManager.Startより
            // 後に走った場合はbaseTMPが未セットで使えない。その場合はテキストなしで
            // 背景とロゴ・進捗バーだけを表示する(致命的ではないため握りつぶして続行)。
            try
            {
                _statusText = TMPTemplate.Create(
                    name: "TOHhm_LoadingStatusText",
                    text: StepLabels[0],
                    color: Color.white,
                    fontSize: 2.0f,
                    alignment: TextAlignmentOptions.Center,
                    setActive: true,
                    parent: _root.transform);
                _statusText.transform.localPosition = new Vector3(0f, -1.6f, -1f);
                _statusText.rectTransform.sizeDelta = new Vector2(10f, 1f);
                var textRenderer = _statusText.GetComponent<MeshRenderer>();
                if (textRenderer != null) textRenderer.sortingOrder = 32002;
            }
            catch (System.Exception ex)
            {
                Logger.Info($"ロード画面: 進行状況テキストの生成に失敗したため省略します: {ex.Message}", "LoadingScreen");
                _statusText = null;
            }

            // ===== 進捗バー(簡易: 背景バー+塗りつぶしバーの2枚のSpriteRenderer) =====
            // localScaleでの幅制御を安全に行うため、PPU=1の専用の1x1白テクスチャを用意する
            // (背景用スプライトはPPUが画像依存のため流用しない)。
            var barTex = new Texture2D(1, 1);
            barTex.SetPixel(0, 0, Color.white);
            barTex.Apply();
            var barSprite = Sprite.Create(barTex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

            var barBg = new GameObject("TOHhm_LoadingBarBg");
            barBg.transform.parent = _root.transform;
            barBg.transform.localPosition = new Vector3(0f, -2.1f, -1f);
            var barBgRenderer = barBg.AddComponent<SpriteRenderer>();
            barBgRenderer.sprite = barSprite;
            barBgRenderer.color = new Color(1f, 1f, 1f, 0.15f);
            barBgRenderer.sortingOrder = 32001;
            barBg.transform.localScale = new Vector3(4.2f, 0.28f, 1f);

            var barFillObj = new GameObject("TOHhm_LoadingBarFill");
            barFillObj.transform.parent = _root.transform;
            var barFillRenderer = barFillObj.AddComponent<SpriteRenderer>();
            barFillRenderer.sprite = barSprite;
            barFillRenderer.color = new Color(0.36f, 0.78f, 0.98f, 1f); // TOHhamoの水色系
            barFillRenderer.sortingOrder = 32002;
            _barFillTransform = barFillObj.transform;
            _barFillMaxWidth = 4.2f;
            _barFillHeight = 0.22f;
            SetBarProgress(0f);

            RunSteps(0);
        }

        private static Transform _barFillTransform;
        private static float _barFillMaxWidth;
        private static float _barFillHeight;

        private static void SetBarProgress(float ratio01)
        {
            if (_barFillTransform == null) return;
            ratio01 = Mathf.Clamp01(ratio01);
            // 単色1x1テクスチャのSpriteをlocalScaleだけで矩形に引き伸ばして使う
            // (SpriteのPPUが1のため、scaleの値がそのままワールド単位の幅/高さになる)。
            var width = _barFillMaxWidth * ratio01;
            var safeWidth = Mathf.Max(width, 0.0001f);
            _barFillTransform.localScale = new Vector3(safeWidth, _barFillHeight, 1f);
            // pivotが中央のSpriteを左端固定で伸ばしたいので、幅の半分だけ右にずらして
            // 常に左端がバー全体の左端(-2.1)に来るようにする。
            _barFillTransform.localPosition = new Vector3(-2.1f + safeWidth / 2f, -2.1f, -2f);
        }

        // 実際の初期化は既に完了済みなので、ここは表示の進行だけを一定間隔で進める演出。
        // 各ステップの間隔は短めにして、合計の待ち時間が長くなりすぎないようにする。
        private const float StepIntervalSeconds = 0.18f;

        private static void RunSteps(int index)
        {
            if (_root == null) return; // 万一途中でシーン遷移等により破棄されていたら中断

            if (index >= StepLabels.Length)
            {
                FadeOutAndDestroy();
                return;
            }

            if (_statusText != null) _statusText.text = StepLabels[index];
            SetBarProgress((float)(index + 1) / StepLabels.Length);

            _ = new LateTask(() => RunSteps(index + 1), StepIntervalSeconds, "TOHhm_LoadingScreen.Step", true);
        }

        private const float FadeSeconds = 0.35f;
        private const int FadeFrames = 6;

        private static void FadeOutAndDestroy()
        {
            FadeStep(0);
        }

        private static void FadeStep(int frame)
        {
            if (_root == null) return;

            var t = 1f - (float)frame / FadeFrames;
            if (_bg != null)
            {
                var c = _bg.color;
                c.a = 0.97f * t;
                _bg.color = c;
            }
            if (_logo != null)
            {
                var c = _logo.color;
                c.a = t;
                _logo.color = c;
            }
            if (_statusText != null)
            {
                var c = _statusText.color;
                c.a = t;
                _statusText.color = c;
            }

            if (frame >= FadeFrames)
            {
                Cleanup();
                return;
            }

            _ = new LateTask(() => FadeStep(frame + 1), FadeSeconds / FadeFrames, "TOHhm_LoadingScreen.Fade", true);
        }

        private static void Cleanup()
        {
            if (_root != null) Object.Destroy(_root);
            _root = null;
            _statusText = null;
            _bg = null;
            _logo = null;
            _barFillTransform = null;
        }
    }
}
