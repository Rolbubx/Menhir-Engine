/* The scripting API's runtime, compiled with the scripts like menhir_script.c: objects (through the
   engine's mh_api), tweens, timers and coroutines. The engine calls mh_runtime_update each frame after
   the scripts' Update, mh_runtime_fixed_step after each fixed step's FixedUpdate (the physics,
   menhir_physics.c) and mh_runtime_shutdown when the game ends. Everything here runs the same in the
   editor's Play mode and in the built game. */
#include <stdlib.h>
#include <string.h>
#include "menhir.h"
#include "menhir_internal.h"

/* ---- Objects ---------------------------------------------------------------------------------- */

bool mh_is_alive(mh_entity entity) { return entity != MH_NONE && mh_engine->is_alive(entity) != 0; }

mh_entity mh_instantiate(mh_entity entity, mh_entity parent) { return mh_engine->instantiate(entity, parent); }

mh_entity mh_spawn(mh_entity entity, mh_vec3 position)
{
    mh_entity copy = mh_engine->instantiate(entity, MH_NONE);
    if (copy != MH_NONE)
        mh_set_position(copy, position);
    return copy;
}

void mh_destroy(mh_entity entity) { mh_engine->destroy(entity); }
void mh_set_active(mh_entity entity, bool active) { mh_engine->set_active(entity, active); }
bool mh_is_active(mh_entity entity) { return mh_engine->get_active(entity, 1) != 0; }
bool mh_is_active_self(mh_entity entity) { return mh_engine->get_active(entity, 0) != 0; }
int32_t mh_child_count(mh_entity entity) { return mh_engine->child_count(entity); }
mh_entity mh_get_child(mh_entity entity, int32_t index) { return mh_engine->child_at(entity, index); }

mh_entity mh_find_child(mh_entity entity, const char *name)
{
    for (int32_t i = 0, n = mh_child_count(entity); i < n; i++) {
        mh_entity child = mh_get_child(entity, i);
        if (!strcmp(mh_get_name(child), name))
            return child;
    }
    return MH_NONE;
}

void mh_set_parent(mh_entity entity, mh_entity parent) { mh_engine->set_parent(entity, parent); }

void *mh_get_script_at(mh_entity entity, int32_t index, const mh_script_type **type)
{
    const mh_script_type *found = NULL;
    void *script = mh_engine->script_at(entity, index, &found);
    if (type)
        *type = found;
    return script;
}

mh_entity mh_main_camera(void) { return mh_engine->main_camera(); }
const char *mh_asset_path(const char *relative) { return mh_engine->asset_path(relative); }
float mh_fixed_delta_time(void) { return mh_engine->get_time(3); }

mh_vec3 mh_get_lossy_scale(mh_entity entity)
{
    mh_vec3 scale = mh_v3(1, 1, 1);
    for (int depth = 0; entity != MH_NONE && depth < 256; depth++) {
        scale = mh_v3_mul(scale, mh_get_local_scale(entity));
        entity = mh_get_parent(entity);
    }
    return scale;
}

/* MH_SCENE.Name of the simple syntax: an object by its name as a C identifier ("Main_Camera" also finds
   "Main Camera"), remembered while it exists. */
mh_entity mh__scene_object(const char *identifier, mh_entity *cache)
{
    if (*cache != MH_NONE && mh_is_alive(*cache))
        return *cache;
    mh_entity found = mh_find(identifier);
    if (found == MH_NONE && strchr(identifier, '_')) {
        char spaced[256];
        size_t n = strlen(identifier);
        if (n < sizeof spaced) {
            for (size_t i = 0; i <= n; i++)
                spaced[i] = identifier[i] == '_' ? ' ' : identifier[i];
            found = mh_find(spaced);
        }
    }
    *cache = found;
    return found;
}

/* MH_FIND_COMPONENT: a script or built-in component by type name, or the first one (MH_ANY_COMPONENT). */
void *mh__find_component(mh_entity entity, const char *type)
{
    if (!strcmp(type, "MH_ANY_COMPONENT"))
        return mh_get_script_at(entity, 0, NULL);
    return mh_get_script(entity, type);
}

/* ---- Growing arrays ----------------------------------------------------------------------------- */

static void *mh_grow(void *items, int32_t *capacity, int32_t count, size_t size)
{
    if (count < *capacity)
        return items;
    int32_t grown = *capacity ? *capacity * 2 : 16;
    void *bigger = realloc(items, size * (size_t)grown);
    if (!bigger) {
        mh_log_error("Out of memory.");
        return NULL;
    }
    *capacity = grown;
    return bigger;
}

