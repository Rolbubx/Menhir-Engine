using System;

namespace Tests
{
    public static class Enums
    {
        public enum Color
        {
            Red,
            Green = 5,
            Blue,
        }

        [Flags]
        public enum Access : byte
        {
            None = 0,
            Read = 1,
            Write = 2,
            Execute = 4,
        }

        public enum Big : long
        {
            Huge = 1L << 40,
        }

        public struct Pixel
        {
            public Color Color;
            public byte Alpha;
        }

        public static void Run()
        {
            Color c = Color.Blue;
            Console.WriteLine((int)c);
            Console.WriteLine((int)Color.Green);
            Console.WriteLine(c > Color.Green);
            Console.WriteLine(Name(c));
            Console.WriteLine(Name(Color.Red));
            Console.WriteLine(Name((Color)42));

            Access access = Access.Read | Access.Execute;
            Console.WriteLine((int)access);
            Console.WriteLine((access & Access.Write) != 0);
            Console.WriteLine((access & Access.Execute) == Access.Execute);
            access |= Access.Write;
            access &= ~Access.Read;
            Console.WriteLine((byte)access);

            Console.WriteLine((long)Big.Huge);

            var pixel = new Pixel { Color = Color.Green, Alpha = 200 };
            Console.WriteLine((int)pixel.Color + pixel.Alpha);
            Color[] palette = { Color.Blue, Color.Red };
            Console.WriteLine((int)palette[0] + (int)palette[1]);
        }

        private static string Name(Color color)
        {
            switch (color)
            {
                case Color.Red: return "red";
                case Color.Green: return "green";
                case Color.Blue: return "blue";
                default: return "unknown";
            }
        }
    }
}
