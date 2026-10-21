using System;
using System.Collections.Generic;
using System.Linq;

namespace Tests
{
    public static class Collections
    {
        private struct Point : IEquatable<Point>
        {
            public int X, Y;
            public Point(int x, int y) { X = x; Y = y; }
            public bool Equals(Point other) => X == other.X && Y == other.Y;
            public override bool Equals(object obj) => obj is Point p && Equals(p);
            public override int GetHashCode() => X * 397 ^ Y;
            public override string ToString() => "(" + X + "," + Y + ")";
        }

        private class Enemy : IComparable<Enemy>
        {
            public string Name;
            public int Health;
            public int CompareTo(Enemy other) => Health.CompareTo(other.Health);
            public override string ToString() => Name + ":" + Health;
        }

        private static IEnumerable<int> Fibonacci(int count)
        {
            int a = 0, b = 1;
            for (int i = 0; i < count; i++)
            {
                yield return a;
                int t = a + b;
                a = b;
                b = t;
            }
        }

        private static string Show<T>(IEnumerable<T> items) => "[" + string.Join(", ", items) + "]";

        public static void Run()
        {
            var list = new List<int> { 5, 3, 9, 1 };
            list.Add(7);
            list.Insert(0, 4);
            list.Remove(9);
            Console.WriteLine(Show(list) + " count " + list.Count + " contains 3: " + list.Contains(3) + " index of 7: " + list.IndexOf(7));
            list.Sort();
            Console.WriteLine(Show(list));
            list.Sort((a, b) => b.CompareTo(a));
            Console.WriteLine(Show(list));
            list.RemoveAll(x => x % 2 == 1);
            Console.WriteLine(Show(list));
            list.Reverse();
            int sum = 0;
            foreach (int x in list)
                sum += x;
            Console.WriteLine(sum + " " + list.Find(x => x > 3) + " " + list.Exists(x => x == 4));

            var big = new List<int>();
            var random = new Random(42);
            for (int i = 0; i < 200; i++)
                big.Add(random.Next(1000));
            big.Sort();
            bool sorted = true;
            for (int i = 1; i < big.Count; i++)
                sorted &= big[i - 1] <= big[i];
            Console.WriteLine("sorted " + sorted + " first " + big[0] + " last " + big[big.Count - 1]);

            var enemies = new List<Enemy>
            {
                new Enemy { Name = "orc", Health = 30 },
                new Enemy { Name = "bat", Health = 5 },
                new Enemy { Name = "troll", Health = 80 },
            };
            enemies.Sort();
            Console.WriteLine(Show(enemies));

            var scores = new Dictionary<string, int>();
            scores["ana"] = 10;
            scores["bo"] = 20;
            scores.Add("cy", 30);
            scores["ana"] += 5;
            Console.WriteLine(scores["ana"] + " " + scores.Count + " " + scores.ContainsKey("bo") + " " + scores.ContainsKey("zed"));
            foreach (KeyValuePair<string, int> pair in scores)
                Console.WriteLine(pair.Key + "=" + pair.Value);
            scores.Remove("bo");
            scores["dee"] = 40;
            foreach (var (name, score) in scores)
                Console.WriteLine(name + " -> " + score);
            Console.WriteLine(scores.TryGetValue("cy", out int cy) + " " + cy + " " + scores.TryGetValue("bo", out int bo) + " " + bo);
            Console.WriteLine(Show(scores.Keys) + " " + Show(scores.Values));
            try
            {
                Console.WriteLine(scores["nobody"]);
            }
            catch (KeyNotFoundException e)
            {
                Console.WriteLine(e.Message);
            }

            var grid = new Dictionary<Point, string>();
            for (int x = 0; x < 30; x++)
                grid[new Point(x, x * 2)] = "cell" + x;
            Console.WriteLine(grid[new Point(7, 14)] + " " + grid.Count + " " + grid.ContainsKey(new Point(7, 15)));

            var set = new HashSet<string> { "a", "b", "a", "c" };
            Console.WriteLine(set.Count + " " + set.Contains("b") + " " + set.Add("b") + " " + set.Add("d") + " " + Show(set));

            var queue = new Queue<int>();
            for (int i = 1; i <= 6; i++)
                queue.Enqueue(i * 10);
            Console.Write(queue.Dequeue() + " " + queue.Dequeue() + " ");
            queue.Enqueue(70);
            Console.WriteLine(Show(queue) + " peek " + queue.Peek());

            var stack = new Stack<string>();
            stack.Push("x");
            stack.Push("y");
            stack.Push("z");
            Console.WriteLine(stack.Pop() + " " + Show(stack) + " " + stack.Count);

            Console.WriteLine(Show(Fibonacci(10)));
            int[] numbers = { 4, 8, 15, 16, 23, 42 };
            Console.WriteLine(Show(numbers.Where(n => n % 2 == 0).Select(n => n * 10)));
            Console.WriteLine(numbers.Sum() + " " + numbers.Max() + " " + numbers.Min() + " " + numbers.Count(n => n > 10) + " " + numbers.Average());
            Console.WriteLine(numbers.Any(n => n > 40) + " " + numbers.All(n => n > 0) + " " + numbers.First(n => n > 10) + " " + numbers.FirstOrDefault(n => n > 100));
            Console.WriteLine(Show(enemies.OrderByDescending(e => e.Health).Select(e => e.Name)));
            Console.WriteLine(Show(new[] { "pear", "fig", "apple", "kiwi" }.OrderBy(w => w.Length)));
            Console.WriteLine(Show(Enumerable.Range(1, 5).Reverse().Take(3).Concat(Enumerable.Repeat(0, 2))));
            Console.WriteLine(Show(new[] { 1, 2, 2, 3, 1 }.Distinct().ToList()));
            Dictionary<int, string> byLength = new[] { "a", "bb", "ccc" }.ToDictionary(w => w.Length);
            Console.WriteLine(byLength[2] + " " + Show(numbers.Skip(4)));

            List<int> modified = new List<int> { 1, 2, 3 };
            try
            {
                foreach (int n in modified)
                    modified.Add(n);
            }
            catch (InvalidOperationException e)
            {
                Console.WriteLine(e.Message);
            }
        }
    }
}