/* ---- Easing ----------------------------------------------------------------------------------------- */

static float mh_bounce_out(float t)
{
    if (t < 1 / 2.75f)
        return 7.5625f * t * t;
    if (t < 2 / 2.75f) {
        t -= 1.5f / 2.75f;
        return 7.5625f * t * t + 0.75f;
    }
    if (t < 2.5f / 2.75f) {
        t -= 2.25f / 2.75f;
        return 7.5625f * t * t + 0.9375f;
    }
    t -= 2.625f / 2.75f;
    return 7.5625f * t * t + 0.984375f;
}

float mh_ease(int32_t ease, float t)
{
    t = mh_clamp(t, 0, 1);
    const float back = 1.70158f;
    switch (ease) {
    case MH_EASE_IN: return t * t * t;
    case MH_EASE_OUT: { float u = 1 - t; return 1 - u * u * u; }
    case MH_EASE_IN_OUT: return t < 0.5f ? 4 * t * t * t : 1 - powf(-2 * t + 2, 3) / 2;
    case MH_BACK_IN: return (back + 1) * t * t * t - back * t * t;
    case MH_BACK_OUT: { float u = t - 1; return 1 + (back + 1) * u * u * u + back * u * u; }
    case MH_ELASTIC:
        return t <= 0 ? 0 : t >= 1 ? 1 : powf(2, -10 * t) * sinf((t * 10 - 0.75f) * (2 * MH_PI / 3)) + 1;
    case MH_BOUNCE: return mh_bounce_out(t);
    default: return t;
    }
}

/* ---- Tweens ----------------------------------------------------------------------------------------- */

enum { MH_TWEEN_MOVE, MH_TWEEN_ROTATE, MH_TWEEN_ROTATE_TO, MH_TWEEN_SCALE, MH_TWEEN_FLOAT };

typedef struct {
    mh_tween id;
    mh_entity entity;
    int32_t kind, ease;
    float duration, elapsed, delay;
    int32_t loops; /* plays left, -1 forever */
    bool yoyo, backwards, started, relative;
    mh_vec3 from, to;  /* positions or scales, or the Euler angles of rotate_to */
    mh_vec3 by;        /* relative tweens: the offset, factor or Euler angles */
    mh_quat from_rotation, to_rotation;
    float *value;
    float from_value, to_value;
    mh_callback on_complete;
    void *self;
} mh_tween_state;

static mh_tween_state *mh_tweens;
static int32_t mh_tween_count, mh_tween_capacity;
static mh_tween mh_next_tween = 1;

static mh_tween_state *mh_tween_find(mh_tween id)
{
    for (int32_t i = 0; i < mh_tween_count; i++)
        if (mh_tweens[i].id == id)
            return &mh_tweens[i];
    return NULL;
}

static mh_tween_state *mh_tween_add(mh_entity entity, int32_t kind, float duration, int32_t ease, bool relative)
{
    if (!mh_is_alive(entity)) {
        mh_log_error("A tween needs an entity that exists.");
        return NULL;
    }
    mh_tween_state *grown = mh_grow(mh_tweens, &mh_tween_capacity, mh_tween_count, sizeof(*mh_tweens));
    if (!grown)
        return NULL;
    mh_tweens = grown;
    mh_tween_state *t = &mh_tweens[mh_tween_count++];
    memset(t, 0, sizeof(*t));
    t->id = mh_next_tween++;
    t->entity = entity;
    t->kind = kind;
    t->ease = ease;
    t->duration = duration > 0 ? duration : 0;
    t->loops = 1;
    t->relative = relative;
    return t;
}

/* The start values are read when the tween starts (after its delay), so tweens can follow each other. */
static void mh_tween_begin(mh_tween_state *t)
{
    t->started = true;
    switch (t->kind) {
    case MH_TWEEN_MOVE:
        t->from = mh_get_local_position(t->entity);
        if (t->relative)
            t->to = mh_v3_add(t->from, t->by);
        break;
    case MH_TWEEN_SCALE:
        t->from = mh_get_local_scale(t->entity);
        if (t->relative)
            t->to = mh_v3_mul(t->from, t->by);
        break;
    case MH_TWEEN_ROTATE:
        t->from_rotation = mh_get_local_rotation(t->entity);
        break;
    case MH_TWEEN_ROTATE_TO:
        t->from_rotation = mh_get_local_rotation(t->entity);
        t->to_rotation = mh_quat_euler(t->to);
        break;
    case MH_TWEEN_FLOAT:
        t->from_value = *t->value;
        break;
    }
}

