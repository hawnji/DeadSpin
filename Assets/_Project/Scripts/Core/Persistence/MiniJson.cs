using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace DeadSpin.Core.Persistence
{
    /// <summary>
    /// A tiny JSON reader and writer with no dependencies, so saves work the same in Unity and in
    /// headless tests. Objects are Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers
    /// long when integral (double otherwise), plus string, bool and null.
    /// </summary>
    public static class MiniJson
    {
        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); return;
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case ulong u: sb.Append(u.ToString(CultureInfo.InvariantCulture)); return;
                case float f: sb.Append(((double)f).ToString("R", CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
                case Enum e: sb.Append(Convert.ToInt64(e).ToString(CultureInfo.InvariantCulture)); return;
                case IDictionary<string, object> map:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (KeyValuePair<string, object> pair in map)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, pair.Key);
                        sb.Append(':');
                        Write(sb, pair.Value);
                    }
                    sb.Append('}');
                    return;
                }
                case System.Collections.IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (object item in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    return;
                }
                default: WriteString(sb, value.ToString()); return;
            }
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        public static object Deserialize(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            int index = 0;
            object value = ParseValue(json, ref index);
            SkipWhitespace(json, ref index);
            if (index != json.Length) throw new FormatException("Unexpected text after JSON value at " + index + ".");
            return value;
        }

        private static void SkipWhitespace(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWhitespace(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON.");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't' && Matches(s, i, "true")) { i += 4; return true; }
            if (c == 'f' && Matches(s, i, "false")) { i += 5; return false; }
            if (c == 'n' && Matches(s, i, "null")) { i += 4; return null; }
            return ParseNumber(s, ref i);
        }

        private static bool Matches(string s, int i, string word)
        {
            return string.CompareOrdinal(s, i, word, 0, word.Length) == 0;
        }

        private static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var map = new Dictionary<string, object>();
            i++;
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }
            while (true)
            {
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("Expected a key at " + i + ".");
                string key = ParseString(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("Expected ':' at " + i + ".");
                i++;
                map[key] = ParseValue(s, ref i);
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated object.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return map; }
                throw new FormatException("Expected ',' or '}' at " + i + ".");
            }
        }

        private static List<object> ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++;
            SkipWhitespace(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWhitespace(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated array.");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return list; }
                throw new FormatException("Expected ',' or ']' at " + i + ".");
            }
        }

        private static string ParseString(string s, ref int i)
        {
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) break;
                char e = s[i++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("Bad unicode escape.");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("Bad escape \\" + e + ".");
                }
            }
            throw new FormatException("Unterminated string.");
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            string text = s.Substring(start, i - start);
            if (text.Length == 0) throw new FormatException("Unexpected character '" + s[start] + "' at " + start + ".");
            long l;
            if (text.IndexOfAny(new[] { '.', 'e', 'E' }) < 0 && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
            ulong u;
            if (text.IndexOfAny(new[] { '.', 'e', 'E', '-' }) < 0 && ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out u)) return u;
            return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Typed reads from parsed JSON with defaults, so old or partial saves load safely.</summary>
    public static class JsonRead
    {
        public static object Get(IDictionary<string, object> map, string key)
        {
            object v;
            return map != null && map.TryGetValue(key, out v) ? v : null;
        }

        public static long Long(IDictionary<string, object> map, string key, long fallback = 0)
        {
            object v = Get(map, key);
            switch (v)
            {
                case long l: return l;
                case ulong u: return (long)u;
                case double d: return (long)d;
                case bool b: return b ? 1 : 0;
                default: return fallback;
            }
        }

        public static ulong ULong(IDictionary<string, object> map, string key, ulong fallback = 0)
        {
            object v = Get(map, key);
            switch (v)
            {
                case ulong u: return u;
                case long l: return unchecked((ulong)l);
                case double d: return (ulong)d;
                default: return fallback;
            }
        }

        public static int Int(IDictionary<string, object> map, string key, int fallback = 0) { return (int)Long(map, key, fallback); }

        public static double Double(IDictionary<string, object> map, string key, double fallback = 0)
        {
            object v = Get(map, key);
            switch (v)
            {
                case double d: return d;
                case long l: return l;
                case ulong u: return u;
                default: return fallback;
            }
        }

        public static bool Bool(IDictionary<string, object> map, string key, bool fallback = false)
        {
            object v = Get(map, key);
            return v is bool b ? b : fallback;
        }

        public static string String(IDictionary<string, object> map, string key, string fallback = null)
        {
            object v = Get(map, key);
            return v is string s ? s : fallback;
        }

        public static IDictionary<string, object> Object(IDictionary<string, object> map, string key)
        {
            return Get(map, key) as IDictionary<string, object>;
        }

        public static List<object> Array(IDictionary<string, object> map, string key)
        {
            return Get(map, key) as List<object> ?? new List<object>();
        }

        public static long AsLong(object v)
        {
            switch (v)
            {
                case long l: return l;
                case ulong u: return (long)u;
                case double d: return (long)d;
                default: return 0;
            }
        }
    }
}
