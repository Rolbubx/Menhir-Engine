/* --dump: the first frame's camera, world matrices, lights and shadow maps as text, one value
   per line group. The editor's tests compare it with what the editor computes. */
#include <stdlib.h>
#include <string.h>
#include "mh_data.h"

static void mh_dump_floats(const char *key, int index, const float *v, int n)
{
    printf("%s %d", key, index);
    for (int i = 0; i < n; i++)
        printf(" %.9g", v[i]);
    printf("\n");
}

static void mh_dump_slice(const char *key, int index, const mh_shadow_slice *s)
{
    float v[16 + 16 + 4 + 5] = { 0 };
    for (int i = 0; i < 16; i++) {
        v[i] = s->view_projection.m[i];
        v[16 + i] = s->world_to_shadow.m[i];
    }
    v[32] = s->atlas_rect.x;
    v[33] = s->atlas_rect.y;
    v[34] = s->atlas_rect.z;
    v[35] = s->atlas_rect.w;
    v[36] = (float)s->x;
    v[37] = (float)s->y;
    v[38] = (float)s->size;
    v[39] = s->depth_bias;
    v[40] = s->normal_bias;
    mh_dump_floats(key, index, v, 41);
}

/* The active scene's world matrices, the view, lights and shadow maps of the loaded scenes. */
static void mh_dump(const mh_game *game, const mh_world *w, int width, int height)
{
    const mh_scene *scene = &w->scenes[0].scene;
    mh_environment env;
    mh_world_environment(w, &env);
    mh_view view;
    mh_world_view(w, (float)width / (float)height, &view);

    printf("scene 0 %s\n", scene->name);
    for (int i = 0; i < scene->entity_count; i++)
        mh_dump_floats("world", i, w->scenes[0].world[i].m, 16);
    mh_dump_floats("view", 0, view.view.m, 16);
    mh_dump_floats("projection", 0, view.projection.m, 16);
    float sun[7] = { env.to_sun.x, env.to_sun.y, env.to_sun.z, env.sun_color.x, env.sun_color.y, env.sun_color.z,
                     (float)env.sun_shadows };
    mh_dump_floats("sun", 0, sun, 7);
    for (int i = 0; i < env.light_count; i++) {
        const mh_punctual_light *l = &env.lights[i];
        float v[12] = { l->position.x, l->position.y, l->position.z, l->direction.x, l->direction.y, l->direction.z,
                        l->color.x, l->color.y, l->color.z, l->range, l->spot_cos_outer, l->spot_cos_inner };
        mh_dump_floats("light", i, v, 12);
    }

    mh_bounds casters = { 0, { 0, 0, 0 }, { 0, 0, 0 } };
    for (int si = 0; si < w->count; si++) {
        const mh_scene *s = &w->scenes[si].scene;
        for (int i = 0; i < s->renderer_count; i++)
            if (s->renderers[i].cast_shadows && w->scenes[si].visible[s->renderers[i].entity])
                casters = mh_bounds_encapsulate(casters,
                                                mh_renderer_bounds(&s->renderers[i], w->scenes[si].world[s->renderers[i].entity]));
    }

    mh_main_shadows main_shadows;
    mh_main_light_shadows(&env, &view, &game->rendering, casters, &main_shadows);
    if (main_shadows.on) {
        float v[6] = { (float)main_shadows.atlas_size, main_shadows.strength, (float)main_shadows.filter,
                       main_shadows.max_distance, main_shadows.fade_border, (float)main_shadows.cascade_count };
        mh_dump_floats("main", 0, v, 6);
        for (int i = 0; i < main_shadows.cascade_count; i++) {
            mh_dump_slice("cascade", i, &main_shadows.cascades[i]);
            mh_dump_floats("sphere", i, &main_shadows.spheres[i].x, 4);
        }
    }

    mh_additional_shadows additional;
    mh_additional_light_shadows(&env, &game->rendering, casters, &additional);
    if (additional.on) {
        float v[2] = { (float)additional.atlas_size, (float)additional.slice_count };
        mh_dump_floats("additional", 0, v, 2);
        for (int i = 0; i < env.light_count; i++) {
            float l[3] = { (float)additional.first_slice[i], additional.strength[i], (float)additional.filter[i] };
            mh_dump_floats("lightshadow", i, l, 3);
        }
        for (int i = 0; i < additional.slice_count; i++)
            mh_dump_slice("slice", i, &additional.slices[i]);
    }
}

