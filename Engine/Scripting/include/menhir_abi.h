/* Menhir scripting: what the engine and the scripts share in memory. The scripts include menhir.h
   (which includes this file); the engine side (the player's mh_scripts.c, the editor's
   Scripting/NativeScripts.cs) only needs these types. Changing a layout here means changing the
   editor's C# copies too. */
#ifndef MENHIR_ABI_H
#define MENHIR_ABI_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* An entity of a loaded scene; MH_NONE for none. Valid while its scene stays loaded. */
typedef int32_t mh_entity;
#define MH_NONE ((mh_entity)-1)

struct mh_script_type;

/* What the scripts ask the engine. The engine fills one and gives it to mh_bind. */
typedef struct mh_api {
    /* Local transform: what 0 position (3 floats), 1 rotation (quaternion x, y, z, w), 2 scale (3 floats). */
    void (*get_transform)(mh_entity entity, int32_t what, float *out);
    void (*set_transform)(mh_entity entity, int32_t what, const float *values);
    mh_entity (*get_parent)(mh_entity entity);
    const char *(*get_name)(mh_entity entity);
    mh_entity (*find)(const char *name);
    /* The entity's first script of that type, or NULL. */
    void *(*get_script)(mh_entity entity, const char *type);
    float (*get_axis)(const char *name, int32_t raw);
    /* which: 0 held, 1 went down this frame, 2 went up this frame. */
    int32_t (*get_button)(const char *name, int32_t which);
    int32_t (*get_key)(const char *name, int32_t which);
    /* Pixels from the bottom-left corner of the window (or of the editor's Game panel). */
    void (*get_mouse_position)(float *out);
    /* which: 0 delta time, 1 time since the game started, 2 frame count, 3 fixed delta time. */
    float (*get_time)(int32_t which);
    /* level: 0 message, 1 warning, 2 error. */
    void (*log)(int32_t level, const char *message);
    /* A scene by name, or by build index when name is NULL. mode: 0 single, 1 additive. */
    int32_t (*load_scene)(const char *name, int32_t index, int32_t mode);
    void (*quit)(void);

    /* The entity exists (not destroyed, its scene still loaded). */
    int32_t (*is_alive)(mh_entity entity);
    /* A copy of the entity, its children and their components, under parent (MH_NONE: a root of the
       entity's scene). Its scripts' Awake runs at once. */
    mh_entity (*instantiate)(mh_entity entity, mh_entity parent);
    /* At the end of the frame, with its children (their scripts' OnDestroy runs then). */
    void (*destroy)(mh_entity entity);
    void (*set_active)(mh_entity entity, int32_t active);
    /* in_hierarchy: and all its parents are active. */
    int32_t (*get_active)(mh_entity entity, int32_t in_hierarchy);
    int32_t (*child_count)(mh_entity entity);
    mh_entity (*child_at)(mh_entity entity, int32_t index);
    /* MH_NONE: a root. Keeps the local transform. */
    void (*set_parent)(mh_entity entity, mh_entity parent);
    /* The entity's index-th script (and its type), or NULL past the last one. */
    void *(*script_at)(mh_entity entity, int32_t index, const struct mh_script_type **type);
    /* The full path of a file of the project's Assets folder (copied next to the built game). */
    const char *(*asset_path)(const char *relative);
    /* The entity of the camera the game renders with, or MH_NONE. */
    mh_entity (*main_camera)(void);
} mh_api;

/* The kinds of fields a scene can set, and the inspector edits. */
enum {
    MH_FIELD_INT,    /* int32_t */
    MH_FIELD_FLOAT,  /* float */
    MH_FIELD_BOOL,   /* bool (one byte) */
    MH_FIELD_VEC2,   /* mh_vec2 */
    MH_FIELD_VEC3,   /* mh_vec3 */
    MH_FIELD_COLOR,  /* mh_color: linear red, green, blue, alpha */
    MH_FIELD_STRING, /* const char *: the engine owns the text */
    MH_FIELD_ENUM,   /* int32_t, an index into options */
};

typedef struct mh_field {
    const char *name;
    int32_t kind;            /* MH_FIELD_* */
    uint32_t offset;         /* in the script's struct */
    double number[4];        /* the default (int, float, bool, enum index, vector or color components) */
    const char *text;        /* the default of a string */
    int32_t has_range;       /* a slider from min to max */
    float min, max;
    const char *tooltip;
    const char *const *options; /* enum: the names of its values */
    int32_t option_count;
} mh_field;

/* Every callback gets the script's struct and its entity. */
typedef void (*mh_callback)(void *self, mh_entity entity);
/* Collisions and triggers also get the other entity. */
typedef void (*mh_contact_callback)(void *self, mh_entity entity, mh_entity other);

typedef struct mh_script_type {
    const char *name;
    uint32_t size;           /* of the script's struct */
    const mh_field *fields;
    int32_t field_count;
    mh_callback awake, start, update, late_update;
    mh_callback fixed_update; /* every fixed step (mh_fixed_delta_time), before the physics */
    mh_callback on_destroy;   /* the entity is destroyed, or its scene unloaded */
    mh_callback on_game_close; /* the game is closing (before OnDestroy) */
    mh_contact_callback on_collision_enter, on_collision_exit, on_trigger_enter, on_trigger_exit;
} mh_script_type;

#ifdef __cplusplus
}
#endif

#endif
