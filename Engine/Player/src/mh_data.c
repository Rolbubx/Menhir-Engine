/* Reads GameData.binchunk. The table is read when the file opens; each chunk is read from disk
   when the game asks for it (a scene when it loads, then its meshes and textures). */
#include <stdlib.h>
#include <string.h>
#include "mh_data.h"

static void mh_fatal(const char *format, const char *detail)
{
    fprintf(stderr, "menhir: ");
    fprintf(stderr, format, detail);
    fprintf(stderr, "\n");
    exit(1);
}

static void *mh_alloc(size_t size)
{
    void *p = calloc(1, size ? size : 1);
    if (!p)
        mh_fatal("%s", "out of memory");
    return p;
}

/* A cursor over a chunk's bytes; reading past the end is a corrupt file. */
typedef struct {
    const unsigned char *data;
    size_t size, at;
    const char *what;
} mh_reader;

static const unsigned char *mh_take(mh_reader *r, size_t n)
{
    if (n > r->size - r->at)
        mh_fatal("the %s chunk of the data file is cut off", r->what);
    const unsigned char *p = r->data + r->at;
    r->at += n;
    return p;
}

static uint32_t mh_u32(mh_reader *r)
{
    const unsigned char *p = mh_take(r, 4);
    return (uint32_t)p[0] | (uint32_t)p[1] << 8 | (uint32_t)p[2] << 16 | (uint32_t)p[3] << 24;
}

static uint64_t mh_u64_bytes(const unsigned char *p)
{
    uint64_t v = 0;
    for (int i = 7; i >= 0; i--)
        v = v << 8 | p[i];
    return v;
}

static int mh_i32(mh_reader *r) { return (int)(int32_t)mh_u32(r); }

static float mh_f32(mh_reader *r)
{
    uint32_t bits = mh_u32(r);
    float f;
    memcpy(&f, &bits, 4);
    return f;
}

static vec2 mh_vec2(mh_reader *r) { float x = mh_f32(r); return v2(x, mh_f32(r)); }
static vec3 mh_vec3(mh_reader *r) { float x = mh_f32(r), y = mh_f32(r); return v3(x, y, mh_f32(r)); }
static vec4 mh_vec4(mh_reader *r) { float x = mh_f32(r), y = mh_f32(r), z = mh_f32(r); return v4(x, y, z, mh_f32(r)); }
static quat mh_quat(mh_reader *r) { vec4 v = mh_vec4(r); quat q = { v.x, v.y, v.z, v.w }; return q; }

static char *mh_string(mh_reader *r)
{
    uint32_t length = mh_u32(r);
    const unsigned char *bytes = mh_take(r, length);
    char *s = mh_alloc(length + 1);
    memcpy(s, bytes, length);
    return s;
}

/* A count of items of at least item_size bytes each, checked against what is left. */
static int mh_count(mh_reader *r, size_t item_size)
{
    uint32_t n = mh_u32(r);
    if (item_size && n > (r->size - r->at) / item_size)
        mh_fatal("the %s chunk of the data file is cut off", r->what);
    return (int)n;
}

static void mh_data_open(mh_data *data, const char *path)
{
    data->file = fopen(path, "rb");
    if (!data->file)
        mh_fatal("cannot open the data file %s", path);
    unsigned char header[16];
    if (fread(header, 1, 16, data->file) != 16)
        mh_fatal("%s is not a Menhir data file", path);
    mh_reader r = { header, 16, 0, "header" };
    if (mh_u32(&r) != MH_FOURCC('M', 'N', 'H', 'R'))
        mh_fatal("%s is not a Menhir data file", path);
    if (mh_u32(&r) != 1)
        mh_fatal("%s was made by another version of Menhir", path);
    data->count = mh_u32(&r);
    if (data->count > 1u << 20)
        mh_fatal("%s is corrupt", path);
    data->entries = mh_alloc(sizeof(mh_chunk_entry) * data->count);
    for (uint32_t i = 0; i < data->count; i++) {
        unsigned char e[24];
        if (fread(e, 1, 24, data->file) != 24)
            mh_fatal("the chunk table of %s is cut off", path);
        mh_reader er = { e, 24, 0, "table" };
        data->entries[i].type = mh_u32(&er);
        data->entries[i].id = mh_u32(&er);
        data->entries[i].offset = mh_u64_bytes(e + 8);
        data->entries[i].size = mh_u64_bytes(e + 16);
    }
}

