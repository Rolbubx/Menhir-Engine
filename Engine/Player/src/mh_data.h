/* GameData.binchunk: the chunk table, and the game, scene, mesh and texture chunks the editor's
   GameDataExporter writes (layouts in Docs/BinChunk.md). */
#ifndef MH_DATA_H
#define MH_DATA_H

#include <stdint.h>
#include <stdio.h>
#include "mh_math.h"

#define MH_FOURCC(a, b, c, d) ((uint32_t)(a) | (uint32_t)(b) << 8 | (uint32_t)(c) << 16 | (uint32_t)(d) << 24)
#define MH_CHUNK_GAME MH_FOURCC('G', 'A', 'M', 'E')
#define MH_CHUNK_SCENE MH_FOURCC('S', 'C', 'N', 'E')
#define MH_CHUNK_MESH MH_FOURCC('M', 'E', 'S', 'H')
#define MH_CHUNK_TEXTURE MH_FOURCC('T', 'E', 'X', 'R')

#define MH_BOKEH_KERNEL 42
#define MH_CURVE_SAMPLES 128

typedef struct {
    uint32_t type, id;
    uint64_t offset, size;
} mh_chunk_entry;

/* The open data file: chunks are read from it when needed. */
typedef struct {
    FILE *file;
    uint32_t count;
    mh_chunk_entry *entries;
} mh_data;

typedef struct {
    int main_shadows, main_resolution;
    int additional_shadows, additional_resolution;
    float max_distance;
    int cascade_count;
    float cascade_ends[4];
    float last_border, depth_bias, normal_bias;
    int soft_taps; /* filter taps of soft shadows, 0 when the project turns them off */
} mh_rendering;

/* An Input Manager entry (the editor's InputAxis); buttons are codes (see mh_input.c), -1 for none. */
enum { MH_AXIS_KEYS, MH_AXIS_MOUSE, MH_AXIS_JOYSTICK };

typedef struct {
    char *name;
    int type;
    int negative, positive, alt_negative, alt_positive;
    float gravity, dead, sensitivity;
    int snap, invert, axis;
} mh_input_axis;

typedef struct {
    int width, height;
    char *title;
    int scene_count;
    mh_rendering rendering;
    int axis_count;
    mh_input_axis *axes;
} mh_game;

typedef struct {
    int parent; /* index, -1 for a root */
    vec3 position;
    quat rotation;
    vec3 scale;
} mh_entity;

typedef struct {
    int entity;
    uint32_t mesh;
    vec3 bounds_min, bounds_max; /* the mesh's box, for shadow fitting */
    vec4 color;
    float metallic, smoothness;
    int cast_shadows, receive_shadows;
} mh_mesh_renderer;

enum { MH_LIGHT_DIRECTIONAL, MH_LIGHT_POINT, MH_LIGHT_SPOT };
enum { MH_SHADOWS_NONE, MH_SHADOWS_HARD, MH_SHADOWS_SOFT };

typedef struct {
    int entity, type;
    vec3 color; /* times intensity */
    float range, spot_angle;
    int shadows;
    float shadow_strength;
} mh_light;

enum { MH_CLEAR_SKYBOX, MH_CLEAR_SOLID_COLOR };

typedef struct {
    int entity;
    float fov_degrees, near, far;
    int clear_flags;
    vec3 background;
} mh_camera;

typedef struct {
    char *name;
    int kind; /* 0 int, 1 float, 2 vec2, 3 vec3, 4 vec4 */
    vec4 value;
} mh_uniform;

/* Post-processing settings, ready for the passes (see GLPostProcessor in the editor). */
typedef struct {
    int dof_mode; /* 0 off, 1 gaussian, 2 bokeh */
    float dof_gaussian_start, dof_gaussian_end, dof_gaussian_max_radius;
    int dof_high_quality;
    float dof_focus_distance, dof_lens_coefficient;
    vec2 dof_kernel[MH_BOKEH_KERNEL];

    int motion_on;
    float motion_intensity, motion_clamp;
    int motion_samples;

    int panini_on;
    float panini_distance, panini_crop;

    int bloom_on, bloom_downscale, bloom_max_iterations;
    float bloom_threshold, bloom_knee, bloom_clamp, bloom_scatter;
    int bloom_high_quality;
    float bloom_intensity;
    vec3 bloom_tint;

    int vignette_on;
    vec3 vignette_color;
    vec2 vignette_center;
    float vignette_intensity, vignette_smoothness;
    int vignette_rounded;

    uint32_t lut_texture; /* 0: none */
    float lut_contribution;

    uint32_t grain_texture; /* 0: none */
    float grain_intensity, grain_response;

    int uniform_count;
    mh_uniform *uniforms; /* the uber pass's other settings */

    int curves_on;
    float curves[MH_CURVE_SAMPLES * 8];
} mh_post;

typedef struct {
    int entity;
    float move_speed, look_speed, fast_multiplier;
    float yaw, pitch; /* degrees, from the entity's rotation */
    char *horizontal_axis, *vertical_axis, *look_x_axis, *look_y_axis, *fast_button;
    int require_right_mouse_button;
} mh_fly_camera;

enum { MH_LOAD_SINGLE, MH_LOAD_ADDITIVE };

typedef struct {
    int entity;
    char *button;
    int scene, mode;
} mh_scene_switcher;

typedef struct {
    char *name;
    int sun_disk;
    float sun_size, sun_size_convergence, atmosphere_thickness;
    vec3 sky_tint_gamma, ground_color;
    float exposure;
    vec3 ambient_top, ambient_horizon, ambient_bottom;

    int entity_count;
    mh_entity *entities;
    int renderer_count;
    mh_mesh_renderer *renderers;
    int light_count;
    mh_light *lights;
    int camera_count;
    mh_camera *cameras;
    mh_post post;
    int fly_camera_count;
    mh_fly_camera *fly_cameras;
    int switcher_count;
    mh_scene_switcher *switchers;
} mh_scene;

typedef struct {
    uint32_t vertex_count, index_count;
    float *vertices; /* position, normal */
    uint32_t *indices;
} mh_mesh_data;

typedef struct {
    int width, height, channels, repeat;
    unsigned char *pixels;
} mh_texture_data;

#endif
