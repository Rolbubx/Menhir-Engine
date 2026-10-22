/* Physics, in the scripting runtime: the BoxCollider, SphereCollider and Rigidbody components are script
   types of their own (the inspector shows their fields like a script's), and each fixed step moves the
   bodies, pushes them out of each other and calls the scripts' collision and trigger callbacks. Boxes
   turn with their entity (oriented boxes); bodies do not turn from collisions. */
#include <stdlib.h>
#include <string.h>
#include "menhir.h"
#include "menhir_internal.h"

typedef struct {
    mh_vec3 center, size;
    bool is_trigger;
} BoxCollider;

typedef struct {
    mh_vec3 center;
    float radius;
    bool is_trigger;
} SphereCollider;

typedef struct {
    float mass;
    bool use_gravity, is_kinematic;
    float drag, bounciness, friction;
    mh_vec3 velocity;
    mh_vec3 force; /* added this step */
} Rigidbody;

static const mh_field BoxCollider_fields[] = {
    MH_VEC3(BoxCollider, center, 0, 0, 0),
    MH_VEC3(BoxCollider, size, 1, 1, 1),
    MH_BOOL(BoxCollider, is_trigger, 0, MH_TOOLTIP("Overlaps without pushing: OnTriggerEnter/Exit")),
};

static const mh_field SphereCollider_fields[] = {
    MH_VEC3(SphereCollider, center, 0, 0, 0),
    MH_FLOAT(SphereCollider, radius, 0.5),
    MH_BOOL(SphereCollider, is_trigger, 0, MH_TOOLTIP("Overlaps without pushing: OnTriggerEnter/Exit")),
};

static const mh_field Rigidbody_fields[] = {
    MH_FLOAT(Rigidbody, mass, 1, MH_TOOLTIP("Kilograms")),
    MH_BOOL(Rigidbody, use_gravity, 1),
    MH_BOOL(Rigidbody, is_kinematic, 0, MH_TOOLTIP("Moved by scripts only; pushes the others")),
    MH_FLOAT(Rigidbody, drag, 0, MH_RANGE(0, 10)),
    MH_FLOAT(Rigidbody, bounciness, 0, MH_RANGE(0, 1)),
    MH_FLOAT(Rigidbody, friction, 0.4, MH_RANGE(0, 1)),
    MH_VEC3(Rigidbody, velocity, 0, 0, 0, MH_TOOLTIP("Meters per second, at the start")),
};

/* ---- The components that exist ------------------------------------------------------------------- */

enum { MH_SHAPE_BOX, MH_SHAPE_SPHERE };

typedef struct {
    mh_entity entity;
    int32_t shape;
    void *data; /* BoxCollider or SphereCollider */
} mh_collider_ref;

typedef struct {
    mh_entity entity;
    Rigidbody *body;
} mh_body_ref;

static mh_collider_ref *mh_colliders;
static int32_t mh_collider_count, mh_collider_capacity;
static mh_body_ref *mh_bodies;
static int32_t mh_body_count, mh_body_capacity;
static mh_vec3 mh_gravity = { 0, -9.81f, 0 };

static void mh_add_collider(mh_entity entity, int32_t shape, void *data)
{
    if (mh_collider_count == mh_collider_capacity) {
        int32_t capacity = mh_collider_capacity ? mh_collider_capacity * 2 : 32;
        mh_collider_ref *grown = realloc(mh_colliders, sizeof(*grown) * (size_t)capacity);
        if (!grown)
            return;
        mh_colliders = grown;
        mh_collider_capacity = capacity;
    }
    mh_colliders[mh_collider_count++] = (mh_collider_ref){ entity, shape, data };
}

static void mh_remove_collider(void *data)
{
    for (int32_t i = 0; i < mh_collider_count; i++)
        if (mh_colliders[i].data == data) {
            memmove(&mh_colliders[i], &mh_colliders[i + 1], sizeof(*mh_colliders) * (size_t)(mh_collider_count - i - 1));
            mh_collider_count--;
            return;
        }
}

