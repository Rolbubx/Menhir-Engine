namespace System
{
    // The primitive types. Their single field holds the value; the translator maps them to C types.
    public struct Boolean
    {
        private bool m_value;

        public override string ToString() => m_value ? "True" : "False";
    }

    public struct Char
    {
        private char m_value;

        public override string ToString() => string.FromChar(m_value);
    }

    public struct SByte
    {
        private sbyte m_value;

        public override string ToString() => Number.Int64ToString(m_value);
    }

    public struct Byte
    {
        private byte m_value;

        public override string ToString() => Number.Int64ToString(m_value);
    }

    public struct Int16
    {
        private short m_value;

        public override string ToString() => Number.Int64ToString(m_value);
    }

    public struct UInt16
    {
        private ushort m_value;

        public override string ToString() => Number.Int64ToString(m_value);
    }

    public struct Int32
    {
        private int m_value;

        public const int MaxValue = 2147483647;
        public const int MinValue = -2147483648;

        public override string ToString() => Number.Int64ToString(m_value);

        public override int GetHashCode() => m_value;
    }

    public struct UInt32
    {
        private uint m_value;

        public const uint MaxValue = 4294967295;

        public override string ToString() => Number.Int64ToString(m_value);
    }

    public struct Int64
    {
        private long m_value;

        public const long MaxValue = 9223372036854775807;
        public const long MinValue = -9223372036854775808;

        public override string ToString() => Number.Int64ToString(m_value);
    }

    public struct UInt64
    {
        private ulong m_value;

        public override string ToString() => Number.UInt64ToString(m_value);
    }

    public struct IntPtr
    {
        private unsafe void* m_value;
    }

    public struct UIntPtr
    {
        private unsafe void* m_value;
    }

    public struct Single
    {
        private float m_value;

        public override string ToString() => Number.SingleToString(m_value);
    }

    public struct Double
    {
        private double m_value;

        public override string ToString() => Number.DoubleToString(m_value);
    }
}
