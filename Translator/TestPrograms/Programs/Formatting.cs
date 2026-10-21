using System;
using System.Text;

namespace Tests
{
    public static class Formatting
    {
        private enum Color { Red, Green = 5, Blue }

        [Flags]
        private enum Abilities { None = 0, Jump = 1, Run = 2, Swim = 4, All = Jump | Run | Swim }

        public static void Run()
        {
            double[] values = { 0, 1, -1, 2.5, 0.125, 1.005, 3.14159265, 1234567.891, -0.0001, 1e21, 123.456, 0.5, 1.5, -2.5 };
            string[] formats = { "F2", "F0", "N2", "N0", "P1", "E3", "0.00", "#.##", "0", "#,0.0", "0.0%", "G3" };
            foreach (string format in formats)
            {
                var line = new StringBuilder(format).Append(':');
                foreach (double v in values)
                    line.Append(' ').Append(v.ToString(format));
                Console.WriteLine(line.ToString());
            }
            float f = 2.675f;
            Console.WriteLine(f.ToString("F2") + " " + f.ToString("0.00") + " " + (1.0f / 3).ToString("F5") + " " + (0.1f + 0.2f));
            int[] ints = { 0, 7, -42, 255, 123456789, int.MinValue };
            foreach (string format in new[] { "D5", "X", "x8", "N0", "F1", "000", "#,#" })
            {
                var line = new StringBuilder(format).Append(':');
                foreach (int v in ints)
                    line.Append(' ').Append(v.ToString(format));
                Console.WriteLine(line.ToString());
            }
            Console.WriteLine(string.Format("{0} + {1} = {2:F1} [{3,6}] [{3,-6}] {{braces}}", 1, 2.5, 3.5, "ab"));
            string name = "Ana";
            int score = 1234;
            float time = 12.3456f;
            Console.WriteLine($"{name} scored {score:N0} in {time:F2}s ({score / 10.0:0.#}%) at level {3}");
            Console.WriteLine($"{Color.Green} {Color.Blue} {(Color)7} {Abilities.Jump | Abilities.Swim} {Abilities.All} {Abilities.None} {(Abilities)8}");
            Console.WriteLine(Color.Red.ToString() + " " + ((int)Color.Blue) + " " + (Abilities.All.HasFlag(Abilities.Run)));

            Console.WriteLine(int.Parse(" -123 ") + " " + int.TryParse("12a", out int bad) + " " + bad + " " + long.Parse("9223372036854775807"));
            Console.WriteLine(double.Parse("3.5e2") + " " + float.Parse("0.1") + " " + double.TryParse("1.2.3", out double d) + " " + bool.Parse("TRUE"));
            try
            {
                int.Parse("99999999999");
            }
            catch (OverflowException e)
            {
                Console.WriteLine(e.Message);
            }
            try
            {
                int.Parse("x1");
            }
            catch (FormatException e)
            {
                Console.WriteLine(e.Message);
            }

            string text = "  Hello, World!  ";
            Console.WriteLine("[" + text.Trim() + "][" + text.TrimStart() + "][" + text.TrimEnd() + "]");
            string t = text.Trim();
            Console.WriteLine(t.ToUpper() + " " + t.ToLower() + " " + t.Replace("World", "Menhir") + " " + t.Replace('l', 'L'));
            Console.WriteLine(t.IndexOf("World") + " " + t.LastIndexOf('o') + " " + t.Contains("lo,") + " " + t.EndsWith("!") + " " + t.StartsWith("Hell"));
            Console.WriteLine(string.Join("|", "a,b,,c".Split(',')) + " " + string.Join("|", "one two  three".Split(' ')) + " " + "x--y--z".Split("--").Length);
            Console.WriteLine("7".PadLeft(3, '0') + " " + "ab".PadRight(4) + "| " + new string('*', 5) + " " + "abc".Insert(1, "XY") + " " + "abcdef".Remove(2, 3));
            Console.WriteLine(string.Compare("apple", "banana") < 0 ? "apple first" : "banana first");
            var builder = new StringBuilder();
            for (int i = 0; i < 5; i++)
                builder.Append(i).Append(',');
            builder.Length -= 1;
            builder.Insert(0, "[").Append(']').AppendLine().AppendFormat("{0}-{1}", "x", 9);
            Console.WriteLine(builder.ToString());

            Console.WriteLine(typeof(int) + " " + typeof(Color).Name + " " + "s".GetType().FullName + " " + new int[0].GetType() + " " + (typeof(string) == "x".GetType()));
            int? maybe = null;
            Console.WriteLine(maybe.HasValue + " " + maybe.GetValueOrDefault(7) + " [" + maybe + "]");
            maybe = 3;
            object boxed = maybe;
            int? back = (int?)boxed;
            Console.WriteLine(maybe.Value + " " + boxed + " " + (back == 3) + " " + ((int?)null == null));
            (int, string) pair = (1, "one");
            var (number, word) = pair;
            Console.WriteLine(pair + " " + number + word + " " + (2, 3.5, "x"));

            var r = new Random(12345);
            var rolls = new StringBuilder();
            for (int i = 0; i < 6; i++)
                rolls.Append(r.Next(1, 7)).Append(' ');
            rolls.Append(r.Next()).Append(' ').Append(r.Next(100)).Append(' ').Append(r.NextDouble().ToString("F6"));
            Console.WriteLine(rolls.ToString());
            Console.WriteLine(Math.Round(2.5) + " " + Math.Round(3.5) + " " + Math.Round(2.5, MidpointRounding.AwayFromZero) + " " + Math.Round(1.23456, 2) + " " + Math.Sign(-3.5) + " " + Math.Abs(-7));
            Console.WriteLine(MathF.Sqrt(2f) + " " + MathF.Sin(1f) + " " + Math.Atan2(1, 2) + " " + Math.Log10(1000) + " " + MathF.Pow(2f, 0.5f) + " " + Math.Max(-0.0, 0.0));
        }
    }
}
