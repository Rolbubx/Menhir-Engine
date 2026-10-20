/* Shadow map placement, ported from the editor's Lighting/ShadowSetup.cs (which explains it):
   the sun's cascades fitted with bounding spheres and snapped to texels, one slice per spot
   light and six per point light. */
#include "mh_data.h"

#define MH_MAX_CASCADES 4
#define MH_MAX_ADDITIONAL_SLICES 16
#define MH_SOFT_KERNEL_RADIUS 2.5f

typedef struct {
    mat4 view_projection, world_to_shadow;
    vec4 atlas_rect;
    int x, y, size;
    float depth_bias, normal_bias;
} mh_shadow_slice;

typedef struct {
    int on, atlas_size, cascade_count;
    mh_shadow_slice cascades[MH_MAX_CASCADES];
    vec4 spheres[MH_MAX_CASCADES];
    float strength;
    int filter;
    float max_distance, fade_border;
} mh_main_shadows;

typedef struct {
    int on, atlas_size, slice_count;
    mh_shadow_slice slices[MH_MAX_ADDITIONAL_SLICES];
    int first_slice[MH_MAX_ADDITIONAL_LIGHTS];
    float strength[MH_MAX_ADDITIONAL_LIGHTS];
    int filter[MH_MAX_ADDITIONAL_LIGHTS];
} mh_additional_shadows;

typedef struct {
    int valid;
    vec3 min, max;
} mh_bounds;

static int mh_filter_taps(int shadows, const mh_rendering *settings)
{
    return shadows == MH_SHADOWS_SOFT ? settings->soft_taps : 0;
}

static vec3 mh_up_for(vec3 forward)
{
    return fabsf(v3_dot(v3_normalize(forward), v3(0, 1, 0))) > 0.99f ? v3(0, 0, 1) : v3(0, 1, 0);
}

static vec3 mh_bounds_corner(mh_bounds b, int i)
{
    return v3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
}

static mh_bounds mh_bounds_transform(mh_bounds local, mat4 m)
{
    mh_bounds r = { 1, v3(3.4028235e38f, 3.4028235e38f, 3.4028235e38f), v3(-3.4028235e38f, -3.4028235e38f, -3.4028235e38f) };
    for (int i = 0; i < 8; i++) {
        vec3 p = m4_point(m, mh_bounds_corner(local, i));
        r.min = v3_min(r.min, p);
        r.max = v3_max(r.max, p);
    }
    return r;
}

static mh_bounds mh_bounds_encapsulate(mh_bounds a, mh_bounds b)
{
    if (!a.valid)
        return b;
    mh_bounds r = { 1, v3_min(a.min, b.min), v3_max(a.max, b.max) };
    return r;
}

static mh_shadow_slice mh_slice(mat4 view_projection, int x, int y, int size, int atlas_size, float depth_bias,
                                float normal_bias)
{
    float scale = (float)size / (float)atlas_size;
    float u = (float)x / (float)atlas_size, v = (float)y / (float)atlas_size;
    /* Clip space (-1..1) to 0..1, then into the slice's tile (depth stays 0..1). */
    mat4 to_texture = m4_mul(m4_scale(v3(0.5f, 0.5f, 0.5f)), m4_translation(v3(0.5f, 0.5f, 0.5f)));
    mat4 to_tile = m4_mul(m4_scale(v3(scale, scale, 1.0f)), m4_translation(v3(u, v, 0.0f)));
    mh_shadow_slice s;
    s.view_projection = view_projection;
    s.world_to_shadow = m4_mul(m4_mul(view_projection, to_texture), to_tile);
    s.atlas_rect = v4(u, v, u + scale, v + scale);
    s.x = x;
    s.y = y;
    s.size = size;
    s.depth_bias = depth_bias;
    s.normal_bias = normal_bias;
    return s;
}

