# Scripting

Games are scripted in C. The `.c` files in a project's `Assets/` folder are its scripts (with any `.h`
files they include); a script is a struct holding its state, the functions the engine calls on it, and
`MH_SCRIPT` to declare them. Everything comes from one header, `menhir.h`
([Engine/Scripting/include](../Engine/Scripting/include/menhir.h)).

```c
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
    if (mh_key_down("space"))
        self->speed = -self->speed;
    mh_rotate(entity, mh_v3_scale(self->axis, self->speed * mh_delta_time()), true);
}

MH_SCRIPT(Spinner, MH_FIELDS(Spinner_fields), MH_UPDATE(Spinner_update));
```

**Project → Create → C Script** makes a file like this one. The compiler includes `menhir.h` before every
script by itself (`-include menhir.h`), so a `.c` file made elsewhere works without the `#include` line; the
template keeps it so that code editors (VS Code, CLion...) know the API. The same code runs in two places:

- **Play mode**: the editor compiles the scripts into a shared library (`Cache/Scripts/GameScripts.so`,
  `.dll` on Windows) and loads a fresh copy of it at each Play. The scripts run inside the editor, so a
  crash in a script (a bad pointer) closes the editor.
- **The built game**: File → Build compiles them with the player.

Both use the C compiler of **Project Settings → Build** (GCC or Clang) with the same flags, so a script
computes exactly the same thing in both. The editor's tests check it: `PlayerScriptTests` runs the same
scripts in Play mode and in the built game with the same input, and compares the log word for word and
every entity's transform.

## Declaring a script

`MH_SCRIPT(T, ...)` declares the struct `T` as a script named `T` (the name the inspector shows and
scenes save). After the name, in any order:

| | |
|-|-|
| `MH_FIELDS(array)` | The fields a scene can set (below) |
| `MH_AWAKE(fn)` | When the scene is loaded, for every script of the scene, before any start |
| `MH_START(fn)` | Once, before the script's first update |
| `MH_UPDATE(fn)` | Every frame |
| `MH_LATE_UPDATE(fn)` | Every frame, after every update and the built-in components (cameras following something) |

Each function takes the script's struct and its entity: `void fn(T *self, mh_entity entity)`. The engine
allocates the struct (zeroed), sets the fields to their default, then to the scene's values.

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
with a warning.

## Each frame

1. The input devices are read (the Input Manager, [Input.md](Input.md)); the time moves on.
2. Start for the scripts not started yet.
3. Update.
4. The built-in components: Fly Camera, Scene Switcher.
5. Late update.
6. The scene change asked for during the frame (`mh_load_scene`), if any: the new scene's scripts are
   created and get their awake then, and their start on the next frame.

Scripts run in the order of the loaded scenes, then of the entities in their scene, then of the
components on the entity.

## API

| | |
|-|-|
| Transform | `mh_get_position` / `mh_set_position`, `mh_get_rotation` / `mh_set_rotation` (in the world), `mh_get_local_position`, `_rotation`, `_scale` and their `mh_set_…`, `mh_forward`, `mh_right`, `mh_up`, `mh_translate(e, v, world)`, `mh_rotate(e, degrees, world)`, `mh_look_at(e, point)` |
| Entities | `mh_get_name`, `mh_get_parent` (`MH_NONE` for none), `mh_find(name)` (the first entity of the loaded scenes with that name), `mh_get_script(e, "Type")` (that entity's script struct, or NULL) |
| Input | `mh_axis`, `mh_axis_raw`, `mh_button`, `mh_button_down`, `mh_button_up` by Input Manager name; `mh_key`, `mh_key_down`, `mh_key_up` by key name (`"space"`, `"a"`, `"left shift"`, `"mouse 0"`, `"joystick button 0"`…); `mh_mouse_position()` (pixels from the bottom-left corner of the window or Game panel) |
| Time | `mh_delta_time()`, `mh_time()`, `mh_frame_count()` |
| Scenes | `mh_load_scene(name, MH_LOAD_SINGLE / MH_LOAD_ADDITIVE)`, `mh_load_scene_index`, `mh_quit()` (in Play mode: back to Edit mode) |
| Log | `mh_log`, `mh_log_warning`, `mh_log_error`, printf-style (the Console; the game's stdout, stderr for warnings and errors) |
| Random | `mh_random_seed`, `mh_random_value()` (0 to 1), `mh_random_range(min, max)`, `mh_random_int(min, max)` (max excluded); seeded from the clock when the game starts |
| Math | `mh_vec2`, `mh_vec3`, `mh_color`, `mh_quat`; `mh_v3`, `mh_v3_add`, `_sub`, `_scale`, `_mul`, `_dot`, `_cross`, `_length`, `_distance`, `_normalize`, `_lerp`; `mh_quat_identity`, `_mul`, `_inverse`, `_normalize`, `_rotate` (a vector), `_euler` (degrees), `_to_euler`, `_axis_angle`, `_look`, `_slerp`; `mh_clamp`, `mh_lerp`, `mh_move_towards`, `MH_PI`, `MH_DEG2RAD`, `MH_RAD2DEG` |

Conventions: right-handed, Y up (forward is −Z), like the editor; angles in degrees; Euler angles apply Z,
then X, then Y, like Unity. An `mh_entity` is a handle, valid while its scene is loaded: using a stale one
logs an error and does nothing. A wrong key name logs an error. The C standard library is all yours
(`<string.h>`, `<stdlib.h>`, `<math.h>`…).

## How it fits together

| | |
|-|-|
| `Engine/Scripting/include/menhir.h` | What scripts use: the declarations, math (inline), the API's functions |
| `Engine/Scripting/include/menhir_abi.h` | What the engine and the scripts share: `mh_api` (the engine's functions, as a table), `mh_field`, `mh_script_type` |
| `Engine/Scripting/src/menhir_script.c` | Compiled with the scripts: their registry (filled when the library or game starts), `mh_bind` (receives the `mh_api` table), and the parts of the API in plain C (transforms through the parents, log formatting, random) |
| `Engine/Player/src/mh_scripts.c` | The game's `mh_api` and the scripts' life cycle |
| `Editor/Menhir.Editor.Core/Scripting/` | `ScriptCompiler` (the library), `ScriptLibrary` (loading it, reading the script types), `ScriptHost` (the editor's `mh_api`, forwarding to `Runtime/PlaySession.cs`), `ScriptFields` (values) |

Scenes save a script's values as JSON (`ScriptComponent`); the data file keeps them that way
([BinChunk.md](BinChunk.md)) and the game converts them to the fields' types like Play mode does.
