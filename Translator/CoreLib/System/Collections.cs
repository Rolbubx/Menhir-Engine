using System.Collections;

namespace System.Collections.Generic
{
    internal static class CollectionHelpers
    {
        internal static void ThrowVersion() => throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");

        /// <summary>
        /// Sorts items[0..count) with <paramref name="comparer"/>: insertion sort for short runs,
        /// quicksort (median of three) otherwise. Not stable, like .NET's.
        /// </summary>
        internal static void Sort<T>(T[] items, int start, int count, IComparer<T> comparer)
        {
            int lo = start, hi = start + count - 1;
            while (hi - lo > 16)
            {
                int mid = lo + (hi - lo) / 2;
                if (comparer.Compare(items[mid], items[lo]) < 0) Swap(items, mid, lo);
                if (comparer.Compare(items[hi], items[lo]) < 0) Swap(items, hi, lo);
                if (comparer.Compare(items[hi], items[mid]) < 0) Swap(items, hi, mid);
                T pivot = items[mid];
                int i = lo, j = hi;
                while (i <= j)
                {
                    while (comparer.Compare(items[i], pivot) < 0) i++;
                    while (comparer.Compare(items[j], pivot) > 0) j--;
                    if (i <= j)
                    {
                        Swap(items, i, j);
                        i++;
                        j--;
                    }
                }
                // Recurse into the smaller side, loop on the larger one.
                if (j - lo < hi - i)
                {
                    Sort(items, lo, j - lo + 1, comparer);
                    lo = i;
                }
                else
                {
                    Sort(items, i, hi - i + 1, comparer);
                    hi = j;
                }
            }
            for (int i = lo + 1; i <= hi; i++)
            {
                T item = items[i];
                int j = i - 1;
                while (j >= lo && comparer.Compare(items[j], item) > 0)
                {
                    items[j + 1] = items[j];
                    j--;
                }
                items[j + 1] = item;
            }
        }

        private static void Swap<T>(T[] items, int a, int b)
        {
            T t = items[a];
            items[a] = items[b];
            items[b] = t;
        }
    }

    public class List<T> : IList<T>, IReadOnlyList<T>
    {
        private T[] _items;
        private int _size;
        private int _version;

        public List() { _items = new T[0]; }

        public List(int capacity) { _items = new T[capacity < 0 ? 0 : capacity]; }

        public List(IEnumerable<T> collection) : this()
        {
            AddRange(collection);
        }

        public int Count => _size;

