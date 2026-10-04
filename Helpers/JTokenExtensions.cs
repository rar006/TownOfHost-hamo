using Newtonsoft.Json.Linq;

namespace TownOfHost;

/// <summary>
/// JToken型の変数に対する token["key"] は、コンパイル時に JToken.get_Item(object) を呼ぶコードになる。
/// ゲーム/BepInEx側が読み込む Newtonsoft.Json のバージョンによってはこのメソッドが存在せず、
/// 実行時に MissingMethodException になる(アップデート確認・ブラックリスト・MODニュース取得が失敗していた)。
/// JObject.GetValue(string) 経由で取得すれば、どのバージョンでも動く。
/// </summary>
public static class JTokenExtensions
{
    /// <summary>token["key"] の代わり。キーが無い/オブジェクトでない場合は null を返す</summary>
    public static JToken GetJ(this JToken token, string key)
        => (token as JObject)?.GetValue(key);
}
