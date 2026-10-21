using System;

namespace Tests
{
    public static class Interfaces
    {
        public interface IShape
        {
            double Area();
            string Name { get; }
        }

        public interface INamed
        {
            string Name { get; }
        }

        public interface ICounter : INamed
        {
            int Next();
        }

        public interface IValue<T>
        {
            T Get();
            void Set(T value);
        }

        public interface IGreeter
        {
            string Greet(string who) => "Hello, " + who + " from " + Kind();
            string Kind();
        }

        public class Square : IShape
        {
            public double Side;
            public Square(double side) { Side = side; }
            public virtual double Area() => Side * Side;
            public string Name => "square";
        }

        public class Cube : Square
        {
            public Cube(double side) : base(side) { }
            public override double Area() => 6 * base.Area();
        }

        public class Circle : IShape
        {
            public double Radius;
            public Circle(double radius) { Radius = radius; }
            public double Area() => 3 * Radius * Radius;
            string IShape.Name => "circle (explicit)";
            public string Name => "circle";
        }

        public struct Counter : ICounter, IValue<int>
        {
            private int _value;
            public string Name => "counter at " + _value;
            public int Next() => ++_value;
            public int Get() => _value;
            public void Set(int value) { _value = value; }
        }

        public class Cell<T> : IValue<T>
        {
            private T _value;
            public T Get() => _value;
            public void Set(T value) { _value = value; }
        }

        public class Robot : IGreeter
        {
            public string Kind() => "a robot";
        }

        public class Person : IGreeter
        {
            public string Kind() => "a person";
            public string Greet(string who) => "Hi " + who;
        }

        public static T Roundtrip<T, V>(V holder, T value) where V : IValue<T>
        {
            holder.Set(value);
            return holder.Get();
        }

        public static int Advance<C>(ref C counter) where C : ICounter => counter.Next() + counter.Next();

        public static void Run()
        {
            IShape[] shapes = { new Square(2), new Cube(1), new Circle(1) };
            foreach (IShape shape in shapes)
                Console.WriteLine(shape.Name + " " + shape.Area());
            Console.WriteLine(new Circle(2).Name);

            object o = new Cube(3);
            Console.WriteLine((o is IShape) + " " + (o is INamed) + " " + (o is ICounter));
            IShape cast = (IShape)o;
            Console.WriteLine(cast.Area());

            var counter = new Counter();
            ICounter boxed = counter;
            boxed.Next();
            boxed.Next();
            Console.WriteLine(boxed.Name + " " + counter.Name);
            INamed named = boxed;
            Console.WriteLine(named.Name + " " + (named is IValue<int>) + " " + (named is IValue<string>));
            Console.WriteLine(Advance(ref counter) + " " + counter.Name);

            Console.WriteLine(Roundtrip<int, Counter>(new Counter(), 42));
            Console.WriteLine(Roundtrip<string, Cell<string>>(new Cell<string>(), "text"));
            IValue<double> cell = new Cell<double>();
            cell.Set(2.5);
            Console.WriteLine(cell.Get());

            IGreeter[] greeters = { new Robot(), new Person() };
            foreach (IGreeter greeter in greeters)
                Console.WriteLine(greeter.Greet("Ana"));

            try
            {
                object notShape = "text";
                IShape bad = (IShape)notShape;
                Console.WriteLine(bad.Area());
            }
            catch (InvalidCastException)
            {
                Console.WriteLine("not a shape");
            }
        }
    }
}
