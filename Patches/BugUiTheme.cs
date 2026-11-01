using System;
using TMPro;
using UnityEngine;

namespace TownOfHost.Patches
{
    public static class BugUiTheme
    {
        // TOH-hm のオプション画面と同じ見た目: 黒い背景・白い二重のふち・ふち付きの暗いボタン(選ぶとピンク)
        public static readonly Color Background = new Color32(0, 0, 0, 248);
        public static readonly Color Header = new Color32(18, 18, 20, 255);
        public static readonly Color Accent = new Color32(255, 133, 217, 255);
        public static readonly Color32 SubtleText = new(170, 170, 170, 255);
        private static readonly Color ButtonOutline = new Color32(110, 110, 116, 255);

        private static Sprite _rounded;

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

        private static Sprite _frame;

        // 角丸のふちだけの絵。UiSkin.Frame は基準点が左下なので、ここで使うと枠がボタンの右上にずれて出ていた
        public static Sprite Frame()
        {
            if (_frame != null) return _frame;
            const int size = 64, radius = 16;
            const float width = 2.4f;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                    var cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                    var d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    var a = Mathf.Clamp01(radius - d + 0.5f) - Mathf.Clamp01(radius - width - d + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            _frame = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 130f, 0, SpriteMeshType.FullRect, new Vector4(radius + 2, radius + 2, radius + 2, radius + 2));
            _frame.hideFlags = HideFlags.DontUnloadUnusedAsset;
            _frame.texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return _frame;
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
                MakeRect(root.transform, "ThemeOutline", panelSize + new Vector2(0.09f, 0.09f), new Vector3(0f, 0f, 0.06f), Color.white, bgOrder - 1);
                var inner = MakeRect(root.transform, "ThemeInnerLine", panelSize - new Vector2(0.08f, 0.08f), new Vector3(0f, 0f, -0.05f), new Color(1f, 1f, 1f, 0.45f), bgOrder + 1);
                inner.sprite = Frame();
                inner.size = panelSize - new Vector2(0.08f, 0.08f);
                // ヘッダー(タイトルの帯)と、その下の細いアクセント線
                var halfH = panelSize.y / 2f;
                MakeRect(root.transform, "ThemeHeader", new Vector2(panelSize.x - 0.1f, 0.62f), new Vector3(0f, halfH - 0.36f, -0.3f), Header, bgOrder + 1);
                MakeRect(root.transform, "ThemeHeaderLine", new Vector2(panelSize.x - 0.6f, 0.025f), new Vector3(0f, halfH - 0.69f, -0.3f), new Color(1f, 1f, 1f, 0.3f), bgOrder + 1);

                if (title != null)
                {
                    title.color = Color.white;
                    title.fontStyle = FontStyles.Bold;
                }

                // 入力欄の枠など、中にある四角い枠線はアクセント色の薄い線にする
                foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>(true))
                {
                    if (sr == null || sr.transform.parent == root.transform) continue;
                    if (sr.gameObject.name.StartsWith("Border")) sr.color = new Color(1f, 1f, 1f, 0.4f);
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
            // ボタンのふち(ふだんは灰色、カーソルを乗せるとピンク)
            if (sr.color.a >= 0.05f && go.transform.Find("ThemeButtonOutline") == null)
            {
                var o = new GameObject("ThemeButtonOutline");
                o.layer = go.layer;
                o.transform.SetParent(go.transform, false);
                o.transform.localPosition = new Vector3(0f, 0f, -0.01f);
                var or = o.AddComponent<SpriteRenderer>();
                or.sprite = Frame();
                or.drawMode = SpriteDrawMode.Sliced;
                or.size = size;
                or.color = hover ? Accent : ButtonOutline;
                or.sortingOrder = sr.sortingOrder + 1;
            }
        }

        private static Color Remap(Color c, bool hover)
        {
            Color32 n, h;
            var r = c.r; var g = c.g; var b = c.b;
            if (c.a < 0.05f) return c;                                    // 透明ボタンはそのまま
            if (r > 0.3f && r < 0.4f && g > 0.38f && b > 0.9f) return c;  // Discordの青はそのまま
            if (r > 0.65f && g < 0.35f) { n = new(200, 70, 150, 255); h = new(255, 110, 200, 255); }        // 赤系(送信・追加報告)はピンクで目立たせる
            else if (g > r + 0.15f && g > b) { n = new(40, 120, 90, 255); h = new(56, 150, 112, 255); }     // 緑(次へ)
            else { n = new(14, 14, 16, 255); h = new(30, 30, 34, 255); }                                    // そのほかは黒いボタン
            return hover ? h : n;
        }
    }
}