static void BoxCollider_awake(BoxCollider *self, mh_entity entity) { mh_add_collider(entity, MH_SHAPE_BOX, self); }
static void BoxCollider_destroy(BoxCollider *self, mh_entity entity) { (void)entity; mh_remove_collider(self); }
static void SphereCollider_awake(SphereCollider *self, mh_entity entity) { mh_add_collider(entity, MH_SHAPE_SPHERE, self); }
static void SphereCollider_destroy(SphereCollider *self, mh_entity entity) { (void)entity; mh_remove_collider(self); }

static void Rigidbody_awake(Rigidbody *self, mh_entity entity)
{
    if (mh_body_count == mh_body_capacity) {
        int32_t capacity = mh_body_capacity ? mh_body_capacity * 2 : 32;
        mh_body_ref *grown = realloc(mh_bodies, sizeof(*grown) * (size_t)capacity);
        if (!grown)
            return;
        mh_bodies = grown;
        mh_body_capacity = capacity;
    }
    mh_bodies[mh_body_count++] = (mh_body_ref){ entity, self };
}

static void Rigidbody_destroy(Rigidbody *self, mh_entity entity)
{
    (void)entity;
    for (int32_t i = 0; i < mh_body_count; i++)
        if (mh_bodies[i].body == self) {
            memmove(&mh_bodies[i], &mh_bodies[i + 1], sizeof(*mh_bodies) * (size_t)(mh_body_count - i - 1));
            mh_body_count--;
            return;
        }
}

MH_SCRIPT(BoxCollider, MH_FIELDS(BoxCollider_fields), MH_AWAKE(BoxCollider_awake), MH_ON_DESTROY(BoxCollider_destroy));
MH_SCRIPT(SphereCollider, MH_FIELDS(SphereCollider_fields), MH_AWAKE(SphereCollider_awake), MH_ON_DESTROY(SphereCollider_destroy));
MH_SCRIPT(Rigidbody, MH_FIELDS(Rigidbody_fields), MH_AWAKE(Rigidbody_awake), MH_ON_DESTROY(Rigidbody_destroy));

/* ---- Shapes in the world ---------------------------------------------------------------------------- */

typedef struct {
    mh_entity entity; /* the collider's */
    int32_t body;     /* index in mh_bodies of its rigidbody (it or a parent), -1 for a static collider */
    bool trigger, sphere;
    mh_vec3 center;
    float radius;      /* sphere */
    mh_vec3 axis[3];   /* box: its axes in the world */
    mh_vec3 half;      /* box: half sizes along them */
} mh_shape;

static int32_t mh_body_of(mh_entity entity)
{
    for (int depth = 0; entity != MH_NONE && depth < 256; depth++) {
        for (int32_t b = 0; b < mh_body_count; b++)
            if (mh_bodies[b].entity == entity)
                return b;
        entity = mh_get_parent(entity);
    }
    return -1;
}

static bool mh_shape_of(const mh_collider_ref *c, mh_shape *s)
{
    if (!mh_is_alive(c->entity) || !mh_is_active(c->entity))
        return false;
    mh_vec3 position = mh_get_position(c->entity), scale = mh_get_lossy_scale(c->entity);
    mh_quat rotation = mh_get_rotation(c->entity);
    s->entity = c->entity;
    s->body = mh_body_of(c->entity);
    if (c->shape == MH_SHAPE_SPHERE) {
        const SphereCollider *sc = c->data;
        float k = fmaxf(fabsf(scale.x), fmaxf(fabsf(scale.y), fabsf(scale.z)));
        s->sphere = true;
        s->trigger = sc->is_trigger;
        s->center = mh_v3_add(position, mh_quat_rotate(rotation, mh_v3_mul(sc->center, scale)));
        s->radius = fabsf(sc->radius) * k;
    } else {
        const BoxCollider *bc = c->data;
        s->sphere = false;
        s->trigger = bc->is_trigger;
        s->center = mh_v3_add(position, mh_quat_rotate(rotation, mh_v3_mul(bc->center, scale)));
        s->axis[0] = mh_quat_rotate(rotation, mh_v3(1, 0, 0));
        s->axis[1] = mh_quat_rotate(rotation, mh_v3(0, 1, 0));
        s->axis[2] = mh_quat_rotate(rotation, mh_v3(0, 0, 1));
        s->half = mh_v3(fabsf(bc->size.x * scale.x) * 0.5f, fabsf(bc->size.y * scale.y) * 0.5f, fabsf(bc->size.z * scale.z) * 0.5f);
    }
    return true;
}

