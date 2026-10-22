/* The game's scripts (MENHIR_SCRIPTS builds): the project's .c files and the scripting runtime
   (Engine/Scripting/src) are compiled with the player. This file gives them the engine (the mh_api
   table, see menhir_abi.h), creates the scripts of each loaded scene with the values the scene saved,
   runs their callbacks, and creates, destroys and deactivates entities for them, like the editor's
   Play mode (Runtime/PlaySession.cs). */
#include <errno.h>
#include <stdlib.h>
#include <string.h>
#include <menhir_abi.h>

/* From the runtime (menhir_script.c, menhir_runtime.c). */
void mh_bind(const mh_api *api);
int32_t mh_script_type_count(void);
const mh_script_type *mh_script_type_at(int32_t index);
void mh_runtime_update(void);
void mh_runtime_fixed_step(void);
void mh_runtime_shutdown(void);

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
    case MH_FIXED_UPDATE: return type->fixed_update;
    default: return type->late_update;
    }
}

static int32_t mh_handle(const mh_loaded_scene *s, int entity) { return (s->serial & 0x7F) << 24 | entity; }

/* Room for one more script. */
static int mh_script_slot(mh_loaded_scene *s)
{
    if (s->script_total == s->script_capacity) {
        int capacity = maxi(16, s->script_capacity * 2);
        s->script_entity = realloc(s->script_entity, sizeof(int) * (size_t)capacity);
        s->types = realloc((void *)s->types, sizeof(*s->types) * (size_t)capacity);
        s->scripts = realloc(s->scripts, sizeof(void *) * (size_t)capacity);
        s->started = realloc(s->started, (size_t)capacity);
        s->destroyed = realloc(s->destroyed, (size_t)capacity);
        if (!s->script_entity || !s->types || !s->scripts || !s->started || !s->destroyed)
            mh_fatal("%s", "out of memory");
        s->script_capacity = capacity;
    }
    int i = s->script_total++;
    s->script_entity[i] = -1;
    s->types[i] = NULL;
    s->scripts[i] = NULL;
    s->started[i] = 0;
    s->destroyed[i] = 0;
    return i;
}

static void mh_awake(mh_loaded_scene *s, int first, int last)
{
    for (int i = first; i < last; i++)
        if (s->types[i] && !s->destroyed[i] && s->types[i]->awake)
            s->types[i]->awake(s->scripts[i], mh_handle(s, s->script_entity[i]));
}

static void mh_scripts_load(mh_world *w, mh_loaded_scene *s)
{
    for (int i = 0; i < s->scene.script_count; i++) {
        const mh_script_data *d = &s->scene.scripts[i];
        const char *entity = s->scene.entity_names[d->entity];
        const mh_script_type *type = mh_find_script_type(d->script_name);
        if (!type) {
            mh_log(MH_LOG_WARNING, "%s: the script %s does not exist.", entity, d->script_name);
            continue;
        }
        void *script = mh_alloc(type->size ? type->size : 1);
        memset(script, 0, type->size ? type->size : 1);
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
        int k = mh_script_slot(s);
        s->script_entity[k] = d->entity;
        s->types[k] = type;
        s->scripts[k] = script;
    }
    mh_host_world = w;
    if (!mh_host_input)
        mh_host_input = &mh_no_input;
    mh_awake(s, 0, s->script_total);
}

/* OnDestroy for every script (the other scenes are still there), then the memory goes. */
static void mh_scripts_unload(mh_world *w, mh_loaded_scene *s)
{
    mh_host_world = w;
    for (int i = 0; i < s->script_total; i++)
        if (s->types[i] && !s->destroyed[i] && s->types[i]->on_destroy)
            s->types[i]->on_destroy(s->scripts[i], mh_handle(s, s->script_entity[i]));
    for (int i = 0; i < s->script_total; i++)
        if (!s->destroyed[i])
            free(s->scripts[i]);
    for (int i = 0; i < s->string_count; i++)
        free(s->strings[i]);
    free(s->script_entity);
    free((void *)s->types);
    free(s->scripts);
    free(s->started);
    free(s->destroyed);
    free(s->strings);
    s->script_entity = NULL;
    s->types = NULL;
    s->scripts = NULL;
    s->started = s->destroyed = NULL;
    s->strings = NULL;
    s->script_total = s->script_capacity = s->string_count = 0;
    for (int i = 0; i < s->scene.entity_count; i++)
        s->alive[i] = 0;
}

