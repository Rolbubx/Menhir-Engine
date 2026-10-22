/* Menhir game player: opens the window, loads GameData.binchunk and renders its scenes with the
   Menhir Universal Pipeline. File > Build in the editor turns this file and the ones it includes
   into one standalone main.c (with the shaders), compiled against GLFW.

   Options:
     --data <file>        data file (default: GameData.binchunk next to the executable)
     --scene <index>      scene to start with (default: 0)
     --size <w>x<h>       window size (default: from the build settings)
     --frames <n>         quit after n frames
     --screenshot <file>  save the last frame as a binary PPM image when the game quits
     --dump               print the first frame's matrices, lights and shadows, without a window
     --simulate <file>    play a script of input events without a window (see mh_dump.c)
   --dump and --simulate are the only options of a MENHIR_HEADLESS build, used by the editor's tests.
   A game with scripts is built with MENHIR_SCRIPTS and linked with them and Engine/Scripting's
   menhir_script.c (see mh_scripts.c). */
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

#ifndef MENHIR_HEADLESS
#define GLAD_GL_IMPLEMENTATION
#include "glad/gl.h"
#include <GLFW/glfw3.h>
#include "shaders.gen.h"
#endif

#include "mh_math.h"
#include "mh_data.h"
#include "mh_data.c"
#include "mh_scene.c"
#include "mh_shadows.c"
#include "mh_input.c"
#include "mh_world.c"
#ifdef MENHIR_SCRIPTS
#include "mh_scripts.c"
#endif
#include "mh_dump.c"

#ifndef MENHIR_HEADLESS
#include "mh_gl.c"
#include "mh_renderer.c"
#include "mh_post.c"
#endif

typedef struct {
    const char *data, *screenshot, *simulate;
    int scene, width, height, frames, dump;
} mh_options;

static void mh_usage(void)
{
    fprintf(stderr, "usage: game [--data file] [--scene n] [--size WxH] [--frames n] [--screenshot file.ppm] [--dump] "
                    "[--simulate file]\n");
    exit(2);
}

static mh_options mh_parse(int argc, char **argv)
{
    mh_options o = { NULL, NULL, NULL, 0, 0, 0, 0, 0 };
    for (int i = 1; i < argc; i++) {
        const char *a = argv[i];
        int more = i + 1 < argc;
        if (!strcmp(a, "--data") && more)
            o.data = argv[++i];
        else if (!strcmp(a, "--scene") && more)
            o.scene = atoi(argv[++i]);
        else if (!strcmp(a, "--size") && more) {
            if (sscanf(argv[++i], "%dx%d", &o.width, &o.height) != 2 || o.width <= 0 || o.height <= 0)
                mh_usage();
        } else if (!strcmp(a, "--frames") && more)
            o.frames = atoi(argv[++i]);
        else if (!strcmp(a, "--screenshot") && more)
            o.screenshot = argv[++i];
        else if (!strcmp(a, "--dump"))
            o.dump = 1;
        else if (!strcmp(a, "--simulate") && more)
            o.simulate = argv[++i];
        else
            mh_usage();
    }
    return o;
}

/* GameData.binchunk in the executable's folder, so the game starts from anywhere. */
static char *mh_default_data_path(const char *argv0)
{
    const char *name = "GameData.binchunk";
    const char *slash = strrchr(argv0, '/');
#ifdef _WIN32
    const char *backslash = strrchr(argv0, '\\');
    if (backslash && (!slash || backslash > slash))
        slash = backslash;
#endif
    size_t dir = slash ? (size_t)(slash - argv0 + 1) : 0;
    char *path = malloc(dir + strlen(name) + 1);
    if (!path)
        exit(1);
    memcpy(path, argv0, dir);
    strcpy(path + dir, name);
    return path;
}

