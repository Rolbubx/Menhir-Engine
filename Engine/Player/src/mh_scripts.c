/* The game's scripts (MENHIR_SCRIPTS builds): the project's .c files and the API's menhir_script.c
   (Engine/Scripting) are compiled with the player. This file gives them the engine (the mh_api table,
   see menhir_abi.h), creates the scripts of each loaded scene with the values the scene saved, and
   runs their callbacks, like the editor's Play mode (Runtime/PlaySession.cs). */
#include <errno.h>
#include <stdlib.h>
#include <string.h>
#include <menhir_abi.h>

/* From menhir_script.c. */
void mh_bind(const mh_api *api);
int32_t mh_script_type_count(void);
const mh_script_type *mh_script_type_at(int32_t index);

static mh_world *mh_host_world;
static const mh_input *mh_host_input; /* this frame's (empty while the first scene loads) */
static mh_input mh_no_input;

static const mh_script_type *mh_find_script_type(const char *name)
{
    for (int32_t i = 0; i < mh_script_type_count(); i++)
        if (!strcmp(mh_script_type_at(i)->name, name))
            return mh_script_type_at(i);
    return NULL;
}

/* ---- Saved values: converted to the field's kind like the editor's ScriptFields.FromJson ---- */

static int mh_parse_int(const char *text, int32_t *out)
{
    char *end;
    errno = 0;
    long long v = strtoll(text, &end, 10);
    if (end == text || *end || errno == ERANGE || v < INT32_MIN || v > INT32_MAX)
        return 0;
    *out = (int32_t)v;
    return 1;
}

static int mh_parse_float(const char *text, float *out)
{
    char *end;
    float v = strtof(text, &end);
    if (end == text || *end || !isfinite(v))
        return 0;
    *out = v;
    return 1;
}

static void mh_set_string(mh_loaded_scene *s, void *at, const char *text)
{
    size_t length = strlen(text);
    char *copy = mh_alloc(length + 1);
    memcpy(copy, text, length);
    s->strings = realloc(s->strings, sizeof(char *) * (size_t)(s->string_count + 1));
    if (!s->strings)
        mh_fatal("%s", "out of memory");
    s->strings[s->string_count++] = copy;
    memcpy(at, &copy, sizeof copy);
}

/* The field's default from its declaration. */
static void mh_apply_default(mh_loaded_scene *s, void *script, const mh_field *f)
{
    char *at = (char *)script + f->offset;
    float v[4];
    for (int i = 0; i < 4; i++)
        v[i] = (float)f->number[i];
    switch (f->kind) {
    case MH_FIELD_INT:
    case MH_FIELD_ENUM: {
        int32_t i = (int32_t)f->number[0];
        memcpy(at, &i, 4);
        break;
    }
    case MH_FIELD_FLOAT: memcpy(at, v, 4); break;
    case MH_FIELD_BOOL: *(unsigned char *)at = f->number[0] != 0; break;
    case MH_FIELD_VEC2: memcpy(at, v, 8); break;
    case MH_FIELD_VEC3: memcpy(at, v, 12); break;
    case MH_FIELD_COLOR: memcpy(at, v, 16); break;
    case MH_FIELD_STRING: mh_set_string(s, at, f->text ? f->text : ""); break;
    }
}

/* Returns 0 when the value does not fit the field (which then keeps its default). */
static int mh_apply_value(mh_loaded_scene *s, void *script, const mh_field *f, const mh_script_value *value)
{
    char *at = (char *)script + f->offset;
    int32_t i;
    float v[4];
    switch (f->kind) {
    case MH_FIELD_INT:
        if (value->kind != MH_VALUE_NUMBER || !mh_parse_int(value->text, &i))
            return 0;
        memcpy(at, &i, 4);
        return 1;
    case MH_FIELD_FLOAT:
        if (value->kind != MH_VALUE_NUMBER || !mh_parse_float(value->text, &v[0]))
            return 0;
        memcpy(at, v, 4);
        return 1;
    case MH_FIELD_BOOL:
        if (value->kind != MH_VALUE_BOOL)
            return 0;
        *(unsigned char *)at = value->boolean != 0;
        return 1;
    case MH_FIELD_VEC2:
    case MH_FIELD_VEC3:
    case MH_FIELD_COLOR: {
        int n = f->kind == MH_FIELD_VEC2 ? 2 : f->kind == MH_FIELD_VEC3 ? 3 : 4;
        if (value->kind != MH_VALUE_NUMBERS || value->count != n)
            return 0;
        for (int k = 0; k < n; k++)
            if (!mh_parse_float(value->items[k], &v[k]))
                return 0;
        memcpy(at, v, sizeof(float) * (size_t)n);
        return 1;
    }
    case MH_FIELD_STRING:
        if (value->kind != MH_VALUE_STRING)
            return 0;
        mh_set_string(s, at, value->text);
        return 1;
    case MH_FIELD_ENUM:
        /* By name (as the editor saves it), or by index. */
        if (value->kind == MH_VALUE_STRING) {
            for (i = 0; i < f->option_count && strcmp(f->options[i], value->text); i++)
                ;
        } else if (value->kind != MH_VALUE_NUMBER || !mh_parse_int(value->text, &i))
            return 0;
        if (i < 0 || i >= f->option_count)
            return 0;
        memcpy(at, &i, 4);
        return 1;
    }
    return 0;
}

