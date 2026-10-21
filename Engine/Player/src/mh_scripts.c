/* The game's scripts (MENHIR_SCRIPTS builds): IL2C translates them, with the scripting API
   (Engine/Scripting) and Menhir.CoreLib, into a C file of its own that gives the m_scripts table
   (runtime.h). This file creates the scripts of each loaded scene, gives them the values the scene
   saved, runs their callbacks, and implements the API's internal calls (MenhirEngine.Host), like
   the editor's Play mode (Runtime/PlaySession.cs). An exception a callback does not catch is logged
   and the frame goes on. */
#include <errno.h>
#include <setjmp.h>
#include <stdlib.h>
#include <string.h>
#include <menhir/runtime.h>

static mh_world *mh_host_world;
static const mh_input *mh_host_input; /* this frame's (empty while the first scene loads) */
static mh_input mh_no_input;

static const char *const mh_callback_names[] = { "Awake", "Start", "Update", "LateUpdate" };

/* "Game.Player" -> "Player", "Game.Outer+Inner" -> "Inner" (.NET's Type.Name). */
static const char *mh_short_name(const char *name)
{
    const char *dot = strrchr(name, '.'), *plus = strrchr(name, '+');
    const char *last = dot > plus ? dot : plus;
    return last ? last + 1 : name;
}

static void mh_log_exception(const char *class_name, const char *method, const char *entity, MObject *exception)
{
    char *message = m_string_to_utf8(m_exception_message(exception));
    mh_log(MH_LOG_ERROR, "%s.%s on '%s': %s: %s", mh_short_name(class_name), method, entity,
           mh_short_name(exception->type->name), message);
    free(message);
}

static int mh_script_enabled(MObject *script)
{
    return *(const bool *)((const char *)script + m_scripts.enabledOffset);
}

static const MScriptClass *mh_script_class(const char *name)
{
    for (uint32_t i = 0; i < m_scripts.classCount; i++)
        if (!strcmp(m_scripts.classes[i].name, name))
            return &m_scripts.classes[i];
    return NULL;
}

/* ---- Saved values: converted to the field's type like the editor's ScriptFields.FromJson ---- */