#ifndef MENHIR_HEADLESS
static void mh_save_ppm(const char *path, int width, int height, GLuint framebuffer)
{
    unsigned char *pixels = malloc((size_t)width * (size_t)height * 3);
    if (!pixels)
        exit(1);
    glBindFramebuffer(GL_READ_FRAMEBUFFER, framebuffer);
    glPixelStorei(GL_PACK_ALIGNMENT, 1);
    glReadPixels(0, 0, width, height, GL_RGB, GL_UNSIGNED_BYTE, pixels);
    FILE *f = fopen(path, "wb");
    if (!f) {
        fprintf(stderr, "menhir: cannot write %s\n", path);
        exit(1);
    }
    fprintf(f, "P6\n%d %d\n255\n", width, height);
    for (int y = height - 1; y >= 0; y--) /* OpenGL's rows go bottom-up */
        fwrite(pixels + (size_t)y * (size_t)width * 3, 1, (size_t)width * 3, f);
    fclose(f);
    free(pixels);
}

static float mh_scroll_steps;

static void mh_on_scroll(GLFWwindow *window, double x, double y)
{
    (void)window;
    (void)x;
    mh_scroll_steps += (float)y;
}

/* Reads the devices into this frame's input: keys, mouse, and the first gamepad GLFW knows. */
static void mh_read_devices(GLFWwindow *window, mh_input_frame *frame, double *last_x, double *last_y, int *has_last)
{
    for (int key = GLFW_KEY_SPACE; key <= GLFW_KEY_LAST; key++)
        frame->keys[key] = glfwGetKey(window, key) == GLFW_PRESS;
    for (int b = 0; b < MH_MOUSE_BUTTONS; b++)
        frame->mouse[b] = glfwGetMouseButton(window, b) == GLFW_PRESS;

    double x, y;
    int window_width, window_height;
    glfwGetCursorPos(window, &x, &y);
    glfwGetWindowSize(window, &window_width, &window_height);
    frame->mouse_x = (float)x;
    frame->mouse_y = (float)(window_height - y); /* from the bottom, like Unity's mousePosition */
    frame->mouse_dx = *has_last ? (float)(x - *last_x) : 0.0f;
    frame->mouse_dy = *has_last ? (float)(y - *last_y) : 0.0f;
    *last_x = x;
    *last_y = y;
    *has_last = 1;
    frame->scroll = mh_scroll_steps;
    mh_scroll_steps = 0.0f;

    memset(frame->pad, 0, sizeof frame->pad);
    memset(frame->pad_axes, 0, sizeof frame->pad_axes);
    for (int jid = GLFW_JOYSTICK_1; jid <= GLFW_JOYSTICK_LAST; jid++) {
        GLFWgamepadstate pad;
        if (!glfwJoystickIsGamepad(jid) || !glfwGetGamepadState(jid, &pad))
            continue;
        for (int b = 0; b < MH_GAMEPAD_BUTTONS; b++)
            frame->pad[b] = pad.buttons[b] == GLFW_PRESS;
        for (int a = 0; a < 4; a++)
            frame->pad_axes[a] = pad.axes[a];
        frame->pad_axes[4] = (pad.axes[GLFW_GAMEPAD_AXIS_LEFT_TRIGGER] + 1.0f) * 0.5f; /* -1..1 to 0..1 */
        frame->pad_axes[5] = (pad.axes[GLFW_GAMEPAD_AXIS_RIGHT_TRIGGER] + 1.0f) * 0.5f;
        break;
    }
}

static void mh_on_glfw_error(int code, const char *description)
{
    fprintf(stderr, "menhir: GLFW error %d: %s\n", code, description);
}
#endif