static void mh_tween_apply(mh_tween_state *t, float progress)
{
    float k = mh_ease(t->ease, t->backwards ? 1 - progress : progress);
    switch (t->kind) {
    case MH_TWEEN_MOVE: mh_set_local_position(t->entity, mh_v3_add(t->from, mh_v3_scale(mh_v3_sub(t->to, t->from), k))); break;
    case MH_TWEEN_SCALE: mh_set_local_scale(t->entity, mh_v3_add(t->from, mh_v3_scale(mh_v3_sub(t->to, t->from), k))); break;
    case MH_TWEEN_ROTATE:
        /* Euler angles scaled, so 360 degrees is a full turn (a slerp would take the short way). */
        mh_set_local_rotation(t->entity, mh_quat_normalize(mh_quat_mul(t->from_rotation, mh_quat_euler(mh_v3_scale(t->by, k)))));
        break;
    case MH_TWEEN_ROTATE_TO: {
        /* Not clamped, for the curves that overshoot. */
        mh_quat a = t->from_rotation, b = t->to_rotation;
        if (a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w < 0)
            b = (mh_quat){ -b.x, -b.y, -b.z, -b.w };
        mh_quat q = { a.x + (b.x - a.x) * k, a.y + (b.y - a.y) * k, a.z + (b.z - a.z) * k, a.w + (b.w - a.w) * k };
        mh_set_local_rotation(t->entity, mh_quat_normalize(q));
        break;
    }
    case MH_TWEEN_FLOAT: *t->value = t->from_value + (t->to_value - t->from_value) * k; break;
    }
}

mh_tween mh_tween_move(mh_entity entity, mh_vec3 offset, float duration, int32_t ease)
{
    mh_tween_state *t = mh_tween_add(entity, MH_TWEEN_MOVE, duration, ease, true);
    return t ? (t->by = offset, t->id) : 0;
}

mh_tween mh_tween_move_to(mh_entity entity, mh_vec3 position, float duration, int32_t ease)
{
    mh_tween_state *t = mh_tween_add(entity, MH_TWEEN_MOVE, duration, ease, false);
    return t ? (t->to = position, t->id) : 0;
}

mh_tween mh_tween_rotate(mh_entity entity, mh_vec3 degrees, float duration, int32_t ease)
{
    mh_tween_state *t = mh_tween_add(entity, MH_TWEEN_ROTATE, duration, ease, true);
    return t ? (t->by = degrees, t->id) : 0;
}

mh_tween mh_tween_rotate_to(mh_entity entity, mh_vec3 degrees, float duration, int32_t ease)
{
    mh_tween_state *t = mh_tween_add(entity, MH_TWEEN_ROTATE_TO, duration, ease, false);
    return t ? (t->to = degrees, t->id) : 0;
}

mh_tween mh_tween_scale(mh_entity entity, mh_vec3 factor, float duration, int32_t ease)
{
    mh_tween_state *t = mh_tween_add(entity, MH_TWEEN_SCALE, duration, ease, true);
    return t ? (t->by = factor, t->id) : 0;
}

mh_tween mh_tween_scale_to(mh_entity entity, mh_vec3 scale, float duration, int32_t ease)
{
    mh_tween_state *t = mh_tween_add(entity, MH_TWEEN_SCALE, duration, ease, false);
    return t ? (t->to = scale, t->id) : 0;
}

mh_tween mh_tween_float(mh_entity entity, float *value, float to, float duration, int32_t ease)
{
    if (!value)
        return 0;
    mh_tween_state *t = mh_tween_add(entity, MH_TWEEN_FLOAT, duration, ease, false);
    return t ? (t->value = value, t->to_value = to, t->id) : 0;
}

void mh_tween_delay(mh_tween tween, float seconds)
{
    mh_tween_state *t = mh_tween_find(tween);
    if (t)
        t->delay = seconds > 0 ? seconds : 0;
}

void mh_tween_loop(mh_tween tween, int32_t count, bool yoyo)
{
    mh_tween_state *t = mh_tween_find(tween);
    if (t) {
        t->loops = count < 0 ? -1 : count > 0 ? count : 1;
        t->yoyo = yoyo;
    }
}

