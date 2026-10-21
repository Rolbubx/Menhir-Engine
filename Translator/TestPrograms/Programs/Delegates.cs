using System;

namespace Tests
{
    public static class Delegates
    {
        public delegate int Operation(int a, int b);

        private static int Add(int a, int b) => a + b;

        private class Accumulator
        {
            public int Total;
            public void Add(int value) { Total += value; }
            public virtual string Describe() => "accumulator " + Total;
        }

        private class Loud : Accumulator
        {
            public override string Describe() => "LOUD " + Total;
        }

        private struct Point
        {
            public int X;
            public int Length() => X < 0 ? -X : X;
        }

        public class Button
        {
            public event Action<string> Clicked;
            public void Click(string who) => Clicked?.Invoke(who);
        }

        private static int Apply(Func<int, int> f, int value) => f(value);

        private static T[] Map<T>(T[] items, Func<T, T> f)
        {
            var result = new T[items.Length];
            for (int i = 0; i < items.Length; i++)
                result[i] = f(items[i]);
            return result;
        }

        public static void Run()
        {
            Operation op = Add;
            Console.WriteLine(op(2, 3));
            op = (a, b) => a * b;
            Console.WriteLine(op(4, 5));

            int factor = 3;
            Func<int, int> triple = x => x * factor;
            factor = 4;
            Console.WriteLine(triple(5) + " " + Apply(x => x - 1, 10));

            var acc = new Accumulator();
            Action<int> add = acc.Add;
            add(5);
            add(6);
            Console.WriteLine(acc.Total);

            Func<string> describe = new Loud { Total = 2 }.Describe;
            Console.WriteLine(describe());

            var p = new Point { X = -9 };
            Func<int> length = p.Length;
            Console.WriteLine(length());

            Action chain = () => Console.WriteLine("first");
            chain += () => Console.WriteLine("second");
            Action third = () => Console.WriteLine("third");
            chain += third;
            chain();
            chain -= third;
            chain();

            var button = new Button();
            int clicks = 0;
            Action<string> onClick = who => { clicks++; Console.WriteLine("clicked by " + who); };
            button.Clicked += onClick;
            button.Clicked += who => Console.WriteLine("logged " + who);
            button.Click("Ana");
            button.Clicked -= onClick;
            button.Click("Bo");
            Console.WriteLine("clicks " + clicks);

            string[] words = Map(new[] { "a", "b" }, w => w + w);
            int[] squares = Map(new[] { 1, 2, 3 }, n => n * n);
            Console.WriteLine(words[0] + words[1] + " " + squares[0] + squares[1] + squares[2]);

            Func<int, Func<int, int>> adder = a => b => a + b;
            Console.WriteLine(adder(10)(32));

            Predicate<int> isEven = n => n % 2 == 0;
            Comparison<int> compare = (a, b) => a.CompareTo(b);
            Console.WriteLine(isEven(4) + " " + isEven(5) + " " + compare(1, 2) + " " + compare(3, 3));
            Console.WriteLine((op == op) + " " + (chain != null));
        }
    }
}