/* Sphere around the part of the camera's view between two distances. */
static void mh_slice_sphere(const mh_view *camera, float start, float end, vec3 *center, float *radius)
{
    float tan_y = tanf(camera->fov_y / 2.0f);
    float tan_x = tan_y * camera->aspect;
    vec3 corners[8];
    int n = 0;
    float distances[2] = { start, end };
    for (int di = 0; di < 2; di++) {
        float d = distances[di];
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                corners[n++] = m4_point(camera->camera_to_world, v3((float)sx * tan_x * d, (float)sy * tan_y * d, -d));
    }
    vec3 c = v3(0, 0, 0);
    for (int i = 0; i < 8; i++)
        c = v3_add(c, corners[i]);
    c = v3_scale(c, 1.0f / 8.0f);
    float r = 0.0f;
    for (int i = 0; i < 8; i++)
        r = fmaxf(r, v3_distance(c, corners[i]));
    *center = c;
    *radius = r;
}

static void mh_main_light_shadows(const mh_environment *env, const mh_view *camera, const mh_rendering *settings,
                                  mh_bounds casters, mh_main_shadows *out)
{
    out->on = 0;
    if (!settings->main_shadows || env->sun_shadows == MH_SHADOWS_NONE || v3_is_zero(env->sun_color) || !casters.valid)
        return;

    int atlas_size = settings->main_resolution;
    int count = settings->cascade_count;
    int tile_size = count == 1 ? atlas_size : atlas_size / 2;
    int filter = mh_filter_taps(env->sun_shadows, settings);
    float max_distance = fmaxf(settings->max_distance, camera->near + 0.1f);
    vec3 to_sun = v3_normalize(env->to_sun);

    float start = camera->near;
    for (int i = 0; i < count; i++) {
        float end = fmaxf(settings->cascade_ends[i] * max_distance, start + 0.01f);
        vec3 center;
        float radius;
        mh_slice_sphere(camera, start, end, &center, &radius);
        /* Quantized so it does not change with the camera's rotation (stable shadow edges). */
        radius = ceilf(radius * 16.0f) / 16.0f;

        /* Snap the center to whole texels in the light's view. */
        mat4 light_rotation = m4_look_at(v3(0, 0, 0), v3_neg(to_sun), mh_up_for(v3_neg(to_sun)));
        float texel = 2.0f * radius / (float)tile_size;
        vec3 light_space = m4_point(light_rotation, center);
        light_space.x = floorf(light_space.x / texel) * texel;
        light_space.y = floorf(light_space.y / texel) * texel;
        mat4 light_to_world;
        m4_invert(light_rotation, &light_to_world);
        center = m4_point(light_to_world, light_space);

        /* Back far enough towards the sun to catch every caster, even outside the camera's view. */
        float back = radius;
        for (int c = 0; c < 8; c++)
            back = fmaxf(back, v3_dot(v3_sub(mh_bounds_corner(casters, c), center), to_sun));
        back += 1.0f;

        vec3 eye = v3_add(center, v3_scale(to_sun, back));
        mat4 view = m4_look_at(eye, center, mh_up_for(v3_neg(to_sun)));
        mat4 projection = m4_orthographic(radius, radius, 0.0f, back + radius);
        int column = i % 2, row = i / 2;
        int x = count == 1 ? 0 : column * tile_size, y = count == 1 ? 0 : row * tile_size;
        int size = count == 1 ? atlas_size : tile_size;

        float bias = filter > 0 ? MH_SOFT_KERNEL_RADIUS : 1.0f;
        out->cascades[i] = mh_slice(m4_mul(view, projection), x, y, size, atlas_size,
                                    -settings->depth_bias * texel * bias, -settings->normal_bias * texel * bias);
        out->spheres[i] = v4_from3(center, radius * radius);
        start = end;
    }

    out->on = 1;
    out->atlas_size = atlas_size;
    out->cascade_count = count;
    out->strength = env->sun_shadow_strength;
    out->filter = filter;
    out->max_distance = max_distance;
    out->fade_border = clampf(settings->last_border, 0.001f, 1.0f);
}