static float mh_half_along(const mh_shape *box, int i) { return i == 0 ? box->half.x : i == 1 ? box->half.y : box->half.z; }

/* The point of the box closest to p. */
static mh_vec3 mh_box_closest(const mh_shape *box, mh_vec3 p)
{
    mh_vec3 d = mh_v3_sub(p, box->center), result = box->center;
    for (int i = 0; i < 3; i++) {
        float h = mh_half_along(box, i), k = mh_clamp(mh_v3_dot(d, box->axis[i]), -h, h);
        result = mh_v3_add(result, mh_v3_scale(box->axis[i], k));
    }
    return result;
}

/* Whether a and b overlap; normal (from a towards b) and depth say how to push them apart. */
static bool mh_overlap(const mh_shape *a, const mh_shape *b, mh_vec3 *normal, float *depth)
{
    if (a->sphere && b->sphere) {
        mh_vec3 d = mh_v3_sub(b->center, a->center);
        float distance = mh_v3_length(d), reach = a->radius + b->radius;
        if (distance >= reach)
            return false;
        *normal = distance > 1e-6f ? mh_v3_scale(d, 1 / distance) : mh_v3(0, 1, 0);
        *depth = reach - distance;
        return true;
    }
    if (a->sphere != b->sphere) {
        const mh_shape *sphere = a->sphere ? a : b, *box = a->sphere ? b : a;
        mh_vec3 closest = mh_box_closest(box, sphere->center), d = mh_v3_sub(sphere->center, closest);
        float distance = mh_v3_length(d);
        mh_vec3 n; /* from the box towards the sphere */
        float pen;
        if (distance > 1e-6f) {
            if (distance >= sphere->radius)
                return false;
            n = mh_v3_scale(d, 1 / distance);
            pen = sphere->radius - distance;
        } else {
            /* The center is inside the box: out through the nearest face. */
            mh_vec3 local = mh_v3_sub(sphere->center, box->center);
            float best = INFINITY;
            n = mh_v3(0, 1, 0);
            for (int i = 0; i < 3; i++) {
                float k = mh_v3_dot(local, box->axis[i]), out = mh_half_along(box, i) - fabsf(k);
                if (out < best) {
                    best = out;
                    n = mh_v3_scale(box->axis[i], k < 0 ? -1.0f : 1.0f);
                }
            }
            pen = best + sphere->radius;
        }
        *normal = a->sphere ? mh_v3_scale(n, -1) : n;
        *depth = pen;
        return true;
    }
    /* Two oriented boxes: separating axes (their 6 face axes and the 9 cross products). */
    mh_vec3 axes[15];
    int count = 0;
    for (int i = 0; i < 3; i++) {
        axes[count++] = a->axis[i];
        axes[count++] = b->axis[i];
    }
    for (int i = 0; i < 3; i++)
        for (int j = 0; j < 3; j++) {
            mh_vec3 c = mh_v3_cross(a->axis[i], b->axis[j]);
            if (mh_v3_dot(c, c) > 1e-8f)
                axes[count++] = mh_v3_normalize(c);
        }
    mh_vec3 d = mh_v3_sub(b->center, a->center);
    float best = INFINITY;
    mh_vec3 best_axis = mh_v3(0, 1, 0);
    for (int k = 0; k < count; k++) {
        mh_vec3 axis = axes[k];
        float ra = 0, rb = 0;
        for (int i = 0; i < 3; i++) {
            ra += mh_half_along(a, i) * fabsf(mh_v3_dot(a->axis[i], axis));
            rb += mh_half_along(b, i) * fabsf(mh_v3_dot(b->axis[i], axis));
        }
        float distance = mh_v3_dot(d, axis), overlap = ra + rb - fabsf(distance);
        if (overlap <= 0)
            return false;
        /* Face axes win ties (k < 6), so resting boxes push straight up. */
        if (overlap < best - 1e-5f) {
            best = overlap;
            best_axis = distance < 0 ? mh_v3_scale(axis, -1) : axis;
        }
    }
    *normal = best_axis;
    *depth = best;
    return true;
}

/* ---- Contacts and their callbacks --------------------------------------------------------------------- */

typedef struct {
    mh_entity a, b; /* the colliders' entities, a < b */
    bool trigger;
} mh_contact;

