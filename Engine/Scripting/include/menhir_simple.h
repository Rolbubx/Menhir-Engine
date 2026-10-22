/* Menhir's simple script syntax: one file is one script, named after the file (see Docs/Scripting.md).

       MH_VARS {
           float speed = 90;        // MH_RANGE(0, 360) Degrees per second
           MH_GAMEOBJECT target;    // not a field the inspector shows: kept for the script
       };

       MH_START() {
           self.target = MH_PARENT;
       }

       MH_UPDATE() {
           MH_SPIN(self.target, MH_VEC3(0, self.speed, 0));
       }

   The editor compiles a generated copy of the file (Library/Cache/Scripts/Generated): it defines
   MH_SCRIPT_NAME, includes this header, turns MH_VARS' defaults into fields, and adds MH_REGISTER at the
   end with the callbacks the file has (and an empty MH_ON_GAME_CLOSE when it has none). Its #line
   directives keep the compiler's messages on the file's own lines. Not for files that use MH_SCRIPT. */
#ifndef MENHIR_SIMPLE_H
#define MENHIR_SIMPLE_H

#include <string.h>
#include "menhir.h"

#ifndef MH_SCRIPT_NAME
#error "MH_SCRIPT_NAME is defined by the editor's generated copy of the script."
#endif

#define MH__CAT2(a, b) a##b
#define MH__CAT(a, b) MH__CAT2(a, b)
#define MH__STR2(x) #x
#define MH__STR(x) MH__STR2(x)
#define MH__NAME(suffix) MH__CAT(MH_SCRIPT_NAME, suffix)

/* The script's variables: struct <Name>_vars, one per object that has the script. */
#define MH__VARS_T MH__NAME(_vars)
typedef struct MH__VARS_T MH__VARS_T;
#define MH_VARS struct MH__VARS_T

/* Inside the callbacks: the script's variables (self.speed), and the object the script is on. */
#define self (*mh__self)
#define MH_PARENT mh__parent
#define MH_SELF mh__self

/* ---- Types ---------------------------------------------------------------------------------------- */

typedef mh_entity MH_GAMEOBJECT;
/* A reference to an object: MH_REFERENCE *p = &player; the API takes objects or such pointers. */
typedef mh_entity MH_REFERENCE;
typedef float MH_TIMESTAMP;
typedef float MH_GAME_SETTINGS_TIME;
typedef const char *MH_STRING;
typedef mh_tween MH_TWEEN;
typedef mh_timer MH_TIMER_ID;
typedef mh_hit MH_HIT;
#define MH_NULL_OBJECT MH_NONE

/* MH_VEC3 is both a type and a constructor: MH_VEC3 axis = MH_VEC3(0, 1, 0); */
#undef MH_VEC2
#undef MH_VEC3
#undef MH_COLOR
#undef MH_STRING
typedef mh_vec2 MH_VEC2;
typedef mh_vec3 MH_VEC3;
typedef mh_color MH_COLOR;
typedef mh_quat MH_QUAT;
#define MH_VEC2(x, y) mh_v2((float)(x), (float)(y))
#define MH_VEC3(x, y, z) mh_v3((float)(x), (float)(y), (float)(z))
#define MH_COLOR(r, g, b, a) ((mh_color){ (float)(r), (float)(g), (float)(b), (float)(a) })

/* An object, or a pointer to one (MH_REFERENCE *). */
static inline mh_entity mh__object(mh_entity entity) { return entity; }
static inline mh_entity mh__object_at(const mh_entity *entity) { return entity ? *entity : MH_NONE; }
#define MH__OBJ(x) _Generic((x), mh_entity *: mh__object_at, const mh_entity *: mh__object_at, default: mh__object)(x)

/* ---- Callbacks: write the body right after them -------------------------------------------------------- */

#undef MH_AWAKE
#undef MH_START
#undef MH_UPDATE
#undef MH_LATE_UPDATE
#undef MH_FIXED_UPDATE
#undef MH_ON_DESTROY
#undef MH_ON_GAME_CLOSE
#undef MH_ON_COLLISION_ENTER
#undef MH_ON_COLLISION_EXIT
#undef MH_ON_TRIGGER_ENTER
#undef MH_ON_TRIGGER_EXIT

#define MH__CALLBACK(suffix) static void MH__NAME(suffix)(MH__VARS_T *mh__self, mh_entity mh__parent)
#define MH__CONTACT(suffix, other) static void MH__NAME(suffix)(MH__VARS_T *mh__self, mh_entity mh__parent, mh_entity other)

