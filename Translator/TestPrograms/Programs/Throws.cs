using System;

namespace Tests
{
    /// <summary>An unhandled exception ends the program (catching them comes with IL2C-2).</summary>
    public static class Throws
    {
        public static void Run(string kind)
        {
            Console.WriteLine("before");
            if (kind == "throw")
                throw new InvalidOperationException("Something went wrong.");
            if (kind == "index")
            {
                int[] items = new int[2];
                int i = 5;
                Console.WriteLine(items[i]);
            }
            if (kind == "null")
            {
                string text = null;
                Console.WriteLine(text.Length);
            }
            if (kind == "divide")
            {
                int zero = 0;
                Console.WriteLine(10 / zero);
            }
            if (kind == "cast")
            {
                object o = "text";
                Console.WriteLine(((Classes.Plain)o).ToString());
            }
            Console.WriteLine("not reached");
        }
    }
}
