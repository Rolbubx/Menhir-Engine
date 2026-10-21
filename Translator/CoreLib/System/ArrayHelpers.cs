using System.Collections;
using System.Collections.Generic;

namespace System
{
    /// <summary>
    /// What T[] does as IList&lt;T&gt;, IReadOnlyList&lt;T&gt; and IEnumerable&lt;T&gt;: the translator
    /// points the interface maps of array types at these methods, the array being their first argument.
    /// </summary>
    internal static class SZArrayHelper<T>
    {
        public static IEnumerator<T> GetEnumerator(T[] array) => new ArrayEnumerator<T>(array);
        public static int get_Count(T[] array) => array.Length;
        public static T get_Item(T[] array, int index) => array[index];
        public static void set_Item(T[] array, int index, T value) => array[index] = value;

        public static bool Contains(T[] array, T item) => IndexOf(array, item) >= 0;

        public static int IndexOf(T[] array, T item)
        {
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < array.Length; i++)
            {
                if (comparer.Equals(array[i], item))
                    return i;
            }
            return -1;
        }

        public static void Add(T[] array, T item) => throw new NotSupportedException("Collection was of a fixed size.");
        public static void Insert(T[] array, int index, T item) => throw new NotSupportedException("Collection was of a fixed size.");
        public static void RemoveAt(T[] array, int index) => throw new NotSupportedException("Collection was of a fixed size.");
        public static bool Remove(T[] array, T item) => throw new NotSupportedException("Collection was of a fixed size.");
        public static void Clear(T[] array) => throw new NotSupportedException("Collection is read-only.");
    }

    internal sealed class ArrayEnumerator<T> : IEnumerator<T>
    {
        private readonly T[] _array;
        private int _index = -1;

        public ArrayEnumerator(T[] array) { _array = array; }

        public T Current => _array[_index];
        object IEnumerator.Current => _array[_index];
        public bool MoveNext() => ++_index < _array.Length;
        public void Reset() => _index = -1;
        public void Dispose() { }
    }
}