/* When the object is created (before any Start). */
#define MH_AWAKE() MH__CALLBACK(__awake)
/* The first thing on load: before the object's first update. */
#define MH_START() MH__CALLBACK(__start)
/* Every frame. */
#define MH_UPDATE() MH__CALLBACK(__update)
/* Every frame, after every object's update. */
#define MH_LATE_UPDATE() MH__CALLBACK(__late_update)
/* Every fixed step (physics): dt is the fixed delta time. */
#define MH_FIXED_UPDATE(dt)                                                                          \
    static void MH__NAME(__fixed_body)(MH__VARS_T *, mh_entity, MH_GAME_SETTINGS_TIME);               \
    static void MH__NAME(__fixed_update)(MH__VARS_T *s, mh_entity e) { MH__NAME(__fixed_body)(s, e, mh_fixed_delta_time()); } \
    static void MH__NAME(__fixed_body)(MH__VARS_T *mh__self, mh_entity mh__parent, MH_GAME_SETTINGS_TIME dt)
/* When the game closes: time is how long it ran (seconds). */
#define MH_ON_GAME_CLOSE(time)                                                                      \
    static void MH__NAME(__close_body)(MH__VARS_T *, mh_entity, MH_TIMESTAMP);                       \
    static void MH__NAME(__on_game_close)(MH__VARS_T *s, mh_entity e) { MH__NAME(__close_body)(s, e, mh_time()); } \
    static void MH__NAME(__close_body)(MH__VARS_T *mh__self, mh_entity mh__parent, MH_TIMESTAMP time)
/* When the object is destroyed (or its scene unloaded). */
#define MH_ON_DESTROY() MH__CALLBACK(__on_destroy)
/* Physics: other is the object hit (or entered). */
#define MH_ON_COLLISION_ENTER(other) MH__CONTACT(__collision_enter, other)
#define MH_ON_COLLISION_EXIT(other) MH__CONTACT(__collision_exit, other)
#define MH_ON_TRIGGER_ENTER(other) MH__CONTACT(__trigger_enter, other)
#define MH_ON_TRIGGER_EXIT(other) MH__CONTACT(__trigger_exit, other)
#define MH_ON_COLLISION(other) MH_ON_COLLISION_ENTER(other)
#define MH_ON_TRIGGER(other) MH_ON_TRIGGER_ENTER(other)

/* An unused parameter's name (the generated MH_ON_GAME_CLOSE uses it). */
#define MH_UNUSED_CMP mh__unused

/* MH_REGISTER(MH_ENTRYPOINT_ONLOAD, MH_ONEVERYFRAME_UPDATE, ...): the editor adds it at the end of the
   file with what the file has; write it yourself to choose. */
#define MH_ENTRYPOINT_AWAKE .awake = (mh_callback)MH__NAME(__awake)
#define MH_ENTRYPOINT_ONLOAD .start = (mh_callback)MH__NAME(__start)
#define MH_ONEVERYFRAME_UPDATE .update = (mh_callback)MH__NAME(__update)
#define MH_ONEVERYFRAME_LATE_UPDATE .late_update = (mh_callback)MH__NAME(__late_update)
#define MH_ONEVERYFRAME_FIX_UP .fixed_update = (mh_callback)MH__NAME(__fixed_update)
#define MH_ONGAMECLOSE .on_game_close = (mh_callback)MH__NAME(__on_game_close)
#define MH_ONDESTROY .on_destroy = (mh_callback)MH__NAME(__on_destroy)
#define MH_ONCOLLISION_ENTER .on_collision_enter = (mh_contact_callback)MH__NAME(__collision_enter)
#define MH_ONCOLLISION_EXIT .on_collision_exit = (mh_contact_callback)MH__NAME(__collision_exit)
#define MH_ONTRIGGER_ENTER .on_trigger_enter = (mh_contact_callback)MH__NAME(__trigger_enter)
#define MH_ONTRIGGER_EXIT .on_trigger_exit = (mh_contact_callback)MH__NAME(__trigger_exit)

/* The generated copy defines MH__FIELD_LIST (the fields of MH_VARS) and MH__INIT (gives MH_VARS'
   other variables their values, then runs MH_AWAKE). */
#define MH_REGISTER(...)                                                                            \
    static const mh_script_type MH__NAME(__mh_type) = {                                            \
        .name = MH__STR(MH_SCRIPT_NAME), .size = (uint32_t)sizeof(MH__VARS_T), MH__FIELD_LIST, __VA_ARGS__, \
        .awake = (mh_callback)MH__NAME(__mh_init) };                                                \
    static void MH__NAME(__mh_register)(void) __attribute__((constructor));                         \
    static void MH__NAME(__mh_register)(void) { mh_register_script(&MH__NAME(__mh_type)); }

/* The file names its script: MH_CREATE Test.c (or MH_CREATE(Test)); optional, the editor checks it. */
#define MH_CREATE(name)

