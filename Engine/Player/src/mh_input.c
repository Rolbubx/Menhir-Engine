/* The Input Manager at run time, like the editor's Input/InputState.cs (which documents it):
   named axes and buttons from keys, mouse buttons and movement, and the first gamepad.
   Codes: GLFW key codes, 1000 + n for mouse button n, 2000 + n for gamepad button n. */
#include <string.h>
#include "mh_data.h"

#define MH_KEY_CODES 349
#define MH_FIRST_MOUSE_BUTTON 1000
#define MH_MOUSE_BUTTONS 8
#define MH_FIRST_GAMEPAD_BUTTON 2000
#define MH_GAMEPAD_BUTTONS 15
#define MH_RIGHT_MOUSE_BUTTON (MH_FIRST_MOUSE_BUTTON + 1)

/* What the devices report for one frame. */
typedef struct {
    unsigned char keys[MH_KEY_CODES], mouse[MH_MOUSE_BUTTONS], pad[MH_GAMEPAD_BUTTONS];
    float mouse_dx, mouse_dy, scroll; /* pixels since the last frame (y down), wheel steps */
    float mouse_x, mouse_y;           /* the cursor in pixels from the window's bottom-left corner */
    float pad_axes[6];                /* left x, left y (down +1), right x, right y, triggers 0..1 */
} mh_input_frame;

typedef struct {
    const mh_input_axis *axes;
    int count;
    float *values, *raw;
    mh_input_frame current, previous;
} mh_input;

static int mh_frame_down(const mh_input_frame *f, int code)
{
    if (code >= 0 && code < MH_KEY_CODES)
        return f->keys[code];
    if (code >= MH_FIRST_MOUSE_BUTTON && code < MH_FIRST_MOUSE_BUTTON + MH_MOUSE_BUTTONS)
        return f->mouse[code - MH_FIRST_MOUSE_BUTTON];
    if (code >= MH_FIRST_GAMEPAD_BUTTON && code < MH_FIRST_GAMEPAD_BUTTON + MH_GAMEPAD_BUTTONS)
        return f->pad[code - MH_FIRST_GAMEPAD_BUTTON];
    return 0;
}

static void mh_frame_set(mh_input_frame *f, int code, int down)
{
    if (code >= 0 && code < MH_KEY_CODES)
        f->keys[code] = (unsigned char)down;
    else if (code >= MH_FIRST_MOUSE_BUTTON && code < MH_FIRST_MOUSE_BUTTON + MH_MOUSE_BUTTONS)
        f->mouse[code - MH_FIRST_MOUSE_BUTTON] = (unsigned char)down;
    else if (code >= MH_FIRST_GAMEPAD_BUTTON && code < MH_FIRST_GAMEPAD_BUTTON + MH_GAMEPAD_BUTTONS)
        f->pad[code - MH_FIRST_GAMEPAD_BUTTON] = (unsigned char)down;
}

static void mh_input_init(mh_input *input, const mh_game *game)
{
    memset(input, 0, sizeof(*input));
    input->axes = game->axes;
    input->count = game->axis_count;
    input->values = mh_alloc(sizeof(float) * (size_t)maxi(1, game->axis_count));
    input->raw = mh_alloc(sizeof(float) * (size_t)maxi(1, game->axis_count));
}

static float mh_move_towards(float current, float target, float max_delta)
{
    if (fabsf(target - current) <= max_delta)
        return target;
    return current + (target > current ? 1.0f : -1.0f) * max_delta;
}

static void mh_input_update(mh_input *input, const mh_input_frame *frame, float dt)
{
    input->previous = input->current;
    input->current = *frame;
    for (int i = 0; i < input->count; i++) {
        const mh_input_axis *a = &input->axes[i];
        if (a->type == MH_AXIS_KEYS) {
            int positive = mh_frame_down(frame, a->positive) || mh_frame_down(frame, a->alt_positive);
            int negative = mh_frame_down(frame, a->negative) || mh_frame_down(frame, a->alt_negative);
            float target = (positive ? 1.0f : 0.0f) - (negative ? 1.0f : 0.0f);
            float value = input->values[i];
            if (a->snap && target != 0.0f && value * target < 0.0f)
                value = 0.0f;
            value = target != 0.0f ? mh_move_towards(value, target, a->sensitivity * dt)
                                   : mh_move_towards(value, 0.0f, a->gravity * dt);
            input->values[i] = value;
            input->raw[i] = target;
        } else if (a->type == MH_AXIS_MOUSE) {
            float movement = a->axis == 0 ? frame->mouse_dx : a->axis == 1 ? -frame->mouse_dy : frame->scroll;
            input->values[i] = input->raw[i] = movement * a->sensitivity;
        } else {
            float position = frame->pad_axes[a->axis];
            if (fabsf(position) < a->dead)
                position = 0.0f;
            input->values[i] = input->raw[i] = position * a->sensitivity;
        }
    }
}

