/* Vectors and matrices with the conventions of System.Numerics (the editor's math), so the game
   computes exactly what the editor shows: row vectors, row-major storage (m[0] = M11, m[1] = M12,
   ..., m[12..14] = translation), M = S * R * T. Matrices go to OpenGL as they are, without
   transposing, and the shaders compute M * v. */
#ifndef MH_MATH_H
#define MH_MATH_H

#include <math.h>

#define MH_PI 3.14159265358979323846f

typedef struct { float x, y; } vec2;
typedef struct { float x, y, z; } vec3;
typedef struct { float x, y, z, w; } vec4;
typedef struct { float x, y, z, w; } quat;
typedef struct { float m[16]; } mat4;

static inline vec2 v2(float x, float y) { vec2 r = { x, y }; return r; }
static inline vec3 v3(float x, float y, float z) { vec3 r = { x, y, z }; return r; }
static inline vec4 v4(float x, float y, float z, float w) { vec4 r = { x, y, z, w }; return r; }
static inline vec4 v4_from3(vec3 v, float w) { return v4(v.x, v.y, v.z, w); }

static inline vec3 v3_add(vec3 a, vec3 b) { return v3(a.x + b.x, a.y + b.y, a.z + b.z); }
static inline vec3 v3_sub(vec3 a, vec3 b) { return v3(a.x - b.x, a.y - b.y, a.z - b.z); }
static inline vec3 v3_scale(vec3 a, float s) { return v3(a.x * s, a.y * s, a.z * s); }
static inline vec3 v3_neg(vec3 a) { return v3(-a.x, -a.y, -a.z); }
static inline float v3_dot(vec3 a, vec3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
static inline vec3 v3_cross(vec3 a, vec3 b)
{
    return v3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
}
static inline float v3_length_sq(vec3 a) { return v3_dot(a, a); }
static inline float v3_length(vec3 a) { return sqrtf(v3_dot(a, a)); }
static inline vec3 v3_normalize(vec3 a) { return v3_scale(a, 1.0f / v3_length(a)); }
static inline float v3_distance(vec3 a, vec3 b) { return v3_length(v3_sub(a, b)); }
static inline vec3 v3_min(vec3 a, vec3 b) { return v3(fminf(a.x, b.x), fminf(a.y, b.y), fminf(a.z, b.z)); }
static inline vec3 v3_max(vec3 a, vec3 b) { return v3(fmaxf(a.x, b.x), fmaxf(a.y, b.y), fmaxf(a.z, b.z)); }
static inline int v3_is_zero(vec3 a) { return a.x == 0.0f && a.y == 0.0f && a.z == 0.0f; }

static inline float clampf(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }
static inline int clampi(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }
static inline int maxi(int a, int b) { return a > b ? a : b; }
static inline int mini(int a, int b) { return a < b ? a : b; }

static inline mat4 m4_identity(void)
{
    mat4 r = { { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1 } };
    return r;
}

static inline mat4 m4_mul(mat4 a, mat4 b)
{
    mat4 r;
    for (int i = 0; i < 4; i++)
        for (int j = 0; j < 4; j++)
            r.m[i * 4 + j] = a.m[i * 4 + 0] * b.m[0 * 4 + j] + a.m[i * 4 + 1] * b.m[1 * 4 + j] +
                             a.m[i * 4 + 2] * b.m[2 * 4 + j] + a.m[i * 4 + 3] * b.m[3 * 4 + j];
    return r;
}

static inline mat4 m4_transpose(mat4 a)
{
    mat4 r;
    for (int i = 0; i < 4; i++)
        for (int j = 0; j < 4; j++)
            r.m[i * 4 + j] = a.m[j * 4 + i];
    return r;
}

static inline mat4 m4_scale(vec3 s)
{
    mat4 r = m4_identity();
    r.m[0] = s.x;
    r.m[5] = s.y;
    r.m[10] = s.z;
    return r;
}

static inline mat4 m4_translation(vec3 t)
{
    mat4 r = m4_identity();
    r.m[12] = t.x;
    r.m[13] = t.y;
    r.m[14] = t.z;
    return r;
}

/* Matrix4x4.CreateFromQuaternion. */
static inline mat4 m4_from_quat(quat q)
{
    float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
    float xy = q.x * q.y, wz = q.z * q.w, xz = q.z * q.x, wy = q.y * q.w, yz = q.y * q.z, wx = q.x * q.w;
    mat4 r = m4_identity();
    r.m[0] = 1.0f - 2.0f * (yy + zz);
    r.m[1] = 2.0f * (xy + wz);
    r.m[2] = 2.0f * (xz - wy);
    r.m[4] = 2.0f * (xy - wz);
    r.m[5] = 1.0f - 2.0f * (zz + xx);
    r.m[6] = 2.0f * (yz + wx);
    r.m[8] = 2.0f * (xz + wy);
    r.m[9] = 2.0f * (yz - wx);
    r.m[10] = 1.0f - 2.0f * (yy + xx);
    return r;
}

/* Matrix4x4.CreateLookAt (right-handed: the camera looks down -Z). */
static inline mat4 m4_look_at(vec3 eye, vec3 target, vec3 up)
{
    vec3 z = v3_normalize(v3_sub(eye, target));
    vec3 x = v3_normalize(v3_cross(up, z));
    vec3 y = v3_cross(z, x);
    mat4 r = { { x.x, y.x, z.x, 0, x.y, y.y, z.y, 0, x.z, y.z, z.z, 0,
                 -v3_dot(x, eye), -v3_dot(y, eye), -v3_dot(z, eye), 1 } };
    return r;
}

/* Vector3.Transform: the point times the matrix (with translation). */
static inline vec3 m4_point(mat4 a, vec3 p)
{
    return v3(p.x * a.m[0] + p.y * a.m[4] + p.z * a.m[8] + a.m[12],
              p.x * a.m[1] + p.y * a.m[5] + p.z * a.m[9] + a.m[13],
              p.x * a.m[2] + p.y * a.m[6] + p.z * a.m[10] + a.m[14]);
}

/* Vector3.TransformNormal: the direction times the matrix (no translation). */
static inline vec3 m4_direction(mat4 a, vec3 d)
{
    return v3(d.x * a.m[0] + d.y * a.m[4] + d.z * a.m[8],
              d.x * a.m[1] + d.y * a.m[5] + d.z * a.m[9],
              d.x * a.m[2] + d.y * a.m[6] + d.z * a.m[10]);
}

static inline vec3 m4_get_translation(mat4 a) { return v3(a.m[12], a.m[13], a.m[14]); }

/* Matrix4x4.Invert: returns 0 (and NaNs, like .NET) when the matrix cannot be inverted. */
static inline int m4_invert(mat4 a, mat4 *out)
{
    const float *m = a.m;
    float inv[16];
    inv[0] = m[5] * m[10] * m[15] - m[5] * m[11] * m[14] - m[9] * m[6] * m[15] + m[9] * m[7] * m[14] +
             m[13] * m[6] * m[11] - m[13] * m[7] * m[10];
    inv[4] = -m[4] * m[10] * m[15] + m[4] * m[11] * m[14] + m[8] * m[6] * m[15] - m[8] * m[7] * m[14] -
             m[12] * m[6] * m[11] + m[12] * m[7] * m[10];
    inv[8] = m[4] * m[9] * m[15] - m[4] * m[11] * m[13] - m[8] * m[5] * m[15] + m[8] * m[7] * m[13] +
             m[12] * m[5] * m[11] - m[12] * m[7] * m[9];
    inv[12] = -m[4] * m[9] * m[14] + m[4] * m[10] * m[13] + m[8] * m[5] * m[14] - m[8] * m[6] * m[13] -
              m[12] * m[5] * m[10] + m[12] * m[6] * m[9];
    inv[1] = -m[1] * m[10] * m[15] + m[1] * m[11] * m[14] + m[9] * m[2] * m[15] - m[9] * m[3] * m[14] -
             m[13] * m[2] * m[11] + m[13] * m[3] * m[10];
    inv[5] = m[0] * m[10] * m[15] - m[0] * m[11] * m[14] - m[8] * m[2] * m[15] + m[8] * m[3] * m[14] +
             m[12] * m[2] * m[11] - m[12] * m[3] * m[10];
    inv[9] = -m[0] * m[9] * m[15] + m[0] * m[11] * m[13] + m[8] * m[1] * m[15] - m[8] * m[3] * m[13] -
             m[12] * m[1] * m[11] + m[12] * m[3] * m[9];
    inv[13] = m[0] * m[9] * m[14] - m[0] * m[10] * m[13] - m[8] * m[1] * m[14] + m[8] * m[2] * m[13] +
              m[12] * m[1] * m[10] - m[12] * m[2] * m[9];
    inv[2] = m[1] * m[6] * m[15] - m[1] * m[7] * m[14] - m[5] * m[2] * m[15] + m[5] * m[3] * m[14] +
             m[13] * m[2] * m[7] - m[13] * m[3] * m[6];
    inv[6] = -m[0] * m[6] * m[15] + m[0] * m[7] * m[14] + m[4] * m[2] * m[15] - m[4] * m[3] * m[14] -
             m[12] * m[2] * m[7] + m[12] * m[3] * m[6];
    inv[10] = m[0] * m[5] * m[15] - m[0] * m[7] * m[13] - m[4] * m[1] * m[15] + m[4] * m[3] * m[13] +
              m[12] * m[1] * m[7] - m[12] * m[3] * m[5];
    inv[14] = -m[0] * m[5] * m[14] + m[0] * m[6] * m[13] + m[4] * m[1] * m[14] - m[4] * m[2] * m[13] -
              m[12] * m[1] * m[6] + m[12] * m[2] * m[5];
    inv[3] = -m[1] * m[6] * m[11] + m[1] * m[7] * m[10] + m[5] * m[2] * m[11] - m[5] * m[3] * m[10] -
             m[9] * m[2] * m[7] + m[9] * m[3] * m[6];
    inv[7] = m[0] * m[6] * m[11] - m[0] * m[7] * m[10] - m[4] * m[2] * m[11] + m[4] * m[3] * m[10] +
             m[8] * m[2] * m[7] - m[8] * m[3] * m[6];
    inv[11] = -m[0] * m[5] * m[11] + m[0] * m[7] * m[9] + m[4] * m[1] * m[11] - m[4] * m[3] * m[9] -
              m[8] * m[1] * m[7] + m[8] * m[3] * m[5];
    inv[15] = m[0] * m[5] * m[10] - m[0] * m[6] * m[9] - m[4] * m[1] * m[10] + m[4] * m[2] * m[9] +
              m[8] * m[1] * m[6] - m[8] * m[2] * m[5];

    float det = m[0] * inv[0] + m[1] * inv[4] + m[2] * inv[8] + m[3] * inv[12];
    if (fabsf(det) < 1.401298e-45f) { /* float.Epsilon in .NET */
        for (int i = 0; i < 16; i++)
            out->m[i] = NAN;
        return 0;
    }
    float s = 1.0f / det;
    for (int i = 0; i < 16; i++)
        out->m[i] = inv[i] * s;
    return 1;
}

/* Projections.Perspective: OpenGL's -1..1 clip depth. */
static inline mat4 m4_perspective(float fov_y, float aspect, float near, float far)
{
    float f = 1.0f / tanf(fov_y / 2.0f);
    mat4 r = { { f / aspect, 0, 0, 0, 0, f, 0, 0, 0, 0, (far + near) / (near - far), -1,
                 0, 0, 2.0f * far * near / (near - far), 0 } };
    return r;
}

/* Projections.Orthographic: a box from -hw..hw, -hh..hh, near..far in front (-z). */
static inline mat4 m4_orthographic(float half_width, float half_height, float near, float far)
{
    mat4 r = { { 1.0f / half_width, 0, 0, 0, 0, 1.0f / half_height, 0, 0, 0, 0, -2.0f / (far - near), 0,
                 0, 0, -(far + near) / (far - near), 1 } };
    return r;
}

/* The inverse-transpose, which keeps normals perpendicular under non-uniform scale. */
static inline mat4 m4_normal_matrix(mat4 model)
{
    mat4 inverse;
    return m4_invert(model, &inverse) ? m4_transpose(inverse) : model;
}

#endif
