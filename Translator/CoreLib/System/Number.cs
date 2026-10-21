using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>Number formatting, the way .NET's ToString() does it with the invariant culture.</summary>
    internal static partial class Number
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

    /// <summary>Parsing with the invariant culture: optional spaces and sign, then digits (and '.', exponent for floats).</summary>
    internal static partial class Number
    {
        /// <summary>0: parsed; 1: bad format; 2: out of range.</summary>
        internal static int TryParseInteger(string s, long min, long max, out long value)
        {
            value = 0;
            if (s == null)
                return 1;
            string text = s.Trim();
            int i = 0;
            bool negative = false;
            if (i < text.Length && (text[i] == '-' || text[i] == '+'))
                negative = text[i++] == '-';
            if (i >= text.Length)
                return 1;
            ulong magnitude = 0;
            bool overflow = false;
            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9')
                    return 1;
                ulong next = magnitude * 10 + (ulong)(c - '0');
                if (magnitude > 1844674407370955161UL || next < magnitude)
                    overflow = true;
                magnitude = next;
            }
            if (overflow)
                return 2;
            if (negative)
            {
                if (magnitude > (ulong)(-(min + 1)) + 1)
                    return 2;
                value = magnitude == (ulong)(-(min + 1)) + 1 ? min : -(long)magnitude;
            }
            else
            {
                if (magnitude > (ulong)max)
                    return 2;
                value = (long)magnitude;
            }
            return 0;
        }

        internal static long ParseInteger(string s, long min, long max, string type)
        {
            int status = TryParseInteger(s, min, max, out long value);
            if (status == 1)
                throw new FormatException("The input string '" + s + "' was not in a correct format.");
            if (status == 2)
                throw new OverflowException("Value was either too large or too small for " + (type[0] == 'I' ? "an " : "a ") + type + ".");
            return value;
        }

        internal static int TryParseUnsigned(string s, ulong max, out ulong value)
        {
            value = 0;
            if (s == null)
                return 1;
            string text = s.Trim();
            int i = 0;
            if (i < text.Length && text[i] == '+')
                i++;
            bool negative = i < text.Length && text[i] == '-';
            if (negative)
                i++;
            if (i >= text.Length)
                return 1;
            bool overflow = false;
            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (c < '0' || c > '9')
                    return 1;
                ulong next = value * 10 + (ulong)(c - '0');
                if (value > 1844674407370955161UL || next < value)
                    overflow = true;
                value = next;
            }
            if (overflow || value > max || (negative && value != 0))
                return 2;
            return 0;
        }

        internal static ulong ParseUnsigned(string s, ulong max, string type)
        {
            int status = TryParseUnsigned(s, max, out ulong value);
            if (status == 1)
                throw new FormatException("The input string '" + s + "' was not in a correct format.");
            if (status == 2)
                throw new OverflowException("Value was either too large or too small for a " + type + ".");
            return value;
        }

        internal static bool TryParseFloat(string s, bool single, out double value)
        {
            value = 0;
            if (s == null)
                return false;
            string text = s.Trim();
            if (text == "NaN") { value = double.NaN; return true; }
            if (text == "Infinity" || text == "+Infinity" || text == "\u221e") { value = double.PositiveInfinity; return true; }
            if (text == "-Infinity" || text == "-\u221e") { value = double.NegativeInfinity; return true; }
            // [sign] digits [. digits] [e [sign] digits], at least one digit before the exponent.
            int i = 0, digits = 0;
            if (i < text.Length && (text[i] == '+' || text[i] == '-')) i++;
            while (i < text.Length && text[i] >= '0' && text[i] <= '9') { i++; digits++; }
            if (i < text.Length && text[i] == '.')
            {
                i++;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9') { i++; digits++; }
            }
            if (digits == 0)
                return false;
            if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
            {
                i++;
                if (i < text.Length && (text[i] == '+' || text[i] == '-')) i++;
                int exponentDigits = 0;
                while (i < text.Length && text[i] >= '0' && text[i] <= '9') { i++; exponentDigits++; }
                if (exponentDigits == 0)
                    return false;
            }
            if (i != text.Length)
                return false;
            value = ParseFloat(text, single);
            return true;
        }

        /// <summary>strtod / strtof of a validated number (correctly rounded).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern double ParseFloat(string text, bool single);
    }

    internal static class ThrowHelpers
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void IndexOutOfRange();

        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern void ArgumentOutOfRange();
    }
}
