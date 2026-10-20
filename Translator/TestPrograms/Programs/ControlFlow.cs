using System;

namespace Tests
{
    public static class ControlFlow
    {
        public static void Run()
        {
            for (int i = 0; i < 12; i += 3)
                Console.WriteLine(Classify(i));

            int found = -1;
            for (int i = 0; i < 10; i++)
            {
                if (i % 2 == 0)
                    continue;
                if (i * i > 20)
                {
                    found = i;
                    break;
                }
            }
            Console.WriteLine(found);

            int n = 27, steps = 0;
            while (n != 1)
            {
                n = n % 2 == 0 ? n / 2 : 3 * n + 1;
                steps++;
            }
            Console.WriteLine(steps);

            int k = 0;
            do
            {
                k += 7;
            } while (k < 50);
            Console.WriteLine(k);

            int outer = 0;
            for (int i = 0; i < 5; i++)
            {
                for (int j = 0; j < 5; j++)
                {
                    if (j > i)
                        break;
                    outer += j;
                }
            }
            Console.WriteLine(outer);

            int g = 0;
        again:
            g++;
            if (g < 3)
                goto again;
            Console.WriteLine(g);

            Console.WriteLine(WithFinally(1));
            Console.WriteLine(WithFinally(-1));
            Nested();
            Console.WriteLine(EarlyReturn(new[] { 4, 8, 15, 16, 23, 42 }, 15));

            bool yes = true, no = false;
            Console.WriteLine(yes && !no);
            Console.WriteLine(yes ^ no);
            Console.WriteLine(no || (yes & yes));
            object choice = yes ? "text" : null;
            Console.WriteLine(choice);
        }

        private static string Classify(int value)
        {
            switch (value)
            {
                case 0: return "zero";
                case 3: return "three";
                case 6:
                case 7: return "six or seven";
                case 100: return "hundred";
                default: return "other " + value;
            }
        }

        private static int WithFinally(int value)
        {
            int result = 0;
            try
            {
                Console.WriteLine("try " + value);
                if (value < 0)
                    return -100;
                result = value * 10;
            }
            finally
            {
                Console.WriteLine("finally " + value);
                result += 1;
            }
            return result;
        }

        private static void Nested()
        {
            try
            {
                try
                {
                    Console.WriteLine("inner try");
                }
                finally
                {
                    Console.WriteLine("inner finally");
                }
                Console.WriteLine("between");
            }
            finally
            {
                Console.WriteLine("outer finally");
            }
        }

        private static int EarlyReturn(int[] items, int wanted)
        {
            for (int i = 0; i < items.Length; i++)
            {
                try
                {
                    if (items[i] == wanted)
                        return i;
                }
                finally
                {
                    Console.WriteLine("checked " + items[i]);
                }
            }
            return -1;
        }
    }
}
