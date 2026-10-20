namespace System
{
    /// <summary>
    /// Base of all exceptions. Its first field must stay the message: the runtime reads it to
    /// report an unhandled exception.
    /// </summary>
    public class Exception
    {
        private string _message;

        public Exception()
        {
        }

        public Exception(string message)
        {
            _message = message;
        }

        public virtual string Message => _message ?? "Exception of type '" + GetTypeName() + "' was thrown.";

        public override string ToString() => GetTypeName() + ": " + Message;
    }

    public class SystemException : Exception
    {
        public SystemException() { }
        public SystemException(string message) : base(message) { }
    }

    public class ArgumentException : SystemException
    {
        public ArgumentException() { }
        public ArgumentException(string message) : base(message) { }
    }

    public class ArgumentNullException : ArgumentException
    {
        public ArgumentNullException() { }
        public ArgumentNullException(string message) : base(message) { }
    }

    public class ArgumentOutOfRangeException : ArgumentException
    {
        public ArgumentOutOfRangeException() { }
        public ArgumentOutOfRangeException(string message) : base(message) { }
    }

    public class InvalidOperationException : SystemException
    {
        public InvalidOperationException() { }
        public InvalidOperationException(string message) : base(message) { }
    }

    public class NotSupportedException : SystemException
    {
        public NotSupportedException() { }
        public NotSupportedException(string message) : base(message) { }
    }

    public class NotImplementedException : SystemException
    {
        public NotImplementedException() { }
        public NotImplementedException(string message) : base(message) { }
    }

    public class ArithmeticException : SystemException
    {
        public ArithmeticException() { }
        public ArithmeticException(string message) : base(message) { }
    }

    public class DivideByZeroException : ArithmeticException
    {
        public DivideByZeroException() { }
        public DivideByZeroException(string message) : base(message) { }
    }

    public class OverflowException : ArithmeticException
    {
        public OverflowException() { }
        public OverflowException(string message) : base(message) { }
    }

    public class NullReferenceException : SystemException
    {
        public NullReferenceException() { }
        public NullReferenceException(string message) : base(message) { }
    }

    public class IndexOutOfRangeException : SystemException
    {
        public IndexOutOfRangeException() { }
        public IndexOutOfRangeException(string message) : base(message) { }
    }

    public class InvalidCastException : SystemException
    {
        public InvalidCastException() { }
        public InvalidCastException(string message) : base(message) { }
    }
}