static int mh_parse_integer(const char *text, int64_t min, int64_t max, int64_t *out)
{
    char *end;
    errno = 0;
    long long v = strtoll(text, &end, 10);
    if (end == text || *end || errno == ERANGE || v < min || v > max)
        return 0;
    *out = v;
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

static int mh_parse_double(const char *text, double *out)
{
    char *end;
    double v = strtod(text, &end);
    if (end == text || *end || !isfinite(v))
        return 0;
    *out = v;
    return 1;
}

static void mh_store_integer(void *at, uint32_t size, int64_t value)
{
    switch (size) {
    case 1: { int8_t v = (int8_t)value; memcpy(at, &v, 1); break; }
    case 2: { int16_t v = (int16_t)value; memcpy(at, &v, 2); break; }
    case 8: memcpy(at, &value, 8); break;
    default: { int32_t v = (int32_t)value; memcpy(at, &v, 4); break; }
    }
}

/* An enum constant by name: its value (sign extended when the enum is signed). */
static int mh_enum_value(const MType *type, const char *name, int64_t *out)
{
    const MEnumInfo *info = type->enumInfo;
    if (!info)
        return 0;
    for (uint32_t i = 0; i < info->count; i++) {
        if (strcmp(info->names[i], name))
            continue;
        uint64_t bits = info->values[i];
        uint32_t size = type->instanceSize;
        if (info->isSigned && size < 8) {
            int shift = 64 - 8 * (int)size;
            *out = (int64_t)(bits << shift) >> shift;
        } else
            *out = (int64_t)bits;
        return 1;
    }
    return 0;
}

/* Returns 0 when the value does not fit the field (which then keeps its default). */
static int mh_apply_value(MObject *script, const MScriptField *field, const mh_script_value *value)
{
    void *at = (char *)script + field->offset;
    int64_t integer;
    switch (field->kind) {
    case M_FIELD_INT:
    case M_FIELD_LONG:
        if (value->kind != MH_VALUE_NUMBER ||
            !mh_parse_integer(value->text, field->kind == M_FIELD_INT ? INT32_MIN : INT64_MIN,
                              field->kind == M_FIELD_INT ? INT32_MAX : INT64_MAX, &integer))
            return 0;
        mh_store_integer(at, field->size, integer);
        return 1;
    case M_FIELD_FLOAT: {
        float f;
        if (value->kind != MH_VALUE_NUMBER || !mh_parse_float(value->text, &f))
            return 0;
        memcpy(at, &f, 4);
        return 1;
    }
    case M_FIELD_DOUBLE: {
        double d;
        if (value->kind != MH_VALUE_NUMBER || !mh_parse_double(value->text, &d))
            return 0;
        memcpy(at, &d, 8);
        return 1;
    }
    case M_FIELD_BOOL:
        if (value->kind != MH_VALUE_BOOL)
            return 0;
        *(bool *)at = value->boolean != 0;
        return 1;
    case M_FIELD_STRING:
        if (value->kind != MH_VALUE_STRING)
            return 0;
        *(MString **)at = m_string_from_utf8(value->text);
        return 1;
    case M_FIELD_ENUM:
        /* By name (as the editor saves it), or by number. */
        if (value->kind == MH_VALUE_STRING ? !mh_enum_value(field->enumType, value->text, &integer)
            : value->kind != MH_VALUE_NUMBER || !mh_parse_integer(value->text, INT64_MIN, INT64_MAX, &integer))
            return 0;
        mh_store_integer(at, field->size, integer);
        return 1;
    case M_FIELD_VECTOR2:
    case M_FIELD_VECTOR3: {
        int n = field->kind == M_FIELD_VECTOR2 ? 2 : 3;
        float v[3];
        if (value->kind != MH_VALUE_NUMBERS || value->count != n)
            return 0;
        for (int i = 0; i < n; i++)
            if (!mh_parse_float(value->items[i], &v[i]))
                return 0;
        memcpy(at, v, sizeof(float) * (size_t)n);
        return 1;
    }
    }
    return 0;
}

/* ---- Lifecycle ----------------------------------------------------------------------------- */

/* Runs one callback; an exception it throws is logged. */
static void mh_script_call(mh_loaded_scene *s, int i, int callback)
{
    MObject *volatile script = s->scripts[i];
    void (*method)(MObject *) = (void (*)(MObject *))script->type->vtable[m_scripts.slots[callback]];
    MFrame frame;
    m_push_frame(&frame);
    if (!setjmp(frame.jump)) {
        method(script);
        m_pop_frame(&frame);
        return;
    }
    /* m_throw popped the frame. */
    const mh_script_data *d = &s->scene.scripts[i];
    mh_log_exception(d->class_name, mh_callback_names[callback], s->scene.entity_names[d->entity], frame.exception);
}

static MObject *mh_script_create(const MScriptClass *type, const mh_script_data *d, const char *entity)
{
    MFrame frame;
    m_push_frame(&frame);
    if (setjmp(frame.jump)) {
        mh_log_exception(d->class_name, ".ctor", entity, frame.exception);
        return NULL;
    }
    MObject *script = type->create();
    m_pop_frame(&frame);
    return script;
}

static void mh_scripts_load(mh_world *w, mh_loaded_scene *s)
{
    (void)w;
    int count = s->scene.script_count;
    s->scripts = m_alloc_roots(sizeof(MObject *) * (size_t)maxi(1, count));
    s->started = mh_alloc((size_t)maxi(1, count));
    for (int i = 0; i < count; i++) {
        const mh_script_data *d = &s->scene.scripts[i];
        const char *entity = s->scene.entity_names[d->entity];
        const MScriptClass *type = mh_script_class(d->class_name);
        if (!type) {
            mh_log(MH_LOG_WARNING, "%s: the script %s does not exist (or does not compile).", entity, d->class_name);
            continue;
        }
        MObject *script = mh_script_create(type, d, entity);
        if (!script)
            continue;
        for (uint32_t f = 0; f < type->fieldCount; f++) {
            for (int v = 0; v < d->value_count; v++) {
                const mh_script_value *value = &d->values[v];
                if (strcmp(type->fields[f].name, value->name))
                    continue;
                if (!mh_apply_value(script, &type->fields[f], value))
                    mh_log(MH_LOG_WARNING, "%s: the saved value of %s.%s does not fit the field; it keeps its default.",
                           entity, d->class_name, value->name);
                break;
            }
        }
        *(int32_t *)((char *)script + m_scripts.entityOffset) = (s->serial & 0x7FFF) << 16 | d->entity;
        s->scripts[i] = script;
    }
    mh_host_world = w;
    if (!mh_host_input)
        mh_host_input = &mh_no_input;
    for (int i = 0; i < count; i++)
        if (s->scripts[i])
            mh_script_call(s, i, MH_AWAKE);
}

static void mh_scripts_unload(mh_loaded_scene *s)
{
    if (s->scripts)
        m_free_roots(s->scripts);
    free(s->started);
    s->scripts = NULL;
    s->started = NULL;
}

/* Start runs once, before a script's first Update, on the first frame its script is enabled. */
static void mh_scripts_run(mh_world *w, const mh_input *input, int callback)
{
    mh_host_world = w;
    mh_host_input = input;
    for (int si = 0; si < w->count; si++) {
        mh_loaded_scene *s = &w->scenes[si];
        for (int i = 0; i < s->scene.script_count; i++) {
            MObject *script = s->scripts[i];
            if (!script || !mh_script_enabled(script))
                continue;
            if (callback == MH_START) {
                if (s->started[i])
                    continue;
                s->started[i] = 1;
            } else if (!s->started[i])
                continue;
            mh_script_call(s, i, callback);
        }
    }
}

/* ---- MenhirEngine.Host: the internal calls of the scripting API ---------------------------- */

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

static mh_entity *mh_entity_of(int32_t handle, mh_loaded_scene **scene)
{
    int index;
    mh_loaded_scene *s = mh_resolve(handle, &index);
    if (!s)
        m_throw_named("System.InvalidOperationException", "This entity is not in a loaded scene any more.");
    if (scene)
        *scene = s;
    return &s->scene.entities[index];
}

float MenhirEngine_Host__GetFloat(int32_t entity, int32_t property)
{
    mh_entity *e = mh_entity_of(entity, NULL);
    float v[10] = { e->position.x, e->position.y, e->position.z, e->rotation.x, e->rotation.y, e->rotation.z,
                    e->rotation.w, e->scale.x, e->scale.y, e->scale.z };
    return v[property >= 0 && property < 10 ? property : 9];
}

void MenhirEngine_Host__SetFloat(int32_t entity, int32_t property, float value)
{
    mh_entity *e = mh_entity_of(entity, NULL);
    float *fields[10] = { &e->position.x, &e->position.y, &e->position.z, &e->rotation.x, &e->rotation.y,
                          &e->rotation.z, &e->rotation.w, &e->scale.x, &e->scale.y, &e->scale.z };
    *fields[property >= 0 && property < 10 ? property : 9] = value;
}

int32_t MenhirEngine_Host__GetParent(int32_t entity)
{
    int index;
    mh_loaded_scene *s = mh_resolve(entity, &index);
    if (!s || s->scene.entities[index].parent < 0)
        return -1;
    return (s->serial & 0x7FFF) << 16 | s->scene.entities[index].parent;
}

MString *MenhirEngine_Host__GetName(int32_t entity)
{
    mh_loaded_scene *s;
    mh_entity *e = mh_entity_of(entity, &s);
    return m_string_from_utf8(s->scene.entity_names[e - s->scene.entities]);
}

int32_t MenhirEngine_Host__FindEntity(MString *name)
{
    char *text = m_string_to_utf8(name);
    int32_t found = -1;
    for (int si = 0; si < mh_host_world->count && found < 0; si++) {
        mh_loaded_scene *s = &mh_host_world->scenes[si];
        for (int i = 0; i < s->scene.entity_count; i++) {
            if (!strcmp(s->scene.entity_names[i], text)) {
                found = (s->serial & 0x7FFF) << 16 | i;
                break;
            }
        }
    }
    free(text);
    return found;
}

int32_t MenhirEngine_Host__GetScriptCount(int32_t entity)
{
    int index, count = 0;
    mh_loaded_scene *s = mh_resolve(entity, &index);
    for (int i = 0; s && i < s->scene.script_count; i++)
        count += s->scene.scripts[i].entity == index && s->scripts[i];
    return count;
}

MObject *MenhirEngine_Host__GetScript(int32_t entity, int32_t which)
{
    int index;
    mh_loaded_scene *s = mh_resolve(entity, &index);
    for (int i = 0; s && i < s->scene.script_count; i++)
        if (s->scene.scripts[i].entity == index && s->scripts[i] && which-- == 0)
            return s->scripts[i];
    return NULL;
}

float MenhirEngine_Host__GetAxis(MString *name, bool raw)
{
    char *text = m_string_to_utf8(name);
    float v = raw ? mh_get_axis_raw(mh_host_input, text) : mh_get_axis(mh_host_input, text);
    free(text);
    return v;
}

bool MenhirEngine_Host__GetButton(MString *name, int32_t which)
{
    char *text = m_string_to_utf8(name);
    int v = which == 0 ? mh_get_button(mh_host_input, text)
          : which == 1 ? mh_get_button_down(mh_host_input, text) : mh_get_button_up(mh_host_input, text);
    free(text);
    return v != 0;
}

bool MenhirEngine_Host__GetKey(MString *name, int32_t which)
{
    char *text = m_string_to_utf8(name);
    int code = mh_key_code(text);
    if (code < 0) {
        char message[160];
        snprintf(message, sizeof message, "Input Key named: %.100s is unknown", text);
        free(text);
        m_throw_named("System.ArgumentException", message);
    }
    free(text);
    int v = which == 0 ? mh_get_key(mh_host_input, code)
          : which == 1 ? mh_get_key_down(mh_host_input, code) : mh_get_key_up(mh_host_input, code);
    return v != 0;
}

float MenhirEngine_Host__GetMouse(int32_t axis)
{
    return axis == 0 ? mh_host_input->current.mouse_x : mh_host_input->current.mouse_y;
}

float MenhirEngine_Host__GetTime(int32_t which)
{
    return which == 0 ? mh_host_world->delta_time : which == 1 ? mh_host_world->time : (float)mh_host_world->frame_count;
}

void MenhirEngine_Host__Log(int32_t level, MString *message)
{
    char *text = m_string_to_utf8(message);
    mh_log(level == 0 ? MH_LOG_INFO : level == 1 ? MH_LOG_WARNING : MH_LOG_ERROR, "%s", text);
    free(text);
}

bool MenhirEngine_Host__LoadScene(MString *name, int32_t index, int32_t mode)
{
    int target = index;
    if (name) {
        char *text = m_string_to_utf8(name);
        target = mh_find_scene(mh_host_world->game, text);
        free(text);
    }
    if (target < 0 || target >= mh_host_world->game->scene_count)
        return false;
    mh_host_world->request_scene = target;
    mh_host_world->request_mode = mode;
    return true;
}

void MenhirEngine_Host__Quit(void)
{
    mh_host_world->quit = 1;
}
