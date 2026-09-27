using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace TownOfHost
{
    #region Sprite
    public static class UtilsSprite
    {
        // ===== 軽量化: スプライトキャッシュ =====
        // 設定メニュー(役職タブ・オプション項目)はタブ/役職の数だけ同じ画像
        // (ラベル背景・タブアイコン等)を繰り返し LoadSprite していたため、
        // 設定を開くたびに埋め込みリソースの読み込み+デコード+Texture2D生成が
        // 何百回も走り、これがホスト側の重さの一因になっていた。
        //
        // キャッシュは2段構成にしている:
        //   ・_textureCache : path 単位でデコード済みTexture2Dを保持 (一番重い「読み込み+デコード」はここで1回だけ)
        //   ・_spriteCache  : (path, pixelsPerUnit, border) 単位でSpriteを保持 (Sprite.Create自体は軽いのでppu違いでも都度作るだけで十分)
        // これにより「同じ画像を別のppuで呼ぶ」場合でも再デコードが発生しない。
        // さらに起動時のロード画面(HamoLoadingScreenPatch)で全埋め込み画像をあらかじめ
        // _textureCache に載せておくことで、実際にプレイ中/設定画面を開いた時のカクつきを減らせる。
        private static readonly Dictionary<string, Texture2D> _textureCache = new();
        private static readonly Dictionary<(string path, float ppu, Vector4 border), Sprite> _spriteCache = new();
        private static string[] _allEmbeddedImagePaths;

        /// <param name="border">
        /// 9-slice(Sliced描画)用の境界(left, bottom, right, top)。
        /// SpriteRendererがSliced/Tiledモードで使われる画像を差し替える場合は、
        /// 元Spriteのborderを渡すことで、伸縮時の見た目崩れを防げる。
        /// 通常のSimple描画の画像では省略してよい(既定はVector4.zero)。
        /// </param>
        public static Sprite LoadSprite(string path, float pixelsPerUnit = 1f, Vector4 border = default)
        {
            var key = (path, pixelsPerUnit, border);
            if (_spriteCache.TryGetValue(key, out var cachedSprite) && cachedSprite != null)
                return cachedSprite;

            var texture = GetOrLoadTexture(path);
            if (texture == null) return null;

            Sprite sprite = null;
            try
            {
                sprite = Sprite.Create(texture, new(0, 0, texture.width, texture.height), new(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
                _spriteCache[key] = sprite;
            }
            catch
            {
                Logger.Error($"\"{path}\"のSprite生成に失敗しました。", "LoadSprite");
            }
            return sprite;
        }

        /// <summary>
        /// 画像の「読み込み+デコード」だけを行いTexture2Dキャッシュに載せる。
        /// 既にキャッシュ済みならそれを返すだけで、余計な処理は走らない。
        /// </summary>
        public static Texture2D GetOrLoadTexture(string path)
        {
            if (_textureCache.TryGetValue(path, out var cached) && cached != null)
                return cached;

            Texture2D texture = null;
            try
            {
                var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(path);
                if (stream == null) return null;
                texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                using MemoryStream ms = new();
                stream.CopyTo(ms);
                ImageConversion.LoadImage(texture, ms.ToArray());
                _textureCache[path] = texture;
            }
            catch
            {
                Logger.Error($"\"{path}\"の読み込みに失敗しました。", "LoadSprite");
            }
            return texture;
        }

        /// <summary>
        /// Modに埋め込まれている画像リソースのパス一覧 (拡張子 .png) をキャッシュして返す。
        /// ロード画面での事前読み込みの進捗計算(何枚中何枚読み終えたか)に使う。
        /// </summary>
        public static string[] GetAllEmbeddedImagePaths()
        {
            return _allEmbeddedImagePaths ??= Assembly.GetExecutingAssembly()
                .GetManifestResourceNames()
                .Where(name => name.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, System.StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>すでにTexture2Dキャッシュに載っている埋め込み画像の枚数。</summary>
        public static int PreloadedTextureCount => _textureCache.Count;
    }
    #endregion
}
