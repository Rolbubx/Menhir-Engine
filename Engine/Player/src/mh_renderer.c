/* The Menhir Universal Pipeline in the game: the same passes as the editor's GLRenderer
   (shadow maps, skybox, lit meshes into an HDR buffer) with the same shaders. */
#include <stdlib.h>
#include <string.h>
#include "mh_data.h"

#define MH_MAX_MESHES 64
#define MH_MAX_TEXTURES 64

typedef struct {
    GLuint texture, framebuffer;
    int size;
} mh_atlas;

typedef struct {
    uint32_t id;
    GLuint texture;
    int width, height;
} mh_gpu_texture;

typedef struct {
    mh_data *data;

    GLuint mesh_program, sky_program, caster_program;
    GLuint empty_vao;
    mh_atlas main_atlas, additional_atlas;

    mh_gpu_mesh meshes[MH_MAX_MESHES];
    int mesh_count;
    mh_gpu_texture textures[MH_MAX_TEXTURES];
    int texture_count;

    /* The frame's buffers: HDR color and depth for the scene, 8-bit output for the screen. */
    int width, height;
    GLuint hdr_texture, depth_texture, output_texture;
    GLuint scene_framebuffer, output_framebuffer;
    int has_previous_view_projection;
    mat4 previous_view_projection;

    struct mh_post_state *post;
} mh_renderer;

static void mh_post_init(mh_renderer *r);
static void mh_post_run(mh_renderer *r, const mh_view *view, const mh_post *post);

static const mh_gpu_mesh *mh_renderer_mesh(mh_renderer *r, uint32_t id)
{
    for (int i = 0; i < r->mesh_count; i++)
        if (r->meshes[i].id == id)
            return &r->meshes[i];
    mh_mesh_data data;
    if (r->mesh_count == MH_MAX_MESHES || !mh_load_mesh(r->data, id, &data))
        return NULL;
    r->meshes[r->mesh_count] = mh_upload_mesh(id, &data);
    free(data.vertices);
    free(data.indices);
    return &r->meshes[r->mesh_count++];
}

static const mh_gpu_texture *mh_renderer_texture(mh_renderer *r, uint32_t id)
{
    if (id == 0)
        return NULL;
    for (int i = 0; i < r->texture_count; i++)
        if (r->textures[i].id == id)
            return &r->textures[i];
    mh_texture_data data;
    if (r->texture_count == MH_MAX_TEXTURES || !mh_load_texture(r->data, id, &data))
        return NULL;
    mh_gpu_texture *t = &r->textures[r->texture_count++];
    t->id = id;
    t->width = data.width;
    t->height = data.height;
    t->texture = data.channels == 1
                     ? mh_texture(data.width, data.height, GL_R8, GL_RED, GL_UNSIGNED_BYTE, data.pixels, 0, data.repeat)
                     : mh_texture(data.width, data.height, GL_RGBA8, GL_RGBA, GL_UNSIGNED_BYTE, data.pixels, 0, data.repeat);
    free(data.pixels);
    return t;
}

static void mh_renderer_init(mh_renderer *r, mh_data *data)
{
    memset(r, 0, sizeof(*r));
    r->data = data;
    r->mesh_program = mh_program(mh_shader_Shaders_MeshVertex, mh_shader_Shaders_MeshFragment, "mesh");
    r->sky_program = mh_program(mh_shader_SkyShaders_Vertex, mh_shader_SkyShaders_Fragment, "sky");
    r->caster_program =
        mh_program(mh_shader_ShadowShaders_CasterVertex, mh_shader_ShadowShaders_CasterFragment, "shadow caster");
    glGenVertexArrays(1, &r->empty_vao);

    /* Shadow maps live on units 5 and 6, which the post-processing passes do not use. */
    mh_use(r->mesh_program);
    mh_set_i("uMainShadowMap", 5);
    mh_set_i("uAdditionalShadowMap", 6);
    mh_post_init(r);
}

