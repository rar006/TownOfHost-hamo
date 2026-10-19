using System.Collections.Generic;
using System.Text;

namespace TownOfHost
{
    public static class OptionSearch
    {
        public static string LastQuery = "";
        public static int Index;
        public static List<OptionItem> Results = new();

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

        public static int Rank(string name, string query)
        {
            if (name.Length == 0) return -1;
            if (name == query) return 0;
            if (name.StartsWith(query, System.StringComparison.Ordinal)) return 1;
            return name.Contains(query, System.StringComparison.Ordinal) ? 2 : -1;
        }
    }
}