int main(int argc, char **argv)
{
    mh_options options = mh_parse(argc, argv);
#ifdef MENHIR_SCRIPTS
    mh_scripts_init(); /* the scripts get the engine before the first scene loads */
#endif
    if (options.dump)
        mh_log_mode = MH_LOG_STDERR;
    else if (options.simulate)
        mh_log_mode = MH_LOG_PREFIXED;
    char *path = options.data ? NULL : mh_default_data_path(argc > 0 ? argv[0] : "");
    mh_data data;
    mh_data_open(&data, options.data ? options.data : path);
    free(path);

    mh_game game;
    mh_load_game(&data, &game);
    if (options.scene < 0 || options.scene >= game.scene_count) {
        fprintf(stderr, "menhir: there is no scene %d (the game has %d)\n", options.scene, game.scene_count);
        return 1;
    }
    mh_world world;
    mh_world_init(&world, &data, &game, options.scene);
    int width = options.width ? options.width : game.width;
    int height = options.height ? options.height : game.height;

    if (options.dump) {
        mh_dump(&game, &world, width, height);
        mh_world_unload_all(&world);
        return 0;
    }
    if (options.simulate) {
        mh_simulate(&game, &world, options.simulate);
        mh_world_unload_all(&world);
        return 0;
    }

#ifdef MENHIR_HEADLESS
    fprintf(stderr, "menhir: this build can only --dump\n");
    return 2;
#else
    glfwSetErrorCallback(mh_on_glfw_error);
    if (!glfwInit())
        return 1;
    glfwWindowHint(GLFW_CONTEXT_VERSION_MAJOR, 3);
    glfwWindowHint(GLFW_CONTEXT_VERSION_MINOR, 3);
    glfwWindowHint(GLFW_OPENGL_PROFILE, GLFW_OPENGL_CORE_PROFILE);
    glfwWindowHint(GLFW_OPENGL_FORWARD_COMPAT, GLFW_TRUE);
    GLFWwindow *window = glfwCreateWindow(width, height, game.title, NULL, NULL);
    if (!window) {
        glfwTerminate();
        return 1;
    }
    glfwMakeContextCurrent(window);
    glfwSwapInterval(1);
    glfwSetScrollCallback(window, mh_on_scroll);
    if (!gladLoadGL((GLADloadfunc)glfwGetProcAddress)) {
        fprintf(stderr, "menhir: OpenGL 3.3 is not available\n");
        return 1;
    }

    mh_renderer renderer;
    mh_renderer_init(&renderer, &data);
    mh_input input;
    mh_input_init(&input, &game);
    mh_input_frame devices;
    memset(&devices, 0, sizeof devices);
    double last_x = 0, last_y = 0, last_time = glfwGetTime();
    int has_last = 0, captured = 0;
    for (int frame = 0; !glfwWindowShouldClose(window); frame++) {
        double now = glfwGetTime();
        float dt = (float)fmin(now - last_time, 0.1); /* a long pause (e.g. dragging the window) is not a jump */
        last_time = now;

        mh_read_devices(window, &devices, &last_x, &last_y, &has_last);
        if (devices.keys[GLFW_KEY_ESCAPE])
            glfwSetWindowShouldClose(window, GLFW_TRUE);
        mh_input_update(&input, &devices, dt);
        mh_world_update(&world, &input, dt);
        if (world.quit)
            glfwSetWindowShouldClose(window, GLFW_TRUE);
        if (world.changed)
            mh_renderer_release_unused(&renderer, &world);
        if (world.cursor_captured != captured) {
            /* Hidden while a fly camera looks around; the jump it makes is not a mouse movement. */
            captured = world.cursor_captured;
            glfwSetInputMode(window, GLFW_CURSOR, captured ? GLFW_CURSOR_DISABLED : GLFW_CURSOR_NORMAL);
            glfwGetCursorPos(window, &last_x, &last_y);
        }

        int fb_width, fb_height;
        glfwGetFramebufferSize(window, &fb_width, &fb_height);
        if (fb_width > 0 && fb_height > 0) {
            mh_renderer_resize(&renderer, fb_width, fb_height);
            mh_view view;
            mh_world_view(&world, (float)fb_width / (float)fb_height, &view);
            mh_render_frame(&renderer, &game, &world, &view);
            glBindFramebuffer(GL_READ_FRAMEBUFFER, renderer.output_framebuffer);
            glBindFramebuffer(GL_DRAW_FRAMEBUFFER, 0);
            glBlitFramebuffer(0, 0, fb_width, fb_height, 0, 0, fb_width, fb_height, GL_COLOR_BUFFER_BIT, GL_NEAREST);
        }
        if (options.frames && frame + 1 >= options.frames)
            break;
        glfwSwapBuffers(window);
        glfwPollEvents();
    }
    if (options.screenshot && renderer.width)
        mh_save_ppm(options.screenshot, renderer.width, renderer.height, renderer.output_framebuffer);

    glfwDestroyWindow(window);
    glfwTerminate();
    mh_world_unload_all(&world);
    return 0;
#endif
}
