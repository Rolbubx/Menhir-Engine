using System.Collections.Generic;

namespace System.Linq
{
    /// <summary>The common LINQ operators (lazy where .NET's are, stable OrderBy like .NET's).</summary>
    public static class Enumerable
    {
        public static IEnumerable<T> Where<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            foreach (T item in source)
            {
                if (predicate(item))
                    yield return item;
            }
        }

        public static IEnumerable<TResult> Select<T, TResult>(this IEnumerable<T> source, Func<T, TResult> selector)
        {
            foreach (T item in source)
                yield return selector(item);
        }

        public static IEnumerable<TResult> SelectMany<T, TResult>(this IEnumerable<T> source, Func<T, IEnumerable<TResult>> selector)
        {
            foreach (T item in source)
                foreach (TResult inner in selector(item))
                    yield return inner;
        }

        public static IEnumerable<T> Take<T>(this IEnumerable<T> source, int count)
        {
            if (count <= 0)
                yield break;
            foreach (T item in source)
            {
                yield return item;
                if (--count == 0)
                    yield break;
            }
        }

        public static IEnumerable<T> Skip<T>(this IEnumerable<T> source, int count)
        {
            foreach (T item in source)
            {
                if (count > 0)
                    count--;
                else
                    yield return item;
            }
        }

        public static IEnumerable<T> Concat<T>(this IEnumerable<T> first, IEnumerable<T> second)
        {
            foreach (T item in first)
                yield return item;
            foreach (T item in second)
                yield return item;
        }

        public static IEnumerable<T> Distinct<T>(this IEnumerable<T> source)
        {
            var seen = new HashSet<T>();
            foreach (T item in source)
            {
                if (seen.Add(item))
                    yield return item;
            }
        }

        public static IEnumerable<T> Reverse<T>(this IEnumerable<T> source)
        {
            T[] items = source.ToArray();
            for (int i = items.Length - 1; i >= 0; i--)
                yield return items[i];
        }

        public static IEnumerable<int> Range(int start, int count)
        {
            for (int i = 0; i < count; i++)
                yield return start + i;
        }

        public static IEnumerable<T> Repeat<T>(T element, int count)
        {
            for (int i = 0; i < count; i++)
                yield return element;
        }

        public static IEnumerable<T> Empty<T>() => new T[0].AsEnumerable();

        public static IEnumerable<T> AsEnumerable<T>(this IEnumerable<T> source) => source;

        public static IEnumerable<T> OrderBy<T, TKey>(this IEnumerable<T> source, Func<T, TKey> keySelector) =>
            Sorted(source, keySelector, descending: false);

        public static IEnumerable<T> OrderByDescending<T, TKey>(this IEnumerable<T> source, Func<T, TKey> keySelector) =>
            Sorted(source, keySelector, descending: true);

        /// <summary>A stable sort (merge sort), like .NET's OrderBy.</summary>
        private static IEnumerable<T> Sorted<T, TKey>(IEnumerable<T> source, Func<T, TKey> keySelector, bool descending)
        {
            T[] items = source.ToArray();
            var keys = new TKey[items.Length];
            for (int i = 0; i < items.Length; i++)
                keys[i] = keySelector(items[i]);
            var order = new int[items.Length];
            for (int i = 0; i < order.Length; i++)
                order[i] = i;
            Comparer<TKey> comparer = Comparer<TKey>.Default;
            var temp = new int[order.Length];
            for (int width = 1; width < order.Length; width *= 2)
            {
                for (int lo = 0; lo < order.Length; lo += 2 * width)
                {
                    int mid = Math.Min(lo + width, order.Length), hi = Math.Min(lo + 2 * width, order.Length);
                    int a = lo, b = mid, k = lo;
                    while (a < mid && b < hi)
                    {
                        int c = comparer.Compare(keys[order[a]], keys[order[b]]);
                        if (descending)
                            c = -c;
                        temp[k++] = c <= 0 ? order[a++] : order[b++];
                    }
                    while (a < mid) temp[k++] = order[a++];
                    while (b < hi) temp[k++] = order[b++];
                }
                int[] swap = order;
                order = temp;
                temp = swap;
            }
            var result = new T[items.Length];
            for (int i = 0; i < order.Length; i++)
                result[i] = items[order[i]];
            return result;
        }

        public static List<T> ToList<T>(this IEnumerable<T> source) => new List<T>(source);

        public static T[] ToArray<T>(this IEnumerable<T> source) => new List<T>(source).ToArray();

        public static Dictionary<TKey, T> ToDictionary<T, TKey>(this IEnumerable<T> source, Func<T, TKey> keySelector)
        {
            var result = new Dictionary<TKey, T>();
            foreach (T item in source)
                result.Add(keySelector(item), item);
            return result;
        }

