/* The part of the scripting API that is plain C: compiled with the game's scripts, into the library
   the editor loads in Play mode and into the built game, so both compute the same thing. The rest
   goes through the engine's mh_api table (given to mh_bind). */
#include <stdarg.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>
#include "menhir.h"

const mh_api *mh_engine;

/* ---- Registry ----------------------------------------------------------------------------- */

static const mh_script_type **mh_types;
static int32_t mh_type_count, mh_type_capacity;

void mh_register_script(const mh_script_type *type)
{
    if (mh_type_count == mh_type_capacity) {
        int32_t capacity = mh_type_capacity ? mh_type_capacity * 2 : 16;
        const mh_script_type **types = realloc((void *)mh_types, sizeof(*types) * (size_t)capacity);
        if (!types)
            return;
        mh_types = types;
        mh_type_capacity = capacity;
    }
    /* Sorted by name, whatever order the files were linked in. */
    int32_t at = mh_type_count;
    while (at > 0 && strcmp(mh_types[at - 1]->name, type->name) > 0) {
        mh_types[at] = mh_types[at - 1];
        at--;
    }
    mh_types[at] = type;
    mh_type_count++;
}

MH_EXPORT int32_t mh_script_type_count(void) { return mh_type_count; }

MH_EXPORT const mh_script_type *mh_script_type_at(int32_t index)
{
    return index >= 0 && index < mh_type_count ? mh_types[index] : NULL;
}

MH_EXPORT void mh_abi_sizes(uint32_t *sizes)
{
    sizes[0] = (uint32_t)sizeof(mh_field);
    sizes[1] = (uint32_t)sizeof(mh_script_type);
    sizes[2] = (uint32_t)sizeof(mh_api);
}

MH_EXPORT void mh_bind(const mh_api *api)
{
    mh_engine = api;
    mh_random_seed((uint32_t)time(NULL));
}

/* ---- Math --------------------------------------------------------------------------------- */

mh_vec3 mh_quat_to_euler(mh_quat q)
{
    /* Like the editor's EulerAngles.FromQuaternion: pitch in [-90, 90], yaw and roll in (-180, 180]. */
    q = mh_quat_normalize(q);
    float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
    float xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z, wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;
    float m11 = 1 - 2 * (yy + zz), m12 = 2 * (xy + wz);
    float m21 = 2 * (xy - wz), m22 = 1 - 2 * (xx + zz);
    float m31 = 2 * (xz + wy), m32 = 2 * (yz - wx), m33 = 1 - 2 * (xx + yy);
    float sin_pitch = mh_clamp(-m32, -1.0f, 1.0f);
    float pitch = asinf(sin_pitch), yaw, roll;
    if (fabsf(sin_pitch) < 0.99999f) {
        yaw = atan2f(m31, m33);
        roll = atan2f(m12, m22);
    } else {
        yaw = 0;
        roll = atan2f(-m21, m11);
    }
    return mh_v3(pitch * MH_RAD2DEG, yaw * MH_RAD2DEG, roll * MH_RAD2DEG);
}

mh_quat mh_quat_look(mh_vec3 forward, mh_vec3 up)
{
    mh_vec3 z = mh_v3_scale(mh_v3_normalize(forward), -1.0f); /* the local +Z points backwards */
    mh_vec3 x = mh_v3_normalize(mh_v3_cross(up, z));
    if (mh_v3_dot(x, x) < 1e-10f)
        x = mh_v3_normalize(mh_v3_cross(fabsf(z.y) < 0.99f ? mh_v3(0, 1, 0) : mh_v3(1, 0, 0), z));
    mh_vec3 y = mh_v3_cross(z, x);
    /* System.Numerics' Quaternion.CreateFromRotationMatrix with the axes as rows. */
    float trace = x.x + y.y + z.z;
    mh_quat q;
    if (trace > 0) {
        float s = sqrtf(trace + 1), inv = 0.5f / s;
        q = (mh_quat){ (y.z - z.y) * inv, (z.x - x.z) * inv, (x.y - y.x) * inv, s * 0.5f };
    } else if (x.x >= y.y && x.x >= z.z) {
        float s = sqrtf(1 + x.x - y.y - z.z), inv = 0.5f / s;
        q = (mh_quat){ 0.5f * s, (x.y + y.x) * inv, (x.z + z.x) * inv, (y.z - z.y) * inv };
    } else if (y.y > z.z) {
        float s = sqrtf(1 + y.y - x.x - z.z), inv = 0.5f / s;
        q = (mh_quat){ (y.x + x.y) * inv, 0.5f * s, (z.y + y.z) * inv, (z.x - x.z) * inv };
    } else {
        float s = sqrtf(1 + z.z - x.x - y.y), inv = 0.5f / s;
        q = (mh_quat){ (z.x + x.z) * inv, (z.y + y.z) * inv, 0.5f * s, (x.y - y.x) * inv };
    }
    return q;
}

