using System;

namespace Tests
{
    public static class Classes
    {
        public abstract class Shape
        {
            private static int s_created;

            protected Shape(string name)
            {
                Name = name;
                s_created++;
            }

            public string Name { get; }

            public static int Created => s_created;

            public abstract double Area();

            public virtual string Describe() => Name + " with area " + Area();

            public override string ToString() => "Shape(" + Name + ")";
        }

        public class Circle : Shape
        {
            private readonly double _radius;

            public Circle(double radius) : base("circle")
            {
                _radius = radius;
            }

            public override double Area() => 3 * _radius * _radius;
        }

        public class Rectangle : Shape
        {
            protected readonly double Width;
            protected readonly double Height;

            public Rectangle(double width, double height) : this("rectangle", width, height)
            {
            }

            protected Rectangle(string name, double width, double height) : base(name)
            {
                Width = width;
                Height = height;
            }

            public override double Area() => Width * Height;
        }

        public sealed class Square : Rectangle
        {
            public Square(double side) : base("square", side, side)
            {
            }

            public override string Describe() => "A square! " + base.Describe();

            public override string ToString() => "Square " + Width;
        }

        public class Plain
        {
        }

        public class Counter
        {
            public static readonly int Start;
            public int Value;

            static Counter()
            {
                Console.WriteLine("Counter static constructor");
                Start = 100;
            }

            public Counter()
            {
                Value = Start;
            }

            public int Next() => ++Value;

            public new string ToString() => "hidden";
        }

        public class Point
        {
            public int X { get; set; }
            public int Y { get; set; }

            public override bool Equals(object obj) => obj is Point other && other.X == X && other.Y == Y;

            public override int GetHashCode() => X * 31 + Y;
        }

        public class Node
        {
            public int Value;
            public Node Next;
        }

        public static void Run()
        {
            Shape[] shapes = { new Circle(2), new Rectangle(2, 3), new Square(4) };
            for (int i = 0; i < shapes.Length; i++)
            {
                Console.WriteLine(shapes[i].Describe());
                Console.WriteLine(shapes[i].ToString());
            }
            Console.WriteLine(Shape.Created);

            object obj = shapes[2];
            Console.WriteLine(obj is Shape);
            Console.WriteLine(obj is Rectangle);
            Console.WriteLine(obj is Circle);
            Rectangle rect = obj as Rectangle;
            Console.WriteLine(rect != null);
            Circle circle = obj as Circle;
            Console.WriteLine(circle == null);
            Console.WriteLine(((Shape)obj).Name);

            Console.WriteLine("before counter");
            var counter = new Counter();
            Console.WriteLine(counter.Next());
            Console.WriteLine(counter.Next());
            Console.WriteLine(counter.ToString());
            Console.WriteLine(((object)counter).ToString());

            Console.WriteLine(new Plain().ToString());
            object plain = new Plain();
            Console.WriteLine(plain.Equals(plain));
            Console.WriteLine(plain.Equals(new Plain()));
            Console.WriteLine(ReferenceEquals(plain, plain));

            var p1 = new Point { X = 1, Y = 2 };
            var p2 = new Point { X = 1, Y = 2 };
            Console.WriteLine(p1 == p2);
            Console.WriteLine(p1.Equals(p2));
            Console.WriteLine(p1.GetHashCode() == p2.GetHashCode());

            Node list = null;
            for (int i = 1; i <= 5; i++)
                list = new Node { Value = i * i, Next = list };
            int total = 0;
            for (Node n = list; n != null; n = n.Next)
                total += n.Value;
            Console.WriteLine(total);

            // Allocate a lot, so the collector has to run.
            Node keep = null;
            for (int i = 0; i < 200000; i++)
            {
                var temporary = new Node { Value = i };
                if (i % 50000 == 0)
                    keep = new Node { Value = temporary.Value, Next = keep };
            }
            int kept = 0;
            for (Node n = keep; n != null; n = n.Next)
                kept += n.Value;
            Console.WriteLine(kept);
        }
    }
}