/* Start runs once, before a script's first Update. Scripts created during the pass wait for the next one. */
static void mh_scripts_run(mh_world *w, const mh_input *input, int callback)
{
    mh_host_world = w;
    mh_host_input = input;
    for (int si = 0; si < w->count; si++) {
        mh_loaded_scene *s = &w->scenes[si];
        for (int i = 0, count = s->script_total; i < count; i++) {
            if (!s->types[i] || s->destroyed[i] || !mh_entity_visible(s, s->script_entity[i]))
                continue;
            if (callback == MH_START) {
                if (s->started[i])
                    continue;
                s->started[i] = 1;
            } else if (!s->started[i])
                continue;
            mh_callback function = mh_callback_of(s->types[i], callback);
            if (function)
                function(s->scripts[i], mh_handle(s, s->script_entity[i]));
        }
    }
}

static void mh_scripts_runtime(mh_world *w, int fixed)
{
    mh_host_world = w;
    if (fixed)
        mh_runtime_fixed_step();
    else
        mh_runtime_update();
}

static void mh_scripts_close(mh_world *w)
{
    mh_host_world = w;
    for (int si = 0; si < w->count; si++) {
        mh_loaded_scene *s = &w->scenes[si];
        for (int i = 0, count = s->script_total; i < count; i++)
            if (s->types[i] && !s->destroyed[i] && s->types[i]->on_game_close)
                s->types[i]->on_game_close(s->scripts[i], mh_handle(s, s->script_entity[i]));
    }
    mh_world_unload_all(w); /* OnDestroy, while the runtime is still there */
    mh_runtime_shutdown();
}

/* ---- Entities ---------------------------------------------------------------------------------- */