        public static Dictionary<TKey, TValue> ToDictionary<T, TKey, TValue>(this IEnumerable<T> source, Func<T, TKey> keySelector,
            Func<T, TValue> valueSelector)
        {
            var result = new Dictionary<TKey, TValue>();
            foreach (T item in source)
                result.Add(keySelector(item), valueSelector(item));
            return result;
        }

        public static int Count<T>(this IEnumerable<T> source)
        {
            if (source is IReadOnlyCollection<T> collection)
                return collection.Count;
            int count = 0;
            foreach (T item in source)
                count++;
            return count;
        }

        public static int Count<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            int count = 0;
            foreach (T item in source)
            {
                if (predicate(item))
                    count++;
            }
            return count;
        }

        public static bool Any<T>(this IEnumerable<T> source)
        {
            foreach (T item in source)
                return true;
            return false;
        }

        public static bool Any<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            foreach (T item in source)
            {
                if (predicate(item))
                    return true;
            }
            return false;
        }

        public static bool All<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            foreach (T item in source)
            {
                if (!predicate(item))
                    return false;
            }
            return true;
        }

        public static bool Contains<T>(this IEnumerable<T> source, T value)
        {
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            foreach (T item in source)
            {
                if (comparer.Equals(item, value))
                    return true;
            }
            return false;
        }

        public static T First<T>(this IEnumerable<T> source)
        {
            foreach (T item in source)
                return item;
            throw new InvalidOperationException("Sequence contains no elements");
        }

        public static T First<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            foreach (T item in source)
            {
                if (predicate(item))
                    return item;
            }
            throw new InvalidOperationException("Sequence contains no matching element");
        }

        public static T FirstOrDefault<T>(this IEnumerable<T> source)
        {
            foreach (T item in source)
                return item;
            return default(T);
        }

        public static T FirstOrDefault<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            foreach (T item in source)
            {
                if (predicate(item))
                    return item;
            }
            return default(T);
        }

        public static T Last<T>(this IEnumerable<T> source)
        {
            bool found = false;
            T last = default(T);
            foreach (T item in source)
            {
                last = item;
                found = true;
            }
            if (!found)
                throw new InvalidOperationException("Sequence contains no elements");
            return last;
        }

        public static T LastOrDefault<T>(this IEnumerable<T> source)
        {
            T last = default(T);
            foreach (T item in source)
                last = item;
            return last;
        }

        public static T ElementAt<T>(this IEnumerable<T> source, int index)
        {
            foreach (T item in source)
            {
                if (index-- == 0)
                    return item;
            }
            throw new ArgumentOutOfRangeException("Index was out of range. Must be non-negative and less than the size of the collection.");
        }

        public static int Sum(this IEnumerable<int> source)
        {
            int sum = 0;
            foreach (int item in source)
                sum = checked(sum + item);
            return sum;
        }

        public static float Sum(this IEnumerable<float> source)
        {
            double sum = 0;
            foreach (float item in source)
                sum += item;
            return (float)sum;
        }

        public static double Sum(this IEnumerable<double> source)
        {
            double sum = 0;
            foreach (double item in source)
                sum += item;
            return sum;
        }

        public static int Sum<T>(this IEnumerable<T> source, Func<T, int> selector) => source.Select(selector).Sum();

        public static float Sum<T>(this IEnumerable<T> source, Func<T, float> selector) => source.Select(selector).Sum();

        public static double Average(this IEnumerable<int> source)
        {
            long sum = 0;
            int count = 0;
            foreach (int item in source)
            {
                sum += item;
                count++;
            }
            if (count == 0)
                throw new InvalidOperationException("Sequence contains no elements");
            return (double)sum / count;
        }

        public static T Min<T>(this IEnumerable<T> source) => Extreme(source, -1);

        public static T Max<T>(this IEnumerable<T> source) => Extreme(source, 1);

        public static TResult Min<T, TResult>(this IEnumerable<T> source, Func<T, TResult> selector) => Extreme(source.Select(selector), -1);

        public static TResult Max<T, TResult>(this IEnumerable<T> source, Func<T, TResult> selector) => Extreme(source.Select(selector), 1);

        private static T Extreme<T>(IEnumerable<T> source, int sign)
        {
            Comparer<T> comparer = Comparer<T>.Default;
            bool found = false;
            T best = default(T);
            foreach (T item in source)
            {
                if (!found || comparer.Compare(item, best) * sign > 0)
                {
                    best = item;
                    found = true;
                }
            }
            if (!found)
                throw new InvalidOperationException("Sequence contains no elements");
            return best;
        }
    }
}