        public int Capacity => _items.Length;

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_size)
                    throw new ArgumentOutOfRangeException("Index was out of range. Must be non-negative and less than the size of the collection.");
                return _items[index];
            }
            set
            {
                if ((uint)index >= (uint)_size)
                    throw new ArgumentOutOfRangeException("Index was out of range. Must be non-negative and less than the size of the collection.");
                _items[index] = value;
                _version++;
            }
        }

        private void Grow(int min)
        {
            int capacity = _items.Length == 0 ? 4 : _items.Length * 2;
            if (capacity < min)
                capacity = min;
            var bigger = new T[capacity];
            for (int i = 0; i < _size; i++)
                bigger[i] = _items[i];
            _items = bigger;
        }

        public void Add(T item)
        {
            if (_size == _items.Length)
                Grow(_size + 1);
            _items[_size++] = item;
            _version++;
        }

        public void AddRange(IEnumerable<T> collection)
        {
            foreach (T item in collection)
                Add(item);
        }

        public void Insert(int index, T item)
        {
            if ((uint)index > (uint)_size)
                throw new ArgumentOutOfRangeException("Index must be within the bounds of the List.");
            if (_size == _items.Length)
                Grow(_size + 1);
            for (int i = _size; i > index; i--)
                _items[i] = _items[i - 1];
            _items[index] = item;
            _size++;
            _version++;
        }

        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)_size)
                throw new ArgumentOutOfRangeException("Index was out of range. Must be non-negative and less than the size of the collection.");
            _size--;
            for (int i = index; i < _size; i++)
                _items[i] = _items[i + 1];
            _items[_size] = default(T);
            _version++;
        }

        public void RemoveRange(int index, int count)
        {
            for (int i = 0; i < count; i++)
                RemoveAt(index);
        }

        public bool Remove(T item)
        {
            int index = IndexOf(item);
            if (index < 0)
                return false;
            RemoveAt(index);
            return true;
        }

        public int RemoveAll(Predicate<T> match)
        {
            int kept = 0;
            for (int i = 0; i < _size; i++)
            {
                if (!match(_items[i]))
                    _items[kept++] = _items[i];
            }
            int removed = _size - kept;
            for (int i = kept; i < _size; i++)
                _items[i] = default(T);
            _size = kept;
            _version++;
            return removed;
        }

        public void Clear()
        {
            for (int i = 0; i < _size; i++)
                _items[i] = default(T);
            _size = 0;
            _version++;
        }

        public int IndexOf(T item)
        {
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < _size; i++)
            {
                if (comparer.Equals(_items[i], item))
                    return i;
            }
            return -1;
        }

        public int LastIndexOf(T item)
        {
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            for (int i = _size - 1; i >= 0; i--)
            {
                if (comparer.Equals(_items[i], item))
                    return i;
            }
            return -1;
        }

        public bool Contains(T item) => IndexOf(item) >= 0;

        public T Find(Predicate<T> match)
        {
            for (int i = 0; i < _size; i++)
            {
                if (match(_items[i]))
                    return _items[i];
            }
            return default(T);
        }

        public int FindIndex(Predicate<T> match)
        {
            for (int i = 0; i < _size; i++)
            {
                if (match(_items[i]))
                    return i;
            }
            return -1;
        }

        public List<T> FindAll(Predicate<T> match)
        {
            var result = new List<T>();
            for (int i = 0; i < _size; i++)
            {
                if (match(_items[i]))
                    result.Add(_items[i]);
            }
            return result;
        }

        public bool Exists(Predicate<T> match) => FindIndex(match) >= 0;

        public bool TrueForAll(Predicate<T> match)
        {
            for (int i = 0; i < _size; i++)
            {
                if (!match(_items[i]))
                    return false;
            }
            return true;
        }

        public void ForEach(Action<T> action)
        {
            int version = _version;
            for (int i = 0; i < _size; i++)
            {
                if (version != _version)
                    CollectionHelpers.ThrowVersion();
                action(_items[i]);
            }
        }

        public List<TOutput> ConvertAll<TOutput>(Func<T, TOutput> converter)
        {
            var result = new List<TOutput>(_size);
            for (int i = 0; i < _size; i++)
                result.Add(converter(_items[i]));
            return result;
        }

        public List<T> GetRange(int index, int count)
        {
            var result = new List<T>(count);
            for (int i = 0; i < count; i++)
                result.Add(this[index + i]);
            return result;
        }

        public T[] ToArray()
        {
            var array = new T[_size];
            for (int i = 0; i < _size; i++)
                array[i] = _items[i];
            return array;
        }

        public void Reverse()
        {
            for (int i = 0, j = _size - 1; i < j; i++, j--)
            {
                T t = _items[i];
                _items[i] = _items[j];
                _items[j] = t;
            }
            _version++;
        }

        public void Sort() => Sort(Comparer<T>.Default);

        public void Sort(Comparison<T> comparison) => Sort(Comparer<T>.Create(comparison));

        public void Sort(IComparer<T> comparer)
        {
            CollectionHelpers.Sort(_items, 0, _size, comparer ?? Comparer<T>.Default);
            _version++;
        }

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);

        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        public struct Enumerator : IEnumerator<T>
        {
            private readonly List<T> _list;
            private readonly int _version;
            private int _index;
            private T _current;

            internal Enumerator(List<T> list)
            {
                _list = list;
                _version = list._version;
                _index = 0;
                _current = default(T);
            }

            public T Current => _current;

            object IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (_version != _list._version)
                    CollectionHelpers.ThrowVersion();
                if (_index < _list._size)
                {
                    _current = _list._items[_index++];
                    return true;
                }
                _current = default(T);
                return false;
            }

            public void Reset()
            {
                _index = 0;
                _current = default(T);
            }

            public void Dispose()
            {
            }
        }
    }

    /// <summary>
    /// A hash table with separate chaining through an entries array, like .NET's: enumeration
    /// follows insertion order as long as nothing is removed.
    /// </summary>
    public class Dictionary<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>, IReadOnlyCollection<KeyValuePair<TKey, TValue>>
    {
        private struct Entry
        {
            public int HashCode;   // -1: free
            public int Next;
            public TKey Key;
            public TValue Value;
        }

        private int[] _buckets;   // 1-based index into _entries, 0: empty
        private Entry[] _entries;
        private int _count;
        private int _freeList = -1;
        private int _freeCount;
        private int _version;
        private readonly IEqualityComparer<TKey> _comparer;

        public Dictionary() : this(0, null) { }

        public Dictionary(int capacity) : this(capacity, null) { }

        public Dictionary(IEqualityComparer<TKey> comparer) : this(0, comparer) { }

        public Dictionary(int capacity, IEqualityComparer<TKey> comparer)
        {
            _comparer = comparer ?? EqualityComparer<TKey>.Default;
            int size = capacity < 3 ? 3 : capacity;
            _buckets = new int[size];
            _entries = new Entry[size];
        }

        public int Count => _count - _freeCount;

        private int Hash(TKey key)
        {
            if (key == null)
                throw new ArgumentNullException("Value cannot be null. (Parameter 'key')");
            return _comparer.GetHashCode(key) & 0x7FFFFFFF;
        }

        private int FindEntry(TKey key)
        {
            int hash = Hash(key);
            for (int i = _buckets[hash % _buckets.Length] - 1; i >= 0; i = _entries[i].Next)
            {
                if (_entries[i].HashCode == hash && _comparer.Equals(_entries[i].Key, key))
                    return i;
            }
            return -1;
        }

        public TValue this[TKey key]
        {
            get
            {
                int i = FindEntry(key);
                if (i < 0)
                    throw new KeyNotFoundException("The given key '" + key.ToString() + "' was not present in the dictionary.");
                return _entries[i].Value;
            }
            set => Insert(key, value, add: false);
        }

        public void Add(TKey key, TValue value) => Insert(key, value, add: true);

        public bool TryAdd(TKey key, TValue value)
        {
            if (FindEntry(key) >= 0)
                return false;
            Insert(key, value, add: true);
            return true;
        }

        private void Insert(TKey key, TValue value, bool add)
        {
            int hash = Hash(key);
            int bucket = hash % _buckets.Length;
            for (int i = _buckets[bucket] - 1; i >= 0; i = _entries[i].Next)
            {
                if (_entries[i].HashCode == hash && _comparer.Equals(_entries[i].Key, key))
                {
                    if (add)
                        throw new ArgumentException("An item with the same key has already been added. Key: " + key.ToString());
                    _entries[i].Value = value;
                    _version++;
                    return;
                }
            }

            int index;
            if (_freeCount > 0)
            {
                index = _freeList;
                _freeList = _entries[index].Next;
                _freeCount--;
            }
            else
            {
                if (_count == _entries.Length)
                {
                    Resize();
                    bucket = hash % _buckets.Length;
                }
                index = _count++;
            }
            _entries[index].HashCode = hash;
            _entries[index].Next = _buckets[bucket] - 1;
            _entries[index].Key = key;
            _entries[index].Value = value;
            _buckets[bucket] = index + 1;
            _version++;
        }

        private void Resize()
        {
            int size = _entries.Length * 2 + 1;
            var entries = new Entry[size];
            for (int i = 0; i < _count; i++)
                entries[i] = _entries[i];
            var buckets = new int[size];
            for (int i = 0; i < _count; i++)
            {
                if (entries[i].HashCode < 0)
                    continue;
                int bucket = entries[i].HashCode % size;
                entries[i].Next = buckets[bucket] - 1;
                buckets[bucket] = i + 1;
            }
            _buckets = buckets;
            _entries = entries;
        }

        public bool Remove(TKey key)
        {
            int hash = Hash(key);
            int bucket = hash % _buckets.Length;
            int last = -1;
            for (int i = _buckets[bucket] - 1; i >= 0; last = i, i = _entries[i].Next)
            {
                if (_entries[i].HashCode != hash || !_comparer.Equals(_entries[i].Key, key))
                    continue;
                if (last < 0)
                    _buckets[bucket] = _entries[i].Next + 1;
                else
                    _entries[last].Next = _entries[i].Next;
                _entries[i].HashCode = -1;
                _entries[i].Next = _freeList;
                _entries[i].Key = default(TKey);
                _entries[i].Value = default(TValue);
                _freeList = i;
                _freeCount++;
                _version++;
                return true;
            }
            return false;
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            int i = FindEntry(key);
            if (i >= 0)
            {
                value = _entries[i].Value;
                return true;
            }
            value = default(TValue);
            return false;
        }

        public TValue GetValueOrDefault(TKey key, TValue defaultValue = default(TValue))
        {
            int i = FindEntry(key);
            return i >= 0 ? _entries[i].Value : defaultValue;
        }

        public bool ContainsKey(TKey key) => FindEntry(key) >= 0;

        public bool ContainsValue(TValue value)
        {
            EqualityComparer<TValue> comparer = EqualityComparer<TValue>.Default;
            for (int i = 0; i < _count; i++)
            {
                if (_entries[i].HashCode >= 0 && comparer.Equals(_entries[i].Value, value))
                    return true;
            }
            return false;
        }

        public void Clear()
        {
            for (int i = 0; i < _buckets.Length; i++)
                _buckets[i] = 0;
            for (int i = 0; i < _count; i++)
                _entries[i] = default(Entry);
            _count = 0;
            _freeList = -1;
            _freeCount = 0;
            _version++;
        }

        public KeyCollection Keys => new KeyCollection(this);
        public ValueCollection Values => new ValueCollection(this);

        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => new Enumerator(this);

        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
        {
            private readonly Dictionary<TKey, TValue> _dictionary;
            private readonly int _version;
            private int _index;
            private KeyValuePair<TKey, TValue> _current;

            internal Enumerator(Dictionary<TKey, TValue> dictionary)
            {
                _dictionary = dictionary;
                _version = dictionary._version;
                _index = 0;
                _current = default(KeyValuePair<TKey, TValue>);
            }

            public KeyValuePair<TKey, TValue> Current => _current;

            object IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (_version != _dictionary._version)
                    CollectionHelpers.ThrowVersion();
                while (_index < _dictionary._count)
                {
                    int i = _index++;
                    if (_dictionary._entries[i].HashCode >= 0)
                    {
                        _current = new KeyValuePair<TKey, TValue>(_dictionary._entries[i].Key, _dictionary._entries[i].Value);
                        return true;
                    }
                }
                _current = default(KeyValuePair<TKey, TValue>);
                return false;
            }

            public void Reset()
            {
                _index = 0;
            }

            public void Dispose()
            {
            }
        }

        public sealed class KeyCollection : IEnumerable<TKey>, IReadOnlyCollection<TKey>
        {
            private readonly Dictionary<TKey, TValue> _dictionary;
            internal KeyCollection(Dictionary<TKey, TValue> dictionary) { _dictionary = dictionary; }
            public int Count => _dictionary.Count;
            public Enumerator GetEnumerator() => new Enumerator(_dictionary);
            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => new Enumerator(_dictionary);
            IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_dictionary);

            public struct Enumerator : IEnumerator<TKey>
            {
                private Dictionary<TKey, TValue>.Enumerator _inner;
                internal Enumerator(Dictionary<TKey, TValue> dictionary) { _inner = dictionary.GetEnumerator(); }
                public TKey Current => _inner.Current.Key;
                object IEnumerator.Current => _inner.Current.Key;
                public bool MoveNext() => _inner.MoveNext();
                public void Reset() => _inner.Reset();
                public void Dispose() { }
            }
        }

        public sealed class ValueCollection : IEnumerable<TValue>, IReadOnlyCollection<TValue>
        {
            private readonly Dictionary<TKey, TValue> _dictionary;
            internal ValueCollection(Dictionary<TKey, TValue> dictionary) { _dictionary = dictionary; }
            public int Count => _dictionary.Count;
            public Enumerator GetEnumerator() => new Enumerator(_dictionary);
            IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => new Enumerator(_dictionary);
            IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_dictionary);

            public struct Enumerator : IEnumerator<TValue>
            {
                private Dictionary<TKey, TValue>.Enumerator _inner;
                internal Enumerator(Dictionary<TKey, TValue> dictionary) { _inner = dictionary.GetEnumerator(); }
                public TValue Current => _inner.Current.Value;
                object IEnumerator.Current => _inner.Current.Value;
                public bool MoveNext() => _inner.MoveNext();
                public void Reset() => _inner.Reset();
                public void Dispose() { }
            }
        }
    }

    /// <summary>A set: a dictionary without values.</summary>
    public class HashSet<T> : IEnumerable<T>, IReadOnlyCollection<T>
    {
        private readonly Dictionary<T, bool> _items;

        public HashSet() { _items = new Dictionary<T, bool>(); }

        public HashSet(IEnumerable<T> collection) : this()
        {
            foreach (T item in collection)
                Add(item);
        }

        public int Count => _items.Count;

        public bool Add(T item) => _items.TryAdd(item, true);

        public bool Remove(T item) => _items.Remove(item);

        public bool Contains(T item) => _items.ContainsKey(item);

        public void Clear() => _items.Clear();

        public void UnionWith(IEnumerable<T> other)
        {
            foreach (T item in other)
                Add(item);
        }

        public void ExceptWith(IEnumerable<T> other)
        {
            foreach (T item in other)
                Remove(item);
        }

        public Dictionary<T, bool>.KeyCollection.Enumerator GetEnumerator() => _items.Keys.GetEnumerator();

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => _items.Keys.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.Keys.GetEnumerator();
    }

    /// <summary>First in, first out, in a circular buffer.</summary>
    public class Queue<T> : IEnumerable<T>, IReadOnlyCollection<T>
    {
        private T[] _items = new T[4];
        private int _head;
        private int _count;

        public int Count => _count;

        public void Enqueue(T item)
        {
            if (_count == _items.Length)
            {
                var bigger = new T[_items.Length * 2];
                for (int i = 0; i < _count; i++)
                    bigger[i] = _items[(_head + i) % _items.Length];
                _items = bigger;
                _head = 0;
            }
            _items[(_head + _count) % _items.Length] = item;
            _count++;
        }

        public T Dequeue()
        {
            if (_count == 0)
                throw new InvalidOperationException("Queue empty.");
            T item = _items[_head];
            _items[_head] = default(T);
            _head = (_head + 1) % _items.Length;
            _count--;
            return item;
        }

        public T Peek() => _count == 0 ? throw new InvalidOperationException("Queue empty.") : _items[_head];

        public bool TryDequeue(out T item)
        {
            if (_count == 0)
            {
                item = default(T);
                return false;
            }
            item = Dequeue();
            return true;
        }

        public void Clear()
        {
            _items = new T[4];
            _head = 0;
            _count = 0;
        }

        public bool Contains(T item)
        {
            for (int i = 0; i < _count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(_items[(_head + i) % _items.Length], item))
                    return true;
            }
            return false;
        }

        private IEnumerable<T> Items()
        {
            for (int i = 0; i < _count; i++)
                yield return _items[(_head + i) % _items.Length];
        }

        public IEnumerator<T> GetEnumerator() => Items().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => Items().GetEnumerator();
    }

    /// <summary>Last in, first out; enumerates from the top.</summary>
    public class Stack<T> : IEnumerable<T>, IReadOnlyCollection<T>
    {
        private T[] _items = new T[4];
        private int _count;

        public int Count => _count;

        public void Push(T item)
        {
            if (_count == _items.Length)
            {
                var bigger = new T[_items.Length * 2];
                for (int i = 0; i < _count; i++)
                    bigger[i] = _items[i];
                _items = bigger;
            }
            _items[_count++] = item;
        }

        public T Pop()
        {
            if (_count == 0)
                throw new InvalidOperationException("Stack empty.");
            T item = _items[--_count];
            _items[_count] = default(T);
            return item;
        }

        public T Peek() => _count == 0 ? throw new InvalidOperationException("Stack empty.") : _items[_count - 1];

        public bool TryPop(out T item)
        {
            if (_count == 0)
            {
                item = default(T);
                return false;
            }
            item = Pop();
            return true;
        }

        public void Clear()
        {
            _items = new T[4];
            _count = 0;
        }

        public bool Contains(T item)
        {
            for (int i = 0; i < _count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(_items[i], item))
                    return true;
            }
            return false;
        }

        private IEnumerable<T> Items()
        {
            for (int i = _count - 1; i >= 0; i--)
                yield return _items[i];
        }

        public IEnumerator<T> GetEnumerator() => Items().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => Items().GetEnumerator();
    }
}
