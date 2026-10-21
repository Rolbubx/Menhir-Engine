using System.Runtime.CompilerServices;

namespace System
{
    public enum MidpointRounding
    {
        ToEven = 0,
        AwayFromZero = 1,
    }

    /// <summary>Math functions: the C library's (like .NET on Linux), so results match exactly.</summary>
    public static class Math
    {
        public const double PI = 3.14159265358979323846;
        public const double E = 2.7182818284590452354;
        public const double Tau = 6.2831853071795864769;

        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Sqrt(double d);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Sin(double a);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Cos(double d);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Tan(double a);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Asin(double d);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Acos(double d);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Atan(double d);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Atan2(double y, double x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Sinh(double value);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Cosh(double value);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Tanh(double value);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Pow(double x, double y);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Exp(double d);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Log(double d);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Log10(double d);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Log2(double x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Floor(double d);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Ceiling(double a);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Truncate(double d);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Cbrt(double d);
        /// <summary>Rounds half to even.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Round(double a);

        public static double Log(double a, double newBase) => Log(a) / Log(newBase);

        public static double Round(double value, MidpointRounding mode) =>
            mode == MidpointRounding.ToEven ? Round(value) : Truncate(value + CopySign(0.49999999999999994, value));

        public static double Round(double value, int digits) => Round(value, digits, MidpointRounding.ToEven);

        public static double Round(double value, int digits, MidpointRounding mode)
        {
            double power = 1;
            for (int i = 0; i < digits; i++)
                power *= 10;
            return Round(value * power, mode) / power;
        }

        public static double CopySign(double x, double y) => (x < 0) == (y < 0 || (y == 0 && 1 / y < 0)) ? x : -x;

        public static int Abs(int value) => value < 0 ? (value == int.MinValue ? throw new OverflowException("Negating the minimum value of a twos complement number is invalid.") : -value) : value;
        public static long Abs(long value) => value < 0 ? -value : value;
        public static float Abs(float value) => value < 0 || (value == 0 && 1 / value < 0) ? -value : value;
        public static double Abs(double value) => value < 0 || (value == 0 && 1 / value < 0) ? -value : value;

        public static int Sign(int value) => value < 0 ? -1 : value > 0 ? 1 : 0;
        public static int Sign(long value) => value < 0 ? -1 : value > 0 ? 1 : 0;
        public static int Sign(float value) => value != value ? throw new ArithmeticException("Function does not accept floating point Not-a-Number values.") : value < 0 ? -1 : value > 0 ? 1 : 0;
        public static int Sign(double value) => value != value ? throw new ArithmeticException("Function does not accept floating point Not-a-Number values.") : value < 0 ? -1 : value > 0 ? 1 : 0;

        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static long Min(long a, long b) => a < b ? a : b;
        public static long Max(long a, long b) => a > b ? a : b;
        public static float Min(float a, float b) => a != a ? a : b != b ? b : a < b ? a : b == a && 1 / a < 0 ? a : b;
        public static float Max(float a, float b) => a != a ? a : b != b ? b : a > b ? a : b == a && 1 / a > 0 ? a : b;
        public static double Min(double a, double b) => a != a ? a : b != b ? b : a < b ? a : b == a && 1 / a < 0 ? a : b;
        public static double Max(double a, double b) => a != a ? a : b != b ? b : a > b ? a : b == a && 1 / a > 0 ? a : b;

        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static long Clamp(long value, long min, long max) => value < min ? min : value > max ? max : value;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;
    }

    /// <summary>Float versions of the math functions (the C library's float functions, like .NET on Linux).</summary>
    public static class MathF
    {
        public const float PI = 3.14159265f;
        public const float E = 2.71828183f;
        public const float Tau = 6.28318531f;

        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Sqrt(float x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Sin(float x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Cos(float x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Tan(float x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Asin(float x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Acos(float x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Atan(float x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Atan2(float y, float x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Pow(float x, float y);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Exp(float x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Log(float x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Log10(float x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Log2(float x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Floor(float x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Ceiling(float x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Truncate(float x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern float Round(float x);

        public static float Abs(float x) => Math.Abs(x);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Sign(float x) => Math.Sign(x);
        public static float Round(float x, int digits) => (float)Math.Round(x, digits);
        public static float Round(float x, MidpointRounding mode) => (float)Math.Round(x, mode);
    }

    /// <summary>
    /// Pseudo-random numbers. With a seed, the same sequence as .NET's Random(seed) (Knuth's
    /// subtractive generator); without one, seeded from the clock.
    /// </summary>
    public class Random
    {
        private readonly int[] _seedArray = new int[56];
        private int _inext;
        private int _inextp;

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern int TimeSeed();

        public Random() : this(TimeSeed()) { }

        public Random(int seed)
        {
            int subtraction = seed == int.MinValue ? int.MaxValue : Math.Abs(seed);
            int mj = 161803398 - subtraction;
            _seedArray[55] = mj;
            int mk = 1;
            int ii = 0;
            for (int i = 1; i < 55; i++)
            {
                if ((ii += 21) >= 55)
                    ii -= 55;
                _seedArray[ii] = mk;
                mk = mj - mk;
                if (mk < 0)
                    mk += int.MaxValue;
                mj = _seedArray[ii];
            }
            for (int k = 1; k < 5; k++)
            {
                for (int i = 1; i < 56; i++)
                {
                    int n = i + 30;
                    if (n >= 55)
                        n -= 55;
                    _seedArray[i] -= _seedArray[1 + n];
                    if (_seedArray[i] < 0)
                        _seedArray[i] += int.MaxValue;
                }
            }
            _inextp = 21;
        }

        private int InternalSample()
        {
            int locINext = _inext;
            if (++locINext >= 56)
                locINext = 1;
            int locINextp = _inextp;
            if (++locINextp >= 56)
                locINextp = 1;
            int retVal = _seedArray[locINext] - _seedArray[locINextp];
            if (retVal == int.MaxValue)
                retVal--;
            if (retVal < 0)
                retVal += int.MaxValue;
            _seedArray[locINext] = retVal;
            _inext = locINext;
            _inextp = locINextp;
            return retVal;
        }

        protected virtual double Sample() => InternalSample() * (1.0 / int.MaxValue);

        private double GetSampleForLargeRange()
        {
            int result = InternalSample();
            if (InternalSample() % 2 == 0)
                result = -result;
            double d = result;
            d += int.MaxValue - 1;
            d /= 2.0 * int.MaxValue - 1;
            return d;
        }

        public virtual int Next() => InternalSample();

        public virtual int Next(int maxValue)
        {
            if (maxValue < 0)
                throw new ArgumentOutOfRangeException("'maxValue' must be greater than zero.");
            return (int)(Sample() * maxValue);
        }

        public virtual int Next(int minValue, int maxValue)
        {
            if (minValue > maxValue)
                throw new ArgumentOutOfRangeException("'minValue' cannot be greater than maxValue.");
            long range = (long)maxValue - minValue;
            return range <= int.MaxValue
                ? (int)(Sample() * range) + minValue
                : (int)((long)(GetSampleForLargeRange() * range) + minValue);
        }

        public virtual double NextDouble() => Sample();

        public virtual float NextSingle() => (float)Sample();
    }
}
