using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>Number formatting, the way .NET's ToString() does it with the invariant culture.</summary>
    internal static class Number
    {
        internal static unsafe string Int64ToString(long value)
        {
            if (value == 0)
                return "0";
            bool negative = value < 0;
            // Work on the negative value so long.MinValue does not overflow.
            long v = negative ? value : -value;
            char* digits = stackalloc char[20];
            int count = 0;
            while (v != 0)
            {
                digits[count++] = (char)('0' - (int)(v % 10));
                v /= 10;
            }
            string result = string.FastAllocate(count + (negative ? 1 : 0));
            fixed (char* r = result)
            {
                int i = 0;
                if (negative)
                    r[i++] = '-';
                while (count > 0)
                    r[i++] = digits[--count];
            }
            return result;
        }

        internal static unsafe string UInt64ToString(ulong value)
        {
            if (value == 0)
                return "0";
            char* digits = stackalloc char[20];
            int count = 0;
            while (value != 0)
            {
                digits[count++] = (char)('0' + (int)(value % 10));
                value /= 10;
            }
            string result = string.FastAllocate(count);
            fixed (char* r = result)
            {
                for (int i = 0; i < result.Length; i++)
                    r[i] = digits[count - 1 - i];
            }
            return result;
        }

        /// <summary>Shortest text that reads back as the same double (like .NET Core 3.0+).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern string DoubleToString(double value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern string SingleToString(float value);
    }

    internal static class ThrowHelpers
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void IndexOutOfRange();

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void ArgumentOutOfRange();
    }
}
