using System.Collections.Generic;
using System.IO;
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
        // 同じ(path, pixelsPerUnit, border)の組み合わせは一度読み込んだSpriteを使い回す。
        private static readonly Dictionary<(string path, float ppu, Vector4 border), Sprite> _spriteCache = new();

        /// <param name="border">
        /// 9-slice(Sliced描画)用の境界(left, bottom, right, top)。
        /// SpriteRendererがSliced/Tiledモードで使われる画像を差し替える場合は、
        /// 元Spriteのborderを渡すことで、伸縮時の見た目崩れを防げる。
        /// 通常のSimple描画の画像では省略してよい(既定はVector4.zero)。
        /// </param>
        public static Sprite LoadSprite(string path, float pixelsPerUnit = 1f, Vector4 border = default)
        {
            var key = (path, pixelsPerUnit, border);
            if (_spriteCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            Sprite sprite = null;
            try
            {
                var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(path);
                if (stream == null) return null;
                var texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
                using MemoryStream ms = new();
                stream.CopyTo(ms);
                ImageConversion.LoadImage(texture, ms.ToArray());
                sprite = Sprite.Create(texture, new(0, 0, texture.width, texture.height), new(0.5f, 0.5f), pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
                _spriteCache[key] = sprite;
            }
            catch
            {
                Logger.Error($"\"{path}\"の読み込みに失敗しました。", "LoadSprite");
            }
            return sprite;
        }
    }
}
#endregion
