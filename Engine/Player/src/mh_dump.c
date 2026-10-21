/* --dump: the first frame's camera, world matrices, lights and shadow maps as text, one value
   per line group. The editor's tests compare it with what the editor computes. */
#include <stdlib.h>
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

static void mh_dump(const mh_game *game, const mh_scene *scene, int width, int height)
{
    mat4 *world = malloc(sizeof(mat4) * (size_t)maxi(1, scene->entity_count));
    if (!world)
        exit(1);
    mh_world_matrices(scene, world);
    mh_environment env;
    mh_gather_environment(scene, world, &env);
    mh_view view;
    mh_camera_view(scene, world, (float)width / (float)height, &view);

    printf("scene 0 %s\n", scene->name);
    for (int i = 0; i < scene->entity_count; i++)
        mh_dump_floats("world", i, world[i].m, 16);
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
    for (int i = 0; i < scene->renderer_count; i++)
        if (scene->renderers[i].cast_shadows)
            casters = mh_bounds_encapsulate(casters, mh_renderer_bounds(&scene->renderers[i], world[scene->renderers[i].entity]));

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
    free(world);
}
