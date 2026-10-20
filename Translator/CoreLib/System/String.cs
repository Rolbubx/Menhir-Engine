using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>
    /// UTF-16 text, immutable. The layout matches the runtime's: the length, then the characters
    /// (starting at _firstChar).
    /// </summary>
    public sealed class String
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
    }
}
