using System;

namespace Tests
{
    public static class Hello
    {
        public static void Run()
        {
            Console.WriteLine("Hello from Menhir!");
            int sum = 0;
            for (int i = 1; i <= 10; i++)
                sum += i;
            Console.WriteLine("1 + ... + 10 = " + sum);
        }
    }
}