static mh_contact *mh_contacts, *mh_previous;
static int32_t mh_contact_count, mh_previous_count, mh_contact_capacity, mh_previous_capacity;

static void mh_contact_add(mh_entity a, mh_entity b, bool trigger)
{
    if (a > b) {
        mh_entity swap = a;
        a = b;
        b = swap;
    }
    for (int32_t i = 0; i < mh_contact_count; i++)
        if (mh_contacts[i].a == a && mh_contacts[i].b == b)
            return; /* two colliders of the same entities */
    if (mh_contact_count == mh_contact_capacity) {
        int32_t capacity = mh_contact_capacity ? mh_contact_capacity * 2 : 32;
        mh_contact *grown = realloc(mh_contacts, sizeof(*grown) * (size_t)capacity);
        if (!grown)
            return;
        mh_contacts = grown;
        mh_contact_capacity = capacity;
    }
    mh_contacts[mh_contact_count++] = (mh_contact){ a, b, trigger };
}

static bool mh_contact_in(const mh_contact *list, int32_t count, mh_contact c)
{
    for (int32_t i = 0; i < count; i++)
        if (list[i].a == c.a && list[i].b == c.b)
            return true;
    return false;
}

enum { MH_ENTER, MH_EXIT };

/* Calls the callback on every script of the entity. */
static void mh_notify(mh_entity entity, mh_entity other, bool trigger, int what)
{
    if (!mh_is_alive(entity))
        return;
    const mh_script_type *type;
    void *script;
    for (int32_t i = 0; (script = mh_get_script_at(entity, i, &type)) != NULL; i++) {
        mh_contact_callback fn = trigger ? (what == MH_ENTER ? type->on_trigger_enter : type->on_trigger_exit)
                                         : (what == MH_ENTER ? type->on_collision_enter : type->on_collision_exit);
        if (fn)
            fn(script, entity, other);
    }
}

static void mh_notify_pair(mh_contact c, int what)
{
    mh_notify(c.a, c.b, c.trigger, what);
    mh_notify(c.b, c.a, c.trigger, what);
}

bool mh_is_touching(mh_entity a, mh_entity b)
{
    mh_contact c = { a < b ? a : b, a < b ? b : a, false };
    return mh_contact_in(mh_contacts, mh_contact_count, c);
}

/* ---- The step ----------------------------------------------------------------------------------------- */

static Rigidbody *mh_rigidbody(mh_entity entity)
{
    Rigidbody *body = mh_get_script(entity, "Rigidbody");
    if (!body)
        mh_log_error("%s has no Rigidbody.", mh_is_alive(entity) ? mh_get_name(entity) : "The entity");
    return body;
}

static float mh_inverse_mass(int32_t body)
{
    if (body < 0)
        return 0;
    const Rigidbody *r = mh_bodies[body].body;
    return r->is_kinematic || r->mass <= 0 ? 0 : 1 / r->mass;
}

static mh_vec3 mh_body_velocity(int32_t body) { return body < 0 ? mh_v3(0, 0, 0) : mh_bodies[body].body->velocity; }

