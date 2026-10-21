using System;

namespace Tests
{
    public static class Exceptions
    {
        public class GameException : Exception
        {
            public int Code;
            public GameException(string message, int code) : base(message) { Code = code; }
        }

        private static int _depth;

        private static void Recurse(int n)
        {
            _depth = n;
            if (n == 0)
                throw new GameException("bottom", 7);
            try
            {
                Recurse(n - 1);
            }
            finally
            {
                Console.WriteLine("unwinding " + n);
            }
        }

        private static string Classify(int i)
        {
            try
            {
                if (i == 0) throw new InvalidOperationException("zero");
                if (i == 1) throw new ArgumentException("one");
                if (i == 2) throw new GameException("two", 2);
                if (i == 3) { int[] a = new int[1]; return a[i].ToString(); }
                if (i == 4) { object s = null; return s.ToString(); }
                if (i == 5) { int z = i - 5; return (10 / z).ToString(); }
                return "none";
            }
            catch (GameException e) when (e.Code > 5)
            {
                return "big game " + e.Message;
            }
            catch (GameException e)
            {
                return "game " + e.Message + " " + e.Code;
            }
            catch (ArgumentException e)
            {
                return "argument " + e.Message;
            }
            catch (NullReferenceException)
            {
                return "null";
            }
            catch (Exception e)
            {
                return "other " + e.Message;
            }
        }

        private static int FinallyOrder()
        {
            int x = 1;
            try
            {
                x = 2;
                try
                {
                    x = 3;
                    throw new Exception("inner");
                }
                finally
                {
                    x *= 10;
                    Console.WriteLine("inner finally " + x);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("caught " + e.Message + " x=" + x);
                x += 1;
            }
            finally
            {
                Console.WriteLine("outer finally " + x);
            }
            return x;
        }

        private static void Rethrow()
        {
            try
            {
                try
                {
                    throw new GameException("again", 1);
                }
                catch (GameException)
                {
                    Console.WriteLine("handling, then rethrowing");
                    throw;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("rethrown: " + e.Message);
            }
        }

        private static int LoopWithTry()
        {
            int total = 0;
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    if (i % 2 == 0)
                        throw new Exception("even");
                    total += i;
                }
                catch (Exception)
                {
                    total += 100;
                    continue;
                }
                finally
                {
                    total += 1000;
                }
                total += 10000;
            }
            return total;
        }

        public static void Run()
        {
            for (int i = 0; i <= 6; i++)
                Console.WriteLine(i + ": " + Classify(i));
            try
            {
                Recurse(3);
            }
            catch (GameException e)
            {
                Console.WriteLine("caught " + e.Message + " at depth " + _depth + " code " + e.Code);
            }
            Console.WriteLine("result " + FinallyOrder());
            Rethrow();
            Console.WriteLine("loop " + LoopWithTry());
            try
            {
                checked
                {
                    int big = int.MaxValue;
                    big += _depth + 1;
                    Console.WriteLine(big);
                }
            }
            catch (OverflowException e)
            {
                Console.WriteLine("overflow: " + e.Message);
            }
            try
            {
                object text = "x";
                int n = (int)text;
                Console.WriteLine(n);
            }
            catch (InvalidCastException)
            {
                Console.WriteLine("bad cast");
            }
            Console.WriteLine(new GameException("msg", 1).ToString());
        }
    }
}