mh_quat mh_quat_slerp(mh_quat a, mh_quat b, float t)
{
    t = mh_clamp(t, 0.0f, 1.0f);
    float cosine = a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w;
    if (cosine < 0) {
        cosine = -cosine;
        b = (mh_quat){ -b.x, -b.y, -b.z, -b.w };
    }
    float wa = 1 - t, wb = t;
    if (cosine < 0.9999f) {
        float angle = acosf(cosine), s = sinf(angle);
        wa = sinf((1 - t) * angle) / s;
        wb = sinf(t * angle) / s;
    }
    return mh_quat_normalize((mh_quat){ wa * a.x + wb * b.x, wa * a.y + wb * b.y, wa * a.z + wb * b.z, wa * a.w + wb * b.w });
}

/* ---- Transforms --------------------------------------------------------------------------- */

mh_vec3 mh_get_local_position(mh_entity e) { float v[3]; mh_engine->get_transform(e, 0, v); return mh_v3(v[0], v[1], v[2]); }
mh_quat mh_get_local_rotation(mh_entity e) { float v[4]; mh_engine->get_transform(e, 1, v); return (mh_quat){ v[0], v[1], v[2], v[3] }; }
mh_vec3 mh_get_local_scale(mh_entity e) { float v[3]; mh_engine->get_transform(e, 2, v); return mh_v3(v[0], v[1], v[2]); }

void mh_set_local_position(mh_entity e, mh_vec3 p) { float v[3] = { p.x, p.y, p.z }; mh_engine->set_transform(e, 0, v); }
void mh_set_local_rotation(mh_entity e, mh_quat q) { float v[4] = { q.x, q.y, q.z, q.w }; mh_engine->set_transform(e, 1, v); }
void mh_set_local_scale(mh_entity e, mh_vec3 s) { float v[3] = { s.x, s.y, s.z }; mh_engine->set_transform(e, 2, v); }

/* The parent's frame in the world: points go local -> world as rotation * (scale * p) + position. */
static void mh_world_frame(mh_entity parent, mh_vec3 *position, mh_quat *rotation, mh_vec3 *scale)
{
    *position = mh_v3(0, 0, 0);
    *rotation = mh_quat_identity();
    *scale = mh_v3(1, 1, 1);
    for (mh_entity p = parent; p != MH_NONE; p = mh_get_parent(p)) {
        mh_quat r = mh_get_local_rotation(p);
        mh_vec3 s = mh_get_local_scale(p);
        *position = mh_v3_add(mh_quat_rotate(r, mh_v3_mul(s, *position)), mh_get_local_position(p));
        *rotation = mh_quat_mul(r, *rotation);
        *scale = mh_v3_mul(s, *scale);
    }
}

mh_vec3 mh_get_position(mh_entity e)
{
    mh_vec3 position, scale;
    mh_quat rotation;
    mh_world_frame(mh_get_parent(e), &position, &rotation, &scale);
    return mh_v3_add(mh_quat_rotate(rotation, mh_v3_mul(scale, mh_get_local_position(e))), position);
}

void mh_set_position(mh_entity e, mh_vec3 world)
{
    mh_vec3 position, scale;
    mh_quat rotation;
    mh_world_frame(mh_get_parent(e), &position, &rotation, &scale);
    mh_vec3 local = mh_quat_rotate(mh_quat_inverse(rotation), mh_v3_sub(world, position));
    mh_set_local_position(e, mh_v3(scale.x != 0 ? local.x / scale.x : 0, scale.y != 0 ? local.y / scale.y : 0,
                                   scale.z != 0 ? local.z / scale.z : 0));
}

mh_quat mh_get_rotation(mh_entity e)
{
    mh_quat rotation = mh_get_local_rotation(e);
    for (mh_entity p = mh_get_parent(e); p != MH_NONE; p = mh_get_parent(p))
        rotation = mh_quat_mul(mh_get_local_rotation(p), rotation);
    return rotation;
}

void mh_set_rotation(mh_entity e, mh_quat world)
{
    mh_entity parent = mh_get_parent(e);
    mh_quat local = parent == MH_NONE ? world : mh_quat_mul(mh_quat_inverse(mh_get_rotation(parent)), world);
    mh_set_local_rotation(e, local);
}

mh_vec3 mh_forward(mh_entity e) { return mh_v3_normalize(mh_quat_rotate(mh_get_rotation(e), mh_v3(0, 0, -1))); }
mh_vec3 mh_right(mh_entity e) { return mh_v3_normalize(mh_quat_rotate(mh_get_rotation(e), mh_v3(1, 0, 0))); }
mh_vec3 mh_up(mh_entity e) { return mh_v3_normalize(mh_quat_rotate(mh_get_rotation(e), mh_v3(0, 1, 0))); }