static const vec3 mh_cube_face_forward[6] = { { 1, 0, 0 }, { -1, 0, 0 }, { 0, 1, 0 }, { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 } };
static const vec3 mh_cube_face_up[6] = { { 0, -1, 0 }, { 0, -1, 0 }, { 0, 0, 1 }, { 0, 0, -1 }, { 0, -1, 0 }, { 0, -1, 0 } };

static void mh_add_slice(mh_additional_shadows *out, const mh_punctual_light *light, const mh_rendering *settings,
                         int grid, int tile_size, float near, float kernel, vec3 forward, vec3 up, float fov)
{
    int index = out->slice_count++;
    int x = index % grid * tile_size, y = index / grid * tile_size;
    mat4 view = m4_look_at(light->position, v3_add(light->position, forward), up);
    mat4 projection = m4_perspective(fov, 1.0f, near, light->range);
    /* Texel size one unit away from the light, like URP for punctual lights. */
    float texel = 2.0f * tanf(fov / 2.0f) / (float)tile_size;
    out->slices[index] = mh_slice(m4_mul(view, projection), x, y, tile_size, out->atlas_size,
                                  -settings->depth_bias * texel * kernel, -settings->normal_bias * texel * kernel);
}

static void mh_additional_light_shadows(const mh_environment *env, const mh_rendering *settings, mh_bounds casters,
                                        mh_additional_shadows *out)
{
    out->on = 0;
    out->slice_count = 0;
    int any = 0;
    for (int i = 0; i < env->light_count; i++)
        any |= env->lights[i].shadows != MH_SHADOWS_NONE;
    if (!settings->additional_shadows || !casters.valid || !any)
        return;

    /* Decide which lights fit, then size the tiles for that many slices. */
    int slice_count = 0;
    for (int i = 0; i < env->light_count; i++) {
        int spot = env->lights[i].spot_cos_outer > -1.0f;
        int needed = spot ? 1 : 6;
        out->strength[i] = 0.0f;
        out->filter[i] = 0;
        if (env->lights[i].shadows == MH_SHADOWS_NONE || slice_count + needed > MH_MAX_ADDITIONAL_SLICES) {
            out->first_slice[i] = -1;
            continue;
        }
        out->first_slice[i] = slice_count;
        slice_count += needed;
    }
    if (slice_count == 0)
        return;

    out->atlas_size = settings->additional_resolution;
    int grid = (int)ceilf(sqrtf((float)slice_count));
    int tile_size = out->atlas_size / grid;

    for (int i = 0; i < env->light_count; i++) {
        if (out->first_slice[i] < 0)
            continue;
        const mh_punctual_light *light = &env->lights[i];
        out->strength[i] = light->shadow_strength;
        out->filter[i] = mh_filter_taps(light->shadows, settings);
        float near = fmaxf(0.05f, light->range * 0.005f);
        float kernel = out->filter[i] > 0 ? MH_SOFT_KERNEL_RADIUS : 1.0f;

        if (light->spot_cos_outer > -1.0f) {
            float cone = 2.0f * acosf(clampf(light->spot_cos_outer, -1.0f, 1.0f));
            mh_add_slice(out, light, settings, grid, tile_size, near, kernel, light->direction, mh_up_for(light->direction),
                         fminf(cone + 0.05f, MH_PI * 0.95f));
        } else {
            /* A little wider than 90°, so filtering near a face's edge stays inside the face. */
            float fov = 2.0f * atanf(1.0f + 6.0f / (float)tile_size);
            for (int f = 0; f < 6; f++)
                mh_add_slice(out, light, settings, grid, tile_size, near, kernel, mh_cube_face_forward[f],
                             mh_cube_face_up[f], fov);
        }
    }
    out->on = 1;
}
