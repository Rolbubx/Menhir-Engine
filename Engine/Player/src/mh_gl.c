/* OpenGL 3.3 helpers: shader programs, uniforms, textures, framebuffers, meshes and the pool of
   render textures the post-processing passes draw into. */
#include <stdlib.h>
#include "mh_data.h"

static GLuint mh_compile(GLenum type, const char *source, const char *name)
{
    GLuint shader = glCreateShader(type);
    glShaderSource(shader, 1, &source, NULL);
    glCompileShader(shader);
    GLint ok = 0;
    glGetShaderiv(shader, GL_COMPILE_STATUS, &ok);
    if (!ok) {
        char log[4096];
        glGetShaderInfoLog(shader, sizeof log, NULL, log);
        fprintf(stderr, "menhir: shader %s does not compile:\n%s\n", name, log);
        exit(1);
    }
    return shader;
}

static GLuint mh_program(const char *vertex, const char *fragment, const char *name)
{
    GLuint vs = mh_compile(GL_VERTEX_SHADER, vertex, name);
    GLuint fs = mh_compile(GL_FRAGMENT_SHADER, fragment, name);
    GLuint program = glCreateProgram();
    glAttachShader(program, vs);
    glAttachShader(program, fs);
    glLinkProgram(program);
    glDeleteShader(vs);
    glDeleteShader(fs);
    GLint ok = 0;
    glGetProgramiv(program, GL_LINK_STATUS, &ok);
    if (!ok) {
        char log[4096];
        glGetProgramInfoLog(program, sizeof log, NULL, log);
        fprintf(stderr, "menhir: shader %s does not link:\n%s\n", name, log);
        exit(1);
    }
    return program;
}

/* Uniform setters, by name, on the program in use (like the editor's GLShaderProgram). */
static GLuint mh_current_program;

static void mh_use(GLuint program)
{
    mh_current_program = program;
    glUseProgram(program);
}

static GLint mh_loc(const char *name) { return glGetUniformLocation(mh_current_program, name); }
static void mh_set_i(const char *name, int v) { glUniform1i(mh_loc(name), v); }
static void mh_set_f(const char *name, float v) { glUniform1f(mh_loc(name), v); }
static void mh_set_v2(const char *name, vec2 v) { glUniform2f(mh_loc(name), v.x, v.y); }
static void mh_set_v3(const char *name, vec3 v) { glUniform3f(mh_loc(name), v.x, v.y, v.z); }
static void mh_set_v4(const char *name, vec4 v) { glUniform4f(mh_loc(name), v.x, v.y, v.z, v.w); }
static void mh_set_m4(const char *name, mat4 m) { glUniformMatrix4fv(mh_loc(name), 1, GL_FALSE, m.m); }
static void mh_set_m4v(const char *name, const mat4 *m, int n) { glUniformMatrix4fv(mh_loc(name), n, GL_FALSE, m->m); }
static void mh_set_v4v(const char *name, const vec4 *v, int n) { glUniform4fv(mh_loc(name), n, &v->x); }
static void mh_set_v2v(const char *name, const vec2 *v, int n) { glUniform2fv(mh_loc(name), n, &v->x); }
static void mh_set_fv(const char *name, const float *v, int n) { glUniform1fv(mh_loc(name), n, v); }
static void mh_set_iv(const char *name, const int *v, int n) { glUniform1iv(mh_loc(name), n, v); }

static void mh_set_texture(const char *name, int unit, GLuint texture)
{
    glActiveTexture(GL_TEXTURE0 + (GLenum)unit);
    glBindTexture(GL_TEXTURE_2D, texture);
    glUniform1i(mh_loc(name), unit);
}

static void mh_set_uniform(const mh_uniform *u)
{
    switch (u->kind) {
    case 0: mh_set_i(u->name, (int)u->value.x); break;
    case 1: mh_set_f(u->name, u->value.x); break;
    case 2: mh_set_v2(u->name, v2(u->value.x, u->value.y)); break;
    case 3: mh_set_v3(u->name, v3(u->value.x, u->value.y, u->value.z)); break;
    default: mh_set_v4(u->name, u->value); break;
    }
}

/* A 2D texture, clamped (or repeating), with linear (or nearest) filtering. */
static GLuint mh_texture(int width, int height, GLint format, GLenum pixel_format, GLenum type, const void *pixels,
                         int nearest, int repeat)
{
    GLuint texture;
    glGenTextures(1, &texture);
    glBindTexture(GL_TEXTURE_2D, texture);
    glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
    glTexImage2D(GL_TEXTURE_2D, 0, format, width, height, 0, pixel_format, type, pixels);
    GLint filter = nearest ? GL_NEAREST : GL_LINEAR;
    GLint wrap = repeat ? GL_REPEAT : GL_CLAMP_TO_EDGE;
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, filter);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, filter);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, wrap);
    glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, wrap);
    glBindTexture(GL_TEXTURE_2D, 0);
    return texture;
}