static void mh_renderer_resize(mh_renderer *r, int width, int height)
{
    width = maxi(1, width);
    height = maxi(1, height);
    if (width == r->width && height == r->height)
        return;
    if (r->width) {
        GLuint framebuffers[2] = { r->scene_framebuffer, r->output_framebuffer };
        GLuint textures[3] = { r->hdr_texture, r->depth_texture, r->output_texture };
        glDeleteFramebuffers(2, framebuffers);
        glDeleteTextures(3, textures);
    }
    r->width = width;
    r->height = height;
    r->has_previous_view_projection = 0;
    r->hdr_texture = mh_texture(width, height, GL_RGBA16F, GL_RGBA, GL_HALF_FLOAT, NULL, 0, 0);
    r->output_texture = mh_texture(width, height, GL_RGBA8, GL_RGBA, GL_UNSIGNED_BYTE, NULL, 0, 0);
    r->depth_texture = mh_texture(width, height, GL_DEPTH24_STENCIL8, GL_DEPTH_STENCIL, GL_UNSIGNED_INT_24_8, NULL, 1, 0);
    r->scene_framebuffer = mh_framebuffer(r->hdr_texture, r->depth_texture, 0);
    r->output_framebuffer = mh_framebuffer(r->output_texture, r->depth_texture, 0);
}

/* Binds the atlas (resized if needed) and clears it to the far plane. */
static void mh_atlas_begin(mh_atlas *atlas, int size)
{
    if (size != atlas->size) {
        if (atlas->size) {
            glDeleteFramebuffers(1, &atlas->framebuffer);
            glDeleteTextures(1, &atlas->texture);
        }
        atlas->size = size;
        atlas->texture = mh_texture(size, size, GL_DEPTH_COMPONENT24, GL_DEPTH_COMPONENT, GL_UNSIGNED_INT, NULL, 0, 0);
        glBindTexture(GL_TEXTURE_2D, atlas->texture);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_COMPARE_MODE, GL_COMPARE_REF_TO_TEXTURE);
        glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_COMPARE_FUNC, GL_LEQUAL);
        glBindTexture(GL_TEXTURE_2D, 0);
        atlas->framebuffer = mh_framebuffer(0, 0, atlas->texture);
    }
    glBindFramebuffer(GL_FRAMEBUFFER, atlas->framebuffer);
    glDisable(GL_SCISSOR_TEST);
    glViewport(0, 0, size, size);
    glClear(GL_DEPTH_BUFFER_BIT);
}

typedef struct {
    const mh_gpu_mesh *mesh;
    mat4 model, normal_matrix;
    const mh_mesh_renderer *renderer;
} mh_draw;

static void mh_draw_slice(const mh_shadow_slice *slice, const mh_draw *draws, int draw_count)
{
    glViewport(slice->x, slice->y, slice->size, slice->size);
    mh_set_m4("uLightViewProjection", slice->view_projection);
    mh_set_f("uDepthBias", slice->depth_bias);
    mh_set_f("uNormalBias", slice->normal_bias);
    for (int i = 0; i < draw_count; i++) {
        if (!draws[i].renderer->cast_shadows)
            continue;
        mh_set_m4("uModel", draws[i].model);
        mh_set_m4("uNormalMatrix", draws[i].normal_matrix);
        mh_draw_mesh(draws[i].mesh);
    }
}

static void mh_render_shadows(mh_renderer *r, const mh_environment *env, const mh_main_shadows *main_shadows,
                              const mh_additional_shadows *additional, const mh_draw *draws, int draw_count)
{
    if (!main_shadows->on && !additional->on)
        return;
    glEnable(GL_DEPTH_TEST);
    glDepthFunc(GL_LESS);
    glDepthMask(GL_TRUE);
    glDisable(GL_BLEND);
    glEnable(GL_CULL_FACE);
    glCullFace(GL_BACK);
    glFrontFace(GL_CCW);
    mh_use(r->caster_program);

    if (main_shadows->on) {
        mh_atlas_begin(&r->main_atlas, main_shadows->atlas_size);
        mh_set_i("uPunctual", 0);
        mh_set_v3("uLightDirection", v3_normalize(env->to_sun));
        for (int i = 0; i < main_shadows->cascade_count; i++)
            mh_draw_slice(&main_shadows->cascades[i], draws, draw_count);
    }
    if (additional->on) {
        mh_atlas_begin(&r->additional_atlas, additional->atlas_size);
        mh_set_i("uPunctual", 1);
        for (int light = 0; light < env->light_count; light++) {
            int first = additional->first_slice[light];
            if (first < 0)
                continue;
            mh_set_v3("uLightPosition", env->lights[light].position);
            int count = env->lights[light].spot_cos_outer > -1.0f ? 1 : 6;
            for (int i = first; i < first + count; i++)
                mh_draw_slice(&additional->slices[i], draws, draw_count);
        }
    }
    glDisable(GL_SCISSOR_TEST);
}

