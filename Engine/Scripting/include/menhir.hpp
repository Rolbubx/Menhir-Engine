/* Menhir's C++ scripting API, in the style of Unity (see Docs/Scripting.md):

       class Player : public MenhirEngine {
       public:
           float speed = 90;               // MH_RANGE(0, 360) Degrees per second  <- a field of the inspector
           MenhirPos jump { 0, 5, 0 };

           void OnLoad() {
               Rotate(MenhirStyle::EaseOut, MenhirAngles { 0, 90, 0 }, 1.0f);
           }

           void OnUpdate(MenhirTick dt) {
               if (KeyDown("space"))
                   AddImpulse(jump);
               Find("Lamp").Spin({ 0, speed, 0 });
           }
       };

   A script is a class deriving from MenhirEngine; it is itself the object it is on (Move, Position,
   Destroy... act on it), with the static helpers of MenhirEngine (Find, Log, KeyDown, After...). Its public
   variables of the types below are fields (shown in the inspector, saved with the scene). The editor compiles
   a generated copy of the file that registers each such class under its name (MENHIR_REGISTER). Callbacks,
   all optional: OnAwake(), OnLoad() (or OnStart()), OnUpdate(MenhirTick dt), OnLateUpdate(MenhirTick dt),
   OnFixedUpdate(MenhirTick dt), OnDestroy(), OnGameClose(MenhirTick time), OnCollisionEnter(MenhirOther),
   OnCollisionExit, OnTriggerEnter, OnTriggerExit, OnMessage(const char *name, MenhirAny value).
   The engine is C: this header wraps menhir.h, and its runtime (tweens, physics, audio) is shared. */
#ifndef MENHIR_HPP
#define MENHIR_HPP

#include "menhir.h"

#include <coroutine>
#include <cstdarg>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cmath>
#include <cxxabi.h>
#include <exception>
#include <functional>
#include <new>
#include <string>
#include <typeinfo>
#include <unordered_map>
#include <utility>
#include <vector>

/* ---- Values ------------------------------------------------------------------------------------------ */

struct MenhirVec2 {
    float x = 0, y = 0;
    constexpr MenhirVec2() = default;
    constexpr MenhirVec2(float x_, float y_) : x(x_), y(y_) {}
    MenhirVec2(mh_vec2 v) : x(v.x), y(v.y) {}
    operator mh_vec2() const { return mh_v2(x, y); }
    MenhirVec2 operator+(MenhirVec2 o) const { return { x + o.x, y + o.y }; }
    MenhirVec2 operator-(MenhirVec2 o) const { return { x - o.x, y - o.y }; }
    MenhirVec2 operator*(float s) const { return { x * s, y * s }; }
    MenhirVec2 operator/(float s) const { return { x / s, y / s }; }
    float Length() const { return std::sqrt(x * x + y * y); }
};

/* A position, a direction, Euler angles (degrees) or a scale. */
struct MenhirVec3 {
    float x = 0, y = 0, z = 0;
    constexpr MenhirVec3() = default;
    constexpr MenhirVec3(float x_, float y_, float z_) : x(x_), y(y_), z(z_) {}
    MenhirVec3(mh_vec3 v) : x(v.x), y(v.y), z(v.z) {}
    operator mh_vec3() const { return mh_v3(x, y, z); }

    MenhirVec3 operator+(MenhirVec3 o) const { return { x + o.x, y + o.y, z + o.z }; }
    MenhirVec3 operator-(MenhirVec3 o) const { return { x - o.x, y - o.y, z - o.z }; }
    MenhirVec3 operator-() const { return { -x, -y, -z }; }
    MenhirVec3 operator*(float s) const { return { x * s, y * s, z * s }; }
    MenhirVec3 operator*(MenhirVec3 o) const { return { x * o.x, y * o.y, z * o.z }; }
    MenhirVec3 operator/(float s) const { return { x / s, y / s, z / s }; }
    MenhirVec3 &operator+=(MenhirVec3 o) { return *this = *this + o; }
    MenhirVec3 &operator-=(MenhirVec3 o) { return *this = *this - o; }
    MenhirVec3 &operator*=(float s) { return *this = *this * s; }
    bool operator==(const MenhirVec3 &o) const = default;

    float Dot(MenhirVec3 o) const { return x * o.x + y * o.y + z * o.z; }
    MenhirVec3 Cross(MenhirVec3 o) const { return mh_v3_cross(*this, o); }
    float Length() const { return mh_v3_length(*this); }
    MenhirVec3 Normalized() const { return mh_v3_normalize(*this); }
    float Distance(MenhirVec3 o) const { return (o - *this).Length(); }
    static MenhirVec3 Lerp(MenhirVec3 a, MenhirVec3 b, float t) { return mh_v3_lerp(a, b, t); }

