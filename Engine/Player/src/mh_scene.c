/* What a frame needs from the scene, computed like the editor does: world matrices
   (Scene.GetWorldMatrix), lights (SceneEnvironment.Gather) and the camera. */
#include "mh_data.h"

#define MH_MAX_ADDITIONAL_LIGHTS 8

/* A point or spot light in world space (the editor's PunctualLight). */
typedef struct {
    vec3 position, direction, color;
    float range, spot_cos_outer, spot_cos_inner; /* cosines are -1 for point lights */
    int shadows;
    float shadow_strength;
} mh_punctual_light;

typedef struct {
    vec3 to_sun, sun_color;
    int sun_shadows;
    float sun_shadow_strength;
    int light_count;
    mh_punctual_light lights[MH_MAX_ADDITIONAL_LIGHTS];
} mh_environment;

typedef struct {
    mat4 view, projection, camera_to_world;
    vec3 position;
    float fov_y, near, far, aspect;
    int clear_flags;
    vec3 background;
} mh_view;

static mat4 mh_local_matrix(const mh_entity *e)
{
    return m4_mul(m4_mul(m4_scale(e->scale), m4_from_quat(e->rotation)), m4_translation(e->position));
}

/* world[i] = local matrix times every ancestor's. */
static void mh_world_matrices(const mh_scene *scene, mat4 *world)
{
    for (int i = 0; i < scene->entity_count; i++) {
        mat4 m = mh_local_matrix(&scene->entities[i]);
        int parent = scene->entities[i].parent;
        for (int depth = 0; parent >= 0 && depth <= scene->entity_count; depth++) {
            m = m4_mul(m, mh_local_matrix(&scene->entities[parent]));
            parent = scene->entities[parent].parent;
        }
        world[i] = m;
    }
}

/* The entity's forward axis (-Z) in world space. */
static vec3 mh_forward(mat4 world)
{
    vec3 forward = m4_direction(world, v3(0, 0, -1));
    return v3_length_sq(forward) > 1e-12f ? v3_normalize(forward) : v3(0, 0, -1);
}

static vec3 mh_default_to_sun(void) { return v3_normalize(v3(-0.32f, 0.77f, 0.55f)); }

static void mh_environment_begin(mh_environment *env)
{
    env->to_sun = mh_default_to_sun();
    env->sun_color = v3(0, 0, 0);
    env->sun_shadows = MH_SHADOWS_NONE;
    env->sun_shadow_strength = 1.0f;
    env->light_count = 0;
}

/* Adds a scene's lights; *has_sun is set once a directional light became the sun. */
static void mh_gather_lights(const mh_scene *scene, const mat4 *world, mh_environment *env, int *has_sun)
{
    for (int i = 0; i < scene->light_count; i++) {
        const mh_light *light = &scene->lights[i];
        mat4 m = world[light->entity];
        vec3 forward = mh_forward(m);
        if (light->type == MH_LIGHT_DIRECTIONAL) {
            if (*has_sun)
                continue; /* one main directional light, like URP */
            *has_sun = 1;
            env->to_sun = v3_neg(forward);
            env->sun_color = light->color;
            env->sun_shadows = light->shadows;
            env->sun_shadow_strength = light->shadow_strength;
        } else if (env->light_count < MH_MAX_ADDITIONAL_LIGHTS) {
            int spot = light->type == MH_LIGHT_SPOT;
            float outer = spot ? clampf(light->spot_angle, 1.0f, 179.0f) * MH_PI / 360.0f : MH_PI;
            mh_punctual_light *p = &env->lights[env->light_count++];
            p->position = m4_get_translation(m);
            p->direction = forward;
            p->color = light->color;
            p->range = fmaxf(light->range, 0.01f);
            p->spot_cos_outer = spot ? cosf(outer) : -1.0f;
            p->spot_cos_inner = spot ? cosf(outer * 0.8f) : -1.0f;
            p->shadows = light->shadows;
            p->shadow_strength = light->shadow_strength;
        }
    }
}

/* The view of camera c placed by matrix m, or with c NULL, of one at (0, 1, 10) looking down -Z. */
static void mh_camera_view(const mh_camera *c, mat4 m, float aspect, mh_view *view)
{
    float fov = 60.0f, near = 0.3f, far = 1000.0f;
    view->clear_flags = MH_CLEAR_SKYBOX;
    view->background = v3(0, 0, 0);
    if (!c)
        m = m4_translation(v3(0, 1, 10));
    else {
        fov = clampf(c->fov_degrees, 1.0f, 179.0f);
        near = fmaxf(c->near, 0.01f);
        far = fmaxf(c->far, near + 0.01f);
        view->clear_flags = c->clear_flags;
        view->background = c->background;
    }

    vec3 position = m4_get_translation(m);
    vec3 forward = mh_forward(m);
    vec3 up = m4_direction(m, v3(0, 1, 0));
    up = v3_length_sq(up) > 1e-12f ? v3_normalize(up) : v3(0, 1, 0);

    view->position = position;
    view->fov_y = fov * MH_PI / 180.0f;
    view->near = near;
    view->far = far;
    view->aspect = aspect;
    view->view = m4_look_at(position, v3_add(position, forward), up);
    view->projection = m4_perspective(view->fov_y, aspect, near, far);
    m4_invert(view->view, &view->camera_to_world);
}
