# Input

The **Input Manager** (Project Settings → Input) works like Unity's: named axes and buttons, each driven by
keys and buttons, mouse movement or a gamepad axis. Several entries can share a name (the defaults have a
keyboard and a gamepad "Horizontal"); reading a name gives the entry with the largest value.

New projects get Unity's defaults: Horizontal (A/D, ←/→, left stick), Vertical (W/S, ↑/↓, left stick),
Fire1 (left Ctrl, left mouse, gamepad A), Fire2 (left Alt, right mouse, B), Fire3 (left Shift, middle mouse, X),
Jump (Space, Y), Mouse X, Mouse Y, Mouse ScrollWheel, Submit (Return, A) and Cancel (Escape, B).

| Type | Value |
|------|-------|
| Key or Mouse Button | Moves towards +1 (positive buttons) or −1 (negative) at **Sensitivity** per second, back to 0 at **Gravity**; **Snap** jumps through 0 when the opposite key is pressed. Raw value: −1, 0 or 1. |
| Mouse Movement | The cursor's movement since the last frame (pixels; up is positive) times **Sensitivity**. Axis 0 = X, 1 = Y, 2 = scroll wheel. |
| Joystick Axis | The first gamepad's axis past the **Dead** zone, times **Sensitivity**. 0 left X, 1 left Y (down is positive, so the default Vertical is inverted), 2 right X, 3 right Y, 4 left trigger, 5 right trigger (0..1). |

**Invert** flips the value. A button is held while one of its positive buttons is held; "down" and "up" are
true for the one frame where that changes.

## Key and button names

| Names | |
|-------|-|
| `a` … `z`, `0` … `9`, `f1` … `f12` | Letters, digits, function keys |
| `space`, `return`, `escape`, `tab`, `backspace`, `insert`, `delete`, `home`, `end`, `page up`, `page down`, `caps lock`, `pause` | |
| `up`, `down`, `left`, `right` | Arrow keys |
| `left shift`, `right shift`, `left ctrl`, `right ctrl`, `left alt`, `right alt`, `left cmd`, `right cmd` | Modifiers |
| `[0]` … `[9]`, `[.]`, `[/]`, `[*]`, `[-]`, `[+]`, `enter` | Keypad |
| `'` `,` `-` `.` `/` `;` `=` `[` `\` `]` `` ` `` | Punctuation (US layout positions) |
| `mouse 0` … `mouse 7` | Mouse buttons (0 left, 1 right, 2 middle) |
| `joystick button 0` … `joystick button 14` | Gamepad: 0 A, 1 B, 2 X, 3 Y, 4 left bumper, 5 right bumper, 6 back, 7 start, 8 guide, 9 left stick, 10 right stick, 11–14 d-pad up, right, down, left |

Gamepads use GLFW's mapping database (Xbox-style layout for every known pad). Names are matched without
case; unknown names are shown with a warning in Project Settings and in the build log, and never trigger.

## Who reads input

For now, two built-in components (Add Component → Gameplay), which run in the built game:

- **Fly Camera**: flies its entity like the viewport camera. Look with the mouse while the right button is
  held (or always, without *Require Right Mouse Button*; the cursor is hidden while looking), move with the
  Horizontal and Vertical axes, faster while Fire3 is held. Axis and button names can be changed.
- **Scene Switcher**: when its button goes down, loads a scene of the build, alone (**Single**: everything
  else is unloaded) or **Additive** (added to the loaded scenes, once). The first loaded scene stays the
  active one: its sky, ambient light, camera (if it has one) and post-processing are used; additive scenes
  add their objects and lights. A scene change happens at the end of the frame that asked for it; meshes and
  textures no longer used are then freed.

Scripts will read the same axes and buttons (`Input.GetAxis`, `Input.GetButtonDown`…) and load scenes;
the editor will run all of this in Play mode.