static mh_loaded_scene *mh_resolve(int32_t handle, int *index)
{
    if (handle < 0 || !mh_host_world)
        return NULL;
    int serial = handle >> 24, entity = handle & 0xFFFFFF;
    for (int i = 0; i < mh_host_world->count; i++) {
        mh_loaded_scene *s = &mh_host_world->scenes[i];
        if ((s->serial & 0x7F) == serial) {
            if (entity >= s->scene.entity_count || !s->alive[entity])
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
        mh_log(MH_LOG_ERROR, "Entity %d does not exist (destroyed, or its scene was unloaded).", (int)handle);
        return NULL;
    }
    if (scene)
        *scene = s;
    return &s->scene.entities[index];
}

/* Whether i is root or one of its children, grandchildren... */
static int mh_in_subtree(const mh_loaded_scene *s, int i, int root)
{
    for (int depth = 0; i >= 0 && depth <= s->scene.entity_count; depth++) {
        if (i == root)
            return 1;
        i = s->scene.entities[i].parent;
    }
    return 0;
}

/* Room for n more entities. */
static void mh_grow_entities(mh_loaded_scene *s, int n)
{
    int needed = s->scene.entity_count + n;
    if (needed <= s->entity_capacity)
        return;
    int capacity = maxi(needed, s->entity_capacity * 2);
    s->scene.entities = realloc(s->scene.entities, sizeof(mh_entity_data) * (size_t)capacity);
    s->scene.entity_names = realloc(s->scene.entity_names, sizeof(char *) * (size_t)capacity);
    s->world = realloc(s->world, sizeof(mat4) * (size_t)capacity);
    s->alive = realloc(s->alive, (size_t)capacity);
    s->active = realloc(s->active, (size_t)capacity);
    s->visible = realloc(s->visible, (size_t)capacity);
    if (!s->scene.entities || !s->scene.entity_names || !s->world || !s->alive || !s->active || !s->visible)
        mh_fatal("%s", "out of memory");
    s->entity_capacity = capacity;
}

#define MH_APPEND(array, count, item)                                                         \
    do {                                                                                      \
        (array) = realloc((array), sizeof(*(array)) * (size_t)((count) + 1));                \
        if (!(array))                                                                         \
            mh_fatal("%s", "out of memory");                                                  \
        (array)[(count)++] = (item);                                                          \
    } while (0)

static int mh_field_size(const mh_field *f)
{
    switch (f->kind) {
    case MH_FIELD_BOOL: return 1;
    case MH_FIELD_VEC2: return 8;
    case MH_FIELD_VEC3: return 12;
    case MH_FIELD_COLOR: return 16;
    default: return 4;
    }
}

/* A copy of the entity and its children, with their renderers, lights, cameras and scripts (with their
   fields' values now; their other variables start from zero), like the editor's PlaySession.Instantiate. */
static int32_t mh_api_instantiate(int32_t entity, int32_t parent)
{
    int root, parent_index = -1;
    mh_loaded_scene *source = mh_resolve(entity, &root), *target = source;
    if (!source) {
        mh_log(MH_LOG_ERROR, "Entity %d does not exist (destroyed, or its scene was unloaded).", (int)entity);
        return -1;
    }
    if (parent >= 0) {
        target = mh_resolve(parent, &parent_index);
        if (!target) {
            mh_log(MH_LOG_ERROR, "Entity %d does not exist (destroyed, or its scene was unloaded).", (int)parent);
            return -1;
        }
    }
    int count = source->scene.entity_count, n = 0;
    int *subtree = mh_alloc(sizeof(int) * (size_t)count), *copy_of = mh_alloc(sizeof(int) * (size_t)count);
    for (int i = 0; i < count; i++) {
        copy_of[i] = -1;
        if (source->alive[i] && mh_in_subtree(source, i, root))
            subtree[n++] = i;
    }
    mh_grow_entities(target, n);
    int base = target->scene.entity_count;
    for (int k = 0; k < n; k++)
        copy_of[subtree[k]] = base + k;
    for (int k = 0; k < n; k++) {
        int from = subtree[k], to = base + k;
        mh_entity_data e = source->scene.entities[from];
        e.parent = k == 0 ? parent_index : copy_of[e.parent];
        target->scene.entities[to] = e;
        const char *name = source->scene.entity_names[from];
        char *copy = mh_alloc(strlen(name) + 1);
        strcpy(copy, name);
        target->scene.entity_names[to] = copy;
        target->alive[to] = 1;
        target->active[to] = source->active[from];
        target->visible[to] = 0;
        target->world[to] = m4_identity();
    }
    target->scene.entity_count += n; /* before the scripts' Awake, which may use them */

    /* What draws them (target may be source: only the components there before). */
    int renderers = source->scene.renderer_count, lights = source->scene.light_count, cameras = source->scene.camera_count;
    for (int i = 0; i < renderers; i++)
        if (copy_of[source->scene.renderers[i].entity] >= 0) {
            mh_mesh_renderer r = source->scene.renderers[i];
            r.entity = copy_of[r.entity];
            MH_APPEND(target->scene.renderers, target->scene.renderer_count, r);
        }
    for (int i = 0; i < lights; i++)
        if (copy_of[source->scene.lights[i].entity] >= 0) {
            mh_light l = source->scene.lights[i];
            l.entity = copy_of[l.entity];
            MH_APPEND(target->scene.lights, target->scene.light_count, l);
        }
    for (int i = 0; i < cameras; i++)
        if (copy_of[source->scene.cameras[i].entity] >= 0) {
            mh_camera c = source->scene.cameras[i];
            c.entity = copy_of[c.entity];
            MH_APPEND(target->scene.cameras, target->scene.camera_count, c);
        }

    /* The scripts, entity by entity, in their order. */
    int first_script = target->script_total, scripts = source->script_total;
    for (int k = 0; k < n; k++)
        for (int i = 0; i < scripts; i++) {
            if (source->destroyed[i] || !source->types[i] || source->script_entity[i] != subtree[k])
                continue;
            const mh_script_type *type = source->types[i];
            void *script = mh_alloc(type->size ? type->size : 1);
            memset(script, 0, type->size ? type->size : 1);
            for (int32_t f = 0; f < type->field_count; f++) {
                const mh_field *field = &type->fields[f];
                const char *from = (const char *)source->scripts[i] + field->offset;
                char *to = (char *)script + field->offset;
                if (field->kind == MH_FIELD_STRING) {
                    const char *text;
                    memcpy(&text, from, sizeof text);
                    mh_set_string(target, to, text ? text : "");
                } else
                    memcpy(to, from, (size_t)mh_field_size(field));
            }
            int slot = mh_script_slot(target);
            target->script_entity[slot] = base + k;
            target->types[slot] = type;
            target->scripts[slot] = script;
        }
    free(subtree);
    free(copy_of);
    mh_awake(target, first_script, target->script_total);
    return mh_handle(target, base);
}

static void mh_api_destroy(int32_t entity)
{
    int index;
    if (!mh_resolve(entity, &index))
        return;
    mh_world *w = mh_host_world;
    for (int i = 0; i < w->destroy_count; i++)
        if (w->destroy_queue[i] == entity)
            return;
    if (w->destroy_count == w->destroy_capacity) {
        w->destroy_capacity = maxi(16, w->destroy_capacity * 2);
        w->destroy_queue = realloc(w->destroy_queue, sizeof(int32_t) * (size_t)w->destroy_capacity);
        if (!w->destroy_queue)
            mh_fatal("%s", "out of memory");
    }
    w->destroy_queue[w->destroy_count++] = entity;
}

/* The entities destroyed during the frame go, with their children: OnDestroy first. */
static void mh_scripts_flush_destroyed(mh_world *w)
{
    mh_host_world = w;
    while (w->destroy_count > 0) {
        int32_t handle = w->destroy_queue[0];
        memmove(w->destroy_queue, w->destroy_queue + 1, sizeof(int32_t) * (size_t)(w->destroy_count - 1));
        w->destroy_count--;
        int root;
        mh_loaded_scene *s = mh_resolve(handle, &root);
        if (!s)
            continue;
        int count = s->script_total;
        for (int i = 0; i < count; i++)
            if (s->types[i] && !s->destroyed[i] && s->alive[s->script_entity[i]] && mh_in_subtree(s, s->script_entity[i], root)
                && s->types[i]->on_destroy)
                s->types[i]->on_destroy(s->scripts[i], mh_handle(s, s->script_entity[i]));
        for (int i = 0; i < count; i++)
            if (!s->destroyed[i] && s->alive[s->script_entity[i]] && mh_in_subtree(s, s->script_entity[i], root)) {
                s->destroyed[i] = 1;
                free(s->scripts[i]);
                s->scripts[i] = NULL;
            }
        /* Children first would cut them off their parent: mark the whole subtree, then kill it. */
        int entities = s->scene.entity_count;
        unsigned char *dead = mh_alloc((size_t)maxi(1, entities));
        for (int i = 0; i < entities; i++)
            dead[i] = s->alive[i] && mh_in_subtree(s, i, root);
        for (int i = 0; i < entities; i++)
            if (dead[i])
                s->alive[i] = 0;
        free(dead);
    }
}

/* ---- The engine for the scripts (mh_api) --------------------------------------------------- */

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
    if (!s)
        return -1;
    int parent = s->scene.entities[index].parent;
    return parent >= 0 && s->alive[parent] ? mh_handle(s, parent) : -1;
}