/* Reads a whole chunk into memory (free it with free()), or returns NULL when there is none. */
static unsigned char *mh_data_load(mh_data *data, uint32_t type, uint32_t id, size_t *size)
{
    for (uint32_t i = 0; i < data->count; i++) {
        mh_chunk_entry *e = &data->entries[i];
        if (e->type != type || e->id != id)
            continue;
        unsigned char *bytes = mh_alloc((size_t)e->size);
        if (fseek(data->file, (long)e->offset, SEEK_SET) != 0 ||
            fread(bytes, 1, (size_t)e->size, data->file) != e->size)
            mh_fatal("%s", "the data file is cut off");
        *size = (size_t)e->size;
        return bytes;
    }
    return NULL;
}

static unsigned char *mh_data_require(mh_data *data, uint32_t type, uint32_t id, size_t *size, const char *what)
{
    unsigned char *bytes = mh_data_load(data, type, id, size);
    if (!bytes)
        mh_fatal("the data file has no %s", what);
    return bytes;
}

static void mh_load_game(mh_data *data, mh_game *game)
{
    size_t size;
    unsigned char *bytes = mh_data_require(data, MH_CHUNK_GAME, 0, &size, "game settings");
    mh_reader r = { bytes, size, 0, "game" };
    game->width = (int)mh_u32(&r);
    game->height = (int)mh_u32(&r);
    game->title = mh_string(&r);
    game->scene_count = (int)mh_u32(&r);
    mh_rendering *s = &game->rendering;
    s->main_shadows = (int)mh_u32(&r);
    s->main_resolution = (int)mh_u32(&r);
    s->additional_shadows = (int)mh_u32(&r);
    s->additional_resolution = (int)mh_u32(&r);
    s->max_distance = mh_f32(&r);
    s->cascade_count = clampi((int)mh_u32(&r), 1, 4);
    for (int i = 0; i < 4; i++)
        s->cascade_ends[i] = mh_f32(&r);
    s->last_border = mh_f32(&r);
    s->depth_bias = mh_f32(&r);
    s->normal_bias = mh_f32(&r);
    s->soft_taps = (int)mh_u32(&r);
    free(bytes);
}

