using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PartnerLive2D.Framework
{
    // Object node that remembers key insertion order (Dictionary order is not contractual).
    public sealed class JsonObject : Dictionary<string, object>
    {
        public readonly List<string> KeyOrder = new List<string>();
    }

    // Minimal JSON parser: objects -> Dictionary<string,object> (JsonObject),
    // arrays -> List<object>, numbers -> double, strings -> string, bool, null.
    public static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new ArgumentNullException("text");
            int i = 0;
            if (text.Length > 0 && text[0] == '﻿') i = 1;
            SkipWs(text, ref i);
            object v = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i != text.Length) throw new FormatException("Trailing characters in JSON at " + i);
            return v;
        }

        public static object ParseFile(string path)
        {
            return Parse(System.IO.File.ReadAllText(path, Encoding.UTF8));
        }

        // ---- accessors (all tolerant: missing/mistyped -> default) -----------------

        public static object Get(object node, string key)
        {
            var d = node as Dictionary<string, object>;
            object v;
            return d != null && d.TryGetValue(key, out v) ? v : null;
        }

        public static object Path(object node, params string[] keys)
        {
            for (int i = 0; i < keys.Length && node != null; i++) node = Get(node, keys[i]);
            return node;
        }

        public static List<object> GetList(object node, string key)
        {
            return Get(node, key) as List<object>;
        }

        public static string GetString(object node, string key, string def = null)
        {
            var s = Get(node, key) as string;
            return s ?? def;
        }

        public static float GetFloat(object node, string key, float def = 0f)
        {
            return ToFloat(Get(node, key), def);
        }

        public static int GetInt(object node, string key, int def = 0)
        {
            object v = Get(node, key);
            return v is double ? (int)(double)v : def;
        }

        public static bool GetBool(object node, string key, bool def = false)
        {
            object v = Get(node, key);
            return v is bool ? (bool)v : def;
        }

        public static float ToFloat(object v, float def = 0f)
        {
            if (v is double) return (float)(double)v;
            return def;
        }

        public static bool IsNull(object node, string key)
        {
            return Get(node, key) == null;
        }

        // ---- parser ----------------------------------------------------------------

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n') i++;
                else break;
            }
        }

        static object ParseValue(string s, ref int i)
        {
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            switch (c)
            {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true"); return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null"); return null;
                default: return ParseNumber(s, ref i);
            }
        }

        static void Expect(string s, ref int i, string word)
        {
            if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException("Bad JSON literal at " + i);
            i += word.Length;
        }

        static object ParseObject(string s, ref int i)
        {
            var obj = new JsonObject();
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return obj; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("Expected string key at " + i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("Expected ':' at " + i);
                i++;
                SkipWs(s, ref i);
                object v = ParseValue(s, ref i);
                if (!obj.ContainsKey(key)) obj.KeyOrder.Add(key);
                obj[key] = v;
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated object");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                throw new FormatException("Expected ',' or '}' at " + i);
            }
            return obj;
        }

        static object ParseArray(string s, ref int i)
        {
            var list = new List<object>();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                SkipWs(s, ref i);
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("Unterminated array");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                throw new FormatException("Expected ',' or ']' at " + i);
            }
            return list;
        }

        static string ParseString(string s, ref int i)
        {
            i++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (i >= s.Length) throw new FormatException("Unterminated string");
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("Bad escape");
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
                        if (i + 4 > s.Length) throw new FormatException("Bad \\u escape");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("Bad escape \\" + e);
                }
            }
            return sb.ToString();
        }

        static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length)
            {
                char c = s[i];
                if ((c >= '0' && c <= '9') || c == '-' || c == '+' || c == '.' || c == 'e' || c == 'E') i++;
                else break;
            }
            if (start == i) throw new FormatException("Unexpected character at " + i);
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