void mh_physics_step(float dt)
{
    /* Forces and gravity, then the bodies move. */
    for (int32_t i = 0; i < mh_body_count; i++) {
        Rigidbody *r = mh_bodies[i].body;
        mh_entity e = mh_bodies[i].entity;
        if (!mh_is_alive(e) || !mh_is_active(e))
            continue;
        if (!r->is_kinematic) {
            mh_vec3 acceleration = r->use_gravity ? mh_gravity : mh_v3(0, 0, 0);
            if (r->mass > 0)
                acceleration = mh_v3_add(acceleration, mh_v3_scale(r->force, 1 / r->mass));
            r->velocity = mh_v3_add(r->velocity, mh_v3_scale(acceleration, dt));
            if (r->drag > 0)
                r->velocity = mh_v3_scale(r->velocity, 1 / (1 + r->drag * dt));
            mh_set_position(e, mh_v3_add(mh_get_position(e), mh_v3_scale(r->velocity, dt)));
        }
        r->force = mh_v3(0, 0, 0);
    }

    /* Where the colliders are now. */
    mh_shape *shapes = mh_collider_count ? malloc(sizeof(*shapes) * (size_t)mh_collider_count) : NULL;
    int32_t shape_count = 0;
    for (int32_t i = 0; shapes && i < mh_collider_count; i++)
        if (mh_shape_of(&mh_colliders[i], &shapes[shape_count]))
            shape_count++;

    mh_contact *swap = mh_previous;
    int32_t swap_capacity = mh_previous_capacity;
    mh_previous = mh_contacts;
    mh_previous_capacity = mh_contact_capacity;
    mh_previous_count = mh_contact_count;
    mh_contacts = swap;
    mh_contact_capacity = swap_capacity;
    mh_contact_count = 0;

    for (int32_t i = 0; i < shape_count; i++)
        for (int32_t j = i + 1; j < shape_count; j++) {
            mh_shape *a = &shapes[i], *b = &shapes[j];
            /* Something has to move: a rigidbody on at least one side, and not the same one. */
            if (a->body == b->body)
                continue;
            mh_vec3 n;
            float depth;
            if (!mh_overlap(a, b, &n, &depth))
                continue;
            bool trigger = a->trigger || b->trigger;
            mh_contact_add(a->entity, b->entity, trigger);
            if (trigger)
                continue;
            float wa = mh_inverse_mass(a->body), wb = mh_inverse_mass(b->body), w = wa + wb;
            if (w <= 0)
                continue;
            /* Out of each other, by their inverse masses. */
            if (wa > 0) {
                mh_entity e = mh_bodies[a->body].entity;
                mh_set_position(e, mh_v3_sub(mh_get_position(e), mh_v3_scale(n, depth * wa / w)));
            }
            if (wb > 0) {
                mh_entity e = mh_bodies[b->body].entity;
                mh_set_position(e, mh_v3_add(mh_get_position(e), mh_v3_scale(n, depth * wb / w)));
            }
            /* Then their speeds: a bounce along the normal, friction across it. */
            mh_vec3 relative = mh_v3_sub(mh_body_velocity(b->body), mh_body_velocity(a->body));
            float along = mh_v3_dot(relative, n);
            if (along >= 0)
                continue;
            float bounce = fmaxf(a->body >= 0 ? mh_bodies[a->body].body->bounciness : 0,
                                 b->body >= 0 ? mh_bodies[b->body].body->bounciness : 0);
            float friction = sqrtf(fmaxf(a->body >= 0 ? mh_bodies[a->body].body->friction : 0.4f, 0) *
                                   fmaxf(b->body >= 0 ? mh_bodies[b->body].body->friction : 0.4f, 0));
            float j = -(1 + bounce) * along / w;
            mh_vec3 impulse = mh_v3_scale(n, j);
            mh_vec3 tangent = mh_v3_sub(relative, mh_v3_scale(n, along));
            float tangent_speed = mh_v3_length(tangent);
            if (tangent_speed > 1e-6f) {
                float jt = fminf(tangent_speed / w, friction * j);
                impulse = mh_v3_add(impulse, mh_v3_scale(tangent, jt / tangent_speed));
            }
            if (wa > 0)
                mh_bodies[a->body].body->velocity = mh_v3_sub(mh_bodies[a->body].body->velocity, mh_v3_scale(impulse, wa));
            if (wb > 0)
                mh_bodies[b->body].body->velocity = mh_v3_add(mh_bodies[b->body].body->velocity, mh_v3_scale(impulse, wb));
        }
    free(shapes);

    /* Exits, then enters. The callbacks may destroy or create colliders: the lists are copies. */
    int32_t previous_count = mh_previous_count, contact_count = mh_contact_count;
    mh_contact *previous = previous_count ? malloc(sizeof(*previous) * (size_t)previous_count) : NULL;
    mh_contact *current = contact_count ? malloc(sizeof(*current) * (size_t)contact_count) : NULL;
    if (previous)
        memcpy(previous, mh_previous, sizeof(*previous) * (size_t)previous_count);
    if (current)
        memcpy(current, mh_contacts, sizeof(*current) * (size_t)contact_count);
    for (int32_t i = 0; previous && i < previous_count; i++)
        if (!mh_contact_in(current, contact_count, previous[i]))
            mh_notify_pair(previous[i], MH_EXIT);
    for (int32_t i = 0; current && i < contact_count; i++)
        if (!mh_contact_in(previous, previous_count, current[i]))
            mh_notify_pair(current[i], MH_ENTER);
    free(previous);
    free(current);
}

