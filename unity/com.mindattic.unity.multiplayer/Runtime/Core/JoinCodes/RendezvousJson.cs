using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MindAttic.Unity.Multiplayer.Core
{
    /// <summary>
    /// The small JSON reader and writer the rendezvous client needs. Core has no JSON library (it is
    /// engine-free and netstandard2.1, and Unity ships no System.Text.Json), and every rendezvous body
    /// is one flat object of strings, numbers, booleans and nulls, so this handles exactly that.
    /// </summary>
    public static class RendezvousJson
    {
        /// <summary>
        /// Writes one flat object. Values may be null, <see cref="string"/>, <see cref="bool"/> or any integer
        /// type; a null value writes <c>null</c>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="fields"/> is null.</exception>
        /// <exception cref="ArgumentException">A value has a type this writer does not handle.</exception>
        public static string Write(IEnumerable<KeyValuePair<string, object>> fields)
        {
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }

            var json = new StringBuilder("{");
            bool first = true;
            foreach (KeyValuePair<string, object> field in fields)
            {
                if (!first)
                {
                    json.Append(',');
                }

                first = false;
                WriteString(json, field.Key);
                json.Append(':');
                WriteValue(json, field.Value);
            }

            return json.Append('}').ToString();
        }

        /// <summary>
        /// Reads one flat object into a dictionary (keys are case-sensitive). Strings come back as
        /// <see cref="string"/>, whole numbers as <see cref="long"/>, other numbers as <see cref="double"/>,
        /// booleans as <see cref="bool"/> and null as null. A nested object or array is skipped and read as null.
        /// </summary>
        /// <returns>False when <paramref name="text"/> is not a JSON object.</returns>
        public static bool TryParseObject(string text, out Dictionary<string, object> fields)
        {
            fields = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var reader = new Reader(text);
            try
            {
                Dictionary<string, object> result = reader.ReadObject();
                reader.SkipWhiteSpace();
                if (!reader.AtEnd)
                {
                    return false;
                }

                fields = result;
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>The string field <paramref name="name"/>, or null when it is missing or not a string.</summary>
        public static string GetString(IReadOnlyDictionary<string, object> fields, string name)
        {
            object value;
            return fields != null && fields.TryGetValue(name, out value) ? value as string : null;
        }

        /// <summary>The whole-number field <paramref name="name"/>, or null when it is missing or not a whole number.</summary>
        public static long? GetInteger(IReadOnlyDictionary<string, object> fields, string name)
        {
            object value;
            if (fields == null || !fields.TryGetValue(name, out value))
            {
                return null;
            }

            if (value is long l)
            {
                return l;
            }

            if (value is double d && Math.Abs(d - Math.Round(d)) < double.Epsilon && d >= long.MinValue && d <= long.MaxValue)
            {
                return (long)d;
            }

            return null;
        }

        private static void WriteValue(StringBuilder json, object value)
        {
            switch (value)
            {
                case null:
                    json.Append("null");
                    break;
                case string s:
                    WriteString(json, s);
                    break;
                case bool b:
                    json.Append(b ? "true" : "false");
                    break;
                case int i:
                    json.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    json.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case ushort us:
                    json.Append(us.ToString(CultureInfo.InvariantCulture));
                    break;
                case uint ui:
                    json.Append(ui.ToString(CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new ArgumentException("RendezvousJson cannot write a " + value.GetType().Name + ".", nameof(value));
            }
        }

        internal static void WriteString(StringBuilder json, string s)
        {
            json.Append('"');
            foreach (char c in s ?? string.Empty)
            {
                switch (c)
                {
                    case '"':
                        json.Append("\\\"");
                        break;
                    case '\\':
                        json.Append("\\\\");
                        break;
                    case '\n':
                        json.Append("\\n");
                        break;
                    case '\r':
                        json.Append("\\r");
                        break;
                    case '\t':
                        json.Append("\\t");
                        break;
                    default:
                        if (c < 0x20)
                        {
                            json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            json.Append(c);
                        }

                        break;
                }
            }

            json.Append('"');
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

            public Dictionary<string, object> ReadObject()
            {
                SkipWhiteSpace();
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
                    SkipWhiteSpace();
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

            private object ReadValue()
            {
                char c = Peek();
                switch (c)
                {
                    case '"':
                        return ReadString();
                    case '{':
                    case '[':
                        SkipContainer();
                        return null;
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

            private void SkipContainer()
            {
                int depth = 0;
                do
                {
                    char c = Peek();
                    if (c == '"')
                    {
                        ReadString();
                        continue;
                    }

                    pos++;
                    if (c == '{' || c == '[')
                    {
                        depth++;
                    }
                    else if (c == '}' || c == ']')
                    {
                        depth--;
                    }
                }
                while (depth > 0);
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
