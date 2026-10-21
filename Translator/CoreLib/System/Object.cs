using System.Runtime.CompilerServices;

namespace System
{
    public class Object
    {
        public Object()
        {
        }

        public virtual string ToString() => GetTypeName();

        public virtual bool Equals(object obj) => this == obj;

        public virtual int GetHashCode() => RuntimeHelpers.GetHashCode(this);

        public static bool ReferenceEquals(object a, object b) => a == b;

        /// <summary>A shallow copy (same type, same field values).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)]
        protected extern object MemberwiseClone();

        /// <summary>Full name of the object's type.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal extern string GetTypeName();

        [MethodImpl(MethodImplOptions.InternalCall)]
        private extern nint GetTypeHandle();

        public Type GetType() => new Type(GetTypeHandle());
    }

    public abstract class ValueType
    {
    }

    public abstract class Enum : ValueType
    {
        /// <summary>The constant's name, the names of a [Flags] combination ("A, B"), or the number.</summary>
        public override string ToString() => EnumToString(this);

        public string ToString(string format) => format == "D" || format == "d" ? EnumToNumber(this) : EnumToString(this);

        public bool HasFlag(Enum flag) => (EnumBits(this) & EnumBits(flag)) == EnumBits(flag);

        public override bool Equals(object obj) => obj != null && obj.GetTypeName() == GetTypeName() && EnumBits(obj) == EnumBits(this);

        public override int GetHashCode() => (int)EnumBits(this);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string EnumToString(object value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string EnumToNumber(object value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern long EnumBits(object value);
    }

    public struct Void
    {
    }

    public abstract class Array
    {
        // Matches the runtime's array header: the length, then the elements.
        private int _length;

        public int Length => _length;
    }

    /// <summary>
    /// Base of delegate types, whose constructor and Invoke the translator generates. A delegate is
    /// a function and its target (none for static methods), or a list of delegates (Combine).
    /// </summary>
    public abstract class Delegate
    {
        internal object _target;
        internal nint _methodPtr;
        internal bool _isStatic;
        internal Delegate[] _invocationList;

        public object Target => _target;

        private Delegate[] List() => _invocationList ?? new[] { this };

        private static bool Same(Delegate a, Delegate b) => a._target == b._target && a._methodPtr == b._methodPtr;

        public static Delegate Combine(Delegate a, Delegate b)
        {
            if ((object)a == null)
                return b;
            if ((object)b == null)
                return a;
            Delegate[] first = a.List(), second = b.List();
            var all = new Delegate[first.Length + second.Length];
            for (int i = 0; i < first.Length; i++)
                all[i] = first[i];
            for (int i = 0; i < second.Length; i++)
                all[first.Length + i] = second[i];
            var result = (Delegate)a.MemberwiseClone();
            result._invocationList = all;
            return result;
        }

        /// <summary>Removes the last occurrence of <paramref name="value"/>'s list from <paramref name="source"/>'s.</summary>
        public static Delegate Remove(Delegate source, Delegate value)
        {
            if ((object)source == null || (object)value == null)
                return source;
            Delegate[] list = source.List(), remove = value.List();
            for (int start = list.Length - remove.Length; start >= 0; start--)
            {
                bool match = true;
                for (int i = 0; i < remove.Length && match; i++)
                    match = Same(list[start + i], remove[i]);
                if (!match)
                    continue;
                int count = list.Length - remove.Length;
                if (count == 0)
                    return null;
                var kept = new Delegate[count];
                for (int i = 0, k = 0; i < list.Length; i++)
                    if (i < start || i >= start + remove.Length)
                        kept[k++] = list[i];
                if (count == 1)
                    return kept[0];
                var result = (Delegate)source.MemberwiseClone();
                result._invocationList = kept;
                return result;
            }
            return source;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Delegate other))
                return false;
            Delegate[] a = List(), b = other.List();
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
                if (!Same(a[i], b[i]))
                    return false;
            return true;
        }

        public override int GetHashCode() => (int)_methodPtr;

        public static bool operator ==(Delegate a, Delegate b) => (object)a == null ? (object)b == null : a.Equals(b);

        public static bool operator !=(Delegate a, Delegate b) => !(a == b);
    }

    public abstract class MulticastDelegate : Delegate
    {
    }

