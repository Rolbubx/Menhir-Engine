using System;

namespace Tests
{
    public static class Generics
    {
        public class Box<T>
        {
            public T Value;
            public static int Created;

            public Box(T value)
            {
                Value = value;
                Created++;
            }

            public virtual string Describe() => "Box of " + Value.ToString();

            public U Convert<U>(Func1<T, U> converter) => converter.Apply(Value);
        }

        public abstract class Func1<A, B>
        {
            public abstract B Apply(A a);
        }

        public sealed class Doubler : Func1<int, string>
        {
            public override string Apply(int a) => (a * 2).ToString();
        }

        public class NamedBox<T> : Box<T>
        {
            public string Name;

            public NamedBox(string name, T value) : base(value)
            {
                Name = name;
            }

            public override string Describe() => Name + ": " + base.Describe();
        }

        public struct Pair<A, B>
        {
            public A First;
            public B Second;

            public Pair(A first, B second)
            {
                First = first;
                Second = second;
            }

            public Pair<B, A> Swapped() => new Pair<B, A>(Second, First);

            public override string ToString() => "(" + First.ToString() + ", " + Second.ToString() + ")";
        }

        public class Stack<T>
        {
            private T[] _items = new T[2];
            private int _count;

            public int Count => _count;

            public void Push(T item)
            {
                if (_count == _items.Length)
                {
                    T[] bigger = new T[_items.Length * 2];
                    for (int i = 0; i < _count; i++)
                        bigger[i] = _items[i];
                    _items = bigger;
                }
                _items[_count++] = item;
            }

            public T Pop()
            {
                T item = _items[--_count];
                _items[_count] = default(T);
                return item;
            }

            public class Node
            {
                public T Item;
            }

            public Node Peek() => new Node { Item = _items[_count - 1] };
        }

        public class IntStack : Stack<int>
        {
            public int Sum()
            {
                int total = 0;
                var copy = new Stack<int>();
                while (Count > 0)
                {
                    int value = Pop();
                    total += value;
                    copy.Push(value);
                }
                while (copy.Count > 0)
                    Push(copy.Pop());
                return total;
            }
        }

        public static void Swap<T>(ref T a, ref T b)
        {
            T t = a;
            a = b;
            b = t;
        }

        public static T First<T>(T[] items) => items[0];

        public static string Show<T>(T value) => "[" + value.ToString() + "]";

        public static bool IsDefault<T>(T value) => value == null ? true : value.Equals(default(T));

        public static Pair<T, int> Tag<T>(T value, int tag) => new Pair<T, int>(value, tag);

        public static void Run()
        {
            var a = new Box<int>(41);
            var b = new Box<string>("hello");
            var c = new NamedBox<double>("pi", 3.25);
            Console.WriteLine(a.Describe());
            Console.WriteLine(b.Describe());
            Console.WriteLine(c.Describe());
            Console.WriteLine(Box<int>.Created + " " + Box<string>.Created + " " + Box<double>.Created);
            Console.WriteLine(a.ToString());
            Console.WriteLine(c.ToString());
            Console.WriteLine(a.Convert(new Doubler()));

            var p = new Pair<int, string>(7, "seven");
            Console.WriteLine(p.ToString());
            Console.WriteLine(p.Swapped().ToString());
            object boxed = p;
            Console.WriteLine(boxed.ToString());
            Console.WriteLine(Tag("x", 3).ToString());

            int x = 1, y = 2;
            Swap(ref x, ref y);
            string s = "left", t = "right";
            Swap(ref s, ref t);
            Console.WriteLine(x + " " + y + " " + s + " " + t);

            Console.WriteLine(First(new[] { 5, 6 }) + " " + First(new[] { "a", "b" }));
            Console.WriteLine(Show(1.5f) + Show(true) + Show('c') + Show(p));
            Console.WriteLine(IsDefault(0) + " " + IsDefault(3) + " " + IsDefault<string>(null) + " " + IsDefault("z"));

            var stack = new IntStack();
            for (int i = 1; i <= 10; i++)
                stack.Push(i * i);
            Console.WriteLine(stack.Count + " " + stack.Sum() + " " + stack.Peek().Item);

            var words = new Stack<Box<string>>();
            words.Push(new Box<string>("one"));
            words.Push(new NamedBox<string>("n", "two"));
            while (words.Count > 0)
                Console.WriteLine(words.Pop().Describe());
            Console.WriteLine(Box<string>.Created);
        }
    }
}