    static constexpr MenhirVec3 Zero() { return { 0, 0, 0 }; }
    static constexpr MenhirVec3 One() { return { 1, 1, 1 }; }
    static constexpr MenhirVec3 Up() { return { 0, 1, 0 }; }
    static constexpr MenhirVec3 Down() { return { 0, -1, 0 }; }
    static constexpr MenhirVec3 Right() { return { 1, 0, 0 }; }
    static constexpr MenhirVec3 Left() { return { -1, 0, 0 }; }
    static constexpr MenhirVec3 Forward() { return { 0, 0, -1 }; }
    static constexpr MenhirVec3 Back() { return { 0, 0, 1 }; }
};
inline MenhirVec3 operator*(float s, MenhirVec3 v) { return v * s; }

using MenhirPos = MenhirVec3;
using MenhirAngles = MenhirVec3;
using MenhirScale = MenhirVec3;

struct MenhirColor {
    float r = 1, g = 1, b = 1, a = 1;
    constexpr MenhirColor() = default;
    constexpr MenhirColor(float r_, float g_, float b_, float a_ = 1) : r(r_), g(g_), b(b_), a(a_) {}
    operator mh_color() const { return mh_color{ r, g, b, a }; }
};

/* Seconds: a frame's or a fixed step's duration, or a time. */
using MenhirTick = float;
/* A text field (the engine owns the text). */
using MenhirText = const char *;

/* The curve of a move, a turn or a scale over time. */
enum class MenhirStyle : int32_t {
    Linear = MH_LINEAR,
    EaseIn = MH_EASE_IN,
    EaseOut = MH_EASE_OUT,
    EaseInOut = MH_EASE_IN_OUT,
    BackIn = MH_BACK_IN,
    BackOut = MH_BACK_OUT,
    Elastic = MH_ELASTIC,
    Bounce = MH_BOUNCE,
};

class MenhirObject;
class MenhirEngine;

namespace menhir_detail {

/* Something a script runs later (a timer, a coroutine, a tween's end), owned by the script that made it:
   it goes when the script does. */
struct Task {
    std::function<void()> fn;
    std::coroutine_handle<> routine;
    mh_timer timer = 0;
    mh_tween tween = 0;
    bool done = false;
    MenhirEngine *owner = nullptr;
};

/* The script whose callback runs now (who owns the tasks it makes). */
inline MenhirEngine *current = nullptr;

struct Current {
    MenhirEngine *saved;
    explicit Current(MenhirEngine *script) : saved(current) { current = script; }
    ~Current() { current = saved; }
};

Task *Adopt(Task *task);
void Release(Task *task);

inline void Run(Task *task)
{
    if (task->done)
        return;
    try {
        task->fn();
    } catch (const std::exception &e) {
        mh_log_error("A script's callback failed: %s", e.what());
    } catch (...) {
        mh_log_error("A script's callback failed.");
    }
}

/* The C runtime's callbacks: self is the task. */
inline void RunOnce(void *self, mh_entity)
{
    auto *task = static_cast<Task *>(self);
    Run(task);
    task->done = true;
    Release(task);
}

inline void RunRepeating(void *self, mh_entity) { Run(static_cast<Task *>(self)); }

/* The C++ name of a type (a script's class name). */
template <class T> const char *TypeName()
{
    static std::string name = [] {
        int status = 0;
        char *demangled = abi::__cxa_demangle(typeid(T).name(), nullptr, nullptr, &status);
        std::string text = status == 0 && demangled ? demangled : typeid(T).name();
        std::free(demangled);
        return text;
    }();
    return name.c_str();
}

inline std::string Format(const char *format, va_list args)
{
    va_list copy;
    va_copy(copy, args);
    int n = std::vsnprintf(nullptr, 0, format, copy);
    va_end(copy);
    std::string text(n > 0 ? (size_t)n : 0, '\0');
    if (n > 0)
        std::vsnprintf(text.data(), (size_t)n + 1, format, args);
    return text;
}

} // namespace menhir_detail

/* A value of any of a few kinds, for messages (MenhirObject::Send) and the like. */
class MenhirAny {
public:
    enum class Kind { None, Int, Float, Bool, Vec3, Text, Object };

    MenhirAny() = default;
    MenhirAny(int v) : _kind(Kind::Int), _int(v) {}
    MenhirAny(float v) : _kind(Kind::Float), _float(v) {}
    MenhirAny(double v) : _kind(Kind::Float), _float((float)v) {}
    MenhirAny(bool v) : _kind(Kind::Bool), _int(v) {}
    MenhirAny(MenhirVec3 v) : _kind(Kind::Vec3), _vec(v) {}
    MenhirAny(const char *v) : _kind(Kind::Text), _text(v ? v : "") {}
    MenhirAny(std::string v) : _kind(Kind::Text), _text(std::move(v)) {}
    MenhirAny(const MenhirObject &v);