    public delegate void Action();
    public delegate void Action<in T>(T obj);
    public delegate void Action<in T1, in T2>(T1 arg1, T2 arg2);
    public delegate void Action<in T1, in T2, in T3>(T1 arg1, T2 arg2, T3 arg3);
    public delegate void Action<in T1, in T2, in T3, in T4>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);
    public delegate TResult Func<out TResult>();
    public delegate TResult Func<in T, out TResult>(T arg);
    public delegate TResult Func<in T1, in T2, out TResult>(T1 arg1, T2 arg2);
    public delegate TResult Func<in T1, in T2, in T3, out TResult>(T1 arg1, T2 arg2, T3 arg3);
    public delegate TResult Func<in T1, in T2, in T3, in T4, out TResult>(T1 arg1, T2 arg2, T3 arg3, T4 arg4);
    public delegate bool Predicate<in T>(T obj);
    public delegate int Comparison<in T>(T x, T y);

    public struct RuntimeTypeHandle
    {
        internal nint _value;
    }

    public struct RuntimeMethodHandle
    {
    }

    public struct RuntimeFieldHandle
    {
    }

    /// <summary>A type, for typeof and GetType: its name (no reflection over members).</summary>
    public sealed class Type
    {
        private readonly nint _handle;

        internal Type(nint handle) { _handle = handle; }

        public static Type GetTypeFromHandle(RuntimeTypeHandle handle) => new Type(handle._value);

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern string GetName(nint handle);

        public string FullName => GetName(_handle);

        public string Name
        {
            get
            {
                string full = FullName;
                int bracket = full.IndexOf('[');
                string bare = bracket > 0 && full[bracket + 1] != ']' ? full.Substring(0, bracket) : full;
                int dot = bare.LastIndexOf('.'), plus = bare.LastIndexOf('+');
                int start = (dot > plus ? dot : plus) + 1;
                return full.Substring(start);
            }
        }

        public override string ToString() => FullName;

        public override bool Equals(object obj) => obj is Type other && other._handle == _handle;

        public override int GetHashCode() => (int)_handle;

        public static bool operator ==(Type a, Type b) => (object)a == null ? (object)b == null : (object)b != null && a._handle == b._handle;

        public static bool operator !=(Type a, Type b) => !(a == b);
    }

    /// <summary>A value type that can also be empty (int?).</summary>
    public struct Nullable<T> where T : struct
    {
        private readonly bool hasValue;
        internal T value;

        public Nullable(T value)
        {
            this.value = value;
            hasValue = true;
        }

        public bool HasValue => hasValue;

        public T Value => hasValue ? value : throw new InvalidOperationException("Nullable object must have a value.");

        public T GetValueOrDefault() => value;

        public T GetValueOrDefault(T defaultValue) => hasValue ? value : defaultValue;

        public override bool Equals(object other) => !hasValue ? other == null : other != null && value.Equals(other);

        public override int GetHashCode() => hasValue ? value.GetHashCode() : 0;

        public override string ToString() => hasValue ? value.ToString() : "";

        public static implicit operator T?(T value) => new T?(value);

        public static explicit operator T(T? value) => value.Value;
    }

    public struct ValueTuple<T1, T2>
    {
        public T1 Item1;
        public T2 Item2;

        public ValueTuple(T1 item1, T2 item2)
        {
            Item1 = item1;
            Item2 = item2;
        }

        public override string ToString() => "(" + Item1?.ToString() + ", " + Item2?.ToString() + ")";

        public override bool Equals(object obj) =>
            obj is ValueTuple<T1, T2> other &&
            System.Collections.Generic.EqualityComparer<T1>.Default.Equals(Item1, other.Item1) &&
            System.Collections.Generic.EqualityComparer<T2>.Default.Equals(Item2, other.Item2);

        public override int GetHashCode() => (Item1 == null ? 0 : Item1.GetHashCode()) * 31 + (Item2 == null ? 0 : Item2.GetHashCode());
    }

    public struct ValueTuple<T1, T2, T3>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;

        public ValueTuple(T1 item1, T2 item2, T3 item3)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
        }

        public override string ToString() => "(" + Item1?.ToString() + ", " + Item2?.ToString() + ", " + Item3?.ToString() + ")";
    }

    public struct ValueTuple<T1, T2, T3, T4>
    {
        public T1 Item1;
        public T2 Item2;
        public T3 Item3;
        public T4 Item4;

        public ValueTuple(T1 item1, T2 item2, T3 item3, T4 item4)
        {
            Item1 = item1;
            Item2 = item2;
            Item3 = item3;
            Item4 = item4;
        }

        public override string ToString() =>
            "(" + Item1?.ToString() + ", " + Item2?.ToString() + ", " + Item3?.ToString() + ", " + Item4?.ToString() + ")";
    }

    public interface IDisposable
    {
        void Dispose();
    }

    public class Attribute
    {
    }

    [Flags]
    public enum AttributeTargets
    {
        Assembly = 1,
        Module = 2,
        Class = 4,
        Struct = 8,
        Enum = 16,
        Constructor = 32,
        Method = 64,
        Property = 128,
        Field = 256,
        Event = 512,
        Interface = 1024,
        Parameter = 2048,
        Delegate = 4096,
        ReturnValue = 8192,
        GenericParameter = 16384,
        All = 32767,
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class AttributeUsageAttribute : Attribute
    {
        public AttributeUsageAttribute(AttributeTargets validOn)
        {
        }

        public bool AllowMultiple { get; set; }
        public bool Inherited { get; set; }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class ParamArrayAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Enum)]
    public sealed class FlagsAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.All)]
    public sealed class ObsoleteAttribute : Attribute
    {
        public ObsoleteAttribute()
        {
        }

        public ObsoleteAttribute(string message)
        {
        }
    }
}

