using System;

namespace MenhirEngine
{
    /// <summary>
    /// Float math helpers, like Unity's Mathf. Conventions of the engine: right-handed, Y up,
    /// forward is -Z, angles in degrees.
    /// </summary>
    public static class Mathf
    {
        public const float PI = 3.14159265f;
        public const float Deg2Rad = PI / 180f;
        public const float Rad2Deg = 180f / PI;
        public const float Epsilon = 1.401298E-45f;
        public const float Infinity = float.PositiveInfinity;

        public static float Sin(float f) => MathF.Sin(f);
        public static float Cos(float f) => MathF.Cos(f);
        public static float Tan(float f) => MathF.Tan(f);
        public static float Asin(float f) => MathF.Asin(f);
        public static float Acos(float f) => MathF.Acos(f);
        public static float Atan(float f) => MathF.Atan(f);
        public static float Atan2(float y, float x) => MathF.Atan2(y, x);
        public static float Sqrt(float f) => MathF.Sqrt(f);
        public static float Pow(float f, float p) => MathF.Pow(f, p);
        public static float Exp(float power) => MathF.Exp(power);
        public static float Log(float f) => MathF.Log(f);
        public static float Abs(float f) => MathF.Abs(f);
        public static int Abs(int value) => Math.Abs(value);
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Floor(float f) => MathF.Floor(f);
        public static float Ceil(float f) => MathF.Ceiling(f);
        public static float Round(float f) => MathF.Round(f);
        public static int FloorToInt(float f) => (int)MathF.Floor(f);
        public static int CeilToInt(float f) => (int)MathF.Ceiling(f);
        public static int RoundToInt(float f) => (int)MathF.Round(f);
        public static float Sign(float f) => f >= 0f ? 1f : -1f;

        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static float Clamp01(float value) => Clamp(value, 0f, 1f);

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float value) => a != b ? Clamp01((value - a) / (b - a)) : 0f;

        public static float MoveTowards(float current, float target, float maxDelta) =>
            Abs(target - current) <= maxDelta ? target : current + Sign(target - current) * maxDelta;

        /// <summary>t looped into [0, length).</summary>
        public static float Repeat(float t, float length) => Clamp(t - Floor(t / length) * length, 0f, length);

        public static float PingPong(float t, float length)
        {
            t = Repeat(t, length * 2f);
            return length - Abs(t - length);
        }

        /// <summary>The shortest difference between two angles, in degrees (-180..180).</summary>
        public static float DeltaAngle(float current, float target)
        {
            float delta = Repeat(target - current, 360f);
            return delta > 180f ? delta - 360f : delta;
        }

        public static float LerpAngle(float a, float b, float t) => a + DeltaAngle(a, b) * Clamp01(t);

        public static float SmoothStep(float from, float to, float t)
        {
            t = Clamp01(t);
            t = -2f * t * t * t + 3f * t * t;
            return to * t + from * (1f - t);
        }

