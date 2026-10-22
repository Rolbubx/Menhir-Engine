/* The loaded scenes, their scripts and the built-in behaviours. The first loaded scene is the active
   one: its sky, ambient light and post-processing are used; additive scenes add their objects and
   lights. Each frame runs like the editor's Play mode (Runtime/PlaySession.cs): Start for the scripts
   not started yet, Update, the built-in behaviours, LateUpdate, then the scene change asked for
   during the frame, like Unity's LoadScene. Scripts exist in MENHIR_SCRIPTS builds (mh_scripts.c). */
#include <stdarg.h>
#include <stdlib.h>
#include <string.h>
#include "mh_data.h"
#ifdef MENHIR_SCRIPTS
#include <menhir_abi.h>
#endif

#define MH_MAX_LOADED_SCENES 8

typedef struct {
    int index;  /* in the build */
    int serial; /* unique per load: entity handles carry it, so handles of unloaded scenes go stale */
    mh_scene scene;
    mat4 *world; /* per entity, updated by mh_world_update_matrices */
    float *fly_yaw, *fly_pitch;
#ifdef MENHIR_SCRIPTS
    const mh_script_type **types; /* per mh_scene.scripts entry (NULL: no such script) */
    void **scripts;               /* each one's struct */
    unsigned char *started;       /* Start has run */
    char **strings;               /* the string fields' texts, freed with the scene */
    int string_count;
#endif
} mh_loaded_scene;

typedef struct {
    mh_data *data;
    const mh_game *game;
    int count;
    mh_loaded_scene scenes[MH_MAX_LOADED_SCENES];
    int request_scene, request_mode; /* -1: nothing asked */
    int changed;                     /* scenes were loaded or unloaded (resources to release) */
    int cursor_captured;             /* a fly camera is looking around */
    int next_serial;
    float time, delta_time;          /* Time.time and Time.deltaTime */
    int frame_count;
    int quit;                        /* Application.Quit was called */
} mh_world;

enum { MH_LOG_INFO, MH_LOG_WARNING, MH_LOG_ERROR };

/* Where the log goes: messages on stdout, warnings and errors on stderr; --simulate prints all of it
   on stdout as "log <level> <message>" lines between its dumps; --dump keeps stdout for the dump. */
enum { MH_LOG_NORMAL, MH_LOG_PREFIXED, MH_LOG_STDERR };
static int mh_log_mode;

/* mh_log in scripts, and the warnings about them. */
static void mh_log(int level, const char *format, ...)
{
    va_list args;
    va_start(args, format);
    FILE *out = mh_log_mode == MH_LOG_PREFIXED || (mh_log_mode == MH_LOG_NORMAL && level == MH_LOG_INFO) ? stdout : stderr;
    if (mh_log_mode == MH_LOG_PREFIXED)
        fprintf(out, "log %d ", level);
    else if (level != MH_LOG_INFO)
        fputs(level == MH_LOG_WARNING ? "Warning: " : "Error: ", out);
    vfprintf(out, format, args);
    fputc('\n', out);
    fflush(out);
    va_end(args);
}

enum { MH_AWAKE, MH_START, MH_UPDATE, MH_LATE_UPDATE };

#ifdef MENHIR_SCRIPTS
static void mh_scripts_load(mh_world *w, mh_loaded_scene *s);
static void mh_scripts_unload(mh_loaded_scene *s);
static void mh_scripts_run(mh_world *w, const mh_input *input, int callback);
#else
static void mh_scripts_load(mh_world *w, mh_loaded_scene *s)
{
    (void)w;
    for (int i = 0; i < s->scene.script_count; i++)
        mh_log(MH_LOG_WARNING, "%s: the script %s does not exist.",
               s->scene.entity_names[s->scene.scripts[i].entity], s->scene.scripts[i].script_name);
}
static void mh_scripts_unload(mh_loaded_scene *s) { (void)s; }
static void mh_scripts_run(mh_world *w, const mh_input *input, int callback) { (void)w; (void)input; (void)callback; }
#endif

static void mh_world_unload_all(mh_world *w)
{
    for (int i = 0; i < w->count; i++) {
        mh_loaded_scene *s = &w->scenes[i];
        mh_scripts_unload(s);
        mh_free_scene(&s->scene);
        free(s->world);
        free(s->fly_yaw);
        free(s->fly_pitch);
    }
    w->count = 0;
}

static void mh_world_update_matrices(mh_world *w)
{
    for (int i = 0; i < w->count; i++)
        mh_world_matrices(&w->scenes[i].scene, w->scenes[i].world);
}

static void mh_world_load(mh_world *w, int index, int mode)
{
    if (index < 0 || index >= w->game->scene_count) {
        fprintf(stderr, "menhir: there is no scene %d\n", index);
        return;
    }
    if (mode == MH_LOAD_SINGLE)
        mh_world_unload_all(w);
    else {
        for (int i = 0; i < w->count; i++)
            if (w->scenes[i].index == index)
                return; /* already loaded */
        if (w->count == MH_MAX_LOADED_SCENES) {
            fprintf(stderr, "menhir: at most %d scenes can be loaded together\n", MH_MAX_LOADED_SCENES);
            return;
        }
    }
    mh_loaded_scene *s = &w->scenes[w->count++];
    memset(s, 0, sizeof(*s));
    s->index = index;
    s->serial = w->next_serial++;
    mh_load_scene(w->data, index, &s->scene);
    s->world = mh_alloc(sizeof(mat4) * (size_t)maxi(1, s->scene.entity_count));
    s->fly_yaw = mh_alloc(sizeof(float) * (size_t)maxi(1, s->scene.fly_camera_count));
    s->fly_pitch = mh_alloc(sizeof(float) * (size_t)maxi(1, s->scene.fly_camera_count));
    for (int i = 0; i < s->scene.fly_camera_count; i++) {
        s->fly_yaw[i] = s->scene.fly_cameras[i].yaw;
        s->fly_pitch[i] = s->scene.fly_cameras[i].pitch;
    }
    mh_world_matrices(&s->scene, s->world);
    w->changed = 1;
    mh_scripts_load(w, s); /* creates the scripts, then runs their Awake */
}

