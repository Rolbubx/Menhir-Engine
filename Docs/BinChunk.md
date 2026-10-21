# GameData.binchunk

The data file of a built game (File → Build), written by `Editor/Menhir.Editor.Core/Build/GameDataExporter.cs`
and read by `Engine/Player/src/mh_data.c`. The game reads the chunk table when it starts, then reads each
chunk from disk when it needs it: a scene when it loads, then the meshes and textures that scene uses.

Everything is little-endian. `u32` / `i32` are 4 bytes, `f32` an IEEE float, `bool` a `u32` (0 or 1),
`vecN` N `f32`, `string` a `u32` byte length then UTF-8 bytes (no terminator). Colors are linear
unless marked gamma.

## Container

| Bytes | Content |
|-------|---------|
| 16    | Header: `"MNHR"`, `u32` version (1), `u32` chunk count, `u32` flags (0) |
| 24 × count | Table: `u32` type (four characters), `u32` id, `u64` offset, `u64` size |
| …     | Chunk data, each starting on a 16-byte boundary |

A type and id pair is unique. Unknown chunk types are ignored by readers.

## `GAME` (id 0)

| Field | |
|-------|-|
| `u32` width, `u32` height | Window size (Project Settings → Build) |
| `string` title | Project name |
| `u32` scene count | Scenes are `SCNE` 0 … count − 1; 0 starts the game |
| `bool` main light shadows, `u32` resolution | |
| `bool` additional light shadows, `u32` resolution | |
| `f32` max distance | |
| `u32` cascade count, `f32` × 4 cascade ends | Fractions of the max distance (`RenderingSettings.CascadeEnds`, padded with 1) |
| `f32` last border, `f32` depth bias, `f32` normal bias | |
| `u32` soft filter taps | 2, 3 or 4; 0 when the project turns soft shadows off |
| `u32` axis count, then per axis | The Input Manager ([Input.md](Input.md)): `string` name, `u32` type (0 keys, 1 mouse, 2 joystick), `i32` negative, positive, alt negative, alt positive button codes (−1: none; GLFW key codes, 1000 + mouse button, 2000 + gamepad button), `f32` gravity, dead, sensitivity, `bool` snap, `bool` invert, `u32` axis |
| `string` × scene count | Scene names, by index (`SceneManager.LoadScene(name)`) |

## `SCNE` (id = scene index)

1. `string` name.
2. Sky: `u32` sun disk (0 none, 1 simple, 2 high quality), `f32` sun size, `f32` sun size convergence,
   `f32` atmosphere thickness, `vec3` sky tint (gamma), `vec3` ground color, `f32` exposure.
3. Ambient light, computed by the editor (`SkyModel.Ambient`): `vec3` top, horizon, bottom.
4. Entities: `u32` count, then per entity `i32` parent index (−1: root), `vec3` position,
   `vec4` rotation (quaternion x, y, z, w), `vec3` scale. Indices follow the scene file's order.
5. Mesh renderers: `u32` count, then `u32` entity, `u32` mesh (`MESH` id), `vec3` mesh bounds min,
   `vec3` mesh bounds max (local space, for shadow fitting), `vec4` color, `f32` metallic,
   `f32` smoothness, `bool` cast shadows, `bool` receive shadows.
6. Lights: `u32` count, then `u32` entity, `u32` type (0 directional, 1 point, 2 spot), `vec3` color × intensity,
   `f32` range, `f32` spot angle (degrees), `u32` shadows (0 none, 1 hard, 2 soft), `f32` shadow strength.
7. Cameras: `u32` count, then `u32` entity, `f32` vertical field of view (degrees), `f32` near, `f32` far,
   `u32` clear flags (0 skybox, 1 solid color), `vec3` background.
8. Post-processing, ready for the shaders (values as `GLPostProcessor` sets them):
   - Depth of field: `u32` mode (0 off, 1 gaussian, 2 bokeh), `f32` gaussian start, `f32` gaussian end,
     `f32` gaussian max radius, `bool` high quality, `f32` focus distance, `f32` lens coefficient,
     `vec2` × 42 bokeh kernel.
   - Motion blur: `bool` on, `f32` intensity, `f32` clamp, `u32` samples.
   - Panini: `bool` on, `f32` distance, `f32` crop to fit.
   - Bloom: `bool` on, `u32` downscale (2 or 4), `u32` max iterations, `f32` threshold (linear), `f32` knee,
     `f32` clamp, `f32` scatter (mapped to 0.05…0.95), `bool` high quality, `f32` intensity, `vec3` tint (normalized).
   - Vignette: `bool` on, `vec3` color, `vec2` center, `f32` intensity × 3, `f32` smoothness × 5, `bool` rounded.
   - Color lookup: `u32` texture (`TEXR` id, 0: none), `f32` contribution.
   - Film grain: `u32` texture (`TEXR` id, 0: none), `f32` intensity × 4, `f32` response.
   - Uber pass uniforms (lens distortion, chromatic aberration, color grading; `PostProcessUniforms.Uber`):
     `u32` count, then `string` uniform name, `u32` kind (0 int, 1 float, 2 vec2, 3 vec3, 4 vec4),
     `vec4` value (an int is stored as a float in x).
   - Color curves: `bool` on, then if on `f32` × 128 × 8 (the curve texture's rows).
9. Fly cameras: `u32` count, then `u32` entity, `f32` move speed, `f32` look speed, `f32` fast multiplier,
   `f32` yaw and `f32` pitch (degrees, from the entity's rotation), `string` horizontal axis, vertical axis,
   look X axis, look Y axis, fast button, `bool` require right mouse button.
10. Scene switchers: `u32` count, then `u32` entity, `string` button, `u32` scene index, `u32` mode (0 single, 1 additive).
11. Entity names: `string` per entity.
12. Scripts ([Scripting.md](Scripting.md)), in entity order then component order: `u32` count, then `u32` entity,
    `string` class name (as .NET's `Type.FullName`), `u32` value count, then per saved field value `string` field
    name, `u32` kind and the value as the scene's JSON had it: 0 number (`string`, the number as written),
    1 boolean (`bool`), 2 string (`string`), 3 list of numbers (`u32` count, then a `string` per number). The game
    converts each value to its field's type (and warns when it does not fit), like Play mode.

The game computes the rest each frame, like the editor: world matrices, the sun and the lights,
the camera, shadow cascades and slices, and what depends on the window's size.

## `MESH` (id = 1 + `PrimitiveMesh` for built-in meshes, from 1000 for imported models)

`u32` vertex count, `u32` index count, then per vertex `vec3` position and `vec3` normal, then `u32`
indices (triangles, counter-clockwise).

## `TEXR` (id from 1)

`u32` width, `u32` height, `u32` channels (1: R8, 4: RGBA8), `bool` repeat (else clamped), then the pixels,
rows from the first (bottom in OpenGL's terms, as uploaded).
