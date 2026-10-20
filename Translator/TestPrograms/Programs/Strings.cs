using System;

namespace Tests
{
    public static class Strings
    {
        public static void Run()
        {
            string hello = "Hello";
            string world = "World";
            string both = hello + ", " + world + "!";
            Console.WriteLine(both);
            Console.WriteLine(both.Length);
            Console.WriteLine(both[7]);
            Console.WriteLine(both.Substring(7));
            Console.WriteLine(both.Substring(0, 5));
            Console.WriteLine(both.IndexOf('W'));
            Console.WriteLine(both.IndexOf('z'));
            Console.WriteLine(both.StartsWith("Hello"));
            Console.WriteLine(both.StartsWith("World"));
            Console.WriteLine(hello == "Hel" + "lo");
            Console.WriteLine(hello != world);
            Console.WriteLine(hello.Equals((object)"Hello"));
            Console.WriteLine(string.IsNullOrEmpty(""));
            Console.WriteLine(string.IsNullOrEmpty(hello));

            string nothing = null;
            Console.WriteLine("[" + nothing + "]");
            Console.WriteLine(string.Concat("a", "b", "c", "d"));

            string built = "";
            for (int i = 0; i < 10; i++)
                built += i;
            Console.WriteLine(built);
            Console.WriteLine("Value: " + 42 + ", " + true + ", " + 'x' + ", " + 2.5 + ", " + -7L);

            Console.WriteLine(Describe("apple"));
            Console.WriteLine(Describe("banana"));
            Console.WriteLine(Describe("cherry"));
            Console.WriteLine(Describe("date"));
            Console.WriteLine(Describe("elderberry"));
            Console.WriteLine(Describe("fig"));
            Console.WriteLine(Describe("grape"));
            Console.WriteLine(Describe("kiwi"));

            Console.WriteLine("Unicode: café → 日本");
            Console.WriteLine(Reverse("stressed"));
            Console.WriteLine(CountVowels("The quick brown fox jumps over the lazy dog"));
        }

        private static string Describe(string fruit)
        {
            switch (fruit)
            {
                case "apple": return "red";
                case "banana": return "yellow";
                case "cherry": return "dark red";
                case "date": return "brown";
                case "elderberry": return "purple";
                case "fig": return "green";
                case "grape": return "violet";
                default: return "unknown";
            }
        }

        private static string Reverse(string text)
        {
            string result = "";
            for (int i = text.Length - 1; i >= 0; i--)
                result += text[i];
            return result;
        }

        private static int CountVowels(string text)
        {
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u')
                    count++;
            }
            return count;
        }
    }
}