static void mh_set_lighting(const mh_scene *scene, const mh_environment *env, const mh_main_shadows *main_shadows,
                            const mh_additional_shadows *additional)
{
    int count = env->light_count;
    vec4 position_range[MH_MAX_ADDITIONAL_LIGHTS], color[MH_MAX_ADDITIONAL_LIGHTS], direction_cos[MH_MAX_ADDITIONAL_LIGHTS];
    float cos_inner[MH_MAX_ADDITIONAL_LIGHTS];
    for (int i = 0; i < count; i++) {
        const mh_punctual_light *l = &env->lights[i];
        position_range[i] = v4_from3(l->position, l->range);
        color[i] = v4_from3(l->color, 1.0f);
        direction_cos[i] = v4_from3(l->direction, l->spot_cos_outer);
        cos_inner[i] = l->spot_cos_inner;
    }
    mh_set_v3("uToSun", env->to_sun);
    mh_set_v3("uSunColor", env->sun_color);
    mh_set_v3("uAmbientTop", scene->ambient_top);
    mh_set_v3("uAmbientHorizon", scene->ambient_horizon);
    mh_set_v3("uAmbientBottom", scene->ambient_bottom);
    mh_set_i("uLightCount", count);
    if (count > 0) {
        mh_set_v4v("uLightPositionRange", position_range, count);
        mh_set_v4v("uLightColor", color, count);
        mh_set_v4v("uLightDirectionCos", direction_cos, count);
        mh_set_fv("uLightCosInner", cos_inner, count);
    }

    mh_set_i("uMainShadowOn", main_shadows->on);
    if (main_shadows->on) {
        int n = main_shadows->cascade_count;
        mat4 matrices[MH_MAX_CASCADES];
        vec4 rects[MH_MAX_CASCADES];
        for (int i = 0; i < n; i++) {
            matrices[i] = main_shadows->cascades[i].world_to_shadow;
            rects[i] = main_shadows->cascades[i].atlas_rect;
        }
        mh_set_i("uCascadeCount", n);
        mh_set_m4v("uCascadeMatrix", matrices, n);
        mh_set_v4v("uCascadeSphere", main_shadows->spheres, n);
        mh_set_v4v("uCascadeRect", rects, n);
        mh_set_v4("uMainShadowParams", v4(main_shadows->strength, (float)main_shadows->filter, main_shadows->max_distance,
                                          main_shadows->fade_border));
        mh_set_f("uMainShadowTexel", 1.0f / (float)main_shadows->atlas_size);
    }

    mh_set_i("uAdditionalShadowOn", additional->on);
    if (additional->on) {
        int n = additional->slice_count;
        mat4 matrices[MH_MAX_ADDITIONAL_SLICES];
        vec4 rects[MH_MAX_ADDITIONAL_SLICES];
        for (int i = 0; i < n; i++) {
            matrices[i] = additional->slices[i].world_to_shadow;
            rects[i] = additional->slices[i].atlas_rect;
        }
        int slice[MH_MAX_ADDITIONAL_LIGHTS];
        vec2 params[MH_MAX_ADDITIONAL_LIGHTS];
        for (int i = 0; i < count; i++) {
            slice[i] = additional->first_slice[i];
            params[i] = v2(additional->strength[i], (float)additional->filter[i]);
        }
        mh_set_m4v("uSliceMatrix", matrices, n);
        mh_set_v4v("uSliceRect", rects, n);
        mh_set_f("uAdditionalShadowTexel", 1.0f / (float)additional->atlas_size);
        if (count > 0) {
            mh_set_iv("uLightShadowSlice", slice, count);
            mh_set_v2v("uLightShadowParams", params, count);
        }
    }
}

static void mh_draw_skybox(mh_renderer *r, const mh_scene *scene, const mh_environment *env, mat4 view_projection)
{
    /* At the far plane, behind everything, without writing depth. */
    mat4 inverse;
    m4_invert(view_projection, &inverse);
    glDisable(GL_CULL_FACE);
    glDepthMask(GL_FALSE);
    glDepthFunc(GL_LEQUAL);
    mh_use(r->sky_program);
    mh_set_m4("uInverseViewProjection", inverse);
    mh_set_v3("uToSun", env->to_sun);
    mh_set_v3("uSunColor", env->sun_color);
    mh_set_i("uSunDisk", scene->sun_disk);
    mh_set_f("uSunSize", scene->sun_size);
    mh_set_f("uSunSizeConvergence", scene->sun_size_convergence);
    mh_set_f("uAtmosphereThickness", scene->atmosphere_thickness);
    mh_set_v3("uSkyTintGamma", scene->sky_tint_gamma);
    mh_set_v3("uGroundColor", scene->ground_color);
    mh_set_f("uExposure", scene->exposure);
    glBindVertexArray(r->empty_vao);
    glDrawArrays(GL_TRIANGLES, 0, 3);
    glDepthFunc(GL_LESS);
    glDepthMask(GL_TRUE);
}