/* ---- Objects and the scene ------------------------------------------------------------------------------ */

/* MH_SCENE.Tiki: the object named Tiki of the loaded scenes ("Main Camera" is MH_SCENE.Main_Camera),
   MH_NULL_OBJECT when there is none. The generated copy lists the names the file uses. */
mh_entity mh__scene_object(const char *identifier, mh_entity *cache);
#define MH_CATCH_SCENE(object) (mh_is_alive(MH__OBJ(object)) ? MH__OBJ(object) : MH_NULL_OBJECT)
#define MH_FIND(name) mh_find(name)
#define MH_IS_NULL(object) (!mh_is_alive(MH__OBJ(object)))
/* A script (or built-in component) of an object: MH_FIND_COMPONENT(obj, Rigidbody); MH_ANY_COMPONENT for the first. */
void *mh__find_component(mh_entity entity, const char *type);
#define MH_FIND_COMPONENT(object, Type) mh__find_component(MH__OBJ(object), #Type)
#define MH_NAME(object) mh_get_name(MH__OBJ(object))

#define MH_SPAWN(object, position) mh_spawn(MH__OBJ(object), position)
#define MH_INSTANTIATE(object) mh_instantiate(MH__OBJ(object), MH_NONE)
#define MH_DESTROY(object) mh_destroy(MH__OBJ(object))
#define MH_SET_ACTIVE(object, active) mh_set_active(MH__OBJ(object), active)
#define MH_IS_ACTIVE(object) mh_is_active(MH__OBJ(object))
#define MH_GET_PARENT(object) mh_get_parent(MH__OBJ(object))
#define MH_SET_PARENT(object, parent) mh_set_parent(MH__OBJ(object), MH__OBJ(parent))
#define MH_CHILD_COUNT(object) mh_child_count(MH__OBJ(object))
#define MH_GET_CHILD(object, index) mh_get_child(MH__OBJ(object), index)
#define MH_FIND_CHILD(object, name) mh_find_child(MH__OBJ(object), name)

/* ---- Transform ------------------------------------------------------------------------------------------- */

#define MH_GET_POSITION(object) mh_get_position(MH__OBJ(object))
#define MH_SET_POSITION(object, position) mh_set_position(MH__OBJ(object), position)
#define MH_GET_LOCAL_POSITION(object) mh_get_local_position(MH__OBJ(object))
#define MH_SET_LOCAL_POSITION(object, position) mh_set_local_position(MH__OBJ(object), position)
#define MH_GET_ROTATION(object) mh_quat_to_euler(mh_get_rotation(MH__OBJ(object)))
#define MH_SET_ROTATION(object, degrees) mh_set_rotation(MH__OBJ(object), mh_quat_euler(degrees))
#define MH_GET_SCALE(object) mh_get_local_scale(MH__OBJ(object))
#define MH_SET_SCALE(object, scale) mh_set_local_scale(MH__OBJ(object), scale)
#define MH_TRANSLATE(object, offset) mh_translate(MH__OBJ(object), offset, false)
#define MH_TURN(object, degrees) mh_rotate(MH__OBJ(object), degrees, false)
#define MH_LOOK_AT(object, target) mh_look_at(MH__OBJ(object), target)
#define MH_FORWARD(object) mh_forward(MH__OBJ(object))
#define MH_RIGHT(object) mh_right(MH__OBJ(object))
#define MH_UP(object) mh_up(MH__OBJ(object))
#define MH_DISTANCE(a, b) mh_v3_distance(mh_get_position(MH__OBJ(a)), mh_get_position(MH__OBJ(b)))

/* ---- Tweens ----------------------------------------------------------------------------------------------- */

#define MH_TWEEN_TYPE(ease) (ease)
/* By degrees / an offset / a factor, in seconds, with a curve: MH_ROTATE(obj, MH_VEC3(0, 90, 0), 1, MH_TWEEN_TYPE(MH_LINEAR)). */
#define MH_ROTATE(object, degrees, seconds, ease) mh_tween_rotate(MH__OBJ(object), degrees, seconds, ease)
#define MH_MOVE(object, offset, seconds, ease) mh_tween_move(MH__OBJ(object), offset, seconds, ease)
#define MH_SCALE(object, factor, seconds, ease) mh_tween_scale(MH__OBJ(object), factor, seconds, ease)
#define MH_ROTATE_TO(object, degrees, seconds, ease) mh_tween_rotate_to(MH__OBJ(object), degrees, seconds, ease)
#define MH_MOVE_TO(object, position, seconds, ease) mh_tween_move_to(MH__OBJ(object), position, seconds, ease)
#define MH_SCALE_TO(object, scale, seconds, ease) mh_tween_scale_to(MH__OBJ(object), scale, seconds, ease)
#define MH_TWEEN_FLOAT(variable, to, seconds, ease) mh_tween_float(mh__parent, &(variable), to, seconds, ease)
#define MH_TWEEN_DELAY(tween, seconds) mh_tween_delay(tween, seconds)
#define MH_TWEEN_LOOP(tween, count, yoyo) mh_tween_loop(tween, count, yoyo)
#define MH_ON_COMPLETE(tween, timer) mh_tween_on_complete(tween, (mh_callback)(timer), mh__self)
#define MH_STOP_TWEEN(tween) mh_tween_stop(tween)
#define MH_STOP_TWEENS(object) mh_tween_stop_all(MH__OBJ(object))
#define MH_IS_TWEENING(object) mh_is_tweening(MH__OBJ(object))
/* Degrees per second, every frame. */
#define MH_SPIN(object, degrees_per_second) mh_spin(MH__OBJ(object), degrees_per_second)