    Kind GetKind() const { return _kind; }
    bool IsNone() const { return _kind == Kind::None; }
    bool IsInt() const { return _kind == Kind::Int; }
    bool IsFloat() const { return _kind == Kind::Float; }
    bool IsBool() const { return _kind == Kind::Bool; }
    bool IsVec3() const { return _kind == Kind::Vec3; }
    bool IsText() const { return _kind == Kind::Text; }
    bool IsObject() const { return _kind == Kind::Object; }

    /* Numbers convert between int, float and bool. */
    int Int() const { return _kind == Kind::Float ? (int)_float : _int; }
    float Float() const { return _kind == Kind::Float ? _float : (float)_int; }
    bool Bool() const { return _kind == Kind::Float ? _float != 0 : _int != 0; }
    MenhirVec3 Vec3() const { return _vec; }
    const char *Text() const { return _text.c_str(); }
    MenhirObject Object() const;

private:
    Kind _kind = Kind::None;
    int _int = 0;
    float _float = 0;
    MenhirVec3 _vec;
    std::string _text;
    mh_entity _object = MH_NONE;
};

/* A move, turn or scale in progress. Calls chain: obj.Move(...).Delay(1).Loop(-1, true). */
class MenhirTween {
public:
    MenhirTween() = default;
    explicit MenhirTween(mh_tween id) : _id(id) {}
    MenhirTween &Delay(float seconds) { mh_tween_delay(_id, seconds); return *this; }
    /* count plays in all, -1 forever; yoyo goes back and forth. */
    MenhirTween &Loop(int count, bool yoyo = false) { mh_tween_loop(_id, count, yoyo); return *this; }
    MenhirTween &OnComplete(std::function<void()> fn)
    {
        if (!_id)
            return *this;
        auto *task = menhir_detail::Adopt(new menhir_detail::Task{ std::move(fn) });
        task->tween = _id;
        mh_tween_on_complete(_id, menhir_detail::RunOnce, task);
        return *this;
    }
    void Stop() { mh_tween_stop(_id); }
    bool Running() const { return mh_tween_running(_id); }
    mh_tween Id() const { return _id; }

private:
    mh_tween _id = 0;
};

/* A timer of After or Every. */
class MenhirTimer {
public:
    MenhirTimer() = default;
    explicit MenhirTimer(menhir_detail::Task *task) : _id(task ? task->timer : 0) {}
    void Cancel() { mh_timer_cancel(_id); }

private:
    mh_timer _id = 0;
};

/* A component of an object: a script (or Rigidbody, BoxCollider, AudioSource...). */
class MenhirComponent {
public:
    MenhirComponent() = default;
    MenhirComponent(void *data, const mh_script_type *type) : _data(data), _type(type) {}
    explicit operator bool() const { return _data != nullptr; }
    const char *Name() const { return _type ? _type->name : ""; }
    void *Data() const { return _data; }
    template <class T> T *As() const { return static_cast<T *>(_data); }

private:
    void *_data = nullptr;
    const mh_script_type *_type = nullptr;
};

struct MenhirHit;

/* An object of the scene (a handle: copies refer to the same object). MenhirObject() is no object. */
class MenhirObject {
public:
    MenhirObject() = default;
    explicit MenhirObject(mh_entity id) : _id(id) {}

    mh_entity Id() const { return _id; }
    /* It exists (not destroyed, its scene loaded). */
    bool IsValid() const { return mh_is_alive(_id); }
    explicit operator bool() const { return IsValid(); }
    bool operator==(const MenhirObject &o) const { return _id == o._id; }
    bool operator!=(const MenhirObject &o) const { return _id != o._id; }
    const char *Name() const { return IsValid() ? mh_get_name(_id) : ""; }