void mh_translate(mh_entity e, mh_vec3 translation, bool world)
{
    mh_vec3 move = world ? translation : mh_quat_rotate(mh_get_rotation(e), translation);
    mh_set_position(e, mh_v3_add(mh_get_position(e), move));
}

void mh_rotate(mh_entity e, mh_vec3 degrees, bool world)
{
    mh_quat delta = mh_quat_euler(degrees);
    if (world)
        mh_set_rotation(e, mh_quat_mul(delta, mh_get_rotation(e)));
    else
        mh_set_local_rotation(e, mh_quat_mul(mh_get_local_rotation(e), delta));
}

void mh_look_at(mh_entity e, mh_vec3 target)
{
    mh_vec3 direction = mh_v3_sub(target, mh_get_position(e));
    if (mh_v3_dot(direction, direction) > 1e-12f)
        mh_set_rotation(e, mh_quat_look(direction, mh_v3(0, 1, 0)));
}

mh_entity mh_get_parent(mh_entity e) { return mh_engine->get_parent(e); }
const char *mh_get_name(mh_entity e) { return mh_engine->get_name(e); }
mh_entity mh_find(const char *name) { return mh_engine->find(name); }
void *mh_get_script(mh_entity e, const char *type) { return mh_engine->get_script(e, type); }

/* ---- Input, time, scenes ------------------------------------------------------------------ */

float mh_axis(const char *name) { return mh_engine->get_axis(name, 0); }
float mh_axis_raw(const char *name) { return mh_engine->get_axis(name, 1); }
bool mh_button(const char *name) { return mh_engine->get_button(name, 0) != 0; }
bool mh_button_down(const char *name) { return mh_engine->get_button(name, 1) != 0; }
bool mh_button_up(const char *name) { return mh_engine->get_button(name, 2) != 0; }
bool mh_key(const char *name) { return mh_engine->get_key(name, 0) != 0; }
bool mh_key_down(const char *name) { return mh_engine->get_key(name, 1) != 0; }
bool mh_key_up(const char *name) { return mh_engine->get_key(name, 2) != 0; }

mh_vec2 mh_mouse_position(void)
{
    float v[2];
    mh_engine->get_mouse_position(v);
    return mh_v2(v[0], v[1]);
}

float mh_delta_time(void) { return mh_engine->get_time(0); }
float mh_time(void) { return mh_engine->get_time(1); }
int32_t mh_frame_count(void) { return (int32_t)mh_engine->get_time(2); }

bool mh_load_scene(const char *name, int32_t mode) { return name && mh_engine->load_scene(name, -1, mode) != 0; }
bool mh_load_scene_index(int32_t index, int32_t mode) { return mh_engine->load_scene(NULL, index, mode) != 0; }
void mh_quit(void) { mh_engine->quit(); }

/* ---- Log ---------------------------------------------------------------------------------- */

static void mh_log_v(int32_t level, const char *format, va_list args)
{
    char buffer[1024];
    va_list copy;
    va_copy(copy, args);
    int length = vsnprintf(buffer, sizeof buffer, format, copy);
    va_end(copy);
    if (length < (int)sizeof buffer) {
        mh_engine->log(level, buffer);
        return;
    }
    char *big = malloc((size_t)length + 1);
    if (!big)
        return;
    vsnprintf(big, (size_t)length + 1, format, args);
    mh_engine->log(level, big);
    free(big);
}

void mh_log(const char *format, ...) { va_list a; va_start(a, format); mh_log_v(0, format, a); va_end(a); }
void mh_log_warning(const char *format, ...) { va_list a; va_start(a, format); mh_log_v(1, format, a); va_end(a); }
void mh_log_error(const char *format, ...) { va_list a; va_start(a, format); mh_log_v(2, format, a); va_end(a); }

/* ---- Random: PCG32 ------------------------------------------------------------------------ */

static uint64_t mh_random_state = 0x853c49e6748fea9bULL;

void mh_random_seed(uint32_t seed)
{
    mh_random_state = 0;
    (void)mh_random_int(0, 1);
    mh_random_state += seed;
    (void)mh_random_int(0, 1);
}

static uint32_t mh_random_next(void)
{
    uint64_t old = mh_random_state;
    mh_random_state = old * 6364136223846793005ULL + 1442695040888963407ULL;
    uint32_t shifted = (uint32_t)(((old >> 18u) ^ old) >> 27u);
    uint32_t rotation = (uint32_t)(old >> 59u);
    return (shifted >> rotation) | (shifted << ((-rotation) & 31));
}

float mh_random_value(void) { return (float)(mh_random_next() >> 8) / 16777215.0f; }

float mh_random_range(float min, float max) { return min + mh_random_value() * (max - min); }

int32_t mh_random_int(int32_t min, int32_t max)
{
    if (max <= min)
        return min;
    uint32_t span = (uint32_t)((int64_t)max - min);
    return (int32_t)(min + (int64_t)(mh_random_next() % span));
}
