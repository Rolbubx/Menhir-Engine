/* Menhir scripting API. A script is a struct with its public settings, the functions the engine
   calls on it, and MH_SCRIPT to declare them (see Docs/Scripting.md):

       #include <menhir.h>

       typedef struct {
           float speed;
           mh_vec3 axis;
       } Spinner;

       static const mh_field Spinner_fields[] = {
           MH_FLOAT(Spinner, speed, 45, MH_RANGE(0, 360), MH_TOOLTIP("Degrees per second")),
           MH_VEC3(Spinner, axis, 0, 1, 0),
       };

       static void Spinner_update(Spinner *self, mh_entity entity)
       {
           mh_rotate(entity, mh_v3_scale(self->axis, self->speed * mh_delta_time()), true);
       }

       MH_SCRIPT(Spinner, MH_FIELDS(Spinner_fields), MH_UPDATE(Spinner_update));

   The same code runs in the editor's Play mode (compiled into a library the editor loads) and in
   the built game (compiled with the player). Conventions: right-handed, Y up, forward is -Z,
   angles in degrees, Euler angles applied Z, then X, then Y (like Unity). */
#ifndef MENHIR_H
#define MENHIR_H

#include <math.h>
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include "menhir_abi.h"

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32) && defined(MH_BUILD_LIBRARY)
#define MH_EXPORT __declspec(dllexport)
#else
#define MH_EXPORT
#endif

/* ---- Declaring a script ------------------------------------------------------------------- */

/* The fields a scene can set; each takes the struct, the field, its default, then options. */
#define MH_INT(T, f, def, ...) { .name = #f, .kind = MH_FIELD_INT, .offset = (uint32_t)offsetof(T, f), .number = { (def) }, __VA_ARGS__ }
#define MH_FLOAT(T, f, def, ...) { .name = #f, .kind = MH_FIELD_FLOAT, .offset = (uint32_t)offsetof(T, f), .number = { (def) }, __VA_ARGS__ }
#define MH_BOOL(T, f, def, ...) { .name = #f, .kind = MH_FIELD_BOOL, .offset = (uint32_t)offsetof(T, f), .number = { (def) }, __VA_ARGS__ }
#define MH_VEC2(T, f, x, y, ...) { .name = #f, .kind = MH_FIELD_VEC2, .offset = (uint32_t)offsetof(T, f), .number = { (x), (y) }, __VA_ARGS__ }
#define MH_VEC3(T, f, x, y, z, ...) { .name = #f, .kind = MH_FIELD_VEC3, .offset = (uint32_t)offsetof(T, f), .number = { (x), (y), (z) }, __VA_ARGS__ }
#define MH_COLOR(T, f, r, g, b, a, ...) { .name = #f, .kind = MH_FIELD_COLOR, .offset = (uint32_t)offsetof(T, f), .number = { (r), (g), (b), (a) }, __VA_ARGS__ }
#define MH_STRING(T, f, def, ...) { .name = #f, .kind = MH_FIELD_STRING, .offset = (uint32_t)offsetof(T, f), .text = (def), __VA_ARGS__ }
/* An int32_t that is one of the options: MH_ENUM(Mover, mode, 0, MH_OPTIONS("Slow", "Fast")). */
#define MH_ENUM(T, f, def, ...) { .name = #f, .kind = MH_FIELD_ENUM, .offset = (uint32_t)offsetof(T, f), .number = { (def) }, __VA_ARGS__ }

#define MH_RANGE(lo, hi) .has_range = 1, .min = (float)(lo), .max = (float)(hi)
#define MH_TOOLTIP(text) .tooltip = (text)
#define MH_OPTIONS(...) .options = (const char *const[]){ __VA_ARGS__ }, \
    .option_count = (int32_t)(sizeof((const char *const[]){ __VA_ARGS__ }) / sizeof(const char *))

