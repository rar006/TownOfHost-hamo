using System.Collections.Generic;
using System.Text;

namespace TownOfHost
{
    /// <summary>設定画面の検索欄(MenuPatch)で使う、表記ゆれを吸収した検索と「次の候補」の状態</summary>
    public static class OptionSearch
    {
        public static string LastQuery = "";
        public static int Index;
        public static List<OptionItem> Results = new();

        /// <summary>全角→半角、大文字→小文字、ひらがな→カタカナにそろえ、空白を除く</summary>
        public static string Normalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (char.IsWhiteSpace(c)) continue;
                sb.Append(c is >= 'ぁ' and <= 'ゖ' ? (char)(c + 0x60) : c);
            }
            return sb.ToString();
        }

        /// <summary>0=完全一致 1=前方一致 2=部分一致 -1=一致しない</summary>
        public static int Rank(string name, string query)
        {
            if (name.Length == 0) return -1;
            if (name == query) return 0;
            if (name.StartsWith(query, System.StringComparison.Ordinal)) return 1;
            return name.Contains(query, System.StringComparison.Ordinal) ? 2 : -1;
        }
    }
}