    /* ---- Transform (positions in the world, rotations as Euler angles in degrees) ---- */
    MenhirPos Position() const { return mh_get_position(_id); }
    void SetPosition(MenhirPos p) const { mh_set_position(_id, p); }
    MenhirPos LocalPosition() const { return mh_get_local_position(_id); }
    void SetLocalPosition(MenhirPos p) const { mh_set_local_position(_id, p); }
    MenhirAngles Rotation() const { return mh_quat_to_euler(mh_get_rotation(_id)); }
    void SetRotation(MenhirAngles degrees) const { mh_set_rotation(_id, mh_quat_euler(degrees)); }
    MenhirAngles LocalRotation() const { return mh_quat_to_euler(mh_get_local_rotation(_id)); }
    void SetLocalRotation(MenhirAngles degrees) const { mh_set_local_rotation(_id, mh_quat_euler(degrees)); }
    MenhirScale Scale() const { return mh_get_local_scale(_id); }
    void SetScale(MenhirScale s) const { mh_set_local_scale(_id, s); }
    /* At once, along its own axes (world: the world's). */
    void Translate(MenhirVec3 by, bool world = false) const { mh_translate(_id, by, world); }
    void Turn(MenhirAngles degrees, bool world = false) const { mh_rotate(_id, degrees, world); }
    void LookAt(MenhirPos point) const { mh_look_at(_id, point); }
    void LookAt(const MenhirObject &target) const { mh_look_at(_id, target.Position()); }
    MenhirVec3 Forward() const { return mh_forward(_id); }
    MenhirVec3 Right() const { return mh_right(_id); }
    MenhirVec3 Up() const { return mh_up(_id); }
    float Distance(const MenhirObject &o) const { return Position().Distance(o.Position()); }

    /* ---- Over time: by an offset / Euler angles (360 is a full turn) / a factor, or to a value ---- */
    MenhirTween Move(MenhirStyle style, MenhirVec3 by, float seconds = 1) const
    { return MenhirTween(mh_tween_move(_id, by, seconds, (int32_t)style)); }
    MenhirTween MoveTo(MenhirStyle style, MenhirPos to, float seconds = 1) const
    { return MenhirTween(mh_tween_move_to(_id, to, seconds, (int32_t)style)); }
    MenhirTween Rotate(MenhirStyle style, MenhirAngles by, float seconds = 1) const
    { return MenhirTween(mh_tween_rotate(_id, by, seconds, (int32_t)style)); }
    MenhirTween RotateTo(MenhirStyle style, MenhirAngles to, float seconds = 1) const
    { return MenhirTween(mh_tween_rotate_to(_id, to, seconds, (int32_t)style)); }
    MenhirTween ScaleBy(MenhirStyle style, MenhirScale factor, float seconds = 1) const
    { return MenhirTween(mh_tween_scale(_id, factor, seconds, (int32_t)style)); }
    MenhirTween ScaleTo(MenhirStyle style, MenhirScale to, float seconds = 1) const
    { return MenhirTween(mh_tween_scale_to(_id, to, seconds, (int32_t)style)); }
    /* Degrees per second; call it every frame. */
    void Spin(MenhirAngles degrees_per_second) const { mh_spin(_id, degrees_per_second); }
    void StopTweens() const { mh_tween_stop_all(_id); }
    bool IsTweening() const { return mh_is_tweening(_id); }

    /* ---- Hierarchy, life ---- */
    MenhirObject Parent() const { return MenhirObject(mh_get_parent(_id)); }
    void SetParent(const MenhirObject &parent) const { mh_set_parent(_id, parent._id); }
    int ChildCount() const { return mh_child_count(_id); }
    MenhirObject Child(int index) const { return MenhirObject(mh_get_child(_id, index)); }
    MenhirObject FindChild(const char *name) const { return MenhirObject(mh_find_child(_id, name)); }
    std::vector<MenhirObject> Children() const
    {
        std::vector<MenhirObject> children;
        for (int i = 0, n = ChildCount(); i < n; i++)
            children.push_back(Child(i));
        return children;
    }
    bool Active() const { return mh_is_active(_id); }
    bool ActiveSelf() const { return mh_is_active_self(_id); }
    void SetActive(bool active) const { mh_set_active(_id, active); }
    /* At the end of the frame, with its children. */
    void Destroy() const { mh_destroy(_id); }
    /* A copy (with its children and components), here or at a position. */
    MenhirObject Clone() const { return MenhirObject(mh_instantiate(_id, MH_NONE)); }
    MenhirObject Spawn(MenhirPos at) const { return MenhirObject(mh_spawn(_id, at)); }

    /* ---- Components ---- */
    MenhirComponent Component(const char *type) const
    {
        const mh_script_type *t = nullptr;
        for (int i = 0;; i++) {
            void *data = mh_get_script_at(_id, i, &t);
            if (!data)
                return {};
            if (!std::strcmp(t->name, type))
                return { data, t };
        }
    }
    std::vector<MenhirComponent> Components() const
    {
        std::vector<MenhirComponent> all;
        const mh_script_type *t = nullptr;
        for (int i = 0; void *data = mh_get_script_at(_id, i, &t); i++)
            all.emplace_back(data, t);
        return all;
    }
    bool Has(const char *type) const { return (bool)Component(type); }
    /* Its script of that class, or nullptr: obj.Get<Enemy>()->health. */
    template <class T> T *Get() const { return Component(menhir_detail::TypeName<T>()).template As<T>(); }
    /* Calls OnMessage(name, value) on each of its scripts. */
    void Send(const char *name, const MenhirAny &value = {}) const;

