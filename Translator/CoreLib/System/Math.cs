using System.Runtime.CompilerServices;

namespace System
{
    public static class Math
    {
        public const double PI = 3.14159265358979323846;
        public const double E = 2.7182818284590452354;

        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Sqrt(double d);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Sin(double a);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Cos(double d);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Tan(double a);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Atan2(double y, double x);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Pow(double x, double y);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Exp(double d);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Log(double d);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Floor(double d);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Ceiling(double a);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern double Round(double a);

        public static int Abs(int value) => value < 0 ? -value : value;
        public static long Abs(long value) => value < 0 ? -value : value;
        public static float Abs(float value) => value < 0 ? -value : value;
        public static double Abs(double value) => value < 0 ? -value : value;

        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static long Min(long a, long b) => a < b ? a : b;
        public static long Max(long a, long b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static double Min(double a, double b) => a < b ? a : b;
        public static double Max(double a, double b) => a > b ? a : b;

        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;
    }
}
