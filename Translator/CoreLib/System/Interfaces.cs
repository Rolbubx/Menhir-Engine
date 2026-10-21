namespace System
{
    public interface IComparable
    {
        int CompareTo(object obj);
    }

    public interface IComparable<in T>
    {
        int CompareTo(T other);
    }

    public interface IEquatable<T>
    {
        bool Equals(T other);
    }

    public interface IFormatProvider
    {
    }

    /// <summary>Values that format with a format string (numbers: "F2", "N0", "X", "0.00"...).</summary>
    public interface IFormattable
    {
        string ToString(string format, IFormatProvider formatProvider);
    }

    public static class Environment
    {
        public static int CurrentManagedThreadId => 1;

        public static string NewLine => "\n";
    }
}

namespace System.Collections.Generic
{
    public interface IEnumerator<out T> : System.Collections.IEnumerator, IDisposable
    {
        new T Current { get; }
    }

    public interface IEnumerable<out T> : System.Collections.IEnumerable
    {
        new IEnumerator<T> GetEnumerator();
    }

    public interface IReadOnlyCollection<out T> : IEnumerable<T>
    {
        int Count { get; }
    }

    public interface IReadOnlyList<out T> : IReadOnlyCollection<T>
    {
        T this[int index] { get; }
    }

    public interface ICollection<T> : IEnumerable<T>
    {
        int Count { get; }
        void Add(T item);
        void Clear();
        bool Contains(T item);
        bool Remove(T item);
    }

    public interface IList<T> : ICollection<T>
    {
        T this[int index] { get; set; }
        int IndexOf(T item);
        void Insert(int index, T item);
        void RemoveAt(int index);
    }

    public interface IComparer<in T>
    {
        int Compare(T x, T y);
    }

    public interface IEqualityComparer<in T>
    {
        bool Equals(T x, T y);
        int GetHashCode(T obj);
    }

    /// <summary>Compares with IComparable&lt;T&gt; (or IComparable); null sorts first.</summary>
    public abstract class Comparer<T> : IComparer<T>
    {
        public static Comparer<T> Default { get; } = new DefaultComparer<T>();

        public abstract int Compare(T x, T y);

        public static Comparer<T> Create(Comparison<T> comparison) => new ComparisonComparer<T>(comparison);
    }

    internal sealed class DefaultComparer<T> : Comparer<T>
    {
        public override int Compare(T x, T y)
        {
            if (x == null)
                return y == null ? 0 : -1;
            if (y == null)
                return 1;
            if (x is IComparable<T> typed)
                return typed.CompareTo(y);
            if (x is IComparable untyped)
                return untyped.CompareTo(y);
            throw new InvalidOperationException("Failed to compare two elements in the array.");
        }
    }

    internal sealed class ComparisonComparer<T> : Comparer<T>
    {
        private readonly Comparison<T> _comparison;
        public ComparisonComparer(Comparison<T> comparison) { _comparison = comparison; }
        public override int Compare(T x, T y) => _comparison(x, y);
    }

    /// <summary>Equals and GetHashCode of the values themselves (null equals null).</summary>
    public abstract class EqualityComparer<T> : IEqualityComparer<T>
    {
        public static EqualityComparer<T> Default { get; } = new DefaultEqualityComparer<T>();

        public abstract bool Equals(T x, T y);
        public abstract int GetHashCode(T obj);
    }

    internal sealed class DefaultEqualityComparer<T> : EqualityComparer<T>
    {
        public override bool Equals(T x, T y)
        {
            if (x == null)
                return y == null;
            if (y == null)
                return false;
            if (x is IEquatable<T> typed)
                return typed.Equals(y);
            return x.Equals(y);
        }

        public override int GetHashCode(T obj) => obj == null ? 0 : obj.GetHashCode();
    }

    public struct KeyValuePair<TKey, TValue>
    {
        private readonly TKey _key;
        private readonly TValue _value;

        public KeyValuePair(TKey key, TValue value)
        {
            _key = key;
            _value = value;
        }

        public TKey Key => _key;
        public TValue Value => _value;

        public void Deconstruct(out TKey key, out TValue value)
        {
            key = _key;
            value = _value;
        }

        public override string ToString() =>
            "[" + (_key == null ? "" : _key.ToString()) + ", " + (_value == null ? "" : _value.ToString()) + "]";
    }
}