/* --simulate: plays a script of input events without a window, frame by frame, and prints the
   state at each "dump" line. Lines: "down <code>", "up <code>", "mouse <dx> <dy>", "pos <x> <y>" (the
   cursor), "scroll <steps>", "pad <axis> <value>", "frame <dt> [count]", "dump"; '#' starts a comment.
   The log comes out as "log <level> <message>" lines; Application.Quit prints "quit" and stops. */
static void mh_simulate(const mh_game *game, mh_world *w, const char *path)
{
    FILE *f = fopen(path, "r");
    if (!f) {
        fprintf(stderr, "menhir: cannot open %s\n", path);
        exit(1);
    }
    mh_input input;
    mh_input_init(&input, game);
    mh_input_frame frame;
    memset(&frame, 0, sizeof frame);
    char line[256];
    while (fgets(line, sizeof line, f)) {
        char *hash = strchr(line, '#');
        if (hash)
            *hash = 0;
        char command[32];
        if (sscanf(line, "%31s", command) != 1)
            continue;
        int code, axis, count = 1;
        float a, b;
        if (!strcmp(command, "down") && sscanf(line, "%*s %d", &code) == 1)
            mh_frame_set(&frame, code, 1);
        else if (!strcmp(command, "up") && sscanf(line, "%*s %d", &code) == 1)
            mh_frame_set(&frame, code, 0);
        else if (!strcmp(command, "mouse") && sscanf(line, "%*s %f %f", &a, &b) == 2) {
            frame.mouse_dx = a;
            frame.mouse_dy = b;
        } else if (!strcmp(command, "pos") && sscanf(line, "%*s %f %f", &a, &b) == 2) {
            frame.mouse_x = a;
            frame.mouse_y = b;
        } else if (!strcmp(command, "scroll") && sscanf(line, "%*s %f", &a) == 1)
            frame.scroll = a;
        else if (!strcmp(command, "pad") && sscanf(line, "%*s %d %f", &axis, &a) == 2 && axis >= 0 && axis < 6)
            frame.pad_axes[axis] = a;
        else if (!strcmp(command, "frame") && sscanf(line, "%*s %f %d", &a, &count) >= 1) {
            for (int i = 0; i < count && !w->quit; i++) {
                mh_input_update(&input, &frame, a);
                mh_world_update(w, &input, a);
                frame.mouse_dx = frame.mouse_dy = frame.scroll = 0.0f; /* movement is per frame */
            }
            if (w->quit) {
                printf("quit\n");
                break;
            }
        } else if (!strcmp(command, "dump")) {
            for (int i = 0; i < game->axis_count; i++) {
                const char *name = game->axes[i].name;
                int first = 1;
                for (int j = 0; j < i; j++)
                    first &= strcmp(game->axes[j].name, name) != 0;
                if (first)
                    printf("axis %s|%.9g %.9g %d %d %d\n", name, mh_get_axis(&input, name), mh_get_axis_raw(&input, name),
                           mh_get_button(&input, name), mh_get_button_down(&input, name), mh_get_button_up(&input, name));
            }
            printf("scenes");
            for (int i = 0; i < w->count; i++)
                printf(" %d", w->scenes[i].index);
            printf("\ncursor %d\n", w->cursor_captured);
            for (int si = 0; si < w->count; si++) {
                const mh_scene *s = &w->scenes[si].scene;
                for (int i = 0; i < s->fly_camera_count; i++) {
                    const mh_entity_data *e = &s->entities[s->fly_cameras[i].entity];
                    float v[9] = { w->scenes[si].fly_yaw[i], w->scenes[si].fly_pitch[i], e->position.x, e->position.y,
                                   e->position.z, e->rotation.x, e->rotation.y, e->rotation.z, e->rotation.w };
                    mh_dump_floats("fly", si * 100 + i, v, 9);
                }
                /* The entities that are there (not destroyed, active), numbered in order like the editor's scene. */
                for (int i = 0, shown = 0; i < s->entity_count; i++) {
                    if (!mh_entity_visible(&w->scenes[si], i))
                        continue;
                    const mh_entity_data *e = &s->entities[i];
                    float v[10] = { e->position.x, e->position.y, e->position.z, e->rotation.x, e->rotation.y,
                                    e->rotation.z, e->rotation.w, e->scale.x, e->scale.y, e->scale.z };
                    mh_dump_floats("entity", si * 1000 + shown++, v, 10);
                }
            }
            printf("time %.9g %.9g %d\n", w->time, w->delta_time, w->frame_count);
            printf("end\n");
        } else {
            fprintf(stderr, "menhir: %s: bad line: %s", path, line);
            exit(2);
        }
    }
    fclose(f);
}