static void mh_world_init(mh_world *w, mh_data *data, const mh_game *game, int first_scene)
{
    memset(w, 0, sizeof(*w));
    w->data = data;
    w->game = game;
    w->request_scene = -1;
    w->next_serial = 1;
    mh_world_load(w, first_scene, MH_LOAD_SINGLE);
}

/* FlyCamera (the editor's Runtime/FlyCameraBehaviour.cs). */
static void mh_fly_camera_update(mh_loaded_scene *s, int i, const mh_input *input, float dt, int *looking)
{
    const mh_fly_camera *f = &s->scene.fly_cameras[i];
    mh_entity_data *e = &s->scene.entities[f->entity];
    int look = !f->require_right_mouse_button || mh_get_key(input, MH_RIGHT_MOUSE_BUTTON);
    if (look) {
        s->fly_yaw[i] -= mh_get_axis(input, f->look_x_axis) * f->look_speed;
        s->fly_pitch[i] = clampf(s->fly_pitch[i] + mh_get_axis(input, f->look_y_axis) * f->look_speed, -89.0f, 89.0f);
        *looking = 1;
    }
    float to_radians = MH_PI / 180.0f;
    e->rotation = q_from_yaw_pitch_roll(s->fly_yaw[i] * to_radians, s->fly_pitch[i] * to_radians, 0.0f);

    mat4 axes = m4_from_quat(e->rotation);
    vec3 move = v3_add(v3_scale(m4_direction(axes, v3(1, 0, 0)), mh_get_axis(input, f->horizontal_axis)),
                       v3_scale(m4_direction(axes, v3(0, 0, -1)), mh_get_axis(input, f->vertical_axis)));
    if (v3_length_sq(move) > 1.0f)
        move = v3_normalize(move);
    float speed = f->move_speed * (mh_get_button(input, f->fast_button) ? fmaxf(f->fast_multiplier, 1.0f) : 1.0f);
    e->position = v3_add(e->position, v3_scale(move, speed * dt));
}

/* Runs the behaviours of every loaded scene, then the scene change they asked for. */
static void mh_world_update(mh_world *w, const mh_input *input, float dt)
{
    int looking = 0;
    w->changed = 0;
    w->delta_time = dt;
    w->time += dt;
    w->frame_count++;
    mh_scripts_run(w, input, MH_START);
    mh_scripts_run(w, input, MH_UPDATE);

    for (int si = 0; si < w->count; si++) {
        mh_loaded_scene *s = &w->scenes[si];
        for (int i = 0; i < s->scene.fly_camera_count; i++)
            mh_fly_camera_update(s, i, input, dt, &looking);
        for (int i = 0; i < s->scene.switcher_count; i++) {
            const mh_scene_switcher *sw = &s->scene.switchers[i];
            if (mh_get_button_down(input, sw->button)) {
                w->request_scene = sw->scene;
                w->request_mode = sw->mode;
            }
        }
    }
    w->cursor_captured = looking;
    mh_scripts_run(w, input, MH_LATE_UPDATE);

    if (w->request_scene >= 0) {
        mh_world_load(w, w->request_scene, w->request_mode);
        w->request_scene = -1;
    }
    mh_world_update_matrices(w);
}

/* Lights of every loaded scene: the sun is the first directional light, in load order. */
static void mh_world_environment(const mh_world *w, mh_environment *env)
{
    mh_environment_begin(env);
    int has_sun = 0;
    for (int i = 0; i < w->count; i++)
        mh_gather_lights(&w->scenes[i].scene, w->scenes[i].world, env, &has_sun);
}

/* The first camera of the loaded scenes, in load order. */
static void mh_world_view(const mh_world *w, float aspect, mh_view *view)
{
    for (int i = 0; i < w->count; i++) {
        const mh_scene *scene = &w->scenes[i].scene;
        if (scene->camera_count > 0) {
            mh_camera_view(&scene->cameras[0], w->scenes[i].world[scene->cameras[0].entity], aspect, view);
            return;
        }
    }
    mh_camera_view(NULL, m4_identity(), aspect, view);
}

#ifndef MENHIR_HEADLESS
/* Whether the loaded scenes use a mesh, or a texture (the active scene's post-processing). */
static int mh_world_uses_mesh(const mh_world *w, uint32_t id)
{
    for (int i = 0; i < w->count; i++)
        for (int r = 0; r < w->scenes[i].scene.renderer_count; r++)
            if (w->scenes[i].scene.renderers[r].mesh == id)
                return 1;
    return 0;
}

static int mh_world_uses_texture(const mh_world *w, uint32_t id)
{
    return w->count > 0 && (w->scenes[0].scene.post.lut_texture == id || w->scenes[0].scene.post.grain_texture == id);
}
#endif