static void mh_read_post(mh_reader *r, mh_post *p)
{
    p->dof_mode = (int)mh_u32(r);
    p->dof_gaussian_start = mh_f32(r);
    p->dof_gaussian_end = mh_f32(r);
    p->dof_gaussian_max_radius = mh_f32(r);
    p->dof_high_quality = (int)mh_u32(r);
    p->dof_focus_distance = mh_f32(r);
    p->dof_lens_coefficient = mh_f32(r);
    for (int i = 0; i < MH_BOKEH_KERNEL; i++)
        p->dof_kernel[i] = mh_vec2(r);

    p->motion_on = (int)mh_u32(r);
    p->motion_intensity = mh_f32(r);
    p->motion_clamp = mh_f32(r);
    p->motion_samples = (int)mh_u32(r);

    p->panini_on = (int)mh_u32(r);
    p->panini_distance = mh_f32(r);
    p->panini_crop = mh_f32(r);

    p->bloom_on = (int)mh_u32(r);
    p->bloom_downscale = (int)mh_u32(r);
    p->bloom_max_iterations = (int)mh_u32(r);
    p->bloom_threshold = mh_f32(r);
    p->bloom_knee = mh_f32(r);
    p->bloom_clamp = mh_f32(r);
    p->bloom_scatter = mh_f32(r);
    p->bloom_high_quality = (int)mh_u32(r);
    p->bloom_intensity = mh_f32(r);
    p->bloom_tint = mh_vec3(r);

    p->vignette_on = (int)mh_u32(r);
    p->vignette_color = mh_vec3(r);
    p->vignette_center = mh_vec2(r);
    p->vignette_intensity = mh_f32(r);
    p->vignette_smoothness = mh_f32(r);
    p->vignette_rounded = (int)mh_u32(r);

    p->lut_texture = mh_u32(r);
    p->lut_contribution = mh_f32(r);

    p->grain_texture = mh_u32(r);
    p->grain_intensity = mh_f32(r);
    p->grain_response = mh_f32(r);

    p->uniform_count = mh_count(r, 24);
    p->uniforms = mh_alloc(sizeof(mh_uniform) * (size_t)p->uniform_count);
    for (int i = 0; i < p->uniform_count; i++) {
        p->uniforms[i].name = mh_string(r);
        p->uniforms[i].kind = (int)mh_u32(r);
        p->uniforms[i].value = mh_vec4(r);
    }

    p->curves_on = (int)mh_u32(r);
    if (p->curves_on)
        for (int i = 0; i < MH_CURVE_SAMPLES * 8; i++)
            p->curves[i] = mh_f32(r);
}

static void mh_load_scene(mh_data *data, int index, mh_scene *scene)
{
    size_t size;
    unsigned char *bytes = mh_data_require(data, MH_CHUNK_SCENE, (uint32_t)index, &size, "such scene");
    mh_reader r = { bytes, size, 0, "scene" };
    memset(scene, 0, sizeof(*scene));
    scene->name = mh_string(&r);
    scene->sun_disk = (int)mh_u32(&r);
    scene->sun_size = mh_f32(&r);
    scene->sun_size_convergence = mh_f32(&r);
    scene->atmosphere_thickness = mh_f32(&r);
    scene->sky_tint_gamma = mh_vec3(&r);
    scene->ground_color = mh_vec3(&r);
    scene->exposure = mh_f32(&r);
    scene->ambient_top = mh_vec3(&r);
    scene->ambient_horizon = mh_vec3(&r);
    scene->ambient_bottom = mh_vec3(&r);

    scene->entity_count = mh_count(&r, 44);
    scene->entities = mh_alloc(sizeof(mh_entity) * (size_t)scene->entity_count);
    for (int i = 0; i < scene->entity_count; i++) {
        mh_entity *e = &scene->entities[i];
        e->parent = mh_i32(&r);
        if (e->parent < -1 || e->parent >= scene->entity_count)
            e->parent = -1;
        e->position = mh_vec3(&r);
        e->rotation = mh_quat(&r);
        e->scale = mh_vec3(&r);
    }

#define MH_ENTITY_INDEX(field)                                     \
    do {                                                           \
        field = (int)mh_u32(&r);                                   \
        if (field < 0 || field >= scene->entity_count)             \
            mh_fatal("%s", "the scene refers to a missing entity"); \
    } while (0)

    scene->renderer_count = mh_count(&r, 40);
    scene->renderers = mh_alloc(sizeof(mh_mesh_renderer) * (size_t)scene->renderer_count);
    for (int i = 0; i < scene->renderer_count; i++) {
        mh_mesh_renderer *m = &scene->renderers[i];
        MH_ENTITY_INDEX(m->entity);
        m->mesh = mh_u32(&r);
        m->color = mh_vec4(&r);
        m->metallic = mh_f32(&r);
        m->smoothness = mh_f32(&r);
        m->cast_shadows = (int)mh_u32(&r);
        m->receive_shadows = (int)mh_u32(&r);
    }

    scene->light_count = mh_count(&r, 36);
    scene->lights = mh_alloc(sizeof(mh_light) * (size_t)scene->light_count);
    for (int i = 0; i < scene->light_count; i++) {
        mh_light *l = &scene->lights[i];
        MH_ENTITY_INDEX(l->entity);
        l->type = (int)mh_u32(&r);
        l->color = mh_vec3(&r);
        l->range = mh_f32(&r);
        l->spot_angle = mh_f32(&r);
        l->shadows = (int)mh_u32(&r);
        l->shadow_strength = mh_f32(&r);
    }

    scene->camera_count = mh_count(&r, 32);
    scene->cameras = mh_alloc(sizeof(mh_camera) * (size_t)scene->camera_count);
    for (int i = 0; i < scene->camera_count; i++) {
        mh_camera *c = &scene->cameras[i];
        MH_ENTITY_INDEX(c->entity);
        c->fov_degrees = mh_f32(&r);
        c->near = mh_f32(&r);
        c->far = mh_f32(&r);
        c->clear_flags = (int)mh_u32(&r);
        c->background = mh_vec3(&r);
    }
#undef MH_ENTITY_INDEX

    mh_read_post(&r, &scene->post);
    free(bytes);
}