static GLuint mh_framebuffer(GLuint color, GLuint depth_stencil, GLuint depth)
{
    GLuint framebuffer;
    glGenFramebuffers(1, &framebuffer);
    glBindFramebuffer(GL_FRAMEBUFFER, framebuffer);
    if (color)
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_COLOR_ATTACHMENT0, GL_TEXTURE_2D, color, 0);
    if (depth_stencil)
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_STENCIL_ATTACHMENT, GL_TEXTURE_2D, depth_stencil, 0);
    if (depth) {
        glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT, GL_TEXTURE_2D, depth, 0);
        glDrawBuffer(GL_NONE);
        glReadBuffer(GL_NONE);
    }
    GLenum status = glCheckFramebufferStatus(GL_FRAMEBUFFER);
    glBindFramebuffer(GL_FRAMEBUFFER, 0);
    if (status != GL_FRAMEBUFFER_COMPLETE) {
        fprintf(stderr, "menhir: framebuffer incomplete (0x%x)\n", status);
        exit(1);
    }
    return framebuffer;
}

typedef struct {
    uint32_t id;
    GLuint vao, vbo, ebo;
    GLsizei index_count;
} mh_gpu_mesh;

static mh_gpu_mesh mh_upload_mesh(uint32_t id, const mh_mesh_data *data)
{
    mh_gpu_mesh mesh = { id, 0, 0, 0, (GLsizei)data->index_count };
    glGenVertexArrays(1, &mesh.vao);
    glBindVertexArray(mesh.vao);
    glGenBuffers(1, &mesh.vbo);
    glBindBuffer(GL_ARRAY_BUFFER, mesh.vbo);
    glBufferData(GL_ARRAY_BUFFER, (GLsizeiptr)(sizeof(float) * 6 * data->vertex_count), data->vertices, GL_STATIC_DRAW);
    glGenBuffers(1, &mesh.ebo);
    glBindBuffer(GL_ELEMENT_ARRAY_BUFFER, mesh.ebo);
    glBufferData(GL_ELEMENT_ARRAY_BUFFER, (GLsizeiptr)(sizeof(uint32_t) * data->index_count), data->indices, GL_STATIC_DRAW);
    glEnableVertexAttribArray(0);
    glVertexAttribPointer(0, 3, GL_FLOAT, GL_FALSE, 6 * sizeof(float), (void *)0);
    glEnableVertexAttribArray(1);
    glVertexAttribPointer(1, 3, GL_FLOAT, GL_FALSE, 6 * sizeof(float), (void *)(3 * sizeof(float)));
    glBindVertexArray(0);
    return mesh;
}

static void mh_draw_mesh(const mh_gpu_mesh *mesh)
{
    glBindVertexArray(mesh->vao);
    glDrawElements(GL_TRIANGLES, mesh->index_count, GL_UNSIGNED_INT, NULL);
}

/* Render textures for the post-processing passes, kept between frames (GLRenderTexturePool). */
#define MH_POOL_SIZE 96

typedef struct {
    GLuint texture, framebuffer;
    int width, height, single_channel, in_use, last_used;
} mh_rt;

typedef struct {
    mh_rt entries[MH_POOL_SIZE];
    int count, frame;
} mh_rt_pool;

static mh_rt *mh_pool_get(mh_rt_pool *pool, int width, int height, int single_channel)
{
    width = maxi(1, width);
    height = maxi(1, height);
    for (int i = 0; i < pool->count; i++) {
        mh_rt *e = &pool->entries[i];
        if (!e->in_use && e->width == width && e->height == height && e->single_channel == single_channel) {
            e->in_use = 1;
            e->last_used = pool->frame;
            return e;
        }
    }
    if (pool->count == MH_POOL_SIZE) {
        fprintf(stderr, "menhir: too many post-processing textures\n");
        exit(1);
    }
    mh_rt *e = &pool->entries[pool->count++];
    e->texture = single_channel ? mh_texture(width, height, GL_R16F, GL_RED, GL_HALF_FLOAT, NULL, 0, 0)
                                : mh_texture(width, height, GL_RGBA16F, GL_RGBA, GL_HALF_FLOAT, NULL, 0, 0);
    e->framebuffer = mh_framebuffer(e->texture, 0, 0);
    e->width = width;
    e->height = height;
    e->single_channel = single_channel;
    e->in_use = 1;
    e->last_used = pool->frame;
    return e;
}

/* Gives everything back; frees what was not used for a while (e.g. after a resize). */
static void mh_pool_release_all(mh_rt_pool *pool)
{
    pool->frame++;
    int kept = 0;
    for (int i = 0; i < pool->count; i++) {
        mh_rt e = pool->entries[i];
        e.in_use = 0;
        if (pool->frame - e.last_used > 120) {
            glDeleteFramebuffers(1, &e.framebuffer);
            glDeleteTextures(1, &e.texture);
            continue;
        }
        pool->entries[kept++] = e;
    }
    pool->count = kept;
}