/* ---- Timers and coroutines ---------------------------------------------------------------------------------- */

/* A function for MH_AFTER, MH_EVERY and MH_ON_COMPLETE: MH_TIMER(blink) { ... self ... }. */
#define MH_TIMER(name) static void name(MH__VARS_T *mh__self, mh_entity mh__parent)
#define MH_AFTER(seconds, timer) mh_after(seconds, (mh_callback)(timer), mh__self, mh__parent)
#define MH_EVERY(seconds, timer) mh_every(seconds, (mh_callback)(timer), mh__self, mh__parent)
#define MH_CANCEL(timer_id) mh_timer_cancel(timer_id)

/* MH_COROUTINE(name) { ... MH_WAIT(1); ... }: local variables do not keep their value across waits
   (use self), and one wait per line. */
#define MH_COROUTINE(name) static bool name(MH__VARS_T *mh__self, mh_entity mh__parent, mh_coroutine *mh__co)
#define MH__CO_BEGIN MH_CO_BEGIN(mh__co);
#define MH__CO_END MH_CO_END(mh__co);
#define MH_WAIT(seconds) MH_CO_WAIT(mh__co, seconds)
#define MH_WAIT_FRAME() MH_CO_WAIT_FRAME(mh__co)
#define MH_WAIT_UNTIL(condition) while (!(condition)) MH_CO_WAIT_FRAME(mh__co)
#define MH_STOP() MH_CO_STOP(mh__co)
#define MH_START_COROUTINE(name) mh_start_coroutine((mh_coroutine_fn)(name), mh__self, mh__parent)
#define MH_STOP_COROUTINE(routine) mh_stop_coroutine(routine)

/* ---- Physics, audio, input, time, the game ------------------------------------------------------------------ */

#define MH_RAYCAST(origin, direction, distance, hit) mh_raycast(origin, direction, distance, hit)
#define MH_ADD_FORCE(object, force) mh_add_force(MH__OBJ(object), force)
#define MH_ADD_IMPULSE(object, impulse) mh_add_impulse(MH__OBJ(object), impulse)
#define MH_GET_VELOCITY(object) mh_get_velocity(MH__OBJ(object))
#define MH_SET_VELOCITY(object, velocity) mh_set_velocity(MH__OBJ(object), velocity)
#define MH_IS_TOUCHING(a, b) mh_is_touching(MH__OBJ(a), MH__OBJ(b))

#define MH_PLAY_SOUND(clip) mh_play_sound(clip, 1.0f)
#define MH_PLAY_SOUND_AT(clip, position) mh_play_sound_at(clip, position, 1.0f)
#define MH_PLAY(object) mh_audio_play(MH__OBJ(object))
#define MH_STOP_SOUND(object) mh_audio_stop(MH__OBJ(object))
#define MH_IS_PLAYING(object) mh_audio_is_playing(MH__OBJ(object))

#define MH_KEY(name) mh_key(name)
#define MH_KEY_DOWN(name) mh_key_down(name)
#define MH_KEY_UP(name) mh_key_up(name)
#define MH_BUTTON(name) mh_button(name)
#define MH_BUTTON_DOWN(name) mh_button_down(name)
#define MH_AXIS(name) mh_axis(name)
#define MH_MOUSE() mh_mouse_position()

#define MH_DELTA_TIME() mh_delta_time()
#define MH_TIME() mh_time()
#define MH_RANDOM(min, max) mh_random_range(min, max)
#define MH_LOG(...) mh_log(__VA_ARGS__)
#define MH_WARN(...) mh_log_warning(__VA_ARGS__)
#define MH_ERROR(...) mh_log_error(__VA_ARGS__)
#define MH_LOAD_SCENE(name) mh_load_scene(name, MH_LOAD_SINGLE)
#define MH_QUIT() mh_quit()

#endif
