using System;
using TMPro;
using UnityEngine;

namespace TownOfHost.Patches
{
    /// <summary>
    /// バグ報告パネル・BUG専用チャットの見た目をまとめて整える(角丸・ヘッダー・配色)。
    /// 各パネルを組み立てた後に Apply を呼ぶだけで、配置や動作には手を加えない。
    /// </summary>
    public static class BugUiTheme
    {
        public static readonly Color Background = new Color32(21, 18, 28, 248);
        public static readonly Color Header = new Color32(35, 27, 48, 255);
        public static readonly Color Accent = new Color32(255, 92, 138, 255);
        public static readonly Color32 SubtleText = new(176, 164, 196, 255);

        private static Sprite _rounded;

        /// <summary>角丸の白い板(9スライス)。色はSpriteRenderer側で付ける</summary>
        public static Sprite Rounded()
        {
            if (_rounded != null) return _rounded;
            const int size = 64, radius = 16;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // 角からの距離でアンチエイリアス付きの角丸を作る
                    var cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    var cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    var d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    var a = Mathf.Clamp01(radius - d + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 130f, 0, SpriteMeshType.FullRect, new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
            _rounded.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _rounded.texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _rounded;
        }

        private static SpriteRenderer MakeRect(Transform parent, string name, Vector2 size, Vector3 pos, Color color, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Rounded();
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = size;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        public static void Apply(GameObject root, Vector2 panelSize, TextMeshPro title = null)
        {
            if (root == null) return;
            try
            {
                if (root.transform.Find("ThemeHeader") != null) return; // 適用済み

                // 背景を角丸に
                var bg = root.GetComponent<SpriteRenderer>();
                var bgOrder = 950;
                if (bg != null)
                {
                    bg.sprite = Rounded();
                    bg.drawMode = SpriteDrawMode.Sliced;
                    bg.size = panelSize;
                    bg.color = Background;
                    bgOrder = bg.sortingOrder;
                }

                // 直下の四角いピンク枠は消して、角丸の細い縁に置き換える
                for (var i = root.transform.childCount - 1; i >= 0; i--)
                {
                    var c = root.transform.GetChild(i);
                    if (c.name.StartsWith("Border")) c.gameObject.SetActive(false);
                }
                MakeRect(root.transform, "ThemeOutline", panelSize + new Vector2(0.05f, 0.05f), new Vector3(0f, 0f, 0.06f), new Color(Accent.r, Accent.g, Accent.b, 0.85f), bgOrder - 1);

                // ヘッダー(タイトルの帯)と、その下の細いアクセント線
                var halfH = panelSize.y / 2f;
                MakeRect(root.transform, "ThemeHeader", new Vector2(panelSize.x - 0.1f, 0.62f), new Vector3(0f, halfH - 0.36f, -0.3f), Header, bgOrder + 1);
                MakeRect(root.transform, "ThemeHeaderLine", new Vector2(panelSize.x - 0.6f, 0.025f), new Vector3(0f, halfH - 0.69f, -0.3f), new Color(Accent.r, Accent.g, Accent.b, 0.55f), bgOrder + 1);

                if (title != null)
                {
                    title.color = Color.white;
                    title.fontStyle = FontStyles.Bold;
                }

                // 入力欄の枠など、中にある四角い枠線はアクセント色の薄い線にする
                foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (sr == null || sr.transform.parent == root.transform) continue;
                    if (sr.gameObject.name.StartsWith("Border")) sr.color = new Color(Accent.r, Accent.g, Accent.b, 0.45f);
                }

                // ボタンを角丸＋落ち着いた配色に
                foreach (var pb in root.GetComponentsInChildren<PassiveButton>(true))
                {
                    if (pb == null || pb.GetComponentInParent<TextBoxTMP>() != null) continue;
                    StyleButtonSprite(pb.inactiveSprites, false, bgOrder);
                    StyleButtonSprite(pb.activeSprites, true, bgOrder);
                }
            }
            catch (Exception e)
            {
                Logger.Warn($"BugUiTheme.Apply: {e.Message}", "BugUiTheme");
            }
        }

        private static void StyleButtonSprite(GameObject go, bool hover, int bgOrder)
        {
            if (go == null) return;
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr == null) return;
            var size = sr.size;
            sr.sprite = Rounded();
            sr.drawMode = SpriteDrawMode.Sliced;
            sr.size = size;
            sr.color = Remap(sr.color, hover);
            if (sr.sortingOrder <= bgOrder + 1) sr.sortingOrder = bgOrder + 3;
        }

        /// <summary>元の色の系統から、テーマの色に置き換える(ボタンの役割は色で判断する)</summary>
        private static Color Remap(Color c, bool hover)
        {
            Color32 n, h;
            var r = c.r; var g = c.g; var b = c.b;
            if (c.a < 0.05f) return c;                                    // 透明ボタンはそのまま
            if (r > 0.3f && r < 0.4f && g > 0.38f && b > 0.9f) return c;  // Discordの青はそのまま
            if (r > 0.65f && g < 0.35f) { n = new(255, 92, 138, 255); h = new(255, 128, 166, 255); }        // 赤系(送信・追加報告)
            else if (r > 0.4f && g < 0.2f) { n = new(64, 38, 56, 255); h = new(96, 52, 78, 255); }          // 暗い赤(閉じる)
            else if (g > r + 0.15f && g > b) { n = new(52, 190, 128, 255); h = new(76, 214, 152, 255); }   // 緑(次へ)
            else if (b > r + 0.15f) { n = new(70, 84, 140, 255); h = new(92, 108, 170, 255); }             // 青(更新など)
            else if (b > r + 0.05f) { n = new(38, 31, 52, 255); h = new(56, 46, 76, 255); }                // 一覧
            else { n = new(46, 39, 60, 255); h = new(64, 54, 84, 255); }                                   // 灰色(戻る・添付)
            return hover ? h : n;
        }
    }
}