        public static bool Approximately(float a, float b) =>
            Abs(b - a) < Max(1E-06f * Max(Abs(a), Abs(b)), Epsilon * 8f);
    }

    public struct Vector2
    {
        public float x, y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 one => new Vector2(1f, 1f);
        public static Vector2 up => new Vector2(0f, 1f);
        public static Vector2 right => new Vector2(1f, 0f);

        public float magnitude => Mathf.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;
        public Vector2 normalized
        {
            get
            {
                float m = magnitude;
                return m > 1E-05f ? new Vector2(x / m, y / m) : zero;
            }
        }

        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t)
        {
            t = Mathf.Clamp01(t);
            return new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        }

        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
        public static bool operator ==(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 9.99999944E-11f;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);

        public override bool Equals(object other) => other is Vector2 v && x == v.x && y == v.y;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2);
        public override string ToString() => "(" + x.ToString("F2") + ", " + y.ToString("F2") + ")";
    }

    public struct Vector3
    {
        public float x, y, z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public Vector3(float x, float y)
        {
            this.x = x;
            this.y = y;
            z = 0f;
        }

        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static Vector3 down => new Vector3(0f, -1f, 0f);
        public static Vector3 right => new Vector3(1f, 0f, 0f);
        public static Vector3 left => new Vector3(-1f, 0f, 0f);
        /// <summary>(0, 0, -1): the engine's forward axis (right-handed, unlike Unity's +Z).</summary>
        public static Vector3 forward => new Vector3(0f, 0f, -1f);
        public static Vector3 back => new Vector3(0f, 0f, 1f);

        public float this[int index]
        {
            get => index == 0 ? x : index == 1 ? y : index == 2 ? z : throw new IndexOutOfRangeException("Invalid Vector3 index!");
            set
            {
                if (index == 0) x = value;
                else if (index == 1) y = value;
                else if (index == 2) z = value;
                else throw new IndexOutOfRangeException("Invalid Vector3 index!");
            }
        }

        public float magnitude => Mathf.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized => Normalize(this);

        public static Vector3 Normalize(Vector3 value)
        {
            float m = value.magnitude;
            return m > 1E-05f ? value / m : zero;
        }

        public void Normalize() => this = Normalize(this);

        public void Set(float newX, float newY, float newZ)
        {
            x = newX;
            y = newY;
            z = newZ;
        }

        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;

        public static Vector3 Cross(Vector3 a, Vector3 b) =>
            new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);

        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;

        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => LerpUnclamped(a, b, Mathf.Clamp01(t));

        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) =>
            new Vector3(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);

        public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDistanceDelta)
        {
            Vector3 delta = target - current;
            float distance = delta.magnitude;
            if (distance <= maxDistanceDelta || distance == 0f)
                return target;
            return current + delta / distance * maxDistanceDelta;
        }

        public static Vector3 Scale(Vector3 a, Vector3 b) => new Vector3(a.x * b.x, a.y * b.y, a.z * b.z);

        public static Vector3 ClampMagnitude(Vector3 vector, float maxLength) =>
            vector.sqrMagnitude > maxLength * maxLength ? vector.normalized * maxLength : vector;

        public static float Angle(Vector3 from, Vector3 to)
        {
            float denominator = Mathf.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (denominator < 1E-15f)
                return 0f;
            return Mathf.Acos(Mathf.Clamp(Dot(from, to) / denominator, -1f, 1f)) * Mathf.Rad2Deg;
        }

        public static Vector3 Project(Vector3 vector, Vector3 onNormal)
        {
            float sqr = Dot(onNormal, onNormal);
            return sqr < Mathf.Epsilon ? zero : onNormal * (Dot(vector, onNormal) / sqr);
        }

        public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal) => vector - Project(vector, planeNormal);

        public static Vector3 Reflect(Vector3 inDirection, Vector3 inNormal) => inDirection - 2f * Dot(inNormal, inDirection) * inNormal;

        public static Vector3 Min(Vector3 a, Vector3 b) => new Vector3(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Min(a.z, b.z));
        public static Vector3 Max(Vector3 a, Vector3 b) => new Vector3(Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y), Mathf.Max(a.z, b.z));

        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 9.99999944E-11f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);

        public override bool Equals(object other) => other is Vector3 v && x == v.x && y == v.y && z == v.z;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
        public override string ToString() => "(" + x.ToString("F2") + ", " + y.ToString("F2") + ", " + z.ToString("F2") + ")";
        public string ToString(string format) => "(" + x.ToString(format) + ", " + y.ToString(format) + ", " + z.ToString(format) + ")";
    }

    /// <summary>A rotation. Euler angles are in degrees, applied Z, then X, then Y (like the editor's inspector).</summary>
    public struct Quaternion
    {
        public float x, y, z, w;

        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static Quaternion identity => new Quaternion(0f, 0f, 0f, 1f);

        /// <summary>Rotation from Euler angles in degrees (System.Numerics' CreateFromYawPitchRoll(y, x, z)).</summary>
        public static Quaternion Euler(float x, float y, float z)
        {
            float roll = z * Mathf.Deg2Rad, pitch = x * Mathf.Deg2Rad, yaw = y * Mathf.Deg2Rad;
            float sr = MathF.Sin(roll * 0.5f), cr = MathF.Cos(roll * 0.5f);
            float sp = MathF.Sin(pitch * 0.5f), cp = MathF.Cos(pitch * 0.5f);
            float sy = MathF.Sin(yaw * 0.5f), cy = MathF.Cos(yaw * 0.5f);
            return new Quaternion(cy * sp * cr + sy * cp * sr, sy * cp * cr - cy * sp * sr, cy * cp * sr - sy * sp * cr,
                cy * cp * cr + sy * sp * sr);
        }

        public static Quaternion Euler(Vector3 euler) => Euler(euler.x, euler.y, euler.z);

        public static Quaternion AngleAxis(float angle, Vector3 axis)
        {
            Vector3 n = axis.normalized;
            float half = angle * Mathf.Deg2Rad * 0.5f;
            float s = MathF.Sin(half);
            return new Quaternion(n.x * s, n.y * s, n.z * s, MathF.Cos(half));
        }

        /// <summary>The rotation that turns <see cref="Vector3.forward"/> (-Z) towards <paramref name="forward"/>.</summary>
        public static Quaternion LookRotation(Vector3 forward, Vector3 up)
        {
            Vector3 zAxis = -forward.normalized;        // the local +Z points backwards
            Vector3 xAxis = Vector3.Cross(up, zAxis).normalized;
            if (xAxis.sqrMagnitude < 1E-10f)
                xAxis = Vector3.Cross(Mathf.Abs(zAxis.y) < 0.99f ? Vector3.up : Vector3.right, zAxis).normalized;
            Vector3 yAxis = Vector3.Cross(zAxis, xAxis);
            return FromAxes(xAxis, yAxis, zAxis);
        }

        public static Quaternion LookRotation(Vector3 forward) => LookRotation(forward, Vector3.up);

        /// <summary>A rotation from the images of the X, Y and Z axes (System.Numerics' CreateFromRotationMatrix).</summary>
        internal static Quaternion FromAxes(Vector3 xAxis, Vector3 yAxis, Vector3 zAxis)
        {
            float m11 = xAxis.x, m12 = xAxis.y, m13 = xAxis.z;
            float m21 = yAxis.x, m22 = yAxis.y, m23 = yAxis.z;
            float m31 = zAxis.x, m32 = zAxis.y, m33 = zAxis.z;
            float trace = m11 + m22 + m33;
            if (trace > 0f)
            {
                float s = MathF.Sqrt(trace + 1f);
                float invS = 0.5f / s;
                return new Quaternion((m23 - m32) * invS, (m31 - m13) * invS, (m12 - m21) * invS, s * 0.5f);
            }
            if (m11 >= m22 && m11 >= m33)
            {
                float s = MathF.Sqrt(1f + m11 - m22 - m33);
                float invS = 0.5f / s;
                return new Quaternion(0.5f * s, (m12 + m21) * invS, (m13 + m31) * invS, (m23 - m32) * invS);
            }
            if (m22 > m33)
            {
                float s = MathF.Sqrt(1f + m22 - m11 - m33);
                float invS = 0.5f / s;
                return new Quaternion((m21 + m12) * invS, 0.5f * s, (m32 + m23) * invS, (m31 - m13) * invS);
            }
            {
                float s = MathF.Sqrt(1f + m33 - m11 - m22);
                float invS = 0.5f / s;
                return new Quaternion((m31 + m13) * invS, (m32 + m23) * invS, 0.5f * s, (m12 - m21) * invS);
            }
        }

        public static Quaternion Inverse(Quaternion q)
        {
            float lengthSquared = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            float inv = 1f / lengthSquared;
            return new Quaternion(-q.x * inv, -q.y * inv, -q.z * inv, q.w * inv);
        }

        public static Quaternion Normalize(Quaternion q)
        {
            float length = MathF.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
            return length < Mathf.Epsilon ? identity : new Quaternion(q.x / length, q.y / length, q.z / length, q.w / length);
        }

        public Quaternion normalized => Normalize(this);

        public static float Dot(Quaternion a, Quaternion b) => a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;

        public static float Angle(Quaternion a, Quaternion b)
        {
            float dot = Mathf.Min(Mathf.Abs(Dot(a, b)), 1f);
            return dot > 0.999999f ? 0f : Mathf.Acos(dot) * 2f * Mathf.Rad2Deg;
        }

        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => SlerpUnclamped(a, b, Mathf.Clamp01(t));

        public static Quaternion SlerpUnclamped(Quaternion a, Quaternion b, float t)
        {
            float cos = Dot(a, b);
            bool flip = cos < 0f;
            if (flip)
                cos = -cos;
            float s1, s2;
            if (cos > 0.999999f)
            {
                s1 = 1f - t;
                s2 = flip ? -t : t;
            }
            else
            {
                float omega = MathF.Acos(cos);
                float invSin = 1f / MathF.Sin(omega);
                s1 = MathF.Sin((1f - t) * omega) * invSin;
                s2 = flip ? -MathF.Sin(t * omega) * invSin : MathF.Sin(t * omega) * invSin;
            }
            return new Quaternion(s1 * a.x + s2 * b.x, s1 * a.y + s2 * b.y, s1 * a.z + s2 * b.z, s1 * a.w + s2 * b.w);
        }

        public static Quaternion Lerp(Quaternion a, Quaternion b, float t)
        {
            t = Mathf.Clamp01(t);
            float sign = Dot(a, b) >= 0f ? 1f : -1f;
            return Normalize(new Quaternion(a.x + (b.x * sign - a.x) * t, a.y + (b.y * sign - a.y) * t,
                a.z + (b.z * sign - a.z) * t, a.w + (b.w * sign - a.w) * t));
        }

        public static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegreesDelta)
        {
            float angle = Angle(from, to);
            return angle == 0f ? to : SlerpUnclamped(from, to, Mathf.Min(1f, maxDegreesDelta / angle));
        }

        /// <summary>Euler angles in degrees: pitch (x) in [-90, 90], yaw (y) and roll (z) in (-180, 180].</summary>
        public Vector3 eulerAngles
        {
            get
            {
                Matrix m = Matrix.Rotation(Normalize(this));
                float sinPitch = Mathf.Clamp(-m.m32, -1f, 1f);
                float pitch = MathF.Asin(sinPitch);
                float yaw, roll;
                if (MathF.Abs(sinPitch) > 0.9999f)
                {
                    yaw = 0f;
                    roll = MathF.Atan2(-m.m21, m.m11);
                }
                else
                {
                    yaw = MathF.Atan2(m.m31, m.m33);
                    roll = MathF.Atan2(m.m12, m.m22);
                }
                return new Vector3(pitch * Mathf.Rad2Deg, yaw * Mathf.Rad2Deg, roll * Mathf.Rad2Deg);
            }
            set => this = Euler(value);
        }

        /// <summary>The two rotations combined, like Unity: a * b rotates by b, then by a.</summary>
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);

        /// <summary>The point rotated.</summary>
        public static Vector3 operator *(Quaternion rotation, Vector3 point)
        {
            Matrix m = Matrix.Rotation(rotation);
            return m.MultiplyDirection(point);
        }

        public static bool operator ==(Quaternion a, Quaternion b) => Dot(a, b) > 0.999999f;
        public static bool operator !=(Quaternion a, Quaternion b) => !(a == b);

        public override bool Equals(object other) => other is Quaternion q && x == q.x && y == q.y && z == q.z && w == q.w;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2) ^ (w.GetHashCode() >> 1);
        public override string ToString() =>
            "(" + x.ToString("F5") + ", " + y.ToString("F5") + ", " + z.ToString("F5") + ", " + w.ToString("F5") + ")";
    }

    /// <summary>A 4×4 matrix with System.Numerics' conventions (row vectors, M = S * R * T), for transforms.</summary>
    internal struct Matrix
    {
        public float m11, m12, m13, m14, m21, m22, m23, m24, m31, m32, m33, m34, m41, m42, m43, m44;

        public static Matrix Identity => new Matrix { m11 = 1f, m22 = 1f, m33 = 1f, m44 = 1f };

        /// <summary>System.Numerics' CreateFromQuaternion.</summary>
        public static Matrix Rotation(Quaternion q)
        {
            float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
            float xy = q.x * q.y, wz = q.z * q.w, xz = q.z * q.x, wy = q.y * q.w, yz = q.y * q.z, wx = q.x * q.w;
            Matrix m = Identity;
            m.m11 = 1f - 2f * (yy + zz);
            m.m12 = 2f * (xy + wz);
            m.m13 = 2f * (xz - wy);
            m.m21 = 2f * (xy - wz);
            m.m22 = 1f - 2f * (zz + xx);
            m.m23 = 2f * (yz + wx);
            m.m31 = 2f * (xz + wy);
            m.m32 = 2f * (yz - wx);
            m.m33 = 1f - 2f * (yy + xx);
            return m;
        }

        public static Matrix TRS(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Matrix m = Rotation(rotation);
            m.m11 *= scale.x; m.m12 *= scale.x; m.m13 *= scale.x;
            m.m21 *= scale.y; m.m22 *= scale.y; m.m23 *= scale.y;
            m.m31 *= scale.z; m.m32 *= scale.z; m.m33 *= scale.z;
            m.m41 = position.x;
            m.m42 = position.y;
            m.m43 = position.z;
            return m;
        }

        public static Matrix operator *(Matrix a, Matrix b) => new Matrix
        {
            m11 = a.m11 * b.m11 + a.m12 * b.m21 + a.m13 * b.m31 + a.m14 * b.m41,
            m12 = a.m11 * b.m12 + a.m12 * b.m22 + a.m13 * b.m32 + a.m14 * b.m42,
            m13 = a.m11 * b.m13 + a.m12 * b.m23 + a.m13 * b.m33 + a.m14 * b.m43,
            m14 = a.m11 * b.m14 + a.m12 * b.m24 + a.m13 * b.m34 + a.m14 * b.m44,
            m21 = a.m21 * b.m11 + a.m22 * b.m21 + a.m23 * b.m31 + a.m24 * b.m41,
            m22 = a.m21 * b.m12 + a.m22 * b.m22 + a.m23 * b.m32 + a.m24 * b.m42,
            m23 = a.m21 * b.m13 + a.m22 * b.m23 + a.m23 * b.m33 + a.m24 * b.m43,
            m24 = a.m21 * b.m14 + a.m22 * b.m24 + a.m23 * b.m34 + a.m24 * b.m44,
            m31 = a.m31 * b.m11 + a.m32 * b.m21 + a.m33 * b.m31 + a.m34 * b.m41,
            m32 = a.m31 * b.m12 + a.m32 * b.m22 + a.m33 * b.m32 + a.m34 * b.m42,
            m33 = a.m31 * b.m13 + a.m32 * b.m23 + a.m33 * b.m33 + a.m34 * b.m43,
            m34 = a.m31 * b.m14 + a.m32 * b.m24 + a.m33 * b.m34 + a.m34 * b.m44,
            m41 = a.m41 * b.m11 + a.m42 * b.m21 + a.m43 * b.m31 + a.m44 * b.m41,
            m42 = a.m41 * b.m12 + a.m42 * b.m22 + a.m43 * b.m32 + a.m44 * b.m42,
            m43 = a.m41 * b.m13 + a.m42 * b.m23 + a.m43 * b.m33 + a.m44 * b.m43,
            m44 = a.m41 * b.m14 + a.m42 * b.m24 + a.m43 * b.m34 + a.m44 * b.m44,
        };

        public Vector3 MultiplyPoint(Vector3 p) => new Vector3(
            p.x * m11 + p.y * m21 + p.z * m31 + m41,
            p.x * m12 + p.y * m22 + p.z * m32 + m42,
            p.x * m13 + p.y * m23 + p.z * m33 + m43);

        public Vector3 MultiplyDirection(Vector3 d) => new Vector3(
            d.x * m11 + d.y * m21 + d.z * m31,
            d.x * m12 + d.y * m22 + d.z * m32,
            d.x * m13 + d.y * m23 + d.z * m33);
    }
}