namespace System.Threading
{
    /// <summary>Atomic operations; programs run on one thread, so plain reads and writes do.</summary>
    public static class Interlocked
    {
        public static T CompareExchange<T>(ref T location, T value, T comparand) where T : class
        {
            T original = location;
            if ((object)original == (object)comparand)
                location = value;
            return original;
        }

        public static int Increment(ref int location) => ++location;

        public static int Decrement(ref int location) => --location;

        public static int Exchange(ref int location, int value)
        {
            int original = location;
            location = value;
            return original;
        }
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.All)]
    public sealed class TupleElementNamesAttribute : Attribute
    {
        public TupleElementNamesAttribute(string[] transformNames)
        {
        }
    }

    [AttributeUsage(AttributeTargets.All)]
    public sealed class NullableAttribute : Attribute
    {
        public NullableAttribute(byte value) { }
        public NullableAttribute(byte[] value) { }
    }

    [AttributeUsage(AttributeTargets.All)]
    public sealed class NullableContextAttribute : Attribute
    {
        public NullableContextAttribute(byte value) { }
    }

    public enum MethodImplOptions
    {
        NoInlining = 8,
        InternalCall = 4096,
        AggressiveInlining = 256,
    }

    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method)]
    public sealed class MethodImplAttribute : Attribute
    {
        public MethodImplAttribute(MethodImplOptions options)
        {
        }
    }

    public static class RuntimeHelpers
    {
        /// <summary>A hash of the object's identity (objects never move).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int GetHashCode(object o);

        /// <summary>Copies a constant array's data (array initializers).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern void InitializeArray(Array array, RuntimeFieldHandle field);
    }

    [AttributeUsage(AttributeTargets.All)]
    public sealed class CompilerGeneratedAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Assembly)]
    public sealed class ExtensionAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.All)]
    public sealed class IsReadOnlyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class IsByRefLikeAttribute : Attribute
    {
    }

    public static class IsVolatile
    {
    }

    /// <summary>Language features the translator supports (the C# compiler checks these names).</summary>
    public static class RuntimeFeature
    {
        public const string DefaultImplementationsOfInterfaces = nameof(DefaultImplementationsOfInterfaces);
    }
}

namespace System.Runtime.InteropServices
{
    public enum LayoutKind
    {
        Sequential = 0,
        Explicit = 2,
        Auto = 3,
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public sealed class StructLayoutAttribute : Attribute
    {
        public StructLayoutAttribute(LayoutKind layoutKind)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class InAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class OutAttribute : Attribute
    {
    }
}

namespace System.Reflection
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
    public sealed class DefaultMemberAttribute : Attribute
    {
        public DefaultMemberAttribute(string memberName)
        {
        }
    }
}

namespace System.Collections
{
    // Needed by the compiler for foreach (which it compiles to an index loop on arrays).
    public interface IEnumerable
    {
        IEnumerator GetEnumerator();
    }

    public interface IEnumerator
    {
        object Current { get; }

        bool MoveNext();

        void Reset();
    }
}
