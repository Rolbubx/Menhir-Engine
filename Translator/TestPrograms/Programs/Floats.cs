using System;

namespace Tests
{
    public static class Floats
    {
        public static void Run()
        {
            double one = 1.0, zero = 0.0;
            Console.WriteLine(0.1);
            Console.WriteLine(one / 3);
            Console.WriteLine(2.0 / 3);
            Console.WriteLine(1e15);
            Console.WriteLine(1e14);
            Console.WriteLine(123456789012345678.0);
            Console.WriteLine(1e-5);
            Console.WriteLine(0.0001);
            Console.WriteLine(1.5e-7);
            Console.WriteLine(-zero);
            Console.WriteLine(3.14159);
            Console.WriteLine(100.0);
            Console.WriteLine(1e300 * 1e10);
            Console.WriteLine(-one / zero);
            Console.WriteLine(zero / zero);
            Console.WriteLine(0.1 + 0.2);
            Console.WriteLine(1.0 / 7 * 7);
            Console.WriteLine(5e-324);
            Console.WriteLine(1.7976931348623157e308);
            Console.WriteLine(-2.5);
            Console.WriteLine(42.0 * 1000000);
            Console.WriteLine(1e16);
            Console.WriteLine(1e17);
            Console.WriteLine(12345678901234567.0);
            Console.WriteLine(0.00012345);
            Console.WriteLine(0.000012345);
            Console.WriteLine(-1e-10);
            Console.WriteLine(1.0 / 1024);

            float third = 1f / 3;
            Console.WriteLine(0.1f);
            Console.WriteLine(third);
            Console.WriteLine(16777216f);
            Console.WriteLine(16777217f);
            Console.WriteLine(1e7f);
            Console.WriteLine(1234567f);
            Console.WriteLine(12345678f);
            Console.WriteLine(3.4e38f);
            Console.WriteLine(1e-5f);
            Console.WriteLine(1e9f);
            Console.WriteLine(123456789f);
            Console.WriteLine(1e8f);
            Console.WriteLine(-0.0001f);
            float fa = 0.1f, fb = 0.2f;
            Console.WriteLine(fa + fb);
            Console.WriteLine((double)fa);
            Console.WriteLine((float)(one / 3));

            Console.WriteLine(Math.Sqrt(2));
            Console.WriteLine(Math.Sin(1));
            Console.WriteLine(Math.Cos(Math.PI));
            Console.WriteLine(Math.Pow(2, 0.5));
            Console.WriteLine(Math.Atan2(1, 1));
            Console.WriteLine(Math.Exp(1));
            Console.WriteLine(Math.Log(10));
            Console.WriteLine(Math.Round(2.5));
            Console.WriteLine(Math.Round(3.5));
            Console.WriteLine(Math.Round(-2.5));
            Console.WriteLine(Math.Floor(-1.5));
            Console.WriteLine(Math.Ceiling(-1.5));
            Console.WriteLine(Math.Abs(-0.5));
            Console.WriteLine(Math.Max(1.5, 2.5));

            double nan = zero / zero;
            Console.WriteLine(nan == nan);
            Console.WriteLine(nan != nan);
            Console.WriteLine(nan < one);
            Console.WriteLine(!(nan >= one));
            Console.WriteLine(one > zero);

            double sum = 0;
            for (int i = 1; i <= 100; i++)
                sum += 1.0 / i;
            Console.WriteLine(sum);

            double r = 7.5 % 2;
            Console.WriteLine(r);
            Console.WriteLine(-7.5 % 2);
            Console.WriteLine(fa * 3 % 0.25f);
        }
    }
}
