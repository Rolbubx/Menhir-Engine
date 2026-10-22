#include <menhir.h>

/* Floats the entity up and down around where it starts. */
typedef struct {
    float height;
    float period;
    mh_vec3 start;
} Bobber;

static const mh_field Bobber_fields[] = {
    MH_FLOAT(Bobber, height, 0.25f),
    MH_FLOAT(Bobber, period, 2, MH_TOOLTIP("Seconds for a full bob")),
};

static void Bobber_start(Bobber *self, mh_entity entity)
{
    self->start = mh_get_position(entity);
    mh_log("%s bobs around (%.2f, %.2f, %.2f)", mh_get_name(entity), self->start.x, self->start.y, self->start.z);
}

static void Bobber_update(Bobber *self, mh_entity entity)
{
    float phase = mh_time() / self->period * 2 * MH_PI;
    mh_set_position(entity, mh_v3_add(self->start, mh_v3(0, sinf(phase) * self->height, 0)));
}

MH_SCRIPT(Bobber, MH_FIELDS(Bobber_fields), MH_START(Bobber_start), MH_UPDATE(Bobber_update));