    /* ---- Physics (a Rigidbody), audio (an AudioSource) ---- */
    void AddForce(MenhirVec3 newtons) const { mh_add_force(_id, newtons); }
    void AddImpulse(MenhirVec3 impulse) const { mh_add_impulse(_id, impulse); }
    MenhirVec3 Velocity() const { return mh_get_velocity(_id); }
    void SetVelocity(MenhirVec3 v) const { mh_set_velocity(_id, v); }
    bool IsTouching(const MenhirObject &other) const { return mh_is_touching(_id, other._id); }
    void Play() const { mh_audio_play(_id); }
    void StopSound() const { mh_audio_stop(_id); }
    void PauseSound() const { mh_audio_pause(_id); }
    bool IsPlaying() const { return mh_audio_is_playing(_id); }
    void SetVolume(float volume) const { mh_audio_set_volume(_id, volume); }
    void SetPitch(float pitch) const { mh_audio_set_pitch(_id, pitch); }

protected:
    mh_entity _id = MH_NONE;
};

/* What a collision or a trigger is with. */
using MenhirOther = MenhirObject;
/* A pointer to an object variable: MenhirReference r = &player; r->Move(...). */
using MenhirReference = MenhirObject *;

inline MenhirAny::MenhirAny(const MenhirObject &v) : _kind(Kind::Object), _object(v.Id()) {}
inline MenhirObject MenhirAny::Object() const { return MenhirObject(_object); }

struct MenhirHit {
    MenhirObject object;
    MenhirPos point, normal;
    float distance = 0;
};

/* ---- Coroutines: MenhirCoroutine Intro() { co_await Wait(1); ... } with StartCoroutine(Intro()) ---- */

class MenhirCoroutine {
public:
    struct promise_type {
        float wait = 0;
        std::function<bool()> until;
        MenhirCoroutine get_return_object() { return MenhirCoroutine(std::coroutine_handle<promise_type>::from_promise(*this)); }
        std::suspend_always initial_suspend() noexcept { return {}; }
        std::suspend_always final_suspend() noexcept { return {}; }
        void return_void() {}
        void unhandled_exception()
        {
            try {
                throw;
            } catch (const std::exception &e) {
                mh_log_error("A coroutine failed: %s", e.what());
            } catch (...) {
                mh_log_error("A coroutine failed.");
            }
        }
    };

    explicit MenhirCoroutine(std::coroutine_handle<promise_type> h) : _handle(h) {}
    MenhirCoroutine(MenhirCoroutine &&o) noexcept : _handle(std::exchange(o._handle, {})) {}
    MenhirCoroutine(const MenhirCoroutine &) = delete;
    ~MenhirCoroutine()
    {
        if (_handle)
            _handle.destroy();
    }
    std::coroutine_handle<promise_type> Release() { return std::exchange(_handle, {}); }

private:
    std::coroutine_handle<promise_type> _handle;
};

struct MenhirWait {
    float seconds;
    bool await_ready() const noexcept { return seconds <= 0; }
    void await_suspend(std::coroutine_handle<MenhirCoroutine::promise_type> h) const noexcept { h.promise().wait = seconds; }
    void await_resume() const noexcept {}
};

struct MenhirNextFrame {
    bool await_ready() const noexcept { return false; }
    void await_suspend(std::coroutine_handle<MenhirCoroutine::promise_type>) const noexcept {}
    void await_resume() const noexcept {}
};

struct MenhirUntil {
    std::function<bool()> condition;
    bool await_ready() const { return condition(); }
    void await_suspend(std::coroutine_handle<MenhirCoroutine::promise_type> h) { h.promise().until = condition; }
    void await_resume() const noexcept {}
};

/* ---- The base of the scripts ------------------------------------------------------------------------------ */

class MenhirEngine : public MenhirObject {
public:
    MenhirEngine() = default;
    MenhirEngine(const MenhirEngine &) = delete;
    MenhirEngine &operator=(const MenhirEngine &) = delete;
    ~MenhirEngine()
    {
        /* What it started stops with it. */
        for (menhir_detail::Task *task : _tasks) {
            if (task->timer)
                mh_timer_cancel(task->timer);
            if (task->tween)
                mh_tween_on_complete(task->tween, nullptr, nullptr);
            if (task->routine)
                task->routine.destroy();
            delete task;
        }
    }

    /* The object this script is on (the script is that object too: Move(...) moves it). */
    MenhirObject Object() const { return MenhirObject(_id); }

