# Scripting

Games are scripted in C or C++. The `.c` and `.cpp` files in a project's `Assets/` folder are its scripts
(with any `.h` and `.hpp` files they include). There are three ways to write one:

- **C++, in the style of Unity** (**Project → Create → C++ Script**): a class deriving from `MenhirEngine`,
  with `OnLoad()`, `OnUpdate(MenhirTick dt)`... ([below](#c-scripts)).
- **The simple C syntax** (**Project → Create → C Script**): one file is one script, named after the file;
  its variables go in `MH_VARS`, and each callback is written like a function, `MH_START() { ... }`.
- **The classic C syntax**: a struct, its functions and `MH_SCRIPT(...)` to declare them. Several scripts can
  share a file. The simple syntax is made of macros over the classic API, and the C++ one is classes over it:
  all three share the same engine, runtime, physics and audio, and can be mixed in a project.

Everything comes from `menhir.h` and `menhir_simple.h` ([Engine/Scripting/include](../Engine/Scripting/include)).
The compiler includes `menhir.h` before every script by itself, so no `#include` line is needed.

```c
MH_CREATE Player.c

MH_VARS {
    float speed = 90;               // MH_RANGE(0, 360) Degrees per second
    MH_VEC3 jump = MH_VEC3(0, 5, 0);
    MH_GAMEOBJECT lamp;             // kept for the script, not shown in the inspector
    int coins;                      // MH_HIDE
};

MH_START() {
    self.lamp = MH_SCENE.Lamp;      // the object named "Lamp" (MH_NULL_OBJECT if there is none)
    MH_ROTATE(MH_PARENT, MH_VEC3(0, 90, 0), 1.0f, MH_TWEEN_TYPE(MH_EASE_OUT));
}

MH_UPDATE() {
    MH_SPIN(self.lamp, MH_VEC3(0, self.speed, 0));
    if (MH_KEY_DOWN("space"))
        MH_ADD_IMPULSE(MH_PARENT, self.jump);
}

MH_ON_COLLISION_ENTER(other) {
    if (!strcmp(MH_NAME(other), "Coin")) {
        self.coins++;
        MH_DESTROY(other);
        MH_PLAY_SOUND("Sounds/coin.wav");
    }
}
```

The same code runs in two places:

- **Play mode**: the editor compiles the scripts into a shared library (`Cache/Scripts/GameScripts.so`,
  `.dll` on Windows) and loads a fresh copy of it at each Play. The scripts run inside the editor, so a
  crash in a script (a bad pointer) closes the editor.
- **The built game**: File → Build compiles them with the player.

Both use the C compiler of **Project Settings → Build** (GCC or Clang) with the same flags, so a script
computes exactly the same thing in both. The editor's tests check it: `PlayerScriptTests` runs the same
scripts in Play mode and in the built game with the same input, and compares the log word for word and
every entity's transform (physics, tweens, timers and coroutines included).

## C++ scripts

```cpp
class Player : public MenhirEngine {
public:
    float speed = 90;               // MH_RANGE(0, 360) Degrees per second
    MenhirPos jump { 0, 5, 0 };
    MenhirStyle style = MenhirStyle::EaseOut;

    void OnLoad() {
        Rotate(style, MenhirAngles { 0, 90, 0 }, 1.0f).OnComplete([this] { Log("%s turned", Name()); });
        Every(1.0f, [this] { coins++; });
        StartCoroutine(Intro());
    }

    void OnUpdate(MenhirTick dt) {
        if (KeyDown("space"))
            AddImpulse(jump);
        Find("Lamp").Spin({ 0, speed, 0 });
    }

    void OnCollisionEnter(MenhirOther other) {
        if (!std::strcmp(other.Name(), "Coin")) {
            other.Destroy();
            PlaySound("Sounds/coin.wav");
        }
    }

    MenhirCoroutine Intro() {
        Log("3...");
        co_await Wait(1);
        co_await Until([] { return KeyDown("space"); });
        Find("Enemy").Spawn(MenhirPos { 0, 0, 10 });
    }

private:
    int coins = 0;                  // private: the script's own, not a field
};
```

Every class of a `.cpp` file deriving from `MenhirEngine` is a script, named after the class. The script
**is** the object it is on: `Move(...)`, `Position()`, `Name()`, `Destroy()` act on it (`Object()` gives it as
a `MenhirObject`). Its **public** variables of these types are fields (the inspector shows them, scenes save
them), with their default from their initializer: `int`, `float`, `bool`, `MenhirVec2`, `MenhirPos` /
`MenhirVec3` / `MenhirAngles` / `MenhirScale`, `MenhirColor`, `MenhirText` (a `const char *` the engine owns),
`MenhirStyle` (a drop-down). The comment after one gives `MH_RANGE(min, max)`, `MH_HIDE` and its tooltip.
Other members are the script's own (constructed as written: `std::vector`, `std::string`... all work).

**Callbacks**, all optional: `OnAwake()`, `OnLoad()` (or `OnStart()`), `OnUpdate(MenhirTick dt)`,
`OnLateUpdate(MenhirTick dt)`, `OnFixedUpdate(MenhirTick dt)`, `OnDestroy()`, `OnGameClose(MenhirTick time)`,
`OnCollisionEnter(MenhirOther other)` / `OnCollisionExit`, `OnTriggerEnter(MenhirOther other)` / `OnTriggerExit`,
`OnMessage(const char *name, MenhirAny value)` (from `obj.Send("Hit", 10)`). The ones taking a time can also
be written without it (`void OnUpdate()`). An exception thrown in a callback is logged, not fatal.

**Types**

| | |
|-|-|
| `MenhirObject` | An object of the scene, a handle (copies are the same object); `MenhirObject()` or a destroyed one is not valid (`if (obj)`, `obj.IsValid()`) |
| `MenhirReference` | A pointer to an object variable: `MenhirReference r = &player; r->Move(...)` |
| `MenhirOther` | The other object of a collision or a trigger (a `MenhirObject`) |
| `MenhirComponent` | A component of an object: `obj.Component("Rigidbody")`, `.Name()`, `.As<T>()` |
| `MenhirAny` | An int, float, bool, vector, text or object: `MenhirAny v = 10; v.Int()`, `v.IsText()` |
| `MenhirPos` (`MenhirVec3`, `MenhirAngles`, `MenhirScale`), `MenhirVec2`, `MenhirColor` | With `+ - * /`, `Dot`, `Cross`, `Length`, `Normalized`, `Distance`, `Lerp`, `MenhirVec3::Up()`, `Zero()`, `Forward()`... |
| `MenhirStyle` | `Linear`, `EaseIn`, `EaseOut`, `EaseInOut`, `BackIn`, `BackOut`, `Elastic`, `Bounce` |
| `MenhirTick` | Seconds (a float) |
| `MenhirTween`, `MenhirTimer`, `MenhirCoroutine`, `MenhirHit` | Below |

**MenhirObject** (and so the script itself)

| | |
|-|-|
| Transform | `Position()` / `SetPosition(p)` (world), `LocalPosition()` / `SetLocalPosition`, `Rotation()` / `SetRotation(degrees)`, `LocalRotation` / `SetLocalRotation`, `Scale()` / `SetScale`, `Translate(by, world = false)`, `Turn(degrees, world = false)`, `LookAt(point or object)`, `Forward()`, `Right()`, `Up()`, `Distance(other)` |
| Over time | `Move(style, by, seconds = 1)`, `MoveTo(style, position, seconds)`, `Rotate(style, degrees, seconds)` (360 is a full turn), `RotateTo`, `ScaleBy(style, factor, seconds)`, `ScaleTo`, each giving a `MenhirTween`: `.Delay(s)`, `.Loop(count, yoyo)` (-1 forever), `.OnComplete([this] { ... })`, `.Stop()`, `.Running()`; `Spin(degrees per second)` every frame, `StopTweens()`, `IsTweening()` |
| Hierarchy, life | `Parent()`, `SetParent(obj)`, `ChildCount()`, `Child(i)`, `Children()`, `FindChild("Lid")`, `Active()`, `ActiveSelf()`, `SetActive(bool)`, `Destroy()` (end of the frame), `Clone()`, `Spawn(position)` |
| Components | `Component("Name")`, `Components()`, `Has("Rigidbody")`, `Get<Enemy>()` (its script of that class, or nullptr), `Send("message", value)` |
| Physics, audio | `AddForce`, `AddImpulse`, `Velocity()`, `SetVelocity`, `IsTouching(other)`; `Play()`, `StopSound()`, `PauseSound()`, `IsPlaying()`, `SetVolume`, `SetPitch` (its AudioSource) |
| Other | `Name()`, `Id()`, `IsValid()`, `==` |

**MenhirEngine** (inside a script)

| | |
|-|-|
| Objects | `Find("Lamp")` (no object when there is none), `MainCamera()`, `Spawn(original, position)`, `Destroy(obj)` |
| Time | `DeltaTime()`, `FixedDeltaTime()`, `Time()`, `Frame()` |
| Timers | `After(seconds, [this] { ... })`, `Every(seconds, [this] { ... })` (0: every frame), giving a `MenhirTimer` (`.Cancel()`); they stop with the script |
| Coroutines | A method returning `MenhirCoroutine`, started with `StartCoroutine(Intro())`: `co_await Wait(seconds)`, `co_await NextFrame()`, `co_await Until([] { return ...; })`; its local variables keep their values; `StopCoroutines()` |
| Input | `Key`, `KeyDown`, `KeyUp` (`"space"`, `"a"`, `"mouse 0"`...), `Button`, `ButtonDown`, `ButtonUp`, `Axis`, `AxisRaw`, `Mouse()` |
| Physics | `Raycast(origin, direction, distance, hit)` (a `MenhirHit`: `object`, `point`, `normal`, `distance`), `Gravity()`, `SetGravity` |
| Audio | `PlaySound("Sounds/coin.wav", volume = 1)`, `PlaySoundAt(clip, position)`, `SetMasterVolume` |
| The game | `Log`, `Warn`, `Error` (printf-style), `Random(min, max)`, `RandomInt(min, max)`, `RandomSeed`, `LoadScene("Level2", additive = false)`, `Quit()` |

The C API (`mh_...`) is there too. The scripts are compiled as C++20 with the C++ compiler that goes with
the C one of **Project Settings → Build** (`g++` for `gcc`, `clang++` for `clang`); on Windows the C++
library goes inside the game. The generated copy (`Cache/Scripts/Generated/<file>.gen.cpp`) includes the file
and adds the fields and `MENHIR_REGISTER(Class, fields, count)` for each class.

## The simple syntax

**`MH_VARS { ... };`** holds the script's variables, one copy per object that has the script. Inside the
callbacks they are `self.name`. A variable can have a default (`float speed = 90;`). Those of these types
are **fields**: the inspector shows them and scenes save their values.

| Type | Inspector |
|------|-----------|
| `int` | Number |
| `float` | Number (a slider with `MH_RANGE(min, max)`) |
| `bool` | Checkbox |
| `MH_VEC2`, `MH_VEC3` (`{x, y, z}` or `MH_VEC3(x, y, z)`) | Vector |
| `MH_COLOR` (`MH_COLOR(r, g, b, a)`, linear) | Color picker |
| `MH_STRING` (`"text"`) | Text |

The comment after a field gives its options: `MH_RANGE(min, max)`, `MH_HIDE` (not a field) and, for the
rest of the comment, its tooltip. Other variables (`MH_GAMEOBJECT`, arrays, pointers, `MH_HIDE` ones) are
the script's own: they start at their default, or zero (`MH_NULL_OBJECT` for an `MH_GAMEOBJECT`).

**Callbacks.** Write the body right after them; in it, `self` is the script's variables and `MH_PARENT` the
object the script is on.

| | When |
|-|------|
| `MH_AWAKE()` | The object is created (scene loaded, or spawned), before any start |
| `MH_START()` | The first thing on load: once, before the object's first update |
| `MH_UPDATE()` | Every frame |
| `MH_LATE_UPDATE()` | Every frame, after every update and the built-in components |
| `MH_FIXED_UPDATE(dt)` | Every fixed step (0.02 s, `dt`), before the physics |
| `MH_ON_GAME_CLOSE(time)` | The game is closing (`time`: how long it ran, in seconds) |
| `MH_ON_DESTROY()` | The object is destroyed, or its scene unloaded |
| `MH_ON_COLLISION_ENTER(other)` / `_EXIT` | Its collider starts / stops touching another one |
| `MH_ON_TRIGGER_ENTER(other)` / `_EXIT` | It enters / leaves a trigger, or something enters / leaves its trigger |

**What the editor adds.** The compiler gets a generated copy of the file (`Cache/Scripts/Generated/`):
`MH_SCRIPT_NAME` and `menhir_simple.h` first, `MH_VARS`' defaults moved into the fields, then at the end
`MH_REGISTER(...)` with the callbacks the file has, and an empty `MH_ON_GAME_CLOSE(MH_UNUSED_CMP) { }` when it
has none. Write `MH_REGISTER(MH_ENTRYPOINT_ONLOAD, MH_ONEVERYFRAME_UPDATE, MH_ONEVERYFRAME_FIX_UP, MH_ONGAMECLOSE, ...)`
yourself to choose which callbacks the engine calls. `MH_CREATE Player.c` (or `MH_CREATE(Player)`) is
optional and must name the file. `#line` directives keep the compiler's messages on your file's lines.

Words to know: `self` is reserved inside a script. `MH_GAMEOBJECT` is an object (a handle, not a copy), and
`MH_REFERENCE *p = &object;` a pointer to one: every `MH_...` macro takes either. `MH_SCENE.Name` is the
object of that name in the loaded scenes (`MH_SCENE.Main_Camera` finds "Main Camera"),
`MH_CATCH_SCENE(object)` gives `MH_NULL_OBJECT` for an object that is gone, and `MH_IS_NULL(object)` says
whether it is. `MH_FIND_COMPONENT(object, Rigidbody)` is a component of the object (`MH_ANY_COMPONENT` for
the first), as a pointer to its struct.

**Timers and coroutines.** `MH_TIMER(name) { ... }` is a function `MH_AFTER(seconds, name)` and
`MH_EVERY(seconds, name)` call (and `MH_ON_COMPLETE(tween, name)`). `MH_COROUTINE(name) { ... }` runs with
`MH_START_COROUTINE(name)` up to its first wait: `MH_WAIT(seconds)`, `MH_WAIT_FRAME()`,
`MH_WAIT_UNTIL(condition)`, `MH_STOP()`. A coroutine's local variables do not keep their value across a
wait (keep what must last in `self`), and a line has at most one wait. Timers, coroutines and tweens stop
when their object is destroyed.

**The rest of the words**, each a macro over the API below:

| | |
|-|-|
| Objects | `MH_SPAWN(object, position)`, `MH_INSTANTIATE(object)`, `MH_DESTROY`, `MH_SET_ACTIVE(object, bool)`, `MH_IS_ACTIVE`, `MH_FIND("name")`, `MH_NAME`, `MH_GET_PARENT`, `MH_SET_PARENT`, `MH_CHILD_COUNT`, `MH_GET_CHILD`, `MH_FIND_CHILD(object, "name")` |
| Transform | `MH_GET_POSITION` / `MH_SET_POSITION` (world), `MH_GET_LOCAL_POSITION` / `MH_SET_LOCAL_POSITION`, `MH_GET_ROTATION` / `MH_SET_ROTATION` (Euler degrees), `MH_GET_SCALE` / `MH_SET_SCALE`, `MH_TRANSLATE`, `MH_TURN`, `MH_LOOK_AT`, `MH_FORWARD`, `MH_RIGHT`, `MH_UP`, `MH_DISTANCE(a, b)` |
| Tweens | `MH_ROTATE(object, degrees, seconds, MH_TWEEN_TYPE(ease))` (by Euler angles: 360 is a full turn), `MH_MOVE` (by an offset), `MH_SCALE` (by a factor), `MH_ROTATE_TO`, `MH_MOVE_TO`, `MH_SCALE_TO`, `MH_TWEEN_FLOAT(self.alpha, to, seconds, ease)`; `MH_TWEEN_DELAY`, `MH_TWEEN_LOOP(tween, count, yoyo)` (-1: forever), `MH_ON_COMPLETE`, `MH_STOP_TWEEN`, `MH_STOP_TWEENS(object)`, `MH_IS_TWEENING`; `MH_SPIN(object, degrees per second)` every frame. Curves: `MH_LINEAR`, `MH_EASE_IN`, `MH_EASE_OUT`, `MH_EASE_IN_OUT`, `MH_BACK_IN`, `MH_BACK_OUT`, `MH_ELASTIC`, `MH_BOUNCE` |
| Physics | `MH_ADD_FORCE`, `MH_ADD_IMPULSE`, `MH_GET_VELOCITY`, `MH_SET_VELOCITY`, `MH_RAYCAST(origin, direction, distance, &hit)` (an `MH_HIT`: `entity`, `point`, `normal`, `distance`), `MH_IS_TOUCHING(a, b)` |
| Audio | `MH_PLAY_SOUND("Sounds/hit.wav")`, `MH_PLAY_SOUND_AT(clip, position)`, `MH_PLAY(object)` / `MH_STOP_SOUND` / `MH_IS_PLAYING` (its AudioSource) |
| Input, time | `MH_KEY`, `MH_KEY_DOWN`, `MH_KEY_UP`, `MH_BUTTON`, `MH_BUTTON_DOWN`, `MH_AXIS`, `MH_MOUSE()`, `MH_DELTA_TIME()`, `MH_TIME()`, `MH_RANDOM(min, max)` |
| The game | `MH_LOG(...)`, `MH_WARN`, `MH_ERROR` (printf-style), `MH_LOAD_SCENE("name")`, `MH_QUIT()` |
| Types | `MH_GAMEOBJECT`, `MH_REFERENCE`, `MH_VEC2`, `MH_VEC3`, `MH_COLOR`, `MH_QUAT`, `MH_STRING`, `MH_TWEEN`, `MH_HIT`, `MH_TIMESTAMP`; `MH_VEC3(x, y, z)` also makes one |

## Built-in components

These come with the scripting runtime (they are script types of its own), so they are in **Add Component**
even in a project without scripts (a C compiler is still needed):

| | Fields |
|-|--------|
| **Box Collider** | `center`, `size`, `is_trigger` (overlaps without pushing: trigger callbacks) |
| **Sphere Collider** | `center`, `radius`, `is_trigger` |
| **Rigidbody** | `mass`, `use_gravity`, `is_kinematic` (moved by scripts only, pushes the others), `drag`, `bounciness`, `friction`, `velocity` (at the start) |
| **Audio Source** | `clip` (a WAV, MP3 or FLAC file of Assets, e.g. `Sounds/music.mp3`), `volume`, `pitch`, `loop`, `play_on_awake`, `spatial` (heard from the main camera), `min_distance`, `max_distance` |

Colliders follow their object's position, rotation (boxes are oriented) and scale; a collider on a child of
a rigidbody moves with it. Something needs a rigidbody to collide or trigger. Rigidbodies fall (gravity
−9.81 on Y, `mh_set_gravity`), are pushed out of the others and bounce, but do not turn from collisions.
Raycasts skip triggers. The game copies the sound files of Assets next to itself (`Assets/`); without an
audio device the game runs silent (one warning).

## The classic syntax

```c
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
```

`MH_SCRIPT(T, ...)` declares the struct `T` as a script named `T`. After the name, in any order:
`MH_FIELDS(array)`, `MH_AWAKE(fn)`, `MH_START(fn)`, `MH_UPDATE(fn)`, `MH_LATE_UPDATE(fn)`, `MH_FIXED_UPDATE(fn)`,
`MH_ON_DESTROY(fn)`, `MH_ON_GAME_CLOSE(fn)`, each a `void fn(T *self, mh_entity entity)`, and
`MH_ON_COLLISION_ENTER(fn)`, `_EXIT`, `MH_ON_TRIGGER_ENTER(fn)`, `_EXIT`, each a
`void fn(T *self, mh_entity entity, mh_entity other)`. The engine allocates the struct (zeroed), sets the
fields to their default, then to the scene's values.

**Fields.** Each one names the struct, the field and its default, then options:

| Macro | C type | Inspector |
|-------|--------|-----------|
| `MH_INT(T, f, default)` | `int32_t` | Number |
| `MH_FLOAT(T, f, default)` | `float` | Number (slider with `MH_RANGE`) |
| `MH_BOOL(T, f, default)` | `bool` | Checkbox |
| `MH_VEC2(T, f, x, y)` / `MH_VEC3(T, f, x, y, z)` | `mh_vec2` / `mh_vec3` | Vector |
| `MH_COLOR(T, f, r, g, b, a)` | `mh_color` (linear) | Color picker |
| `MH_STRING(T, f, "default")` | `const char *` (the engine owns the text) | Text |
| `MH_ENUM(T, f, default, MH_OPTIONS("A", "B"))` | `int32_t` (the option's index) | Drop-down |

Options: `MH_RANGE(min, max)` (a slider), `MH_TOOLTIP("...")`. Fields that are not listed are the
script's own state, set to zero. A saved value that no longer fits its field (its type changed) is left out
with a warning. Coroutines by hand: `bool fn(T *self, mh_entity e, mh_coroutine *co) { MH_CO_BEGIN(co); ...
MH_CO_WAIT(co, 1.0f); ... MH_CO_END(co); }`, started with `mh_start_coroutine(fn, self, e)`.

## Each frame

1. The input devices are read (the Input Manager, [Input.md](Input.md)); the time moves on.
2. Start for the scripts not started yet.
3. The fixed steps due (every 0.02 s of game time, at most 0.25 s caught up): FixedUpdate, then the physics
   (bodies move, collide, and the collision and trigger callbacks run).
4. Update.
5. The runtime: timers, coroutines, tweens, the audio listener.
6. The built-in components: Fly Camera, Scene Switcher.
7. Late update.
8. The objects destroyed during the frame go (with their children), after their OnDestroy.
9. The scene change asked for during the frame (`mh_load_scene`), if any: the new scene's scripts are
   created and get their awake then, and their start on the next frame.

Scripts run in the order of the loaded scenes, then of the entities in their scene (spawned ones at the
end), then of the components on the entity. Inactive objects (and their children) are not drawn and their
scripts do not run. A spawned object's scripts get their fields' values from the original (their other
variables start over), Awake at once and Start on the next frame. When the game closes (Esc, the window,
`mh_quit`, Play mode stopped): OnGameClose, then OnDestroy for everything.

## API

| | |
|-|-|
| Transform | `mh_get_position` / `mh_set_position`, `mh_get_rotation` / `mh_set_rotation` (in the world), `mh_get_local_position`, `_rotation`, `_scale` and their `mh_set_…`, `mh_get_lossy_scale`, `mh_forward`, `mh_right`, `mh_up`, `mh_translate(e, v, world)`, `mh_rotate(e, degrees, world)`, `mh_look_at(e, point)` |
| Entities | `mh_get_name`, `mh_get_parent` (`MH_NONE` for none), `mh_find(name)` (the first entity of the loaded scenes with that name), `mh_get_script(e, "Type")` (that entity's script struct, or NULL), `mh_get_script_at(e, i, &type)`, `mh_is_alive`, `mh_instantiate(e, parent)`, `mh_spawn(e, position)`, `mh_destroy` (at the end of the frame), `mh_set_active`, `mh_is_active`, `mh_is_active_self`, `mh_child_count`, `mh_get_child`, `mh_find_child`, `mh_set_parent`, `mh_main_camera()`, `mh_asset_path("Sounds/a.wav")` |
| Input | `mh_axis`, `mh_axis_raw`, `mh_button`, `mh_button_down`, `mh_button_up` by Input Manager name; `mh_key`, `mh_key_down`, `mh_key_up` by key name (`"space"`, `"a"`, `"left shift"`, `"mouse 0"`, `"joystick button 0"`…); `mh_mouse_position()` (pixels from the bottom-left corner of the window or Game panel) |
| Time | `mh_delta_time()`, `mh_fixed_delta_time()`, `mh_time()`, `mh_frame_count()` |
| Tweens | `mh_tween_move`, `_rotate`, `_scale` (relative), `mh_tween_move_to`, `_rotate_to`, `_scale_to`, `mh_tween_float`; `mh_tween_delay`, `mh_tween_loop`, `mh_tween_on_complete`, `mh_tween_stop`, `mh_tween_stop_all`, `mh_tween_running`, `mh_is_tweening`, `mh_spin`, `mh_ease(curve, t)` |
| Timers, coroutines | `mh_after`, `mh_every`, `mh_timer_cancel`; `mh_start_coroutine`, `mh_stop_coroutine`, `mh_stop_all_coroutines` |
| Physics | `mh_raycast`, `mh_get_velocity`, `mh_set_velocity`, `mh_add_force`, `mh_add_impulse`, `mh_get_gravity`, `mh_set_gravity`, `mh_is_touching` |
| Audio | `mh_play_sound(clip, volume)`, `mh_play_sound_at`, `mh_audio_play`, `_stop`, `_pause`, `_is_playing`, `_set_volume`, `_set_pitch` (an object's AudioSource), `mh_set_master_volume` |
| Scenes | `mh_load_scene(name, MH_LOAD_SINGLE / MH_LOAD_ADDITIVE)`, `mh_load_scene_index`, `mh_quit()` (in Play mode: back to Edit mode) |
| Log | `mh_log`, `mh_log_warning`, `mh_log_error`, printf-style (the Console; the game's stdout, stderr for warnings and errors) |
| Random | `mh_random_seed`, `mh_random_value()` (0 to 1), `mh_random_range(min, max)`, `mh_random_int(min, max)` (max excluded); seeded from the clock when the game starts |
| Math | `mh_vec2`, `mh_vec3`, `mh_color`, `mh_quat`; `mh_v3`, `mh_v3_add`, `_sub`, `_scale`, `_mul`, `_dot`, `_cross`, `_length`, `_distance`, `_normalize`, `_lerp`; `mh_quat_identity`, `_mul`, `_inverse`, `_normalize`, `_rotate` (a vector), `_euler` (degrees), `_to_euler`, `_axis_angle`, `_look`, `_slerp`; `mh_clamp`, `mh_lerp`, `mh_move_towards`, `MH_PI`, `MH_DEG2RAD`, `MH_RAD2DEG` |

Conventions: right-handed, Y up (forward is −Z), like the editor; angles in degrees; Euler angles apply Z,
then X, then Y, like Unity. An `mh_entity` is a handle, valid while its object exists: using a stale one
logs an error and does nothing (`mh_is_alive` tells). A wrong key name logs an error. The C standard library
is all yours (`<string.h>`, `<stdlib.h>`, `<math.h>`…).

## How it fits together

| | |
|-|-|
| `Engine/Scripting/include/menhir.h` | What scripts use: the declarations, math (inline), the API's functions |
| `Engine/Scripting/include/menhir_simple.h` | The simple syntax's macros |
| `Engine/Scripting/include/menhir.hpp` | The C++ API: `MenhirEngine`, `MenhirObject`, the values, tweens, timers, coroutines, registration |
| `Engine/Scripting/include/menhir_abi.h` | What the engine and the scripts share: `mh_api` (the engine's functions, as a table), `mh_field`, `mh_script_type` |
| `Engine/Scripting/src/` | The runtime, compiled with the scripts: `menhir_script.c` (registry, `mh_bind`, transforms, log, random), `menhir_runtime.c` (objects, tweens, timers, coroutines), `menhir_physics.c` (colliders, rigidbodies), `menhir_audio.c` (AudioSource, sounds, with miniaudio, compiled once into the user's cache from `menhir_miniaudio.c`) |
| `Engine/Player/src/mh_scripts.c` | The game's `mh_api` and the scripts' life cycle |
| `Editor/Menhir.Editor.Core/Scripting/` | `ScriptTransform` (the simple syntax's generated copy), `CppScriptTransform` (the C++ one), `ScriptCompiler` (the library), `ScriptLibrary` (loading it, reading the script types), `ScriptHost` (the editor's `mh_api`, forwarding to `Runtime/PlaySession.cs`), `ScriptFields` (values) |

Scenes save a script's values as JSON (`ScriptComponent`); the data file keeps them that way
([BinChunk.md](BinChunk.md)) and the game converts them to the fields' types like Play mode does.