/* The largest value among the entries called name (0 when there is none). */
static float mh_largest(const mh_input *input, const char *name, const float *values, int dead_zone)
{
    float best = 0.0f;
    for (int i = 0; i < input->count; i++) {
        const mh_input_axis *a = &input->axes[i];
        if (strcmp(a->name, name) != 0)
            continue;
        float v = values[i];
        if (dead_zone && a->type == MH_AXIS_KEYS && fabsf(v) < a->dead)
            v = 0.0f;
        if (a->invert)
            v = -v;
        if (fabsf(v) > fabsf(best))
            best = v;
    }
    return best;
}

static float mh_get_axis(const mh_input *input, const char *name) { return mh_largest(input, name, input->values, 1); }
static float mh_get_axis_raw(const mh_input *input, const char *name) { return mh_largest(input, name, input->raw, 0); }

static int mh_any_positive(const mh_input *input, const char *name, const mh_input_frame *frame)
{
    for (int i = 0; i < input->count; i++) {
        const mh_input_axis *a = &input->axes[i];
        if (a->type == MH_AXIS_KEYS && strcmp(a->name, name) == 0 &&
            (mh_frame_down(frame, a->positive) || mh_frame_down(frame, a->alt_positive)))
            return 1;
    }
    return 0;
}

static int mh_get_button(const mh_input *input, const char *name) { return mh_any_positive(input, name, &input->current); }

static int mh_get_button_down(const mh_input *input, const char *name)
{
    return mh_any_positive(input, name, &input->current) && !mh_any_positive(input, name, &input->previous);
}

static int mh_get_button_up(const mh_input *input, const char *name)
{
    return !mh_any_positive(input, name, &input->current) && mh_any_positive(input, name, &input->previous);
}

static int mh_get_key(const mh_input *input, int code) { return mh_frame_down(&input->current, code); }

static int mh_get_key_down(const mh_input *input, int code)
{
    return mh_frame_down(&input->current, code) && !mh_frame_down(&input->previous, code);
}

static int mh_get_key_up(const mh_input *input, int code)
{
    return !mh_frame_down(&input->current, code) && mh_frame_down(&input->previous, code);
}

/* The code of a key or button name, like the editor's InputCodes.TryParse (case and surrounding
   spaces ignored): -1 for an empty name, -2 for an unknown one. */
static int mh_key_code(const char *name)
{
    static const struct { const char *name; int code; } named[] = {
        { "space", 32 }, { "'", 39 }, { ",", 44 }, { "-", 45 }, { ".", 46 }, { "/", 47 }, { ";", 59 }, { "=", 61 },
        { "[", 91 }, { "\\", 92 }, { "]", 93 }, { "`", 96 },
        { "escape", 256 }, { "return", 257 }, { "tab", 258 }, { "backspace", 259 }, { "insert", 260 }, { "delete", 261 },
        { "right", 262 }, { "left", 263 }, { "down", 264 }, { "up", 265 }, { "page up", 266 }, { "page down", 267 },
        { "home", 268 }, { "end", 269 }, { "caps lock", 280 }, { "pause", 284 },
        { "[.]", 330 }, { "[/]", 331 }, { "[*]", 332 }, { "[-]", 333 }, { "[+]", 334 }, { "enter", 335 },
        { "left shift", 340 }, { "left ctrl", 341 }, { "left alt", 342 }, { "left cmd", 343 },
        { "right shift", 344 }, { "right ctrl", 345 }, { "right alt", 346 }, { "right cmd", 347 },
    };
    char key[64], candidate[32];
    while (*name == ' ' || (*name >= '\t' && *name <= '\r'))
        name++;
    size_t length = strlen(name);
    while (length > 0 && (name[length - 1] == ' ' || (name[length - 1] >= '\t' && name[length - 1] <= '\r')))
        length--;
    if (length == 0)
        return -1;
    if (length >= sizeof key)
        return -2;
    for (size_t i = 0; i < length; i++)
        key[i] = (char)(name[i] >= 'A' && name[i] <= 'Z' ? name[i] - 'A' + 'a' : name[i]);
    key[length] = 0;

    if (length == 1 && key[0] >= 'a' && key[0] <= 'z')
        return 65 + (key[0] - 'a');
    if (length == 1 && key[0] >= '0' && key[0] <= '9')
        return 48 + (key[0] - '0');
    for (int d = 0; d <= 9; d++) {
        snprintf(candidate, sizeof candidate, "[%d]", d);
        if (!strcmp(key, candidate))
            return 320 + d;
    }
    for (int f = 1; f <= 12; f++) {
        snprintf(candidate, sizeof candidate, "f%d", f);
        if (!strcmp(key, candidate))
            return 289 + f;
    }
    for (size_t i = 0; i < sizeof named / sizeof named[0]; i++)
        if (!strcmp(key, named[i].name))
            return named[i].code;
    for (int m = 0; m < MH_MOUSE_BUTTONS; m++) {
        snprintf(candidate, sizeof candidate, "mouse %d", m);
        if (!strcmp(key, candidate))
            return MH_FIRST_MOUSE_BUTTON + m;
    }
    for (int b = 0; b < MH_GAMEPAD_BUTTONS; b++) {
        snprintf(candidate, sizeof candidate, "joystick button %d", b);
        if (!strcmp(key, candidate))
            return MH_FIRST_GAMEPAD_BUTTON + b;
    }
    return -2;
}
