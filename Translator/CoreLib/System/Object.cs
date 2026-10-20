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

        /// <summary>Full name of the object's type (no System.Type yet).</summary>
        [MethodImpl(MethodImplOptions.InternalCall)]
        internal extern string GetTypeName();
    }

    public abstract class ValueType
    {
    }

    public abstract class Enum : ValueType
    {
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

    public abstract class Delegate
    {
    }

    public abstract class MulticastDelegate : Delegate
    {
    }

    public struct RuntimeTypeHandle
    {
    }

    public struct RuntimeMethodHandle
    {
    }

    public struct RuntimeFieldHandle
    {
    }

    public abstract class Type
    {
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

namespace System.Runtime.CompilerServices
{
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