void mh_physics_reset(void)
{
    free(mh_colliders);
    free(mh_bodies);
    free(mh_contacts);
    free(mh_previous);
    mh_colliders = NULL;
    mh_bodies = NULL;
    mh_contacts = mh_previous = NULL;
    mh_collider_count = mh_collider_capacity = mh_body_count = mh_body_capacity = 0;
    mh_contact_count = mh_contact_capacity = mh_previous_count = mh_previous_capacity = 0;
    mh_gravity = mh_v3(0, -9.81f, 0);
}

/* ---- For the scripts ---------------------------------------------------------------------------------- */

mh_vec3 mh_get_velocity(mh_entity entity)
{
    Rigidbody *r = mh_rigidbody(entity);
    return r ? r->velocity : mh_v3(0, 0, 0);
}

void mh_set_velocity(mh_entity entity, mh_vec3 velocity)
{
    Rigidbody *r = mh_rigidbody(entity);
    if (r)
        r->velocity = velocity;
}

void mh_add_force(mh_entity entity, mh_vec3 force)
{
    Rigidbody *r = mh_rigidbody(entity);
    if (r)
        r->force = mh_v3_add(r->force, force);
}

void mh_add_impulse(mh_entity entity, mh_vec3 impulse)
{
    Rigidbody *r = mh_rigidbody(entity);
    if (r && !r->is_kinematic && r->mass > 0)
        r->velocity = mh_v3_add(r->velocity, mh_v3_scale(impulse, 1 / r->mass));
}

mh_vec3 mh_get_gravity(void) { return mh_gravity; }
void mh_set_gravity(mh_vec3 gravity) { mh_gravity = gravity; }

/* Where the ray enters the shape (its distance), or false. */
static bool mh_ray_shape(const mh_shape *s, mh_vec3 origin, mh_vec3 dir, float *t, mh_vec3 *normal)
{
    if (s->sphere) {
        mh_vec3 m = mh_v3_sub(origin, s->center);
        float b = mh_v3_dot(m, dir), c = mh_v3_dot(m, m) - s->radius * s->radius;
        if (c > 0 && b > 0)
            return false;
        float disc = b * b - c;
        if (disc < 0)
            return false;
        *t = fmaxf(-b - sqrtf(disc), 0);
        *normal = mh_v3_normalize(mh_v3_sub(mh_v3_add(origin, mh_v3_scale(dir, *t)), s->center));
        return true;
    }
    float near = -INFINITY, far = INFINITY;
    mh_vec3 near_normal = mh_v3(0, 1, 0);
    mh_vec3 p = mh_v3_sub(s->center, origin);
    for (int i = 0; i < 3; i++) {
        float e = mh_v3_dot(s->axis[i], p), f = mh_v3_dot(s->axis[i], dir), h = mh_half_along(s, i);
        if (fabsf(f) > 1e-8f) {
            float t1 = (e + h) / f, t2 = (e - h) / f;
            if (t1 > t2) {
                float swap = t1;
                t1 = t2;
                t2 = swap;
            }
            if (t1 > near) {
                near = t1;
                near_normal = mh_v3_scale(s->axis[i], f > 0 ? -1.0f : 1.0f);
            }
            far = fminf(far, t2);
            if (near > far || far < 0)
                return false;
        } else if (-e - h > 0 || -e + h < 0)
            return false;
    }
    *t = near > 0 ? near : 0;
    *normal = near_normal;
    return true;
}

bool mh_raycast(mh_vec3 origin, mh_vec3 direction, float max_distance, mh_hit *hit)
{
    mh_vec3 dir = mh_v3_normalize(direction);
    if (mh_v3_dot(dir, dir) == 0)
        return false;
    bool found = false;
    float best = max_distance;
    for (int32_t i = 0; i < mh_collider_count; i++) {
        mh_shape s;
        float t;
        mh_vec3 n;
        if (!mh_shape_of(&mh_colliders[i], &s) || s.trigger || !mh_ray_shape(&s, origin, dir, &t, &n) || t > best)
            continue;
        best = t;
        found = true;
        if (hit)
            *hit = (mh_hit){ s.entity, mh_v3_add(origin, mh_v3_scale(dir, t)), n, t };
    }
    return found;
}