/* Inside MH_SCRIPT: the fields array, and the callbacks (each takes T *self and the entity). */
#define MH_FIELDS(array) .fields = (array), .field_count = (int32_t)(sizeof(array) / sizeof((array)[0]))
#define MH_AWAKE(fn) .awake = (mh_callback)(fn)
#define MH_START(fn) .start = (mh_callback)(fn)
#define MH_UPDATE(fn) .update = (mh_callback)(fn)
#define MH_LATE_UPDATE(fn) .late_update = (mh_callback)(fn)
#define MH_FIXED_UPDATE(fn) .fixed_update = (mh_callback)(fn)
#define MH_ON_DESTROY(fn) .on_destroy = (mh_callback)(fn)
#define MH_ON_GAME_CLOSE(fn) .on_game_close = (mh_callback)(fn)
/* These take T *self, the entity and the other entity. */
#define MH_ON_COLLISION_ENTER(fn) .on_collision_enter = (mh_contact_callback)(fn)
#define MH_ON_COLLISION_EXIT(fn) .on_collision_exit = (mh_contact_callback)(fn)
#define MH_ON_TRIGGER_ENTER(fn) .on_trigger_enter = (mh_contact_callback)(fn)
#define MH_ON_TRIGGER_EXIT(fn) .on_trigger_exit = (mh_contact_callback)(fn)

/* Declares the script T (a struct type); its name in the editor is T. */
#define MH_SCRIPT(T, ...)                                                                      \
    static const mh_script_type T##_mh_script = { .name = #T, .size = (uint32_t)sizeof(T), __VA_ARGS__ }; \
    static void T##_mh_register(void) __attribute__((constructor));                            \
    static void T##_mh_register(void) { mh_register_script(&T##_mh_script); }                  \
    typedef int T##_mh_script_declared

/* Called by MH_SCRIPT when the program or library loads. */
void mh_register_script(const mh_script_type *type);

/* For the engine: menhir_script.c is compiled with the scripts. */
MH_EXPORT void mh_bind(const mh_api *api);
/* Each frame after the scripts' Update (timers, coroutines, tweens, audio), each fixed step after
   their FixedUpdate (physics), and once when the game ends. */
MH_EXPORT void mh_runtime_update(void);
MH_EXPORT void mh_runtime_fixed_step(void);
MH_EXPORT void mh_runtime_shutdown(void);
MH_EXPORT int32_t mh_script_type_count(void);
MH_EXPORT const mh_script_type *mh_script_type_at(int32_t index);
/* sizeof mh_field, mh_script_type and mh_api: the engine checks them against its copies. */
MH_EXPORT void mh_abi_sizes(uint32_t *sizes);

extern const mh_api *mh_engine;

/* ---- Math --------------------------------------------------------------------------------- */

#define MH_PI 3.14159265f
#define MH_DEG2RAD (MH_PI / 180.0f)
#define MH_RAD2DEG (180.0f / MH_PI)

typedef struct { float x, y; } mh_vec2;
typedef struct { float x, y, z; } mh_vec3;
typedef struct { float r, g, b, a; } mh_color;
typedef struct { float x, y, z, w; } mh_quat;

static inline float mh_clamp(float v, float lo, float hi) { return v < lo ? lo : v > hi ? hi : v; }
static inline float mh_lerp(float a, float b, float t) { return a + (b - a) * mh_clamp(t, 0.0f, 1.0f); }
static inline float mh_move_towards(float current, float target, float max_delta)
{
    return fabsf(target - current) <= max_delta ? target : current + (target > current ? max_delta : -max_delta);
}

static inline mh_vec2 mh_v2(float x, float y) { mh_vec2 v = { x, y }; return v; }
static inline mh_vec2 mh_v2_add(mh_vec2 a, mh_vec2 b) { return mh_v2(a.x + b.x, a.y + b.y); }
static inline mh_vec2 mh_v2_sub(mh_vec2 a, mh_vec2 b) { return mh_v2(a.x - b.x, a.y - b.y); }
static inline mh_vec2 mh_v2_scale(mh_vec2 a, float s) { return mh_v2(a.x * s, a.y * s); }
static inline float mh_v2_length(mh_vec2 a) { return sqrtf(a.x * a.x + a.y * a.y); }

