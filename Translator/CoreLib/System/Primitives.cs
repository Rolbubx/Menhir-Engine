namespace System
{
    // The primitive types. Their single field holds the value; the translator maps them to C types.
    // Equals, GetHashCode and CompareTo behave like .NET's (NaN equals NaN in Equals, sorts first).
    public struct Boolean
    {
        private bool m_value;

        public static readonly string TrueString = "True";
        public static readonly string FalseString = "False";

        public override string ToString() => m_value ? "True" : "False";

        public bool Equals(bool obj) => m_value == obj;
        public override bool Equals(object obj) => obj is bool other && Equals(other);

        public override int GetHashCode() => m_value ? 1 : 0;

        public int CompareTo(bool value) => m_value == value ? 0 : m_value ? 1 : -1;
    }

    public struct Char
    {
        private char m_value;

        public const char MinValue = '\0';
        public const char MaxValue = '\uffff';

        public override string ToString() => string.FromChar(m_value);

        public bool Equals(char obj) => m_value == obj;
        public override bool Equals(object obj) => obj is char other && Equals(other);

        public override int GetHashCode() => (int)m_value | ((int)m_value << 16);

        public int CompareTo(char value) => m_value < value ? -1 : m_value > value ? 1 : 0;
    }

    public struct SByte
    {
        private sbyte m_value;

        public const sbyte MinValue = -128;
        public const sbyte MaxValue = 127;

        public override string ToString() => Number.Int64ToString(m_value);

        public bool Equals(sbyte obj) => m_value == obj;
        public override bool Equals(object obj) => obj is sbyte other && Equals(other);

        public override int GetHashCode() => m_value;

        public int CompareTo(sbyte value) => m_value < value ? -1 : m_value > value ? 1 : 0;
    }

    public struct Byte
    {
        private byte m_value;

        public const byte MinValue = 0;
        public const byte MaxValue = 255;

        public override string ToString() => Number.Int64ToString(m_value);

        public bool Equals(byte obj) => m_value == obj;
        public override bool Equals(object obj) => obj is byte other && Equals(other);

        public override int GetHashCode() => m_value;

        public int CompareTo(byte value) => m_value < value ? -1 : m_value > value ? 1 : 0;
    }

    public struct Int16
    {
        private short m_value;

        public const short MinValue = -32768;
        public const short MaxValue = 32767;

        public override string ToString() => Number.Int64ToString(m_value);

        public bool Equals(short obj) => m_value == obj;
        public override bool Equals(object obj) => obj is short other && Equals(other);

        public override int GetHashCode() => m_value;

        public int CompareTo(short value) => m_value < value ? -1 : m_value > value ? 1 : 0;
    }

    public struct UInt16
    {
        private ushort m_value;

        public const ushort MinValue = 0;
        public const ushort MaxValue = 65535;

        public override string ToString() => Number.Int64ToString(m_value);

        public bool Equals(ushort obj) => m_value == obj;
        public override bool Equals(object obj) => obj is ushort other && Equals(other);

        public override int GetHashCode() => m_value;

        public int CompareTo(ushort value) => m_value < value ? -1 : m_value > value ? 1 : 0;
    }

    public struct Int32
    {
        private int m_value;

        public const int MinValue = -2147483648;
        public const int MaxValue = 2147483647;

        public override string ToString() => Number.Int64ToString(m_value);

        public bool Equals(int obj) => m_value == obj;
        public override bool Equals(object obj) => obj is int other && Equals(other);

        public override int GetHashCode() => m_value;

        public int CompareTo(int value) => m_value < value ? -1 : m_value > value ? 1 : 0;
    }

    public struct UInt32
    {
        private uint m_value;

        public const uint MinValue = 0;
        public const uint MaxValue = 4294967295;

        public override string ToString() => Number.Int64ToString(m_value);

        public bool Equals(uint obj) => m_value == obj;
        public override bool Equals(object obj) => obj is uint other && Equals(other);

        public override int GetHashCode() => (int)m_value;

        public int CompareTo(uint value) => m_value < value ? -1 : m_value > value ? 1 : 0;
    }

    public struct Int64
    {
        private long m_value;

        public const long MinValue = -9223372036854775808;
        public const long MaxValue = 9223372036854775807;

        public override string ToString() => Number.Int64ToString(m_value);

        public bool Equals(long obj) => m_value == obj;
        public override bool Equals(object obj) => obj is long other && Equals(other);

        public override int GetHashCode() => (int)m_value ^ (int)(m_value >> 32);

        public int CompareTo(long value) => m_value < value ? -1 : m_value > value ? 1 : 0;
    }

    public struct UInt64
    {
        private ulong m_value;

        public const ulong MinValue = 0;
        public const ulong MaxValue = 18446744073709551615;

        public override string ToString() => Number.UInt64ToString(m_value);

        public bool Equals(ulong obj) => m_value == obj;
        public override bool Equals(object obj) => obj is ulong other && Equals(other);

        public override int GetHashCode() => (int)m_value ^ (int)(m_value >> 32);

        public int CompareTo(ulong value) => m_value < value ? -1 : m_value > value ? 1 : 0;
    }

    public struct Single
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
    }

    public struct Double
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