    /* ---- Objects ---- */
    /* The first object of the loaded scenes with that name (inactive ones too); no object when there is none. */
    static MenhirObject Find(const char *name) { return MenhirObject(mh_find(name)); }
    static MenhirObject MainCamera() { return MenhirObject(mh_main_camera()); }
    static MenhirObject Spawn(const MenhirObject &original, MenhirPos at) { return original.Spawn(at); }
    using MenhirObject::Spawn;
    static void Destroy(const MenhirObject &object) { object.Destroy(); }
    using MenhirObject::Destroy;

    /* ---- Time, timers, coroutines ---- */
    static MenhirTick DeltaTime() { return mh_delta_time(); }
    static MenhirTick FixedDeltaTime() { return mh_fixed_delta_time(); }
    static MenhirTick Time() { return mh_time(); }
    static int Frame() { return mh_frame_count(); }
    /* fn once, some seconds from now / every some seconds (0: every frame), while this script exists. */
    MenhirTimer After(float seconds, std::function<void()> fn) { return Schedule(seconds, std::move(fn), false); }
    MenhirTimer Every(float seconds, std::function<void()> fn) { return Schedule(seconds, std::move(fn), true); }
    /* Runs it now up to its first co_await, then on with the frames. */
    void StartCoroutine(MenhirCoroutine coroutine);
    void StopCoroutines()
    {
        for (menhir_detail::Task *task : _tasks)
            if (task->routine && !task->done) {
                task->done = true;
                mh_timer_cancel(task->timer);
            }
    }
    static MenhirWait Wait(float seconds) { return { seconds }; }
    static MenhirNextFrame NextFrame() { return {}; }
    static MenhirUntil Until(std::function<bool()> condition) { return { std::move(condition) }; }

    /* ---- Input ---- */
    static bool Key(const char *name) { return mh_key(name); }
    static bool KeyDown(const char *name) { return mh_key_down(name); }
    static bool KeyUp(const char *name) { return mh_key_up(name); }
    static bool Button(const char *name) { return mh_button(name); }
    static bool ButtonDown(const char *name) { return mh_button_down(name); }
    static bool ButtonUp(const char *name) { return mh_button_up(name); }
    static float Axis(const char *name) { return mh_axis(name); }
    static float AxisRaw(const char *name) { return mh_axis_raw(name); }
    static MenhirVec2 Mouse() { return mh_mouse_position(); }

    /* ---- Physics, audio ---- */
    static bool Raycast(MenhirPos origin, MenhirVec3 direction, float distance, MenhirHit &hit)
    {
        mh_hit h;
        if (!mh_raycast(origin, direction, distance, &h))
            return false;
        hit = MenhirHit{ MenhirObject(h.entity), h.point, h.normal, h.distance };
        return true;
    }
    static MenhirVec3 Gravity() { return mh_get_gravity(); }
    static void SetGravity(MenhirVec3 g) { mh_set_gravity(g); }
    static void PlaySound(const char *clip, float volume = 1) { mh_play_sound(clip, volume); }
    static void PlaySoundAt(const char *clip, MenhirPos at, float volume = 1) { mh_play_sound_at(clip, at, volume); }
    static void SetMasterVolume(float volume) { mh_set_master_volume(volume); }

    /* ---- Log, random, the game ---- */
    static void Log(const char *format, ...) __attribute__((format(printf, 1, 2)))
    {
        va_list args;
        va_start(args, format);
        mh_log("%s", menhir_detail::Format(format, args).c_str());
        va_end(args);
    }
    static void Warn(const char *format, ...) __attribute__((format(printf, 1, 2)))
    {
        va_list args;
        va_start(args, format);
        mh_log_warning("%s", menhir_detail::Format(format, args).c_str());
        va_end(args);
    }
    static void Error(const char *format, ...) __attribute__((format(printf, 1, 2)))
    {
        va_list args;
        va_start(args, format);
        mh_log_error("%s", menhir_detail::Format(format, args).c_str());
        va_end(args);
    }
    static float Random(float min, float max) { return mh_random_range(min, max); }
    /* min included, max excluded. */
    static int RandomInt(int min, int max) { return mh_random_int(min, max); }
    static void RandomSeed(uint32_t seed) { mh_random_seed(seed); }
    static bool LoadScene(const char *name, bool additive = false) { return mh_load_scene(name, additive ? MH_LOAD_ADDITIVE : MH_LOAD_SINGLE); }
    static void Quit() { mh_quit(); }

private:
    friend menhir_detail::Task *menhir_detail::Adopt(menhir_detail::Task *);
    friend void menhir_detail::Release(menhir_detail::Task *);
    template <class T> friend struct MenhirScript;

    MenhirTimer Schedule(float seconds, std::function<void()> fn, bool repeat)
    {
        auto *task = menhir_detail::Adopt(new menhir_detail::Task{ std::move(fn) });
        task->owner = this;
        task->timer = repeat ? mh_every(seconds, menhir_detail::RunRepeating, task, _id)
                             : mh_after(seconds, menhir_detail::RunOnce, task, _id);
        return MenhirTimer(task);
    }

