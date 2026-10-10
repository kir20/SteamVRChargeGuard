using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ChargeGuard
{
    /// <summary>JSON object that keeps its keys in file order.</summary>
    sealed class JsonObject
    {
        public readonly List<KeyValuePair<string, object>> Items = new List<KeyValuePair<string, object>>();

        public object Get(string key)
        {
            foreach (var item in Items)
                if (item.Key == key) return item.Value;
            return null;
        }

        public void Set(string key, object value)
        {
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i].Key != key) continue;
                Items[i] = new KeyValuePair<string, object>(key, value);
                return;
            }
            Items.Add(new KeyValuePair<string, object>(key, value));
        }

        public void Remove(string key) => Items.RemoveAll(item => item.Key == key);
    }

    /// <summary>A number kept exactly as it was written, so round-tripping never changes it.</summary>
    sealed class JsonNumber
    {
        public readonly string Raw;
        public JsonNumber(string raw) { Raw = raw; }
    }

    sealed class JsonNull
    {
        public static readonly JsonNull Value = new JsonNull();
    }

    /// <summary>
    /// Minimal JSON reader/writer for SteamVR's settings files. Values are JsonObject,
    /// List&lt;object&gt;, string, bool, JsonNumber or JsonNull. Output uses SteamVR's own
    /// layout (3-space indent, "key" : value, CRLF).
    /// </summary>
    static class Json
    {
        public static object Parse(string text)
        {
            var parser = new Parser(text);
            parser.SkipWhitespace();
            object value = parser.ParseValue();
            parser.SkipWhitespace();
            if (!parser.AtEnd) throw parser.Error("unexpected data after the end");
            return value;
        }

        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, 0);
            sb.Append("\r\n");
            return sb.ToString();
        }

        sealed class Parser
        {
            readonly string text;
            int pos;

            public Parser(string text)
            {
                this.text = text;
                if (text.Length > 0 && text[0] == '﻿') pos = 1;
            }

            public bool AtEnd => pos >= text.Length;

            public FormatException Error(string message) =>
                new FormatException($"JSON: {message} at offset {pos}");

            public void SkipWhitespace()
            {
                while (pos < text.Length && char.IsWhiteSpace(text[pos])) pos++;
            }

            void Expect(string token)
            {
                if (string.CompareOrdinal(text, pos, token, 0, token.Length) != 0) throw Error($"expected '{token}'");
                pos += token.Length;
            }

            public object ParseValue()
            {
                if (AtEnd) throw Error("unexpected end");
                switch (text[pos])
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return JsonNull.Value;
                    default: return ParseNumber();
                }
            }

            JsonObject ParseObject()
            {
                Expect("{");
                var obj = new JsonObject();
                SkipWhitespace();
                if (!AtEnd && text[pos] == '}') { pos++; return obj; }
                while (true)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();
                    Expect(":");
                    SkipWhitespace();
                    obj.Items.Add(new KeyValuePair<string, object>(key, ParseValue()));
                    SkipWhitespace();
                    if (!AtEnd && text[pos] == ',') { pos++; continue; }
                    Expect("}");
                    return obj;
                }
            }

            List<object> ParseArray()
            {
                Expect("[");
                var list = new List<object>();
                SkipWhitespace();
                if (!AtEnd && text[pos] == ']') { pos++; return list; }
                while (true)
                {
                    SkipWhitespace();
                    list.Add(ParseValue());
                    SkipWhitespace();
                    if (!AtEnd && text[pos] == ',') { pos++; continue; }
                    Expect("]");
                    return list;
                }
            }

            string ParseString()
            {
                Expect("\"");
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("unterminated string");
                    char c = text[pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("unterminated escape");
                    char e = text[pos++];
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
                            if (pos + 4 > text.Length) throw Error("bad \\u escape");
                            sb.Append((char)int.Parse(text.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            pos += 4;
                            break;
                        default: throw Error($"bad escape '\\{e}'");
                    }
                }
            }

            JsonNumber ParseNumber()
            {
                int start = pos;
                while (pos < text.Length && "+-0123456789.eE".IndexOf(text[pos]) >= 0) pos++;
                if (pos == start) throw Error($"unexpected '{text[pos]}'");
                return new JsonNumber(text.Substring(start, pos - start));
            }
        }

        static void WriteValue(StringBuilder sb, object value, int depth)
        {
            switch (value)
            {
                case JsonObject obj:
                    if (obj.Items.Count == 0) { sb.Append("{}"); return; }
                    sb.Append("{\r\n");
                    for (int i = 0; i < obj.Items.Count; i++)
                    {
                        sb.Append(' ', (depth + 1) * 3);
                        WriteString(sb, obj.Items[i].Key);
                        sb.Append(" : ");
                        WriteValue(sb, obj.Items[i].Value, depth + 1);
                        if (i < obj.Items.Count - 1) sb.Append(',');
                        sb.Append("\r\n");
                    }
                    sb.Append(' ', depth * 3).Append('}');
                    return;
                case List<object> list:
                    if (list.Count == 0) { sb.Append("[]"); return; }
                    sb.Append("[\r\n");
                    for (int i = 0; i < list.Count; i++)
                    {
                        sb.Append(' ', (depth + 1) * 3);
                        WriteValue(sb, list[i], depth + 1);
                        if (i < list.Count - 1) sb.Append(',');
                        sb.Append("\r\n");
                    }
                    sb.Append(' ', depth * 3).Append(']');
                    return;
                case string s: WriteString(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case JsonNumber n: sb.Append(n.Raw); return;
                default: sb.Append("null"); return;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
