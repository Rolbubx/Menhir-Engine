using System.Runtime.CompilerServices;

namespace System
{
    public static class Console
    {
        /// <summary>Writes UTF-16 text to stdout as UTF-8.</summary>
        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void WriteString(string value);

        public static void Write(string value)
        {
            if (value != null)
                WriteString(value);
        }

        public static void Write(object value) => Write(value?.ToString());
        public static void Write(bool value) => Write(value.ToString());
        public static void Write(char value) => Write(value.ToString());
        public static void Write(int value) => Write(value.ToString());
        public static void Write(uint value) => Write(value.ToString());
        public static void Write(long value) => Write(value.ToString());
        public static void Write(ulong value) => Write(value.ToString());
        public static void Write(float value) => Write(value.ToString());
        public static void Write(double value) => Write(value.ToString());

        public static void WriteLine() => WriteString("\n");

        public static void WriteLine(string value)
        {
            Write(value);
            WriteString("\n");
        }

        public static void WriteLine(object value) => WriteLine(value?.ToString());
        public static void WriteLine(bool value) => WriteLine(value.ToString());
        public static void WriteLine(char value) => WriteLine(value.ToString());
        public static void WriteLine(int value) => WriteLine(value.ToString());
        public static void WriteLine(uint value) => WriteLine(value.ToString());
        public static void WriteLine(long value) => WriteLine(value.ToString());
        public static void WriteLine(ulong value) => WriteLine(value.ToString());
        public static void WriteLine(float value) => WriteLine(value.ToString());
        public static void WriteLine(double value) => WriteLine(value.ToString());
        public static void WriteLine(string format, object arg0) => WriteLine(string.Format(format, arg0));
        public static void WriteLine(string format, object arg0, object arg1) => WriteLine(string.Format(format, arg0, arg1));
        public static void WriteLine(string format, object arg0, object arg1, object arg2) => WriteLine(string.Format(format, arg0, arg1, arg2));
        public static void WriteLine(string format, params object[] arg) => WriteLine(string.Format(format, arg));
        public static void Write(string format, object arg0) => Write(string.Format(format, arg0));
        public static void Write(string format, params object[] arg) => Write(string.Format(format, arg));
    }
}
