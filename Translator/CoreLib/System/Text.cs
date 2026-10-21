namespace System.Text
{
    /// <summary>A growable buffer of characters for building strings.</summary>
    public sealed class StringBuilder
    {
        private char[] _chars;
        private int _length;

        public StringBuilder() { _chars = new char[16]; }

        public StringBuilder(int capacity) { _chars = new char[capacity < 1 ? 1 : capacity]; }

        public StringBuilder(string value) : this(value == null ? 16 : value.Length + 16) { Append(value); }

        public int Length
        {
            get => _length;
            set
            {
                Ensure(value);
                for (int i = _length; i < value; i++)
                    _chars[i] = '\0';
                _length = value;
            }
        }

        public char this[int index]
        {
            get => (uint)index < (uint)_length ? _chars[index] : throw new IndexOutOfRangeException("Index was outside the bounds of the array.");
            set
            {
                if ((uint)index >= (uint)_length)
                    throw new IndexOutOfRangeException("Index was outside the bounds of the array.");
                _chars[index] = value;
            }
        }

        private void Ensure(int capacity)
        {
            if (capacity <= _chars.Length)
                return;
            int size = _chars.Length * 2;
            if (size < capacity)
                size = capacity;
            var bigger = new char[size];
            for (int i = 0; i < _length; i++)
                bigger[i] = _chars[i];
            _chars = bigger;
        }

        public StringBuilder Append(char value)
        {
            Ensure(_length + 1);
            _chars[_length++] = value;
            return this;
        }

        public StringBuilder Append(char value, int repeatCount)
        {
            for (int i = 0; i < repeatCount; i++)
                Append(value);
            return this;
        }

        public StringBuilder Append(string value)
        {
            if (value == null)
                return this;
            return Append(value, 0, value.Length);
        }

        public StringBuilder Append(string value, int startIndex, int count)
        {
            if (value == null)
                return this;
            Ensure(_length + count);
            for (int i = 0; i < count; i++)
                _chars[_length + i] = value[startIndex + i];
            _length += count;
            return this;
        }

        public StringBuilder Append(object value) => value == null ? this : Append(value.ToString());
        public StringBuilder Append(bool value) => Append(value.ToString());
        public StringBuilder Append(int value) => Append(value.ToString());
        public StringBuilder Append(long value) => Append(value.ToString());
        public StringBuilder Append(float value) => Append(value.ToString());
        public StringBuilder Append(double value) => Append(value.ToString());

        public StringBuilder AppendLine() => Append('\n');

        public StringBuilder AppendLine(string value)
        {
            Append(value);
            return Append('\n');
        }

        public StringBuilder AppendFormat(string format, params object[] args) => Append(string.Format(format, args));

        public StringBuilder Insert(int index, string value)
        {
            if ((uint)index > (uint)_length)
                throw new ArgumentOutOfRangeException("Index was out of range. Must be non-negative and less than or equal to the size of the collection.");
            if (value == null)
                return this;
            Ensure(_length + value.Length);
            for (int i = _length - 1; i >= index; i--)
                _chars[i + value.Length] = _chars[i];
            for (int i = 0; i < value.Length; i++)
                _chars[index + i] = value[i];
            _length += value.Length;
            return this;
        }

        public StringBuilder Remove(int startIndex, int length)
        {
            for (int i = startIndex; i + length < _length; i++)
                _chars[i] = _chars[i + length];
            _length -= length;
            return this;
        }

        public StringBuilder Clear()
        {
            _length = 0;
            return this;
        }

        public override string ToString() => new string(_chars, 0, _length);
    }
}