static inline mh_vec3 mh_v3(float x, float y, float z) { mh_vec3 v = { x, y, z }; return v; }
static inline mh_vec3 mh_v3_add(mh_vec3 a, mh_vec3 b) { return mh_v3(a.x + b.x, a.y + b.y, a.z + b.z); }
static inline mh_vec3 mh_v3_sub(mh_vec3 a, mh_vec3 b) { return mh_v3(a.x - b.x, a.y - b.y, a.z - b.z); }
static inline mh_vec3 mh_v3_scale(mh_vec3 a, float s) { return mh_v3(a.x * s, a.y * s, a.z * s); }
static inline mh_vec3 mh_v3_mul(mh_vec3 a, mh_vec3 b) { return mh_v3(a.x * b.x, a.y * b.y, a.z * b.z); }
static inline float mh_v3_dot(mh_vec3 a, mh_vec3 b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
static inline mh_vec3 mh_v3_cross(mh_vec3 a, mh_vec3 b)
{
    return mh_v3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
}
static inline float mh_v3_length(mh_vec3 a) { return sqrtf(mh_v3_dot(a, a)); }
static inline float mh_v3_distance(mh_vec3 a, mh_vec3 b) { return mh_v3_length(mh_v3_sub(a, b)); }
/* The zero vector stays zero. */
static inline mh_vec3 mh_v3_normalize(mh_vec3 a)
{
    float length = mh_v3_length(a);
    return length > 1e-6f ? mh_v3_scale(a, 1.0f / length) : mh_v3(0, 0, 0);
}
static inline mh_vec3 mh_v3_lerp(mh_vec3 a, mh_vec3 b, float t)
{
    return mh_v3(mh_lerp(a.x, b.x, t), mh_lerp(a.y, b.y, t), mh_lerp(a.z, b.z, t));
}

static inline mh_quat mh_quat_identity(void) { mh_quat q = { 0, 0, 0, 1 }; return q; }
/* a then b applied after it: rotating by the result is rotating by b, then by a. */
static inline mh_quat mh_quat_mul(mh_quat a, mh_quat b)
{
    mh_quat q = { a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y, a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
                  a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w, a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z };
    return q;
}
static inline mh_quat mh_quat_inverse(mh_quat q)
{
    float n = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
    float s = n > 0.0f ? 1.0f / n : 0.0f;
    mh_quat r = { -q.x * s, -q.y * s, -q.z * s, q.w * s };
    return r;
}
static inline mh_quat mh_quat_normalize(mh_quat q)
{
    float n = sqrtf(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
    if (n < 1e-12f)
        return mh_quat_identity();
    mh_quat r = { q.x / n, q.y / n, q.z / n, q.w / n };
    return r;
}
static inline mh_vec3 mh_quat_rotate(mh_quat q, mh_vec3 v)
{
    mh_vec3 u = mh_v3(q.x, q.y, q.z);
    mh_vec3 t = mh_v3_scale(mh_v3_cross(u, v), 2.0f);
    return mh_v3_add(mh_v3_add(v, mh_v3_scale(t, q.w)), mh_v3_cross(u, t));
}
/* Euler angles in degrees (applied Z, then X, then Y), like the inspector's. */
static inline mh_quat mh_quat_euler(mh_vec3 degrees)
{
    float sr = sinf(degrees.z * MH_DEG2RAD * 0.5f), cr = cosf(degrees.z * MH_DEG2RAD * 0.5f);
    float sp = sinf(degrees.x * MH_DEG2RAD * 0.5f), cp = cosf(degrees.x * MH_DEG2RAD * 0.5f);
    float sy = sinf(degrees.y * MH_DEG2RAD * 0.5f), cy = cosf(degrees.y * MH_DEG2RAD * 0.5f);
    mh_quat q = { cy * sp * cr + sy * cp * sr, sy * cp * cr - cy * sp * sr, cy * cp * sr - sy * sp * cr,
                  cy * cp * cr + sy * sp * sr };
    return q;
}
static inline mh_quat mh_quat_axis_angle(mh_vec3 axis, float degrees)
{
    mh_vec3 n = mh_v3_normalize(axis);
    float s = sinf(degrees * MH_DEG2RAD * 0.5f);
    mh_quat q = { n.x * s, n.y * s, n.z * s, cosf(degrees * MH_DEG2RAD * 0.5f) };
    return q;
}
mh_vec3 mh_quat_to_euler(mh_quat q);
/* The rotation whose forward (-Z) points along forward, with up as close to up as it can. */
mh_quat mh_quat_look(mh_vec3 forward, mh_vec3 up);
mh_quat mh_quat_slerp(mh_quat a, mh_quat b, float t);

/* ---- Entities and their transform ------------------------------------------------------------ */

mh_vec3 mh_get_local_position(mh_entity entity);
void mh_set_local_position(mh_entity entity, mh_vec3 position);
mh_quat mh_get_local_rotation(mh_entity entity);
void mh_set_local_rotation(mh_entity entity, mh_quat rotation);
mh_vec3 mh_get_local_scale(mh_entity entity);
void mh_set_local_scale(mh_entity entity, mh_vec3 scale);

/* In the world (through the parents). */
mh_vec3 mh_get_position(mh_entity entity);
void mh_set_position(mh_entity entity, mh_vec3 position);
mh_quat mh_get_rotation(mh_entity entity);
void mh_set_rotation(mh_entity entity, mh_quat rotation);

mh_vec3 mh_forward(mh_entity entity); /* -Z of the entity, in the world */
mh_vec3 mh_right(mh_entity entity);
mh_vec3 mh_up(mh_entity entity);

/* Moves along the entity's own axes, or the world's. */
void mh_translate(mh_entity entity, mh_vec3 translation, bool world);
/* Turns by Euler angles in degrees, around the entity's own axes, or the world's. */
void mh_rotate(mh_entity entity, mh_vec3 degrees, bool world);
/* Turns the entity's forward (-Z) towards a point of the world. */
void mh_look_at(mh_entity entity, mh_vec3 target);

mh_entity mh_get_parent(mh_entity entity);
const char *mh_get_name(mh_entity entity);
/* The first entity of the loaded scenes with this name, or MH_NONE. */
mh_entity mh_find(const char *name);
/* The entity's first script of a type (by its name), or NULL: Mover *m = mh_get_script(e, "Mover"). */
void *mh_get_script(mh_entity entity, const char *type);

/* ---- Input, time, scenes, log ------------------------------------------------------------------ */

/* Axes and buttons of the Input Manager (Project Settings > Input), by name. */
float mh_axis(const char *name);
float mh_axis_raw(const char *name);
bool mh_button(const char *name);
bool mh_button_down(const char *name);
bool mh_button_up(const char *name);
/* Keys and buttons by name, as in the Input Manager: "space", "a", "left shift", "mouse 0", "joystick button 0"... */
bool mh_key(const char *name);
bool mh_key_down(const char *name);
bool mh_key_up(const char *name);
mh_vec2 mh_mouse_position(void);

float mh_delta_time(void);
float mh_time(void);
int32_t mh_frame_count(void);

enum { MH_LOAD_SINGLE, MH_LOAD_ADDITIVE };
/* At the end of the frame. False when the build has no such scene. */
bool mh_load_scene(const char *name, int32_t mode);
bool mh_load_scene_index(int32_t index, int32_t mode);
void mh_quit(void);

/* printf-style: the Console in the editor, stdout (stderr for warnings and errors) in the game. */
void mh_log(const char *format, ...) __attribute__((format(printf, 1, 2)));
void mh_log_warning(const char *format, ...) __attribute__((format(printf, 1, 2)));
void mh_log_error(const char *format, ...) __attribute__((format(printf, 1, 2)));

/* Pseudo-random numbers; seeded from the clock when the game starts, or with mh_random_seed. */
void mh_random_seed(uint32_t seed);
float mh_random_value(void);                 /* 0 to 1 */
float mh_random_range(float min, float max); /* min to max */
int32_t mh_random_int(int32_t min, int32_t max); /* min included, max excluded */

/* ---- Objects: creating, destroying, activating, the hierarchy -------------------------------- */

bool mh_is_alive(mh_entity entity);   /* not destroyed, and its scene still loaded */
/* A copy of the entity with its children and components (MH_NONE parent: a root of its scene). */
mh_entity mh_instantiate(mh_entity entity, mh_entity parent);
/* A copy at a position of the world. */
mh_entity mh_spawn(mh_entity entity, mh_vec3 position);
/* At the end of the frame, with its children. */
void mh_destroy(mh_entity entity);
void mh_set_active(mh_entity entity, bool active);
bool mh_is_active(mh_entity entity);      /* it and its parents are active */
bool mh_is_active_self(mh_entity entity); /* its own setting */
int32_t mh_child_count(mh_entity entity);
mh_entity mh_get_child(mh_entity entity, int32_t index);
/* The first child with that name (direct children), or MH_NONE. */
mh_entity mh_find_child(mh_entity entity, const char *name);
void mh_set_parent(mh_entity entity, mh_entity parent);
/* Its index-th script (any type), or NULL; *type (when not NULL) gets the script's type. */
void *mh_get_script_at(mh_entity entity, int32_t index, const mh_script_type **type);
/* The camera the game renders with. */
mh_entity mh_main_camera(void);
/* The full path of a file of the Assets folder: "Sounds/jump.wav". */
const char *mh_asset_path(const char *relative);
/* The world scale (through the parents; ignores their rotation). */
mh_vec3 mh_get_lossy_scale(mh_entity entity);
float mh_fixed_delta_time(void);

/* ---- Tweens: moving, turning, scaling over time ------------------------------------------------ */

enum {
    MH_LINEAR, MH_EASE_IN, MH_EASE_OUT, MH_EASE_IN_OUT, MH_BACK_IN, MH_BACK_OUT, MH_ELASTIC, MH_BOUNCE,
};
/* The easing curve at t (0 to 1). */
float mh_ease(int32_t ease, float t);

/* A running tween, 0 for none. Tweens stop by themselves when their entity is destroyed. */
typedef int32_t mh_tween;
/* By an offset, by Euler angles (degrees, around its own axes; 360 makes a full turn), or by a factor. */
mh_tween mh_tween_move(mh_entity entity, mh_vec3 offset, float duration, int32_t ease);
mh_tween mh_tween_rotate(mh_entity entity, mh_vec3 degrees, float duration, int32_t ease);
mh_tween mh_tween_scale(mh_entity entity, mh_vec3 factor, float duration, int32_t ease);
/* To a local position, local Euler angles or local scale. */
mh_tween mh_tween_move_to(mh_entity entity, mh_vec3 position, float duration, int32_t ease);
mh_tween mh_tween_rotate_to(mh_entity entity, mh_vec3 degrees, float duration, int32_t ease);
mh_tween mh_tween_scale_to(mh_entity entity, mh_vec3 scale, float duration, int32_t ease);
/* A float of the entity's script (e.g. &self->alpha) from its value to another. */
mh_tween mh_tween_float(mh_entity entity, float *value, float to, float duration, int32_t ease);
/* Waits before starting; plays count times in all (-1: forever), back and forth with yoyo. */
void mh_tween_delay(mh_tween tween, float seconds);
void mh_tween_loop(mh_tween tween, int32_t count, bool yoyo);
/* Calls fn(self, entity) when the tween ends. */
void mh_tween_on_complete(mh_tween tween, mh_callback fn, void *self);
void mh_tween_stop(mh_tween tween);
void mh_tween_stop_all(mh_entity entity);
bool mh_tween_running(mh_tween tween);
bool mh_is_tweening(mh_entity entity);
/* Turns by degrees per second; call it every frame. */
void mh_spin(mh_entity entity, mh_vec3 degrees_per_second);

/* ---- Timers and coroutines ---------------------------------------------------------------------- */

/* A running timer, 0 for none; it stops when its entity is destroyed. */
typedef int32_t mh_timer;
/* fn(self, entity) once after some seconds, or every some seconds. */
mh_timer mh_after(float seconds, mh_callback fn, void *self, mh_entity entity);
mh_timer mh_every(float seconds, mh_callback fn, void *self, mh_entity entity);
void mh_timer_cancel(mh_timer timer);

/* Where a coroutine is, between its waits. */
typedef struct mh_coroutine {
    int32_t line;   /* where it waits, -1 when it ended */
    float wait;     /* seconds left */
    int32_t frames; /* frames left */
} mh_coroutine;
/* Returns true while it waits (see MH_CO_BEGIN). */
typedef bool (*mh_coroutine_fn)(void *self, mh_entity entity, mh_coroutine *co);
typedef int32_t mh_routine;
mh_routine mh_start_coroutine(mh_coroutine_fn fn, void *self, mh_entity entity);
void mh_stop_coroutine(mh_routine routine);
void mh_stop_all_coroutines(mh_entity entity);

/* A coroutine by hand: bool name(T *self, mh_entity entity, mh_coroutine *co) { MH_CO_BEGIN(co); ...
   MH_CO_WAIT(co, 1.0f); ... MH_CO_END(co); } (local variables do not keep their value across waits;
   keep what must last in the script's struct). One wait per line. */
#define MH_CO_BEGIN(co) switch ((co)->line) { case 0:
#define MH_CO_END(co) } (co)->line = -1; return false
#define MH_CO_WAIT(co, seconds) do { (co)->wait = (seconds); (co)->line = __LINE__; return true; case __LINE__:; } while (0)
#define MH_CO_WAIT_FRAME(co) do { (co)->frames = 1; (co)->line = __LINE__; return true; case __LINE__:; } while (0)
#define MH_CO_STOP(co) do { (co)->line = -1; return false; } while (0)

/* ---- Physics -------------------------------------------------------------------------------------
   Components (Add Component): BoxCollider, SphereCollider (is_trigger: overlaps without pushing) and
   Rigidbody (falls and gets pushed). Bodies do not turn from collisions. */

typedef struct {
    mh_entity entity;
    mh_vec3 point, normal;
    float distance;
} mh_hit;

/* The first collider along the ray (direction need not be normalized), within max_distance. */
bool mh_raycast(mh_vec3 origin, mh_vec3 direction, float max_distance, mh_hit *hit);
mh_vec3 mh_get_velocity(mh_entity entity);
void mh_set_velocity(mh_entity entity, mh_vec3 velocity);
/* Newtons for this fixed step (from FixedUpdate), or a change of momentum at once. */
void mh_add_force(mh_entity entity, mh_vec3 force);
void mh_add_impulse(mh_entity entity, mh_vec3 impulse);
mh_vec3 mh_get_gravity(void);
void mh_set_gravity(mh_vec3 gravity);
/* The colliders of the two entities touch (or overlap). */
bool mh_is_touching(mh_entity a, mh_entity b);

/* ---- Audio -----------------------------------------------------------------------------------------
   Files of the Assets folder (WAV, MP3, FLAC). AudioSource component: a clip, played on awake or with
   mh_audio_play; spatial ones sound from where they are, heard from the main camera. */

void mh_play_sound(const char *clip, float volume); /* once, not spatial */
void mh_play_sound_at(const char *clip, mh_vec3 position, float volume);
void mh_audio_play(mh_entity entity); /* its AudioSource */
void mh_audio_stop(mh_entity entity);
void mh_audio_pause(mh_entity entity);
bool mh_audio_is_playing(mh_entity entity);
void mh_audio_set_volume(mh_entity entity, float volume);
void mh_audio_set_pitch(mh_entity entity, float pitch);
void mh_set_master_volume(float volume);

#ifdef __cplusplus
}
#endif

#endif
