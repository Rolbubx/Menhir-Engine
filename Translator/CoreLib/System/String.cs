using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>
    /// UTF-16 text, immutable. The layout matches the runtime's: the length, then the characters
    /// (starting at _firstChar).
    /// </summary>
    public sealed class String : IComparable, IComparable<string>, IEquatable<string>, System.Collections.Generic.IEnumerable<char>
    {
        private int _length;
        private char _firstChar;

        public static readonly string Empty = "";

        public int Length => _length;

        public unsafe char this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_length)
                    ThrowHelpers.IndexOutOfRange();
                fixed (char* chars = &_firstChar)
                    return chars[index];
            }
        }

        /// <summary>A new string of <paramref name="length"/> zero characters, to fill in.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern string FastAllocate(int length);

        internal static unsafe string FromChar(char c)
        {
            string s = FastAllocate(1);
            fixed (char* chars = &s._firstChar)
                chars[0] = c;
            return s;
        }

        public override string ToString() => this;

        /// <summary>Lets <c>fixed (char* p = text)</c> point at the first character.</summary>
        public ref char GetPinnableReference() => ref _firstChar;

        public static unsafe string Concat(string a, string b)
        {
            if (a == null || a._length == 0)
                return b ?? Empty;
            if (b == null || b._length == 0)
                return a;
            string result = FastAllocate(a._length + b._length);
            fixed (char* r = &result._firstChar, pa = &a._firstChar, pb = &b._firstChar)
            {
                for (int i = 0; i < a._length; i++)
                    r[i] = pa[i];
                for (int i = 0; i < b._length; i++)
                    r[a._length + i] = pb[i];
            }
            return result;
        }

        public static string Concat(string a, string b, string c) => Concat(Concat(a, b), c);

        public static string Concat(string a, string b, string c, string d) => Concat(Concat(a, b), Concat(c, d));

        public static string Concat(params string[] values)
        {
            string result = Empty;
            for (int i = 0; i < values.Length; i++)
                result = Concat(result, values[i]);
            return result;
        }

        public static string Concat(object a, object b) => Concat(a?.ToString(), b?.ToString());

        public static string Concat(object a, object b, object c) => Concat(a?.ToString(), b?.ToString(), c?.ToString());

        public static string Concat(params object[] values)
        {
            string result = Empty;
            for (int i = 0; i < values.Length; i++)
                result = Concat(result, values[i]?.ToString());
            return result;
        }

        public static unsafe bool Equals(string a, string b)
        {
            if ((object)a == b)
                return true;
            if (a == null || b == null || a._length != b._length)
                return false;
            fixed (char* pa = &a._firstChar, pb = &b._firstChar)
            {
                for (int i = 0; i < a._length; i++)
                {
                    if (pa[i] != pb[i])
                        return false;
                }
            }
            return true;
        }

        public override bool Equals(object obj) => obj is string s && Equals(this, s);

        public bool Equals(string value) => Equals(this, value);

        public static bool operator ==(string a, string b) => Equals(a, b);

        public static bool operator !=(string a, string b) => !Equals(a, b);

        public override unsafe int GetHashCode()
        {
            // FNV-1a over the characters (not the same values as .NET, which randomizes them).
            uint hash = 2166136261;
            fixed (char* p = &_firstChar)
            {
                for (int i = 0; i < _length; i++)
                    hash = (hash ^ p[i]) * 16777619;
            }
            return (int)hash;
        }

        public unsafe string Substring(int startIndex, int length)
        {
            if (startIndex < 0 || length < 0 || startIndex > _length - length)
                ThrowHelpers.ArgumentOutOfRange();
            if (length == 0)
                return Empty;
            string result = FastAllocate(length);
            fixed (char* r = &result._firstChar, p = &_firstChar)
            {
                for (int i = 0; i < length; i++)
                    r[i] = p[startIndex + i];
            }
            return result;
        }

        public string Substring(int startIndex) => Substring(startIndex, _length - startIndex);

        public unsafe int IndexOf(char value)
        {
            fixed (char* p = &_firstChar)
            {
                for (int i = 0; i < _length; i++)
                {
                    if (p[i] == value)
                        return i;
                }
            }
            return -1;
        }

        public bool StartsWith(string value) =>
            value._length <= _length && Equals(Substring(0, value._length), value);

        public static bool IsNullOrEmpty(string value) => value == null || value._length == 0;

        // ---- Constructors: "new string(...)" calls these (the translator maps String..ctor to String.Ctor) ----

        public extern String(char c, int count);
        public extern String(char[] value);
        public extern String(char[] value, int startIndex, int length);

        private static unsafe string Ctor(char c, int count)
        {
            if (count < 0)
                ThrowHelpers.ArgumentOutOfRange();
            string s = FastAllocate(count);
            fixed (char* p = &s._firstChar)
            {
                for (int i = 0; i < count; i++)
                    p[i] = c;
            }
            return s;
        }

        private static string Ctor(char[] value) => value == null ? Empty : Ctor(value, 0, value.Length);

        private static unsafe string Ctor(char[] value, int startIndex, int length)
        {
            if (startIndex < 0 || length < 0 || startIndex > value.Length - length)
                ThrowHelpers.ArgumentOutOfRange();
            string s = FastAllocate(length);
            fixed (char* p = &s._firstChar)
            {
                for (int i = 0; i < length; i++)
                    p[i] = value[startIndex + i];
            }
            return s;
        }

        // ---- Searching ----

        public static bool IsNullOrWhiteSpace(string value)
        {
            if (value == null)
                return true;
            for (int i = 0; i < value._length; i++)
            {
                if (!char.IsWhiteSpace(value[i]))
                    return false;
            }
            return true;
        }

        public int IndexOf(char value, int startIndex)
        {
            for (int i = startIndex; i < _length; i++)
            {
                if (this[i] == value)
                    return i;
            }
            return -1;
        }

        public int IndexOf(string value) => IndexOf(value, 0);

        public int IndexOf(string value, int startIndex)
        {
            if (value == null)
                throw new ArgumentNullException("Value cannot be null. (Parameter 'value')");
            for (int i = startIndex; i <= _length - value._length; i++)
            {
                if (MatchesAt(i, value))
                    return i;
            }
            return -1;
        }

        public int LastIndexOf(char value)
        {
            for (int i = _length - 1; i >= 0; i--)
            {
                if (this[i] == value)
                    return i;
            }
            return -1;
        }

        public int LastIndexOf(string value)
        {
            for (int i = _length - value._length; i >= 0; i--)
            {
                if (MatchesAt(i, value))
                    return i;
            }
            return -1;
        }

        public int IndexOfAny(char[] anyOf)
        {
            for (int i = 0; i < _length; i++)
            {
                for (int j = 0; j < anyOf.Length; j++)
                {
                    if (this[i] == anyOf[j])
                        return i;
                }
            }
            return -1;
        }

        private bool MatchesAt(int index, string value)
        {
            for (int j = 0; j < value._length; j++)
            {
                if (this[index + j] != value[j])
                    return false;
            }
            return true;
        }

        public bool Contains(string value) => IndexOf(value) >= 0;
        public bool Contains(char value) => IndexOf(value) >= 0;

        public bool StartsWith(char value) => _length > 0 && this[0] == value;
        public bool EndsWith(string value) => value._length <= _length && MatchesAt(_length - value._length, value);
        public bool EndsWith(char value) => _length > 0 && this[_length - 1] == value;

        // ---- Transforming ----

        public string ToUpper() => ToUpperInvariant();
        public string ToLower() => ToLowerInvariant();

        public string ToUpperInvariant()
        {
            var chars = ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                chars[i] = char.ToUpperInvariant(chars[i]);
            return new string(chars);
        }

        public string ToLowerInvariant()
        {
            var chars = ToCharArray();
            for (int i = 0; i < chars.Length; i++)
                chars[i] = char.ToLowerInvariant(chars[i]);
            return new string(chars);
        }

        public char[] ToCharArray()
        {
            var chars = new char[_length];
            for (int i = 0; i < _length; i++)
                chars[i] = this[i];
            return chars;
        }

        public string Trim() => TrimEnd().TrimStart();

        public string TrimStart()
        {
            int start = 0;
            while (start < _length && char.IsWhiteSpace(this[start]))
                start++;
            return Substring(start);
        }

        public string TrimEnd()
        {
            int end = _length;
            while (end > 0 && char.IsWhiteSpace(this[end - 1]))
                end--;
            return Substring(0, end);
        }

        public string Trim(char c) => TrimEnd(c).TrimStart(c);

        public string TrimStart(char c)
        {
            int start = 0;
            while (start < _length && this[start] == c)
                start++;
            return Substring(start);
        }

        public string TrimEnd(char c)
        {
            int end = _length;
            while (end > 0 && this[end - 1] == c)
                end--;
            return Substring(0, end);
        }

        public string PadLeft(int totalWidth) => PadLeft(totalWidth, ' ');
        public string PadLeft(int totalWidth, char c) => _length >= totalWidth ? this : new string(c, totalWidth - _length) + this;
        public string PadRight(int totalWidth) => PadRight(totalWidth, ' ');
        public string PadRight(int totalWidth, char c) => _length >= totalWidth ? this : this + new string(c, totalWidth - _length);

        public string Insert(int startIndex, string value) => Substring(0, startIndex) + value + Substring(startIndex);

        public string Remove(int startIndex) => Substring(0, startIndex);
        public string Remove(int startIndex, int count) => Substring(0, startIndex) + Substring(startIndex + count);

        public string Replace(char oldChar, char newChar)
        {
            var chars = ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (chars[i] == oldChar)
                    chars[i] = newChar;
            }
            return new string(chars);
        }

        public string Replace(string oldValue, string newValue)
        {
            if (oldValue == null || oldValue._length == 0)
                throw new ArgumentException("String cannot be of zero length. (Parameter 'oldValue')");
            var builder = new System.Text.StringBuilder();
            int start = 0;
            while (true)
            {
                int found = IndexOf(oldValue, start);
                if (found < 0)
                    break;
                builder.Append(this, start, found - start);
                builder.Append(newValue);
                start = found + oldValue._length;
            }
            builder.Append(this, start, _length - start);
            return builder.ToString();
        }

        public string[] Split(char separator) => Split(new[] { separator });

        public string[] Split(params char[] separator)
        {
            var parts = new System.Collections.Generic.List<string>();
            int start = 0;
            for (int i = 0; i < _length; i++)
            {
                bool isSeparator = false;
                for (int j = 0; j < separator.Length; j++)
                    isSeparator |= this[i] == separator[j];
                if (separator.Length == 0 && char.IsWhiteSpace(this[i]))
                    isSeparator = true;
                if (isSeparator)
                {
                    parts.Add(Substring(start, i - start));
                    start = i + 1;
                }
            }
            parts.Add(Substring(start));
            return parts.ToArray();
        }

        public string[] Split(string separator)
        {
            var parts = new System.Collections.Generic.List<string>();
            int start = 0;
            while (true)
            {
                int found = IndexOf(separator, start);
                if (found < 0)
                    break;
                parts.Add(Substring(start, found - start));
                start = found + separator._length;
            }
            parts.Add(Substring(start));
            return parts.ToArray();
        }

        // ---- Comparing ----

        /// <summary>Ordinal comparison (.NET compares culture-aware by default: mixed case may sort differently).</summary>
        public static int CompareOrdinal(string a, string b)
        {
            if ((object)a == b)
                return 0;
            if (a == null)
                return -1;
            if (b == null)
                return 1;
            int length = a._length < b._length ? a._length : b._length;
            for (int i = 0; i < length; i++)
            {
                if (a[i] != b[i])
                    return a[i] - b[i];
            }
            return a._length - b._length;
        }

        public static int Compare(string a, string b) => CompareOrdinal(a, b);

        public int CompareTo(string other) => CompareOrdinal(this, other);

        public int CompareTo(object obj) => obj is string s ? CompareOrdinal(this, s) : obj == null ? 1 : throw new ArgumentException("Object must be of type String.");

        // ---- Joining and formatting ----

        public static string Join(string separator, params string[] values)
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    builder.Append(separator);
                builder.Append(values[i]);
            }
            return builder.ToString();
        }

        public static string Join<T>(string separator, System.Collections.Generic.IEnumerable<T> values)
        {
            var builder = new System.Text.StringBuilder();
            bool first = true;
            foreach (T value in values)
            {
                if (!first)
                    builder.Append(separator);
                first = false;
                if (value != null)
                    builder.Append(value.ToString());
            }
            return builder.ToString();
        }

        public static string Join(string separator, params object[] values)
        {
            var builder = new System.Text.StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                    builder.Append(separator);
                if (values[i] != null)
                    builder.Append(values[i].ToString());
            }
            return builder.ToString();
        }

        public static string Format(string format, object arg0) => Format(format, new[] { arg0 });
        public static string Format(string format, object arg0, object arg1) => Format(format, new[] { arg0, arg1 });
        public static string Format(string format, object arg0, object arg1, object arg2) => Format(format, new[] { arg0, arg1, arg2 });

        /// <summary>Composite formatting: {index[,alignment][:format]}, with {{ and }} for braces.</summary>
        public static string Format(string format, params object[] args)
        {
            var builder = new System.Text.StringBuilder();
            int i = 0;
            while (i < format._length)
            {
                char c = format[i];
                if (c == '{')
                {
                    if (i + 1 < format._length && format[i + 1] == '{')
                    {
                        builder.Append('{');
                        i += 2;
                        continue;
                    }
                    int close = format.IndexOf('}', i);
                    if (close < 0)
                        throw new FormatException("Input string was not in a correct format.");
                    string item = format.Substring(i + 1, close - i - 1);
                    string itemFormat = null;
                    int colon = item.IndexOf(':');
                    if (colon >= 0)
                    {
                        itemFormat = item.Substring(colon + 1);
                        item = item.Substring(0, colon);
                    }
                    int alignment = 0;
                    int comma = item.IndexOf(',');
                    if (comma >= 0)
                    {
                        alignment = int.Parse(item.Substring(comma + 1).Trim());
                        item = item.Substring(0, comma);
                    }
                    int index = int.Parse(item.Trim());
                    if (index < 0 || index >= args.Length)
                        throw new FormatException("Index (zero based) must be greater than or equal to zero and less than the size of the argument list.");
                    object arg = args[index];
                    string text = arg == null ? ""
                        : itemFormat != null && arg is IFormattable formattable ? formattable.ToString(itemFormat, null)
                        : arg.ToString();
                    if (alignment > 0)
                        text = text.PadLeft(alignment);
                    else if (alignment < 0)
                        text = text.PadRight(-alignment);
                    builder.Append(text);
                    i = close + 1;
                }
                else if (c == '}')
                {
                    if (i + 1 < format._length && format[i + 1] == '}')
                        i++;
                    builder.Append('}');
                    i++;
                }
                else
                {
                    builder.Append(c);
                    i++;
                }
            }
            return builder.ToString();
        }

        public System.Collections.Generic.IEnumerator<char> GetEnumerator()
        {
            for (int i = 0; i < _length; i++)
                yield return this[i];
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