/* ---- Lifecycle ----------------------------------------------------------------------------- */

static mh_callback mh_callback_of(const mh_script_type *type, int callback)
{
    switch (callback) {
    case MH_AWAKE: return type->awake;
    case MH_START: return type->start;
    case MH_UPDATE: return type->update;
    default: return type->late_update;
    }
}

static int32_t mh_handle(const mh_loaded_scene *s, int entity) { return (s->serial & 0x7FFF) << 16 | entity; }

static void mh_scripts_load(mh_world *w, mh_loaded_scene *s)
{
    int count = s->scene.script_count;
    s->types = mh_alloc(sizeof(*s->types) * (size_t)maxi(1, count));
    s->scripts = mh_alloc(sizeof(void *) * (size_t)maxi(1, count));
    s->started = mh_alloc((size_t)maxi(1, count));
    for (int i = 0; i < count; i++) {
        const mh_script_data *d = &s->scene.scripts[i];
        const char *entity = s->scene.entity_names[d->entity];
        const mh_script_type *type = mh_find_script_type(d->script_name);
        if (!type) {
            mh_log(MH_LOG_WARNING, "%s: the script %s does not exist.", entity, d->script_name);
            continue;
        }
        void *script = mh_alloc(type->size);
        for (int32_t f = 0; f < type->field_count; f++) {
            const mh_field *field = &type->fields[f];
            mh_apply_default(s, script, field);
            for (int v = 0; v < d->value_count; v++) {
                if (strcmp(field->name, d->values[v].name))
                    continue;
                if (!mh_apply_value(s, script, field, &d->values[v]))
                    mh_log(MH_LOG_WARNING, "%s: the saved value of %s.%s does not fit the field; it keeps its default.",
                           entity, d->script_name, field->name);
                break;
            }
        }
        s->types[i] = type;
        s->scripts[i] = script;
    }
    mh_host_world = w;
    if (!mh_host_input)
        mh_host_input = &mh_no_input;
    for (int i = 0; i < count; i++)
        if (s->types[i] && s->types[i]->awake)
            s->types[i]->awake(s->scripts[i], mh_handle(s, s->scene.scripts[i].entity));
}

static void mh_scripts_unload(mh_loaded_scene *s)
{
    for (int i = 0; i < s->scene.script_count && s->scripts; i++)
        free(s->scripts[i]);
    for (int i = 0; i < s->string_count; i++)
        free(s->strings[i]);
    free((void *)s->types);
    free(s->scripts);
    free(s->started);
    free(s->strings);
    s->types = NULL;
    s->scripts = NULL;
    s->started = NULL;
    s->strings = NULL;
    s->string_count = 0;
}

/* Start runs once, before a script's first Update. */
static void mh_scripts_run(mh_world *w, const mh_input *input, int callback)
{
    mh_host_world = w;
    mh_host_input = input;
    for (int si = 0; si < w->count; si++) {
        mh_loaded_scene *s = &w->scenes[si];
        for (int i = 0; i < s->scene.script_count; i++) {
            if (!s->types[i])
                continue;
            if (callback == MH_START) {
                if (s->started[i])
                    continue;
                s->started[i] = 1;
            } else if (!s->started[i])
                continue;
            mh_callback function = mh_callback_of(s->types[i], callback);
            if (function)
                function(s->scripts[i], mh_handle(s, s->scene.scripts[i].entity));
        }
    }
}

/* ---- The engine for the scripts (mh_api) --------------------------------------------------- */

static mh_loaded_scene *mh_resolve(int32_t handle, int *index)
{
    if (handle < 0)
        return NULL;
    int serial = handle >> 16, entity = handle & 0xFFFF;
    for (int i = 0; i < mh_host_world->count; i++) {
        mh_loaded_scene *s = &mh_host_world->scenes[i];
        if ((s->serial & 0x7FFF) == serial) {
            if (entity >= s->scene.entity_count)
                return NULL;
            *index = entity;
            return s;
        }
    }
    return NULL;
}

static mh_entity_data *mh_entity_of(int32_t handle, mh_loaded_scene **scene)
{
    int index;
    mh_loaded_scene *s = mh_resolve(handle, &index);
    if (!s) {
        mh_log(MH_LOG_ERROR, "Entity %d is not in a loaded scene.", (int)handle);
        return NULL;
    }
    if (scene)
        *scene = s;
    return &s->scene.entities[index];
}

