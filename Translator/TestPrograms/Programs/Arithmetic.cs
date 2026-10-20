using System;

namespace Tests
{
    public static class Arithmetic
    {
        public static void Run()
        {
            int a = 17, b = 5, m = -17;
            Console.WriteLine(a + b);
            Console.WriteLine(a - b);
            Console.WriteLine(a * b);
            Console.WriteLine(a / b);
            Console.WriteLine(a % b);
            Console.WriteLine(m / b);
            Console.WriteLine(m % b);
            Console.WriteLine(a / -b);

            int max = int.MaxValue;
            Console.WriteLine(max + 1);
            int min = int.MinValue;
            Console.WriteLine(-min);
            Console.WriteLine(min / 1);

            long big = 1L << 40;
            Console.WriteLine(big * 3);
            Console.WriteLine(long.MaxValue);
            Console.WriteLine(long.MinValue);
            Console.WriteLine(big / 7 + big % 7);

            uint u = 4000000000;
            Console.WriteLine(u);
            Console.WriteLine(u / 3);
            Console.WriteLine((int)u);
            Console.WriteLine(u > 5u);
            ulong ul = 18446744073709551615UL;
            Console.WriteLine(ul);
            Console.WriteLine(ul / 7);
            Console.WriteLine(ul % 1000);

            // Shift counts are masked, like in .NET.
            int one = 1, shift = 33;
            Console.WriteLine(one << shift);
            Console.WriteLine(-16 >> 2);
            uint high = 0x80000000;
            Console.WriteLine(high >> 31);
            long lone = 1;
            Console.WriteLine(lone << 65);
            Console.WriteLine(m >> 1);
            Console.WriteLine((uint)m >> 1);

            int f = 0xF0, c = 0x3C;
            Console.WriteLine(f & c);
            Console.WriteLine(f | c);
            Console.WriteLine(f ^ c);
            Console.WriteLine(~f);

            uint x = 1, y = 0xFFFFFFFF;
            Console.WriteLine(x < y);
            Console.WriteLine((int)x < (int)y);

            int i300 = 300, i200 = 200, i70000 = 70000, i65 = 65;
            Console.WriteLine((byte)i300);
            Console.WriteLine((sbyte)i200);
            Console.WriteLine((short)i70000);
            Console.WriteLine((ushort)-i65);
            Console.WriteLine((char)i65);
            Console.WriteLine((long)m);
            Console.WriteLine((ulong)(uint)m);

            double d1 = 3.99, d2 = -3.99, huge = 1e20, zero = 0.0;
            Console.WriteLine((int)d1);
            Console.WriteLine((int)d2);
            Console.WriteLine((int)huge);
            Console.WriteLine((int)-huge);
            Console.WriteLine((int)(zero / zero));
            Console.WriteLine((long)(-huge * huge));
            Console.WriteLine((uint)d2);
            Console.WriteLine((ulong)(huge * huge));
            Console.WriteLine((byte)d1);
            Console.WriteLine((short)(d1 * 10000));

            int product = checked(a * b);
            Console.WriteLine(product);
            long checkedLong = checked(big + big);
            Console.WriteLine(checkedLong);

            Console.WriteLine(Math.Abs(m));
            Console.WriteLine(Math.Min(a, b));
            Console.WriteLine(Math.Max(a, m));
            Console.WriteLine(Math.Clamp(a, 0, 10));

            Console.WriteLine(Fibonacci(20));
            Console.WriteLine(Factorial(20));
            Console.WriteLine(Gcd(1071, 462));

            char letter = 'a';
            Console.WriteLine((char)(letter + 2));
            Console.WriteLine(letter + 2);

            int counter = 0;
            counter += 5;
            counter *= 3;
            counter -= 1;
            counter /= 2;
            counter <<= 3;
            counter++;
            ++counter;
            counter--;
            Console.WriteLine(counter);
        }

        private static int Fibonacci(int n) => n < 2 ? n : Fibonacci(n - 1) + Fibonacci(n - 2);

        private static long Factorial(int n) => n <= 1 ? 1 : n * Factorial(n - 1);

        private static int Gcd(int a, int b)
        {
            while (b != 0)
            {
                int t = a % b;
                a = b;
                b = t;
            }
            return a;
        }
    }
}
