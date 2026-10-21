using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>
    /// Format strings of numbers, like .NET with the invariant culture: standard formats C, D, E, F,
    /// G, N, P, R and X (with a precision, e.g. "F2"), and custom formats made of 0, #, '.', ',' and
    /// '%' (e.g. "0.00", "#,0"). "F", "N" and "P" round the exact binary value (ties to even), custom
    /// formats round 15 significant digits (7 for float) half away from zero, as .NET does.
    /// </summary>
    internal static class NumberFormat
    {
        /// <summary>printf-style "%.*f" ('f') or "%.*e" ('e') of a double (the C runtime's, which rounds exactly).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal static extern string Printf(double value, int precision, char kind);

        internal static string FormatInteger(long value, bool unsigned, ulong bits, string format)
        {
            if (string.IsNullOrEmpty(format))
                return unsigned ? Number.UInt64ToString((ulong)value) : Number.Int64ToString(value);
            char kind = format[0];
            int precision = ParsePrecision(format, out bool standard);
            if (!standard)
                return Custom(unsigned ? (double)(ulong)value : value, format, 15);
            switch (kind)
            {
                case 'D':
                case 'd':
                {
                    string digits = unsigned ? Number.UInt64ToString((ulong)value) : Number.Int64ToString(value < 0 ? -value : value);
                    if (!unsigned && value == long.MinValue)
                        digits = "9223372036854775808";
                    digits = PadLeft(digits, precision, '0');
                    return !unsigned && value < 0 ? "-" + digits : digits;
                }
                case 'X':
                case 'x':
                    return PadLeft(Hex(bits, kind == 'x'), precision, '0');
                default:
                    return FormatFloat(unsigned ? (double)(ulong)value : value, format, 15, single: false);
            }
        }

        internal static string FormatFloat(double value, string format, int customPrecision, bool single)
        {
            if (string.IsNullOrEmpty(format) || ((format[0] == 'G' || format[0] == 'g' || format[0] == 'R' || format[0] == 'r') && format.Length == 1))
                return single ? Number.SingleToString((float)value) : Number.DoubleToString(value);
            if (value != value)
                return "NaN";
            if (value == 1.0 / 0.0)
                return "Infinity";
            if (value == -1.0 / 0.0)
                return "-Infinity";
            int precision = ParsePrecision(format, out bool standard);
            if (!standard)
                return Custom(value, format, customPrecision);
            switch (format[0])
            {
                case 'F':
                case 'f':
                    return Fixed(value, precision < 0 ? 2 : precision, group: false);
                case 'N':
                case 'n':
                    return Fixed(value, precision < 0 ? 2 : precision, group: true);
                case 'P':
                case 'p':
                    return Fixed(value, precision < 0 ? 2 : precision, group: true, scale: 2) + " %";
                case 'C':
                case 'c':
                {
                    string text = Fixed(value < 0 ? -value : value, precision < 0 ? 2 : precision, group: true);
                    return value < 0 && !IsZero(text) ? "-¤" + text : "¤" + text;
                }
                case 'E':
                case 'e':
                    return Exponential(value, precision < 0 ? 6 : precision, format[0]);
                case 'G':
                case 'g':
                    return General(value, precision);
                default:
                    throw new FormatException("Format specifier was invalid.");
            }
        }

        /// <summary>The precision of a standard format ("F2" → 2; "F" → -1), or not standard (custom).</summary>
        private static int ParsePrecision(string format, out bool standard)
        {
            standard = false;
            char c = format[0];
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')))
                return -1;
            if (format.Length == 1)
            {
                standard = true;
                return -1;
            }
            int precision = 0;
            for (int i = 1; i < format.Length; i++)
            {
                char d = format[i];
                if (d < '0' || d > '9')
                    return -1;
                precision = precision * 10 + (d - '0');
            }
            standard = true;
            return precision;
        }

        private static bool IsZero(string digits)
        {
            for (int i = 0; i < digits.Length; i++)
            {
                if (digits[i] >= '1' && digits[i] <= '9')
                    return false;
            }
            return true;
        }

        /// <summary>Fixed-point digits; <paramref name="scale"/> moves the point right (percent: 2), exactly.</summary>
        private static string Fixed(double value, int decimals, bool group, int scale = 0)
        {
            string text = Printf(value, decimals + scale, 'f');
            bool negative = text[0] == '-';
            if (negative)
                text = text.Substring(1);
            if (scale > 0)
            {
                int dot = text.IndexOf('.');
                string whole = text.Substring(0, dot) + text.Substring(dot + 1, scale);
                string rest = text.Substring(dot + 1 + scale);
                int start = 0;
                while (start < whole.Length - 1 && whole[start] == '0')
                    start++;
                text = whole.Substring(start) + (rest.Length > 0 ? "." + rest : "");
            }
            int point = text.IndexOf('.');
            string integer = point < 0 ? text : text.Substring(0, point);
            string fraction = point < 0 ? "" : text.Substring(point);
            if (group)
                integer = Group(integer);
            // Like .NET Core 3.0 and later, a negative value keeps its sign even when it rounds to zero.
            return (negative ? "-" : "") + integer + fraction;
        }

        private static string Exponential(double value, int decimals, char e)
        {
            string text = Printf(value, decimals, 'e'); // d.ddde+xx
            int mark = text.IndexOf('e');
            string mantissa = text.Substring(0, mark);
            string exponent = text.Substring(mark + 1);
            char sign = exponent[0];
            string digits = exponent.Substring(1);
            digits = PadLeft(TrimLeadingZeros(digits), 3, '0');
            return mantissa + (e == 'E' ? "E" : "e") + sign + digits;
        }

        private static string General(double value, int precision)
        {
            if (precision <= 0)
                return Number.DoubleToString(value);
            string text = Printf(value, precision - 1, 'e');
            int mark = text.IndexOf('e');
            int exponent = int.Parse(text.Substring(mark + 1));
            if (exponent >= -5 && exponent < precision)
            {
                string fixedText = Printf(value, Math.Max(0, precision - 1 - exponent), 'f');
                if (fixedText.IndexOf('.') >= 0)
                {
                    int end = fixedText.Length;
                    while (fixedText[end - 1] == '0')
                        end--;
                    if (fixedText[end - 1] == '.')
                        end--;
                    fixedText = fixedText.Substring(0, end);
                }
                return fixedText;
            }
            string mantissa = text.Substring(0, mark);
            if (mantissa.IndexOf('.') >= 0)
            {
                int end = mantissa.Length;
                while (mantissa[end - 1] == '0')
                    end--;
                if (mantissa[end - 1] == '.')
                    end--;
                mantissa = mantissa.Substring(0, end);
            }
            string exp = TrimLeadingZeros(text.Substring(mark + 2));
            return mantissa + "E" + text[mark + 1] + PadLeft(exp, 2, '0');
        }

        private static string TrimLeadingZeros(string digits)
        {
            int start = 0;
            while (start < digits.Length - 1 && digits[start] == '0')
                start++;
            return digits.Substring(start);
        }

        internal static string PadLeft(string text, int width, char c)
        {
            if (text.Length >= width)
                return text;
            return new string(c, width - text.Length) + text;
        }

        private static string Group(string integer)
        {
            if (integer.Length <= 3)
                return integer;
            var builder = new System.Text.StringBuilder();
            int first = integer.Length % 3;
            if (first == 0)
                first = 3;
            builder.Append(integer, 0, first);
            for (int i = first; i < integer.Length; i += 3)
            {
                builder.Append(',');
                builder.Append(integer, i, 3);
            }
            return builder.ToString();
        }

        private static string Hex(ulong bits, bool lower)
        {
            if (bits == 0)
                return "0";
            string digits = lower ? "0123456789abcdef" : "0123456789ABCDEF";
            var chars = new char[16];
            int count = 0;
            while (bits != 0)
            {
                chars[count++] = digits[(int)(bits & 15)];
                bits >>= 4;
            }
            var result = new char[count];
            for (int i = 0; i < count; i++)
                result[i] = chars[count - 1 - i];
            return new string(result);
        }

        /// <summary>Custom formats: 0 (digit or zero), # (digit if any), '.', ',' (grouping), '%' (× 100), literals.</summary>
        private static string Custom(double value, string format, int significantDigits)
        {
            int point = -1, minInteger = 0, minDecimals = 0, maxDecimals = 0;
            bool grouping = false, percent = false, seenDigit = false;
            for (int i = 0; i < format.Length; i++)
            {
                char c = format[i];
                if (c == '.' && point < 0)
                    point = i;
                else if (c == '0' || c == '#')
                {
                    seenDigit = true;
                    if (point < 0)
                    {
                        if (c == '0')
                            minInteger++;
                    }
                    else
                    {
                        maxDecimals++;
                        if (c == '0')
                            minDecimals = maxDecimals;
                    }
                }
                else if (c == ',' && point < 0 && seenDigit)
                    grouping = true;
                else if (c == '%')
                    percent = true;
            }
            // 15 (or 7) significant digits, then rounded half away from zero at the last decimal.
            string digits = Printf(value, significantDigits - 1, 'e'); // -d.dddde+xx
            bool negative = digits[0] == '-';
            if (negative)
                digits = digits.Substring(1);
            int mark = digits.IndexOf('e');
            int exponent = int.Parse(digits.Substring(mark + 1));
            if (percent)
                exponent += 2; // × 100, on the digits
            if (value == 0)
                exponent = 0;
            string mantissa = digits.Substring(0, 1) + digits.Substring(2, mark - 2);
            // Digit k of mantissa has weight 10^(exponent - k). Keep those with weight >= 10^-maxDecimals.
            int keep = exponent + maxDecimals + 1;
            char[] kept;
            int integerDigits = exponent + 1;
            if (keep < 0)
            {
                kept = new char[0];
            }
            else
            {
                kept = new char[keep + 1]; // one extra for a carry
                bool roundUp = keep < mantissa.Length && mantissa[keep] >= '5';
                for (int i = 0; i < keep; i++)
                    kept[i + 1] = i < mantissa.Length ? mantissa[i] : '0';
                kept[0] = '0';
                if (roundUp)
                {
                    int i = keep;
                    while (true)
                    {
                        if (kept[i] == '9')
                        {
                            kept[i] = '0';
                            i--;
                        }
                        else
                        {
                            kept[i]++;
                            break;
                        }
                    }
                }
                integerDigits++;
            }
            // Integer and decimal parts as text.
            var integer = new System.Text.StringBuilder();
            var fraction = new System.Text.StringBuilder();
            for (int i = 0; i < kept.Length; i++)
            {
                if (i < integerDigits)
                    integer.Append(kept[i]);
                else
                    fraction.Append(kept[i]);
            }
            string intText = integer.ToString();
            int start = 0;
            while (start < intText.Length && intText[start] == '0')
                start++;
            intText = intText.Substring(start);
            if (integerDigits <= 0)
            {
                string zeros = new string('0', -integerDigits);
                fraction.Insert(0, zeros);
            }
            string fracText = fraction.ToString();
            if (fracText.Length > maxDecimals)
                fracText = fracText.Substring(0, maxDecimals);
            while (fracText.Length < maxDecimals)
                fracText += "0";
            int end = fracText.Length;
            while (end > minDecimals && fracText[end - 1] == '0')
                end--;
            fracText = fracText.Substring(0, end);
            intText = PadLeft(intText, minInteger, '0');
            if (grouping)
                intText = Group(intText);
            string body = intText + (fracText.Length > 0 ? "." + fracText : "");
            // A negative value keeps its sign even when it rounds to zero (.NET Core 3.0 and later),
            // unless no digit is written at all ("#.##").
            bool zero = body.Length == 0;
            // Literal characters around the digits are kept in place (prefix and suffix).
            int firstPlaceholder = 0, lastPlaceholder = format.Length - 1;
            while (firstPlaceholder < format.Length && "0#.,".IndexOf(format[firstPlaceholder]) < 0)
                firstPlaceholder++;
            while (lastPlaceholder >= 0 && "0#.,".IndexOf(format[lastPlaceholder]) < 0)
                lastPlaceholder--;
            string prefix = format.Substring(0, firstPlaceholder);
            string suffix = lastPlaceholder + 1 < format.Length ? format.Substring(lastPlaceholder + 1) : "";
            return (negative && !zero ? "-" : "") + prefix + body + suffix;
        }
    }
}
