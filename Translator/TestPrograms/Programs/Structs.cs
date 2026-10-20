using System;

namespace Tests
{
    public static class Structs
    {
        public struct Vector2
        {
            public float X;
            public float Y;

            public Vector2(float x, float y)
            {
                X = x;
                Y = y;
            }

            public float LengthSquared => X * X + Y * Y;

            public void Scale(float factor)
            {
                X *= factor;
                Y *= factor;
            }

            public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.X + b.X, a.Y + b.Y);

            public override string ToString() => "(" + X + ", " + Y + ")";
        }

        public struct Rect
        {
            public Vector2 Min;
            public Vector2 Max;

            public Vector2 Size => new Vector2(Max.X - Min.X, Max.Y - Min.Y);
        }

        public struct Plain
        {
            public int A;
            public long B;
        }

        public class Holder
        {
            public Vector2 Position;
        }

        public static void Run()
        {
            var a = new Vector2(1, 2);
            var b = new Vector2(3, 4);
            Vector2 c = a + b;
            Console.WriteLine(c.ToString());
            Console.WriteLine(c);
            Console.WriteLine(c.LengthSquared);

            Vector2 copy = c;
            copy.Scale(2);
            Console.WriteLine(c);
            Console.WriteLine(copy);

            var points = new Vector2[3];
            points[1] = new Vector2(5, 6);
            points[1].Scale(10);
            points[2].X = 7;
            for (int i = 0; i < points.Length; i++)
                Console.WriteLine(points[i]);

            var rect = new Rect { Min = new Vector2(1, 1), Max = new Vector2(4, 5) };
            Console.WriteLine(rect.Size);

            var holder = new Holder();
            holder.Position.X = 9;
            holder.Position.Scale(0.5f);
            Console.WriteLine(holder.Position);

            object boxed = c;
            Console.WriteLine(boxed.ToString());
            Vector2 unboxed = (Vector2)boxed;
            unboxed.X = 100;
            Console.WriteLine(boxed);
            Console.WriteLine(unboxed);
            Console.WriteLine(boxed is Vector2);

            object number = 42;
            Console.WriteLine(number);
            Console.WriteLine((int)number + 1);
            object flag = true;
            Console.WriteLine(flag);
            Console.WriteLine(new Plain().ToString());

            Plain plain = default;
            plain.A = 3;
            plain.B = 4000000000L;
            Console.WriteLine(plain.A + plain.B);

            int value = 10;
            AddTen(ref value);
            Console.WriteLine(value);
            Split(17, out int quotient, out int remainder);
            Console.WriteLine(quotient + " r " + remainder);
            Console.WriteLine(Sum(in a));

            Vector2 total = default;
            for (int i = 0; i < 4; i++)
                total = total + new Vector2(i, i * 2);
            Console.WriteLine(total);
        }

        private static void AddTen(ref int value) => value += 10;

        private static void Split(int number, out int quotient, out int remainder)
        {
            quotient = number / 5;
            remainder = number % 5;
        }

        private static float Sum(in Vector2 v) => v.X + v.Y;
    }
}
