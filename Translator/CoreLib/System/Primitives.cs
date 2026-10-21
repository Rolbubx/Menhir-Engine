namespace System
{
    // The primitive types. Their single field holds the value; the translator maps them to C types.
    // Equals, GetHashCode, CompareTo, formats and parsing behave like .NET's with the invariant culture.
    public struct Boolean : IComparable, IComparable<bool>, IEquatable<bool>
    {
        private bool m_value;

        public static readonly string TrueString = "True";
        public static readonly string FalseString = "False";

        public static bool Parse(string value) => TryParse(value, out bool result) ? result : throw new FormatException("String '" + value + "' was not recognized as a valid Boolean.");
        public static bool TryParse(string value, out bool result)
        {
            string text = value == null ? "" : value.Trim().ToLowerInvariant();
            result = text == "true";
            return result || text == "false";
        }

        public override string ToString() => m_value ? "True" : "False";

        public bool Equals(bool obj) => m_value == obj;
        public override bool Equals(object obj) => obj is bool other && Equals(other);

        public override int GetHashCode() => m_value ? 1 : 0;

        public int CompareTo(bool value) => m_value == value ? 0 : m_value ? 1 : -1;
        public int CompareTo(object obj) => obj is bool other ? CompareTo(other) : obj == null ? 1 : throw new ArgumentException("Object must be of type Boolean.");
    }

    public struct Char : IComparable, IComparable<char>, IEquatable<char>
    {
        private char m_value;

        public const char MinValue = '\0';
        public const char MaxValue = '\uffff';

        public static bool IsWhiteSpace(char c) => c == ' ' || (c >= '\t' && c <= '\r') || c == '\u00a0' || c == '\u0085' || c == '\u2028' || c == '\u2029' || (c >= '\u2000' && c <= '\u200a') || c == '\u3000' || c == '\u1680' || c == '\u202f' || c == '\u205f';
        public static bool IsDigit(char c) => c >= '0' && c <= '9';
        /// <summary>ASCII and Latin-1 letters (no full Unicode tables yet).</summary>
        public static bool IsLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '\u00c0' && c <= '\u024f' && c != '\u00d7' && c != '\u00f7');
        public static bool IsLetterOrDigit(char c) => IsLetter(c) || IsDigit(c);
        public static bool IsUpper(char c) => (c >= 'A' && c <= 'Z') || (c >= '\u00c0' && c <= '\u00de' && c != '\u00d7');
        public static bool IsLower(char c) => (c >= 'a' && c <= 'z') || (c >= '\u00df' && c <= '\u00ff' && c != '\u00f7');
        public static bool IsPunctuation(char c) => (c >= '!' && c <= '/' && c != '$' && c != '+') || c == ':' || c == ';' || c == '?' || c == '@' || (c >= '[' && c <= ']') || c == '_' || c == '{' || c == '}';
        public static char ToUpperInvariant(char c) => (c >= 'a' && c <= 'z') || (c >= '\u00e0' && c <= '\u00fe' && c != '\u00f7') ? (char)(c - 32) : c == '\u00ff' ? '\u0178' : c;
        public static char ToLowerInvariant(char c) => (c >= 'A' && c <= 'Z') || (c >= '\u00c0' && c <= '\u00de' && c != '\u00d7') ? (char)(c + 32) : c;
        public static char ToUpper(char c) => ToUpperInvariant(c);
        public static char ToLower(char c) => ToLowerInvariant(c);
        public static bool IsSurrogate(char c) => c >= '\ud800' && c <= '\udfff';

        public override string ToString() => string.FromChar(m_value);

        public bool Equals(char obj) => m_value == obj;
        public override bool Equals(object obj) => obj is char other && Equals(other);

        public override int GetHashCode() => (int)m_value | ((int)m_value << 16);

        public int CompareTo(char value) => m_value < value ? -1 : m_value > value ? 1 : 0;
        public int CompareTo(object obj) => obj is char other ? CompareTo(other) : obj == null ? 1 : throw new ArgumentException("Object must be of type Char.");
    }

    public struct SByte : IComparable, IComparable<sbyte>, IEquatable<sbyte>, IFormattable
    {
        private sbyte m_value;

        public const sbyte MinValue = -128;
        public const sbyte MaxValue = 127;

        public override string ToString() => Number.Int64ToString(m_value);
        public string ToString(string format) => NumberFormat.FormatInteger(m_value, false, (ulong)m_value & 0xFF, format);
        public string ToString(string format, IFormatProvider provider) => ToString(format);
        public string ToString(IFormatProvider provider) => ToString();

        public static sbyte Parse(string s) => (sbyte)Number.ParseInteger(s, -128L, 127L, "SByte");
        public static bool TryParse(string s, out sbyte result)
        {
            bool ok = Number.TryParseInteger(s, -128L, 127L, out long value) == 0;
            result = ok ? (sbyte)value : (sbyte)0;
            return ok;
        }

        public bool Equals(sbyte obj) => m_value == obj;
        public override bool Equals(object obj) => obj is sbyte other && Equals(other);

        public override int GetHashCode() => m_value;

        public int CompareTo(sbyte value) => m_value < value ? -1 : m_value > value ? 1 : 0;
        public int CompareTo(object obj) => obj is sbyte other ? CompareTo(other) : obj == null ? 1 : throw new ArgumentException("Object must be of type SByte.");
    }

    public struct Byte : IComparable, IComparable<byte>, IEquatable<byte>, IFormattable
    {
        private byte m_value;

        public const byte MinValue = 0;
        public const byte MaxValue = 255;

        public override string ToString() => Number.Int64ToString(m_value);
        public string ToString(string format) => NumberFormat.FormatInteger((long)m_value, true, m_value, format);
        public string ToString(string format, IFormatProvider provider) => ToString(format);
        public string ToString(IFormatProvider provider) => ToString();

        public static byte Parse(string s) => (byte)Number.ParseUnsigned(s, 255UL, "Byte");
        public static bool TryParse(string s, out byte result)
        {
            bool ok = Number.TryParseUnsigned(s, 255UL, out ulong value) == 0;
            result = ok ? (byte)value : (byte)0;
            return ok;
        }

        public bool Equals(byte obj) => m_value == obj;
        public override bool Equals(object obj) => obj is byte other && Equals(other);

        public override int GetHashCode() => m_value;

        public int CompareTo(byte value) => m_value < value ? -1 : m_value > value ? 1 : 0;
        public int CompareTo(object obj) => obj is byte other ? CompareTo(other) : obj == null ? 1 : throw new ArgumentException("Object must be of type Byte.");
    }

    public struct Int16 : IComparable, IComparable<short>, IEquatable<short>, IFormattable
    {
        private short m_value;

        public const short MinValue = -32768;
        public const short MaxValue = 32767;

        public override string ToString() => Number.Int64ToString(m_value);
        public string ToString(string format) => NumberFormat.FormatInteger(m_value, false, (ulong)m_value & 0xFFFF, format);
        public string ToString(string format, IFormatProvider provider) => ToString(format);
        public string ToString(IFormatProvider provider) => ToString();

        public static short Parse(string s) => (short)Number.ParseInteger(s, -32768L, 32767L, "Int16");
        public static bool TryParse(string s, out short result)
        {
            bool ok = Number.TryParseInteger(s, -32768L, 32767L, out long value) == 0;
            result = ok ? (short)value : (short)0;
            return ok;
        }

        public bool Equals(short obj) => m_value == obj;
        public override bool Equals(object obj) => obj is short other && Equals(other);

        public override int GetHashCode() => m_value;

        public int CompareTo(short value) => m_value < value ? -1 : m_value > value ? 1 : 0;
        public int CompareTo(object obj) => obj is short other ? CompareTo(other) : obj == null ? 1 : throw new ArgumentException("Object must be of type Int16.");
    }

    public struct UInt16 : IComparable, IComparable<ushort>, IEquatable<ushort>, IFormattable
    {
        private ushort m_value;

        public const ushort MinValue = 0;
        public const ushort MaxValue = 65535;

        public override string ToString() => Number.Int64ToString(m_value);
        public string ToString(string format) => NumberFormat.FormatInteger((long)m_value, true, m_value, format);
        public string ToString(string format, IFormatProvider provider) => ToString(format);
        public string ToString(IFormatProvider provider) => ToString();

        public static ushort Parse(string s) => (ushort)Number.ParseUnsigned(s, 65535UL, "UInt16");
        public static bool TryParse(string s, out ushort result)
        {
            bool ok = Number.TryParseUnsigned(s, 65535UL, out ulong value) == 0;
            result = ok ? (ushort)value : (ushort)0;
            return ok;
        }

        public bool Equals(ushort obj) => m_value == obj;
        public override bool Equals(object obj) => obj is ushort other && Equals(other);

        public override int GetHashCode() => m_value;

        public int CompareTo(ushort value) => m_value < value ? -1 : m_value > value ? 1 : 0;
        public int CompareTo(object obj) => obj is ushort other ? CompareTo(other) : obj == null ? 1 : throw new ArgumentException("Object must be of type UInt16.");
    }

    public struct Int32 : IComparable, IComparable<int>, IEquatable<int>, IFormattable
    {
        private int m_value;

        public const int MinValue = -2147483648;
        public const int MaxValue = 2147483647;

        public override string ToString() => Number.Int64ToString(m_value);
        public string ToString(string format) => NumberFormat.FormatInteger(m_value, false, (ulong)m_value & 0xFFFFFFFF, format);
        public string ToString(string format, IFormatProvider provider) => ToString(format);
        public string ToString(IFormatProvider provider) => ToString();

        public static int Parse(string s) => (int)Number.ParseInteger(s, -2147483648L, 2147483647L, "Int32");
        public static bool TryParse(string s, out int result)
        {
            bool ok = Number.TryParseInteger(s, -2147483648L, 2147483647L, out long value) == 0;
            result = ok ? (int)value : (int)0;
            return ok;
        }

        public bool Equals(int obj) => m_value == obj;
        public override bool Equals(object obj) => obj is int other && Equals(other);

        public override int GetHashCode() => m_value;

        public int CompareTo(int value) => m_value < value ? -1 : m_value > value ? 1 : 0;
        public int CompareTo(object obj) => obj is int other ? CompareTo(other) : obj == null ? 1 : throw new ArgumentException("Object must be of type Int32.");
    }

    public struct UInt32 : IComparable, IComparable<uint>, IEquatable<uint>, IFormattable
    {
        private uint m_value;

        public const uint MinValue = 0;
        public const uint MaxValue = 4294967295;

        public override string ToString() => Number.Int64ToString(m_value);
        public string ToString(string format) => NumberFormat.FormatInteger((long)m_value, true, m_value, format);
        public string ToString(string format, IFormatProvider provider) => ToString(format);
        public string ToString(IFormatProvider provider) => ToString();

        public static uint Parse(string s) => (uint)Number.ParseUnsigned(s, 4294967295UL, "UInt32");
        public static bool TryParse(string s, out uint result)
        {
            bool ok = Number.TryParseUnsigned(s, 4294967295UL, out ulong value) == 0;
            result = ok ? (uint)value : (uint)0;
            return ok;
        }

        public bool Equals(uint obj) => m_value == obj;
        public override bool Equals(object obj) => obj is uint other && Equals(other);

        public override int GetHashCode() => (int)m_value;

        public int CompareTo(uint value) => m_value < value ? -1 : m_value > value ? 1 : 0;
        public int CompareTo(object obj) => obj is uint other ? CompareTo(other) : obj == null ? 1 : throw new ArgumentException("Object must be of type UInt32.");
    }

    public struct Int64 : IComparable, IComparable<long>, IEquatable<long>, IFormattable
    {
        private long m_value;

        public const long MinValue = -9223372036854775808;
        public const long MaxValue = 9223372036854775807;

        public override string ToString() => Number.Int64ToString(m_value);
        public string ToString(string format) => NumberFormat.FormatInteger(m_value, false, (ulong)m_value & 0xFFFFFFFFFFFFFFFF, format);
        public string ToString(string format, IFormatProvider provider) => ToString(format);
        public string ToString(IFormatProvider provider) => ToString();

        public static long Parse(string s) => (long)Number.ParseInteger(s, -9223372036854775808L, 9223372036854775807L, "Int64");
        public static bool TryParse(string s, out long result)
        {
            bool ok = Number.TryParseInteger(s, -9223372036854775808L, 9223372036854775807L, out long value) == 0;
            result = ok ? (long)value : (long)0;
            return ok;
        }

        public bool Equals(long obj) => m_value == obj;
        public override bool Equals(object obj) => obj is long other && Equals(other);

        public override int GetHashCode() => (int)m_value ^ (int)(m_value >> 32);

        public int CompareTo(long value) => m_value < value ? -1 : m_value > value ? 1 : 0;
        public int CompareTo(object obj) => obj is long other ? CompareTo(other) : obj == null ? 1 : throw new ArgumentException("Object must be of type Int64.");
    }

    public struct UInt64 : IComparable, IComparable<ulong>, IEquatable<ulong>, IFormattable
    {
        private ulong m_value;

        public const ulong MinValue = 0;
        public const ulong MaxValue = 18446744073709551615;

        public override string ToString() => Number.UInt64ToString(m_value);
        public string ToString(string format) => NumberFormat.FormatInteger((long)m_value, true, m_value, format);
        public string ToString(string format, IFormatProvider provider) => ToString(format);
        public string ToString(IFormatProvider provider) => ToString();

        public static ulong Parse(string s) => (ulong)Number.ParseUnsigned(s, 18446744073709551615UL, "UInt64");
        public static bool TryParse(string s, out ulong result)
        {
            bool ok = Number.TryParseUnsigned(s, 18446744073709551615UL, out ulong value) == 0;
            result = ok ? (ulong)value : (ulong)0;
            return ok;
        }

        public bool Equals(ulong obj) => m_value == obj;
        public override bool Equals(object obj) => obj is ulong other && Equals(other);

        public override int GetHashCode() => (int)m_value ^ (int)(m_value >> 32);

        public int CompareTo(ulong value) => m_value < value ? -1 : m_value > value ? 1 : 0;
        public int CompareTo(object obj) => obj is ulong other ? CompareTo(other) : obj == null ? 1 : throw new ArgumentException("Object must be of type UInt64.");
    }

    public struct Single : IComparable, IComparable<float>, IEquatable<float>, IFormattable
    {
        private float m_value;

        public const float MinValue = -3.40282347E+38f;
        public const float MaxValue = 3.40282347E+38f;
        public const float Epsilon = 1.401298E-45f;
        public const float NaN = ((float)0) / ((float)0);
        public const float PositiveInfinity = ((float)1) / ((float)0);
        public const float NegativeInfinity = ((float)(-1)) / ((float)0);

        public static bool IsNaN(float f) => f != f;
        public static bool IsInfinity(float f) => f == PositiveInfinity || f == NegativeInfinity;
        public static bool IsFinite(float f) => !IsNaN(f) && !IsInfinity(f);
        public static bool IsPositiveInfinity(float f) => f == PositiveInfinity;
        public static bool IsNegativeInfinity(float f) => f == NegativeInfinity;

        public override string ToString() => Number.SingleToString(m_value);
        public string ToString(string format) => NumberFormat.FormatFloat(m_value, format, 7, true);
        public string ToString(string format, IFormatProvider provider) => ToString(format);
        public string ToString(IFormatProvider provider) => ToString();

        public static float Parse(string s) => TryParse(s, out float result) ? result : throw new FormatException("The input string '" + s + "' was not in a correct format.");
        public static bool TryParse(string s, out float result)
        {
            bool ok = Number.TryParseFloat(s, true, out double value);
            result = (float)value;
            return ok;
        }

        public bool Equals(float obj) => m_value == obj || (IsNaN(m_value) && IsNaN(obj));
        public override bool Equals(object obj) => obj is float other && Equals(other);

        public override unsafe int GetHashCode()
        {
            float value = m_value;
            if (value != value || value == 0f)
                value = value != value ? NaN : 0f; // every NaN, and -0, hash like NaN and 0
            return *(int*)&value;
        }

        public int CompareTo(float value)
        {
            if (m_value < value) return -1;
            if (m_value > value) return 1;
            if (m_value == value) return 0;
            if (IsNaN(m_value)) return IsNaN(value) ? 0 : -1;
            return 1;
        }
        public int CompareTo(object obj) => obj is float other ? CompareTo(other) : obj == null ? 1 : throw new ArgumentException("Object must be of type Single.");
    }

    public struct Double : IComparable, IComparable<double>, IEquatable<double>, IFormattable
    {
        private double m_value;

        public const double MinValue = -1.7976931348623157E+308;
        public const double MaxValue = 1.7976931348623157E+308;
        public const double Epsilon = 4.9406564584124654E-324;
        public const double NaN = ((double)0) / ((double)0);
        public const double PositiveInfinity = ((double)1) / ((double)0);
        public const double NegativeInfinity = ((double)(-1)) / ((double)0);

        public static bool IsNaN(double f) => f != f;
        public static bool IsInfinity(double f) => f == PositiveInfinity || f == NegativeInfinity;
        public static bool IsFinite(double f) => !IsNaN(f) && !IsInfinity(f);
        public static bool IsPositiveInfinity(double f) => f == PositiveInfinity;
        public static bool IsNegativeInfinity(double f) => f == NegativeInfinity;

        public override string ToString() => Number.DoubleToString(m_value);
        public string ToString(string format) => NumberFormat.FormatFloat(m_value, format, 15, false);
        public string ToString(string format, IFormatProvider provider) => ToString(format);
        public string ToString(IFormatProvider provider) => ToString();

        public static double Parse(string s) => TryParse(s, out double result) ? result : throw new FormatException("The input string '" + s + "' was not in a correct format.");
        public static bool TryParse(string s, out double result) => Number.TryParseFloat(s, false, out result);

        public bool Equals(double obj) => m_value == obj || (IsNaN(m_value) && IsNaN(obj));
        public override bool Equals(object obj) => obj is double other && Equals(other);

        public override unsafe int GetHashCode()
        {
            double value = m_value;
            if (value != value || value == 0.0)
                value = value != value ? NaN : 0.0;
            long bits = *(long*)&value;
            return (int)bits ^ (int)(bits >> 32);
        }

        public int CompareTo(double value)
        {
            if (m_value < value) return -1;
            if (m_value > value) return 1;
            if (m_value == value) return 0;
            if (IsNaN(m_value)) return IsNaN(value) ? 0 : -1;
            return 1;
        }
        public int CompareTo(object obj) => obj is double other ? CompareTo(other) : obj == null ? 1 : throw new ArgumentException("Object must be of type Double.");
    }

    public struct IntPtr
    {
        private unsafe void* m_value;
    }

    public struct UIntPtr
    {
        private unsafe void* m_value;
    }
}