static void mh_api_get_transform(int32_t entity, int32_t what, float *out)
{
    mh_entity_data *e = mh_entity_of(entity, NULL);
    if (!e) {
        /* Zero position, identity rotation, unit scale. */
        out[0] = out[1] = out[2] = what == 2 ? 1.0f : 0.0f;
        if (what == 1)
            out[3] = 1.0f;
        return;
    }
    if (what == 0)
        memcpy(out, &e->position, sizeof(float) * 3);
    else if (what == 1)
        memcpy(out, &e->rotation, sizeof(float) * 4);
    else
        memcpy(out, &e->scale, sizeof(float) * 3);
}

static void mh_api_set_transform(int32_t entity, int32_t what, const float *values)
{
    mh_entity_data *e = mh_entity_of(entity, NULL);
    if (!e)
        return;
    if (what == 0)
        memcpy(&e->position, values, sizeof(float) * 3);
    else if (what == 1)
        memcpy(&e->rotation, values, sizeof(float) * 4);
    else
        memcpy(&e->scale, values, sizeof(float) * 3);
}

static int32_t mh_api_get_parent(int32_t entity)
{
    int index;
    mh_loaded_scene *s = mh_resolve(entity, &index);
    if (!s || s->scene.entities[index].parent < 0)
        return -1;
    return mh_handle(s, s->scene.entities[index].parent);
}

static const char *mh_api_get_name(int32_t entity)
{
    mh_loaded_scene *s;
    mh_entity_data *e = mh_entity_of(entity, &s);
    return e ? s->scene.entity_names[e - s->scene.entities] : "";
}

static int32_t mh_api_find(const char *name)
{
    for (int si = 0; si < mh_host_world->count; si++) {
        const mh_loaded_scene *s = &mh_host_world->scenes[si];
        for (int i = 0; i < s->scene.entity_count; i++)
            if (!strcmp(s->scene.entity_names[i], name))
                return mh_handle(s, i);
    }
    return -1;
}

static void *mh_api_get_script(int32_t entity, const char *type)
{
    int index;
    mh_loaded_scene *s = mh_resolve(entity, &index);
    for (int i = 0; s && i < s->scene.script_count; i++)
        if (s->scene.scripts[i].entity == index && s->types[i] && !strcmp(s->types[i]->name, type))
            return s->scripts[i];
    return NULL;
}

static float mh_api_get_axis(const char *name, int32_t raw)
{
    return raw ? mh_get_axis_raw(mh_host_input, name) : mh_get_axis(mh_host_input, name);
}

static int32_t mh_api_get_button(const char *name, int32_t which)
{
    return which == 0 ? mh_get_button(mh_host_input, name)
         : which == 1 ? mh_get_button_down(mh_host_input, name) : mh_get_button_up(mh_host_input, name);
}

static int32_t mh_api_get_key(const char *name, int32_t which)
{
    int code = mh_key_code(name);
    if (code < 0) {
        mh_log(MH_LOG_ERROR, "There is no key named '%s'.", name);
        return 0;
    }
    return which == 0 ? mh_get_key(mh_host_input, code)
         : which == 1 ? mh_get_key_down(mh_host_input, code) : mh_get_key_up(mh_host_input, code);
}

static void mh_api_get_mouse_position(float *out)
{
    out[0] = mh_host_input->current.mouse_x;
    out[1] = mh_host_input->current.mouse_y;
}

static float mh_api_get_time(int32_t which)
{
    return which == 0 ? mh_host_world->delta_time : which == 1 ? mh_host_world->time : (float)mh_host_world->frame_count;
}

static void mh_api_log(int32_t level, const char *message)
{
    mh_log(level == 0 ? MH_LOG_INFO : level == 1 ? MH_LOG_WARNING : MH_LOG_ERROR, "%s", message);
}

static int32_t mh_api_load_scene(const char *name, int32_t index, int32_t mode)
{
    int target = name ? mh_find_scene(mh_host_world->game, name) : index;
    if (target < 0 || target >= mh_host_world->game->scene_count)
        return 0;
    mh_host_world->request_scene = target;
    mh_host_world->request_mode = mode == MH_LOAD_ADDITIVE ? MH_LOAD_ADDITIVE : MH_LOAD_SINGLE;
    return 1;
}

static void mh_api_quit(void) { mh_host_world->quit = 1; }

static const mh_api mh_native_api = {
    mh_api_get_transform, mh_api_set_transform, mh_api_get_parent, mh_api_get_name, mh_api_find,
    mh_api_get_script, mh_api_get_axis, mh_api_get_button, mh_api_get_key, mh_api_get_mouse_position,
    mh_api_get_time, mh_api_log, mh_api_load_scene, mh_api_quit,
};

/* Before the first scene loads. */
static void mh_scripts_init(void) { mh_bind(&mh_native_api); }