static void mh_free_scene(mh_scene *scene)
{
    free(scene->name);
    free(scene->entities);
    free(scene->renderers);
    free(scene->lights);
    free(scene->cameras);
    for (int i = 0; i < scene->post.uniform_count; i++)
        free(scene->post.uniforms[i].name);
    free(scene->post.uniforms);
    memset(scene, 0, sizeof(*scene));
}

#ifndef MENHIR_HEADLESS
/* Returns 0 when the file has no such mesh. */
static int mh_load_mesh(mh_data *data, uint32_t id, mh_mesh_data *mesh)
{
    size_t size;
    unsigned char *bytes = mh_data_load(data, MH_CHUNK_MESH, id, &size);
    if (!bytes)
        return 0;
    mh_reader r = { bytes, size, 0, "mesh" };
    mesh->vertex_count = mh_u32(&r);
    mesh->index_count = mh_u32(&r);
    if ((uint64_t)mesh->vertex_count * 24 + (uint64_t)mesh->index_count * 4 > size - r.at)
        mh_fatal("%s", "the mesh chunk of the data file is cut off");
    mesh->vertices = mh_alloc(sizeof(float) * 6 * mesh->vertex_count);
    mesh->indices = mh_alloc(sizeof(uint32_t) * mesh->index_count);
    for (uint32_t i = 0; i < mesh->vertex_count * 6; i++)
        mesh->vertices[i] = mh_f32(&r);
    for (uint32_t i = 0; i < mesh->index_count; i++) {
        mesh->indices[i] = mh_u32(&r);
        if (mesh->indices[i] >= mesh->vertex_count)
            mh_fatal("%s", "a mesh of the data file has a bad index");
    }
    free(bytes);
    return 1;
}

static int mh_load_texture(mh_data *data, uint32_t id, mh_texture_data *texture)
{
    size_t size;
    unsigned char *bytes = mh_data_load(data, MH_CHUNK_TEXTURE, id, &size);
    if (!bytes)
        return 0;
    mh_reader r = { bytes, size, 0, "texture" };
    texture->width = (int)mh_u32(&r);
    texture->height = (int)mh_u32(&r);
    texture->channels = (int)mh_u32(&r);
    texture->repeat = (int)mh_u32(&r);
    if (texture->channels != 1 && texture->channels != 4)
        mh_fatal("%s", "a texture of the data file has an unknown format");
    size_t pixels = (size_t)texture->width * (size_t)texture->height * (size_t)texture->channels;
    const unsigned char *src = mh_take(&r, pixels);
    texture->pixels = mh_alloc(pixels);
    memcpy(texture->pixels, src, pixels);
    free(bytes);
    return 1;
}
#endif