void mh_tween_on_complete(mh_tween tween, mh_callback fn, void *self)
{
    mh_tween_state *t = mh_tween_find(tween);
    if (t) {
        t->on_complete = fn;
        t->self = self;
    }
}

void mh_tween_stop(mh_tween tween)
{
    mh_tween_state *t = mh_tween_find(tween);
    if (t)
        t->entity = MH_NONE; /* removed by the next update */
}

void mh_tween_stop_all(mh_entity entity)
{
    for (int32_t i = 0; i < mh_tween_count; i++)
        if (mh_tweens[i].entity == entity)
            mh_tweens[i].entity = MH_NONE;
}

bool mh_tween_running(mh_tween tween)
{
    mh_tween_state *t = mh_tween_find(tween);
    return t && t->entity != MH_NONE;
}

bool mh_is_tweening(mh_entity entity)
{
    for (int32_t i = 0; i < mh_tween_count; i++)
        if (mh_tweens[i].entity == entity && entity != MH_NONE)
            return true;
    return false;
}

void mh_spin(mh_entity entity, mh_vec3 degrees_per_second)
{
    mh_rotate(entity, mh_v3_scale(degrees_per_second, mh_delta_time()), false);
}

static void mh_tweens_update(float dt)
{
    /* Tweens started by callbacks during the loop run from the next frame. */
    int32_t count = mh_tween_count;
    for (int32_t i = 0; i < count; i++) {
        mh_tween_state *t = &mh_tweens[i];
        if (t->entity == MH_NONE || !mh_is_alive(t->entity)) {
            t->entity = MH_NONE;
            continue;
        }
        float step = dt;
        if (t->delay > 0) {
            t->delay -= step;
            if (t->delay > 0)
                continue;
            step = -t->delay;
            t->delay = 0;
        }
        if (!t->started)
            mh_tween_begin(t);
        t->elapsed += step;
        bool done = t->elapsed >= t->duration;
        mh_tween_apply(t, t->duration > 0 ? mh_clamp(t->elapsed / t->duration, 0, 1) : 1);
        if (!done)
            continue;
        if (t->loops < 0 || --t->loops > 0) {
            t->elapsed = 0;
            if (t->yoyo)
                t->backwards = !t->backwards;
            else if (t->relative)
                mh_tween_begin(t); /* relative again, from where it is */
            continue;
        }
        mh_callback complete = t->on_complete;
        void *self = t->self;
        mh_entity entity = t->entity;
        t->entity = MH_NONE;
        if (complete)
            complete(self, entity); /* may add tweens: t may move */
    }
    int32_t kept = 0;
    for (int32_t i = 0; i < mh_tween_count; i++)
        if (mh_tweens[i].entity != MH_NONE)
            mh_tweens[kept++] = mh_tweens[i];
    mh_tween_count = kept;
}

/* ---- Timers ----------------------------------------------------------------------------------------- */

typedef struct {
    mh_timer id;
    mh_entity entity;
    float interval, left;
    bool repeat;
    mh_callback fn;
    void *self;
} mh_timer_state;

static mh_timer_state *mh_timers;
static int32_t mh_timer_count, mh_timer_capacity;
static mh_timer mh_next_timer = 1;

static mh_timer mh_timer_add(float seconds, mh_callback fn, void *self, mh_entity entity, bool repeat)
{
    if (!fn)
        return 0;
    mh_timer_state *grown = mh_grow(mh_timers, &mh_timer_capacity, mh_timer_count, sizeof(*mh_timers));
    if (!grown)
        return 0;
    mh_timers = grown;
    mh_timer_state *t = &mh_timers[mh_timer_count++];
    *t = (mh_timer_state){ mh_next_timer++, entity, seconds > 0 ? seconds : 0, seconds > 0 ? seconds : 0, repeat, fn, self };
    return t->id;
}

mh_timer mh_after(float seconds, mh_callback fn, void *self, mh_entity entity) { return mh_timer_add(seconds, fn, self, entity, false); }
mh_timer mh_every(float seconds, mh_callback fn, void *self, mh_entity entity) { return mh_timer_add(seconds, fn, self, entity, true); }

void mh_timer_cancel(mh_timer timer)
{
    for (int32_t i = 0; i < mh_timer_count; i++)
        if (mh_timers[i].id == timer)
            mh_timers[i].fn = NULL;
}