/* One frame of the scene into the renderer's output texture, like SceneRenderer.Render. */
static void mh_render_frame(mh_renderer *r, const mh_game *game, const mh_scene *scene, const mh_view *view)
{
    mat4 *world = malloc(sizeof(mat4) * (size_t)maxi(1, scene->entity_count));
    mh_draw *draws = malloc(sizeof(mh_draw) * (size_t)maxi(1, scene->renderer_count));
    mh_world_matrices(scene, world);
    mh_environment env;
    mh_gather_environment(scene, world, &env);

    int draw_count = 0;
    mh_bounds casters = { 0, { 0, 0, 0 }, { 0, 0, 0 } };
    for (int i = 0; i < scene->renderer_count; i++) {
        const mh_mesh_renderer *renderer = &scene->renderers[i];
        const mh_gpu_mesh *mesh = mh_renderer_mesh(r, renderer->mesh);
        if (!mesh)
            continue;
        mh_draw *d = &draws[draw_count++];
        d->mesh = mesh;
        d->model = world[renderer->entity];
        d->normal_matrix = m4_normal_matrix(d->model);
        d->renderer = renderer;
        if (renderer->cast_shadows)
            casters = mh_bounds_encapsulate(casters, mh_renderer_bounds(renderer, d->model));
    }

    mh_main_shadows main_shadows;
    mh_additional_shadows additional;
    mh_main_light_shadows(&env, view, &game->rendering, casters, &main_shadows);
    mh_additional_light_shadows(&env, &game->rendering, casters, &additional);
    mh_render_shadows(r, &env, &main_shadows, &additional, draws, draw_count);

    glBindFramebuffer(GL_FRAMEBUFFER, r->scene_framebuffer);
    glViewport(0, 0, r->width, r->height);
    glDisable(GL_SCISSOR_TEST);
    glEnable(GL_DEPTH_TEST);
    glDepthFunc(GL_LESS);
    glDepthMask(GL_TRUE);
    glDisable(GL_BLEND);
    vec3 clear = view->clear_flags == MH_CLEAR_SOLID_COLOR ? view->background : v3(0, 0, 0);
    glClearColor(clear.x, clear.y, clear.z, 1.0f);
    glClear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

    mh_use(r->mesh_program);
    mh_set_lighting(scene, &env, &main_shadows, &additional);
    if (main_shadows.on) {
        glActiveTexture(GL_TEXTURE5);
        glBindTexture(GL_TEXTURE_2D, r->main_atlas.texture);
    }
    if (additional.on) {
        glActiveTexture(GL_TEXTURE6);
        glBindTexture(GL_TEXTURE_2D, r->additional_atlas.texture);
    }

    mat4 view_projection = m4_mul(view->view, view->projection);
    if (view->clear_flags == MH_CLEAR_SKYBOX)
        mh_draw_skybox(r, scene, &env, view_projection);

    glEnable(GL_CULL_FACE);
    glCullFace(GL_BACK);
    glFrontFace(GL_CCW);
    mh_use(r->mesh_program);
    mh_set_m4("uViewProjection", view_projection);
    mh_set_v3("uCameraPosition", view->position);
    for (int i = 0; i < draw_count; i++) {
        const mh_mesh_renderer *renderer = draws[i].renderer;
        mh_set_m4("uModel", draws[i].model);
        mh_set_m4("uNormalMatrix", draws[i].normal_matrix);
        mh_set_v4("uBaseColor", renderer->color);
        mh_set_f("uMetallic", renderer->metallic);
        mh_set_f("uSmoothness", renderer->smoothness);
        mh_set_i("uReceiveShadows", renderer->receive_shadows ? 1 : 0);
        mh_draw_mesh(draws[i].mesh);
    }

    mh_post_run(r, view, &scene->post);
    glBindVertexArray(0);
    glUseProgram(0);
    glActiveTexture(GL_TEXTURE0);
    glDisable(GL_DEPTH_TEST);
    glDisable(GL_CULL_FACE);
    free(world);
    free(draws);
}