    std::vector<menhir_detail::Task *> _tasks;
};

namespace menhir_detail {

inline Task *Adopt(Task *task)
{
    task->owner = current;
    if (current) {
        /* Ended tasks go when new ones come. */
        std::erase_if(current->_tasks, [](Task *t) {
            if (!t->done)
                return false;
            if (t->routine)
                t->routine.destroy();
            delete t;
            return true;
        });
        current->_tasks.push_back(task);
    }
    return task;
}

/* An ended task: freed now when no script owns it, else with its owner (or its next task). */
inline void Release(Task *task)
{
    if (!task->owner)
        delete task;
}

} // namespace menhir_detail

inline void MenhirEngine::StartCoroutine(MenhirCoroutine coroutine)
{
    std::coroutine_handle<MenhirCoroutine::promise_type> handle = coroutine.Release();
    if (!handle)
        return;
    menhir_detail::Current running(this);
    handle.resume();
    if (handle.done()) {
        handle.destroy();
        return;
    }
    auto *task = menhir_detail::Adopt(new menhir_detail::Task{});
    task->owner = this;
    task->routine = handle;
    /* Every frame, after the updates: the waits count down, then it goes on. */
    task->fn = [this, task, handle] {
        MenhirCoroutine::promise_type &p = handle.promise();
        if (p.until) {
            if (!p.until())
                return;
            p.until = nullptr;
        } else if (p.wait > 0) {
            p.wait -= mh_delta_time();
            if (p.wait > 0)
                return;
            p.wait = 0;
        }
        menhir_detail::Current again(this);
        handle.resume();
        if (handle.done()) {
            task->done = true;
            mh_timer_cancel(task->timer);
        }
    };
    task->timer = mh_every(0, menhir_detail::RunRepeating, task, _id);
}

/* ---- Registration (the editor's generated copy writes MENHIR_REGISTER(Class, fields)) ---------------------- */

namespace menhir_detail {

using MessageHandler = void (*)(void *script, const char *name, const MenhirAny &value);

inline std::unordered_map<std::string, MessageHandler> &MessageHandlers()
{
    static std::unordered_map<std::string, MessageHandler> handlers;
    return handlers;
}

} // namespace menhir_detail

inline void MenhirObject::Send(const char *name, const MenhirAny &value) const
{
    std::vector<MenhirComponent> components = Components();
    for (const MenhirComponent &c : components) {
        auto found = menhir_detail::MessageHandlers().find(c.Name());
        if (found != menhir_detail::MessageHandlers().end())
            found->second(c.Data(), name, value);
    }
}

