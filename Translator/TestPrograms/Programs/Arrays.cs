using System;

namespace Tests
{
    public static class Arrays
    {
        public static void Run()
        {
            int[] numbers = { 5, 3, 8, 1, 9, 2, 7 };
            Sort(numbers);
            Print(numbers);
            int sum = 0;
            foreach (int n in numbers)
                sum += n;
            Console.WriteLine(sum);

            long[] longs = { 1L << 40, -1, 3 };
            Console.WriteLine(longs[0] + longs[1] + longs[2]);
            double[] doubles = { 0.5, 1.25, -2 };
            Console.WriteLine(doubles[0] + doubles[1] + doubles[2]);
            byte[] bytes = { 1, 2, 250 };
            Console.WriteLine(bytes[2] + bytes[1]);
            char[] chars = { 'M', 'e', 'n', 'h', 'i', 'r' };
            string word = "";
            foreach (char ch in chars)
                word += ch;
            Console.WriteLine(word);
            bool[] flags = new bool[3];
            flags[1] = true;
            Console.WriteLine(flags[0] + " " + flags[1]);

            int[][] jagged = new int[3][];
            for (int i = 0; i < jagged.Length; i++)
            {
                jagged[i] = new int[i + 1];
                for (int j = 0; j <= i; j++)
                    jagged[i][j] = i * 10 + j;
            }
            for (int i = 0; i < jagged.Length; i++)
                Print(jagged[i]);

            string[] names = { "Ada", "Grace", "Linus" };
            object[] objects = names;
            Console.WriteLine(objects[1]);
            Console.WriteLine(objects.Length);
            Console.WriteLine(names.ToString());
            Console.WriteLine(numbers.ToString());

            object[] mixed = { 1, "two", 3.5, 'c', null };
            for (int i = 0; i < mixed.Length; i++)
                Console.WriteLine(mixed[i] == null ? "null" : mixed[i].ToString());

            int[] empty = new int[0];
            Console.WriteLine(empty.Length);

            int[] squares = new int[10];
            for (int i = 0; i < squares.Length; i++)
                squares[i] = i * i;
            Console.WriteLine(squares[9]);
            Print(Reverse(squares));
        }

        private static void Sort(int[] items)
        {
            for (int i = 0; i < items.Length; i++)
            {
                for (int j = 0; j < items.Length - 1 - i; j++)
                {
                    if (items[j] > items[j + 1])
                    {
                        int t = items[j];
                        items[j] = items[j + 1];
                        items[j + 1] = t;
                    }
                }
            }
        }

        private static int[] Reverse(int[] items)
        {
            var result = new int[items.Length];
            for (int i = 0; i < items.Length; i++)
                result[items.Length - 1 - i] = items[i];
            return result;
        }

        private static void Print(int[] items)
        {
            string text = "[";
            for (int i = 0; i < items.Length; i++)
                text += (i > 0 ? ", " : "") + items[i];
            Console.WriteLine(text + "]");
        }
    }
}
