# Scripting

Games are scripted in C#, much like Unity. Every `.cs` file in a project's `Assets/` folder is a script;
a class deriving from `MenhirEngine.Script` can be put on entities.

```csharp
using MenhirEngine;

public class Spinner : Script
{
    [Range(0f, 360f), Tooltip("Degrees per second")]
    public float speed = 45f;
    public Vector3 axis = Vector3.up;

    public override void Update()
    {
        if (Input.GetKeyDown("space"))
            speed = -speed;
        transform.Rotate(axis * (speed * Time.deltaTime), Space.World);
    }
}
```

The same scripts run in two places, with the same results:

- **Play mode** in the editor runs them on .NET (`Editor/Menhir.Editor.Core/Runtime/PlaySession.cs`).
- **The built game** runs them natively: File → Build translates them to C with IL2C
  ([Translator/README](../Translator/README.md)) and links them into the player
  ([Engine/README](../Engine/README.md)).

The editor's tests check this: `PlayerScriptTests` runs a project's scripts in Play mode and in the built game
with the same input, and compares the log word for word and every entity's transform.

## Scripts

| Member | |
|--------|-|
| `Awake()` | When the scene is loaded, for every script of the scene, before any `Start` |
| `Start()` | Once, before the script's first `Update` (on the first frame it is enabled) |
| `Update()` | Every frame |
| `LateUpdate()` | Every frame, after every `Update` and the built-in components (cameras following something) |
| `enabled` | A disabled script gets no `Start`, `Update` or `LateUpdate` |
| `entity` (`gameObject`), `transform`, `name` | The script's entity |
| `GetComponent<T>()` | The entity's first script of type `T` |

A class can be added when it is not abstract or generic and has a public parameterless constructor. An
exception a callback does not catch is logged (as an error in the Console, or on stderr in the game), and the
game goes on.

**Fields.** The public fields of a script show in the Inspector and are saved with the scene: `int`, `long`,
`float`, `double`, `bool`, `string`, enums (saved by name), `Vector2` and `Vector3`. `[Range(min, max)]` makes a
slider, `[Tooltip("...")]` adds a tooltip, `[HideInInspector]` hides the field (and the scene's value is not
applied). Fields the scene has no value for keep the value the class gives them. A saved value that no longer
fits its field (its type changed) is left out with a warning.

## Each frame

1. The input devices are read (the Input Manager, [Input.md](Input.md)); `Time` moves on.
2. `Start` for the scripts not started yet.
3. `Update`.
4. The built-in components: Fly Camera, Scene Switcher.
5. `LateUpdate`.
6. The scene change asked for during the frame (`SceneManager.LoadScene`), if any: the new scene's scripts are
   created and get `Awake` then, and `Start` on the next frame.

Scripts run in the order of the loaded scenes, then of the entities in their scene, then of the
components on the entity.

## API (`namespace MenhirEngine`)

| Class | |
|-------|-|
| `Transform` | `position`, `rotation`, `eulerAngles`, `localPosition`, `localRotation`, `localEulerAngles`, `localScale`, `parent`, `forward` / `right` / `up`, `Translate`, `Rotate`, `RotateAround`, `LookAt`, `TransformPoint`, `TransformDirection`, `InverseTransformPoint`, `InverseTransformDirection` |
| `Entity` | `name`, `transform`, `GetComponent<T>()`, `Entity.Find(name)` (the first entity of the loaded scenes with that name, or null) |
| `Input` | `GetAxis`, `GetAxisRaw`, `GetButton(Down/Up)` by Input Manager name; `GetKey(Down/Up)` by key name (`"space"`, `"a"`, `"left shift"`, `"mouse 0"`, `"joystick button 0"`…, as in the Input Manager); `GetMouseButton(Down/Up)`; `mousePosition` (pixels from the bottom-left corner of the window or Game panel) |
| `Time` | `deltaTime`, `time`, `frameCount` |
| `SceneManager` | `LoadScene(name or build index, LoadSceneMode.Single / Additive)` |
| `Debug` | `Log`, `LogWarning`, `LogError` (the Console; the game's stdout and stderr) |
| `Application` | `Quit()` (in Play mode: back to Edit mode) |
| `Random` | `InitState(seed)`, `value`, `Range(min, max)` (float and int), `insideUnitSphere` |
| `Mathf` | `PI`, `Deg2Rad`, `Rad2Deg`, `Sin`, `Cos`, `Sqrt`, `Abs`, `Min`, `Max`, `Clamp`, `Clamp01`, `Lerp`, `LerpAngle`, `MoveTowards`, `SmoothStep`, `PingPong`, `Repeat`, `Approximately`… |
| `Vector2`, `Vector3`, `Quaternion` | Unity's: `x`, `y`, `z`, operators, `magnitude`, `normalized`, `Dot`, `Cross`, `Lerp`, `Distance`; `Quaternion.Euler`, `AngleAxis`, `LookRotation`, `Slerp`, `eulerAngles`… |

Axes: right-handed with Y up (forward is −Z), like the editor; Euler angles are in degrees and apply Z, then X,
then Y, like Unity. Entities are handles: an `Entity` or `Transform` of an unloaded scene throws when used.

What scripts can use from .NET is Menhir's own base library, `Menhir.CoreLib`: the basic types, strings and
`StringBuilder`, formatting and parsing, `Math` / `MathF`, `List`, `Dictionary`, `HashSet`, `Queue`, `Stack`,
LINQ, delegates and lambdas, exceptions, generics, interfaces, `System.Random`. In the editor the scripts compile
against .NET, so a script that uses something else works in Play mode but does not build: the build lists what
is missing.

## How it is built

| | Play mode | Built game |
|-|-----------|------------|
| API | `Engine/Scripting/Menhir.Engine` (.NET); `Host` calls go to `IEngineHost`, implemented by `PlaySession` | `Engine/Scripting/Menhir.Engine.Native` (against `Menhir.CoreLib`, `MENHIR_NATIVE`); `Host` calls are internal calls implemented in `Engine/Player/src/mh_scripts.c` |
| Scripts | `Cache/Scripts/Editor/`, loaded in a collectible context (replaced when they compile again) | `Cache/Scripts/Game/`, then IL2C with the API and the corlib into `Build/<Name>/scripts.c` |
| Scene values | `ScriptFields.Apply` (reflection) | The `SCNE` chunk keeps them as JSON had them ([BinChunk.md](BinChunk.md)); `mh_scripts.c` converts them with the field table IL2C generates (`MScriptTable` in `runtime.h`) |

Both compile with `dotnet build` (the .NET SDK is needed), into the project's `Cache/` folder.