/* The engine's callbacks for a script class T: each one calls T's method when T has it. */
template <class T> struct MenhirScript {
    static T *Of(void *self) { return static_cast<T *>(self); }

    template <class F> static void Guard(void *self, const char *callback, F &&f)
    {
        menhir_detail::Current running(Of(self));
        try {
            f(Of(self));
        } catch (const std::exception &e) {
            mh_log_error("%s.%s failed: %s", menhir_detail::TypeName<T>(), callback, e.what());
        } catch (...) {
            mh_log_error("%s.%s failed.", menhir_detail::TypeName<T>(), callback);
        }
    }

    static inline const mh_field *fields = nullptr;
    static inline int32_t field_count = 0;

    /* The engine set the fields (defaults, then the scene's): T is constructed around them. */
    static void Awake(void *self, mh_entity entity)
    {
        std::vector<unsigned char> saved((size_t)field_count * 16);
        for (int32_t i = 0; i < field_count; i++)
            std::memcpy(&saved[(size_t)i * 16], (char *)self + fields[i].offset, Size(fields[i]));
        try {
            new (self) T();
        } catch (const std::exception &e) {
            mh_log_error("%s could not be created: %s", menhir_detail::TypeName<T>(), e.what());
            std::terminate();
        }
        for (int32_t i = 0; i < field_count; i++)
            std::memcpy((char *)self + fields[i].offset, &saved[(size_t)i * 16], Size(fields[i]));
        Of(self)->_id = entity;
        if constexpr (requires(T t) { t.OnAwake(); })
            Guard(self, "OnAwake", [](T *s) { s->OnAwake(); });
    }

    static void Start(void *self, mh_entity)
    {
        if constexpr (requires(T t) { t.OnLoad(); })
            Guard(self, "OnLoad", [](T *s) { s->OnLoad(); });
        if constexpr (requires(T t) { t.OnStart(); })
            Guard(self, "OnStart", [](T *s) { s->OnStart(); });
    }

    static void Update(void *self, mh_entity)
    {
        if constexpr (requires(T t) { t.OnUpdate(0.0f); })
            Guard(self, "OnUpdate", [](T *s) { s->OnUpdate(mh_delta_time()); });
        else if constexpr (requires(T t) { t.OnUpdate(); })
            Guard(self, "OnUpdate", [](T *s) { s->OnUpdate(); });
    }

    static void LateUpdate(void *self, mh_entity)
    {
        if constexpr (requires(T t) { t.OnLateUpdate(0.0f); })
            Guard(self, "OnLateUpdate", [](T *s) { s->OnLateUpdate(mh_delta_time()); });
        else if constexpr (requires(T t) { t.OnLateUpdate(); })
            Guard(self, "OnLateUpdate", [](T *s) { s->OnLateUpdate(); });
    }

    static void FixedUpdate(void *self, mh_entity)
    {
        if constexpr (requires(T t) { t.OnFixedUpdate(0.0f); })
            Guard(self, "OnFixedUpdate", [](T *s) { s->OnFixedUpdate(mh_fixed_delta_time()); });
        else if constexpr (requires(T t) { t.OnFixedUpdate(); })
            Guard(self, "OnFixedUpdate", [](T *s) { s->OnFixedUpdate(); });
    }

    static void GameClose(void *self, mh_entity)
    {
        if constexpr (requires(T t) { t.OnGameClose(0.0f); })
            Guard(self, "OnGameClose", [](T *s) { s->OnGameClose(mh_time()); });
        else if constexpr (requires(T t) { t.OnGameClose(); })
            Guard(self, "OnGameClose", [](T *s) { s->OnGameClose(); });
    }

    /* OnDestroy, then T goes (the engine frees the memory). */
    static void Destroy(void *self, mh_entity)
    {
        if constexpr (requires(T t) { t.OnDestroy(); })
            Guard(self, "OnDestroy", [](T *s) { s->OnDestroy(); });
        Of(self)->~T();
    }

    template <int Kind> static void Contact(void *self, mh_entity, mh_entity other)
    {
        MenhirOther o(other);
        if constexpr (Kind == 0 && requires(T t, MenhirOther x) { t.OnCollisionEnter(x); })
            Guard(self, "OnCollisionEnter", [&](T *s) { s->OnCollisionEnter(o); });
        if constexpr (Kind == 1 && requires(T t, MenhirOther x) { t.OnCollisionExit(x); })
            Guard(self, "OnCollisionExit", [&](T *s) { s->OnCollisionExit(o); });
        if constexpr (Kind == 2 && requires(T t, MenhirOther x) { t.OnTriggerEnter(x); })
            Guard(self, "OnTriggerEnter", [&](T *s) { s->OnTriggerEnter(o); });
        if constexpr (Kind == 3 && requires(T t, MenhirOther x) { t.OnTriggerExit(x); })
            Guard(self, "OnTriggerExit", [&](T *s) { s->OnTriggerExit(o); });
    }

    static void Message(void *self, const char *name, const MenhirAny &value)
    {
        if constexpr (requires(T t, const MenhirAny &v) { t.OnMessage("", v); })
            Guard(self, "OnMessage", [&](T *s) { s->OnMessage(name, value); });
    }

    static size_t Size(const mh_field &f)
    {
        switch (f.kind) {
        case MH_FIELD_BOOL: return 1;
        case MH_FIELD_VEC2: return 8;
        case MH_FIELD_VEC3: return 12;
        case MH_FIELD_COLOR: return 16;
        case MH_FIELD_STRING: return sizeof(const char *);
        default: return 4;
        }
    }

    static const mh_script_type *Type(const char *name, const mh_field *f, int32_t count)
    {
        fields = f;
        field_count = count;
        static mh_script_type type{};
        type.name = name;
        type.size = (uint32_t)sizeof(T);
        type.fields = f;
        type.field_count = count;
        type.awake = Awake;
        type.start = Start;
        type.update = Update;
        type.late_update = LateUpdate;
        type.fixed_update = FixedUpdate;
        type.on_destroy = Destroy;
        type.on_game_close = GameClose;
        type.on_collision_enter = Contact<0>;
        type.on_collision_exit = Contact<1>;
        type.on_trigger_enter = Contact<2>;
        type.on_trigger_exit = Contact<3>;
        menhir_detail::MessageHandlers()[name] = Message;
        return &type;
    }
};

#define MENHIR__CAT2(a, b) a##b
#define MENHIR__CAT(a, b) MENHIR__CAT2(a, b)
/* Registers the script class T (the editor's generated copy writes it after the file). */
#define MENHIR_REGISTER(T, fields, count)                                                              \
    static const bool MENHIR__CAT(T, __menhir_registered) =                                            \
        (mh_register_script(MenhirScript<T>::Type(#T, fields, count)), true)

#endif
