using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Dil.Generator
{
    /// <summary>
    /// Tiny dependency-free parser for a flat JSON object of string -&gt; string.
    /// Keeps the analyzer free of any NuGet dependencies. Order-preserving.
    /// </summary>
    internal static class FlatJson
    {
        public static List<KeyValuePair<string, string>> Parse(string text)
        {
            var result = new List<KeyValuePair<string, string>>();
            int i = 0;
            SkipWs(text, ref i);
            if (i >= text.Length || text[i] != '{') return result;
            i++;
            while (i < text.Length)
            {
                SkipWs(text, ref i);
                if (i >= text.Length || text[i] == '}') break;
                if (text[i] == ',') { i++; continue; }
                if (text[i] != '"') { i++; continue; }
                var key = ReadString(text, ref i);
                SkipWs(text, ref i);
                if (i < text.Length && text[i] == ':') i++;
                SkipWs(text, ref i);
                if (i < text.Length && text[i] == '"')
                    result.Add(new KeyValuePair<string, string>(key, ReadString(text, ref i)));
                else
                    SkipValue(text, ref i);
            }
            return result;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static string ReadString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++; // opening quote
            while (i < s.Length)
            {
                var c = s[i++];
                if (c == '"') break;
                if (c == '\\' && i < s.Length)
                {
                    var e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'u':
                            if (i + 4 <= s.Length &&
                                int.TryParse(s.Substring(i, 4), NumberStyles.HexNumber,
                                    CultureInfo.InvariantCulture, out var code))
                            {
                                sb.Append((char)code);
                                i += 4;
                            }
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static void SkipValue(string s, ref int i)
        {
            int depth = 0;
            while (i < s.Length)
            {
                var c = s[i];
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') { if (depth == 0) break; depth--; }
                else if (c == ',' && depth == 0) break;
                i++;
            }
        }
    }
}