static void mh_timers_update(float dt)
{
    int32_t count = mh_timer_count;
    for (int32_t i = 0; i < count; i++) {
        if (!mh_timers[i].fn)
            continue;
        if (mh_timers[i].entity != MH_NONE && !mh_is_alive(mh_timers[i].entity)) {
            mh_timers[i].fn = NULL;
            continue;
        }
        mh_timers[i].left -= dt;
        if (mh_timers[i].left > 0)
            continue;
        mh_timer_state fired = mh_timers[i];
        if (fired.repeat && fired.interval > 0)
            mh_timers[i].left += fired.interval;
        else if (fired.repeat)
            mh_timers[i].left = 0; /* every frame */
        else
            mh_timers[i].fn = NULL;
        fired.fn(fired.self, fired.entity); /* may add timers: the array may move */
    }
    int32_t kept = 0;
    for (int32_t i = 0; i < mh_timer_count; i++)
        if (mh_timers[i].fn)
            mh_timers[kept++] = mh_timers[i];
    mh_timer_count = kept;
}

/* ---- Coroutines ------------------------------------------------------------------------------------- */

typedef struct {
    mh_routine id;
    mh_entity entity;
    mh_coroutine_fn fn;
    void *self;
    mh_coroutine co;
} mh_routine_state;

static mh_routine_state *mh_routines;
static int32_t mh_routine_count, mh_routine_capacity;
static mh_routine mh_next_routine = 1;

/* Runs at once up to its first wait, like Unity's StartCoroutine. */
mh_routine mh_start_coroutine(mh_coroutine_fn fn, void *self, mh_entity entity)
{
    if (!fn)
        return 0;
    mh_coroutine co = { 0, 0, 0 };
    if (!fn(self, entity, &co))
        return 0;
    mh_routine_state *grown = mh_grow(mh_routines, &mh_routine_capacity, mh_routine_count, sizeof(*mh_routines));
    if (!grown)
        return 0;
    mh_routines = grown;
    mh_routine_state *r = &mh_routines[mh_routine_count++];
    *r = (mh_routine_state){ mh_next_routine++, entity, fn, self, co };
    return r->id;
}

void mh_stop_coroutine(mh_routine routine)
{
    for (int32_t i = 0; i < mh_routine_count; i++)
        if (mh_routines[i].id == routine)
            mh_routines[i].fn = NULL;
}

void mh_stop_all_coroutines(mh_entity entity)
{
    for (int32_t i = 0; i < mh_routine_count; i++)
        if (mh_routines[i].entity == entity)
            mh_routines[i].fn = NULL;
}

static void mh_coroutines_update(float dt)
{
    int32_t count = mh_routine_count;
    for (int32_t i = 0; i < count; i++) {
        mh_routine_state *r = &mh_routines[i];
        if (!r->fn)
            continue;
        if (r->entity != MH_NONE && !mh_is_alive(r->entity)) {
            r->fn = NULL;
            continue;
        }
        if (r->co.frames > 0 && --r->co.frames > 0)
            continue;
        if (r->co.wait > 0) {
            r->co.wait -= dt;
            if (r->co.wait > 0)
                continue;
        }
        mh_coroutine co = r->co;
        mh_coroutine_fn fn = r->fn;
        bool waiting = fn(r->self, r->entity, &co);
        r = &mh_routines[i]; /* it may have started others */
        if (!r->fn)
            continue; /* stopped by itself */
        r->co = co;
        if (!waiting)
            r->fn = NULL;
    }
    int32_t kept = 0;
    for (int32_t i = 0; i < mh_routine_count; i++)
        if (mh_routines[i].fn)
            mh_routines[kept++] = mh_routines[i];
    mh_routine_count = kept;
}

/* ---- What the engine calls ---------------------------------------------------------------------------- */

MH_EXPORT void mh_runtime_update(void)
{
    float dt = mh_delta_time();
    mh_timers_update(dt);
    mh_coroutines_update(dt);
    mh_tweens_update(dt);
    mh_audio_update();
}

MH_EXPORT void mh_runtime_fixed_step(void) { mh_physics_step(mh_fixed_delta_time()); }

MH_EXPORT void mh_runtime_shutdown(void)
{
    mh_audio_shutdown();
    mh_physics_reset();
    free(mh_tweens);
    free(mh_timers);
    free(mh_routines);
    mh_tweens = NULL;
    mh_timers = NULL;
    mh_routines = NULL;
    mh_tween_count = mh_tween_capacity = mh_timer_count = mh_timer_capacity = mh_routine_count = mh_routine_capacity = 0;
}