static const char *mh_api_get_name(int32_t entity)
{
    mh_loaded_scene *s;
    mh_entity_data *e = mh_entity_of(entity, &s);
    return e ? s->scene.entity_names[e - s->scene.entities] : "";
}

/* The first entity with that name (inactive ones too), in load order. */
static int32_t mh_api_find(const char *name)
{
    for (int si = 0; si < mh_host_world->count; si++) {
        const mh_loaded_scene *s = &mh_host_world->scenes[si];
        for (int i = 0; i < s->scene.entity_count; i++)
            if (s->alive[i] && !strcmp(s->scene.entity_names[i], name))
                return mh_handle(s, i);
    }
    return -1;
}

static void *mh_api_get_script(int32_t entity, const char *type)
{
    int index;
    mh_loaded_scene *s = mh_resolve(entity, &index);
    for (int i = 0; s && i < s->script_total; i++)
        if (s->script_entity[i] == index && s->types[i] && !s->destroyed[i] && !strcmp(s->types[i]->name, type))
            return s->scripts[i];
    return NULL;
}

static void *mh_api_script_at(int32_t entity, int32_t at, const mh_script_type **type)
{
    int index;
    mh_loaded_scene *s = mh_resolve(entity, &index);
    if (type)
        *type = NULL;
    for (int i = 0; s && at >= 0 && i < s->script_total; i++) {
        if (s->script_entity[i] != index || !s->types[i] || s->destroyed[i])
            continue;
        if (at-- == 0) {
            if (type)
                *type = s->types[i];
            return s->scripts[i];
        }
    }
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
    switch (which) {
    case 0: return mh_host_world->delta_time;
    case 1: return mh_host_world->time;
    case 2: return (float)mh_host_world->frame_count;
    default: return MH_FIXED_DELTA_TIME;
    }
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

static int32_t mh_api_is_alive(int32_t entity)
{
    int index;
    return mh_resolve(entity, &index) != NULL;
}

static void mh_api_set_active(int32_t entity, int32_t active)
{
    mh_loaded_scene *s;
    mh_entity_data *e = mh_entity_of(entity, &s);
    if (e)
        s->active[e - s->scene.entities] = active != 0;
}

static int32_t mh_api_get_active(int32_t entity, int32_t in_hierarchy)
{
    int index;
    mh_loaded_scene *s = mh_resolve(entity, &index);
    return s && (in_hierarchy ? mh_entity_visible(s, index) : s->active[index]);
}

static int32_t mh_api_child_count(int32_t entity)
{
    int index, n = 0;
    mh_loaded_scene *s = mh_resolve(entity, &index);
    for (int i = 0; s && i < s->scene.entity_count; i++)
        n += s->alive[i] && s->scene.entities[i].parent == index;
    return n;
}

static int32_t mh_api_child_at(int32_t entity, int32_t at)
{
    int index;
    mh_loaded_scene *s = mh_resolve(entity, &index);
    for (int i = 0; s && at >= 0 && i < s->scene.entity_count; i++)
        if (s->alive[i] && s->scene.entities[i].parent == index && at-- == 0)
            return mh_handle(s, i);
    return -1;
}

static void mh_api_set_parent(int32_t entity, int32_t parent)
{
    mh_loaded_scene *s, *ps = NULL;
    mh_entity_data *e = mh_entity_of(entity, &s);
    if (!e)
        return;
    int index = (int)(e - s->scene.entities), parent_index = -1;
    if (parent >= 0) {
        ps = mh_resolve(parent, &parent_index);
        if (!ps) {
            mh_log(MH_LOG_ERROR, "Entity %d does not exist (destroyed, or its scene was unloaded).", (int)parent);
            return;
        }
        if (ps != s || mh_in_subtree(s, parent_index, index)) {
            mh_log(MH_LOG_ERROR, "%s cannot go under %s (another scene, or one of its own children).",
                   s->scene.entity_names[index], ps->scene.entity_names[parent_index]);
            return;
        }
    }
    e->parent = parent_index;
}

static const char *mh_api_asset_path(const char *relative)
{
    /* Kept per text: the runtime keeps the pointers it gets. */
    static char **paths;
    static int count;
    size_t root = strlen(mh_asset_root), length = strlen(relative);
    for (int i = 0; i < count; i++)
        if (!strncmp(paths[i], mh_asset_root, root) && !strcmp(paths[i] + root, relative))
            return paths[i];
    char *path = mh_alloc(root + length + 1);
    memcpy(path, mh_asset_root, root);
    memcpy(path + root, relative, length + 1);
    MH_APPEND(paths, count, path);
    return path;
}

static int32_t mh_api_main_camera(void)
{
    for (int si = 0; si < mh_host_world->count; si++) {
        const mh_loaded_scene *s = &mh_host_world->scenes[si];
        for (int i = 0; i < s->scene.entity_count; i++) {
            if (!mh_entity_visible(s, i))
                continue;
            for (int c = 0; c < s->scene.camera_count; c++)
                if (s->scene.cameras[c].entity == i)
                    return mh_handle(s, i);
        }
    }
    return -1;
}

static const mh_api mh_native_api = {
    mh_api_get_transform, mh_api_set_transform, mh_api_get_parent, mh_api_get_name, mh_api_find,
    mh_api_get_script, mh_api_get_axis, mh_api_get_button, mh_api_get_key, mh_api_get_mouse_position,
    mh_api_get_time, mh_api_log, mh_api_load_scene, mh_api_quit,
    mh_api_is_alive, mh_api_instantiate, mh_api_destroy, mh_api_set_active, mh_api_get_active,
    mh_api_child_count, mh_api_child_at, mh_api_set_parent, mh_api_script_at, mh_api_asset_path, mh_api_main_camera,
};

/* Before the first scene loads. */
static void mh_scripts_init(void) { mh_bind(&mh_native_api); }
