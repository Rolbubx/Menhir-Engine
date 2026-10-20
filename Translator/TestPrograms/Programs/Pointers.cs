using System;

namespace Tests
{
    public static unsafe class Pointers
    {
        public struct Pair
        {
            public int First;
            public int Second;
        }

        public static void Run()
        {
            int[] values = { 1, 2, 3, 4, 5 };
            int sum = 0;
            fixed (int* start = values)
            {
                for (int* p = start; p < start + values.Length; p++)
                    sum += *p;
                start[0] = 100;
            }
            Console.WriteLine(sum);
            Console.WriteLine(values[0]);

            int* buffer = stackalloc int[8];
            for (int i = 0; i < 8; i++)
                buffer[i] = i * i;
            Console.WriteLine(buffer[7] - buffer[3]);

            Pair pair;
            pair.First = 1;
            pair.Second = 2;
            int* second = &pair.Second;
            *second = 20;
            Console.WriteLine(pair.First + pair.Second);
            Console.WriteLine(sizeof(Pair));
            Console.WriteLine(sizeof(long));

            string text = "pointer";
            int letters = 0;
            fixed (char* chars = text)
            {
                for (char* c = chars; *c != '\0'; c++)
                    letters++;
            }
            Console.WriteLine(letters);

            long big = 0x0102030405060708;
            byte* bytes = (byte*)&big;
            Console.WriteLine(bytes[0]);
            Console.WriteLine((long)(bytes + 3 - bytes));
        }
    }
}
