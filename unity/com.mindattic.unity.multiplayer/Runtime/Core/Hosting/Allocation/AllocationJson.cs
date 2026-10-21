using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Unity.Multiplayer.Core
{
    /// <summary>
    /// A small recursive JSON reader and writer for the Edgegap and Gameye adapters' nested API
    /// bodies (<see cref="RendezvousJson"/> only reads one flat object, which is enough for the
    /// rendezvous protocol but not for these). A value is <see langword="null"/>, <see cref="string"/>,
    /// <see cref="bool"/>, <see cref="long"/>/<see cref="double"/>, a <c>Dictionary&lt;string, object&gt;</c>
    /// (object) or a <c>List&lt;object&gt;</c> (array).
    /// </summary>
    internal static class AllocationJson
    {
        /// <summary>Writes a value tree (built from <see cref="Dictionary{TKey,TValue}"/>, <see cref="List{T}"/> and the scalar types above).</summary>
        public static string Write(object value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        /// <summary>Parses any JSON value. False when <paramref name="text"/> is not valid JSON.</summary>
        public static bool TryParse(string text, out object value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var reader = new Reader(text);
            try
            {
                reader.SkipWhiteSpace();
                object result = reader.ReadValue();
                reader.SkipWhiteSpace();
                if (!reader.AtEnd)
                {
                    return false;
                }

                value = result;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary><paramref name="value"/> as a JSON object, or null when it is not one.</summary>
        public static Dictionary<string, object> AsObject(object value)
        {
            return value as Dictionary<string, object>;
        }

        /// <summary>The string field <paramref name="name"/> of <paramref name="obj"/>, or null when missing or not a string.</summary>
        public static string GetString(Dictionary<string, object> obj, string name)
        {
            object value;
            return obj != null && obj.TryGetValue(name, out value) ? value as string : null;
        }

        /// <summary>The whole-number field <paramref name="name"/> of <paramref name="obj"/>, or null when missing or not a whole number.</summary>
        public static long? GetInteger(Dictionary<string, object> obj, string name)
        {
            object value;
            if (obj == null || !obj.TryGetValue(name, out value))
            {
                return null;
            }

            if (value is long l)
            {
                return l;
            }

            if (value is double d && Math.Abs(d - Math.Round(d)) < double.Epsilon)
            {
                return (long)d;
            }

            return null;
        }

        /// <summary>The boolean field <paramref name="name"/> of <paramref name="obj"/>, or null when missing or not a boolean.</summary>
        public static bool? GetBool(Dictionary<string, object> obj, string name)
        {
            object value;
            return obj != null && obj.TryGetValue(name, out value) && value is bool b ? (bool?)b : null;
        }

        /// <summary>The object field <paramref name="name"/> of <paramref name="obj"/>, or null when missing or not an object.</summary>
        public static Dictionary<string, object> GetObject(Dictionary<string, object> obj, string name)
        {
            object value;
            return obj != null && obj.TryGetValue(name, out value) ? value as Dictionary<string, object> : null;
        }

        /// <summary>The array field <paramref name="name"/> of <paramref name="obj"/>, or null when missing or not an array.</summary>
        public static List<object> GetArray(Dictionary<string, object> obj, string name)
        {
            object value;
            return obj != null && obj.TryGetValue(name, out value) ? value as List<object> : null;
        }

        private static void WriteValue(StringBuilder sb, object value)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            if (value is string s)
            {
                WriteString(sb, s);
                return;
            }

            if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }

            if (value is int i)
            {
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (value is long l)
            {
                sb.Append(l.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (value is double d)
            {
                sb.Append(d.ToString(CultureInfo.InvariantCulture));
                return;
            }

            var obj = value as Dictionary<string, object>;
            if (obj != null)
            {
                sb.Append('{');
                bool firstField = true;
                foreach (KeyValuePair<string, object> field in obj)
                {
                    if (!firstField)
                    {
                        sb.Append(',');
                    }

                    firstField = false;
                    WriteString(sb, field.Key);
                    sb.Append(':');
                    WriteValue(sb, field.Value);
                }

                sb.Append('}');
                return;
            }

            var list = value as System.Collections.IEnumerable;
            if (list != null)
            {
                sb.Append('[');
                bool firstItem = true;
                foreach (object item in list)
                {
                    if (!firstItem)
                    {
                        sb.Append(',');
                    }

                    firstItem = false;
                    WriteValue(sb, item);
                }

                sb.Append(']');
                return;
            }

            throw new ArgumentException("AllocationJson cannot write a " + value.GetType().Name + ".", nameof(value));
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s ?? string.Empty)
            {
                switch (c)
                {
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }

                        break;
                }
            }

            sb.Append('"');
        }

        private sealed class Reader
        {
            private readonly string text;
            private int pos;

            public Reader(string text)
            {
                this.text = text;
            }

            public bool AtEnd
            {
                get { return pos >= text.Length; }
            }

            public void SkipWhiteSpace()
            {
                while (pos < text.Length && char.IsWhiteSpace(text[pos]))
                {
                    pos++;
                }
            }

            public object ReadValue()
            {
                SkipWhiteSpace();
                char c = Peek();
                switch (c)
                {
                    case '{':
                        return ReadObject();
                    case '[':
                        return ReadArray();
                    case '"':
                        return ReadString();
                    case 't':
                        ExpectWord("true");
                        return true;
                    case 'f':
                        ExpectWord("false");
                        return false;
                    case 'n':
                        ExpectWord("null");
                        return null;
                    default:
                        return ReadNumber();
                }
            }

            private Dictionary<string, object> ReadObject()
            {
                Expect('{');
                var fields = new Dictionary<string, object>(StringComparer.Ordinal);
                SkipWhiteSpace();
                if (Peek() == '}')
                {
                    pos++;
                    return fields;
                }

                while (true)
                {
                    SkipWhiteSpace();
                    string key = ReadString();
                    SkipWhiteSpace();
                    Expect(':');
                    fields[key] = ReadValue();
                    SkipWhiteSpace();
                    char c = Next();
                    if (c == '}')
                    {
                        return fields;
                    }

                    if (c != ',')
                    {
                        throw new FormatException("Expected , or } at " + (pos - 1) + ".");
                    }
                }
            }

            private List<object> ReadArray()
            {
                Expect('[');
                var items = new List<object>();
                SkipWhiteSpace();
                if (Peek() == ']')
                {
                    pos++;
                    return items;
                }

                while (true)
                {
                    SkipWhiteSpace();
                    items.Add(ReadValue());
                    SkipWhiteSpace();
                    char c = Next();
                    if (c == ']')
                    {
                        return items;
                    }

                    if (c != ',')
                    {
                        throw new FormatException("Expected , or ] at " + (pos - 1) + ".");
                    }
                }
            }

            private object ReadNumber()
            {
                int start = pos;
                while (pos < text.Length && "+-0123456789.eE".IndexOf(text[pos]) >= 0)
                {
                    pos++;
                }

                string number = text.Substring(start, pos - start);
                long whole;
                if (long.TryParse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out whole))
                {
                    return whole;
                }

                double real;
                if (double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out real))
                {
                    return real;
                }

                throw new FormatException("Bad value at " + start + ".");
            }

            private string ReadString()
            {
                Expect('"');
                var s = new StringBuilder();
                while (true)
                {
                    char c = Next();
                    if (c == '"')
                    {
                        return s.ToString();
                    }

                    if (c != '\\')
                    {
                        s.Append(c);
                        continue;
                    }

                    char e = Next();
                    switch (e)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            s.Append(e);
                            break;
                        case 'b':
                            s.Append('\b');
                            break;
                        case 'f':
                            s.Append('\f');
                            break;
                        case 'n':
                            s.Append('\n');
                            break;
                        case 'r':
                            s.Append('\r');
                            break;
                        case 't':
                            s.Append('\t');
                            break;
                        case 'u':
                            if (pos + 4 > text.Length)
                            {
                                throw new FormatException("Truncated \\u escape.");
                            }

                            int code;
                            if (!int.TryParse(text.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                            {
                                throw new FormatException("Bad \\u escape at " + pos + ".");
                            }

                            s.Append((char)code);
                            pos += 4;
                            break;
                        default:
                            throw new FormatException("Bad escape \\" + e + ".");
                    }
                }
            }

            private void ExpectWord(string word)
            {
                if (string.CompareOrdinal(text, pos, word, 0, word.Length) != 0)
                {
                    throw new FormatException("Expected " + word + " at " + pos + ".");
                }

                pos += word.Length;
            }

            private void Expect(char c)
            {
                if (Next() != c)
                {
                    throw new FormatException("Expected " + c + " at " + (pos - 1) + ".");
                }
            }

            private char Peek()
            {
                if (pos >= text.Length)
                {
                    throw new FormatException("Unexpected end of JSON.");
                }

                return text[pos];
            }

            private char Next()
            {
                char c = Peek();
                pos++;
                return c;
            }
        }
    }
}
