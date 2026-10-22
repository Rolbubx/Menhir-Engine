#include <menhir.h>

/* Turns the entity around an axis; Space reverses it. */
typedef struct {
    float speed;
    mh_vec3 axis;
} Spinner;

static const mh_field Spinner_fields[] = {
    MH_FLOAT(Spinner, speed, 45, MH_RANGE(0, 360), MH_TOOLTIP("Degrees per second")),
    MH_VEC3(Spinner, axis, 0, 1, 0),
};

static void Spinner_update(Spinner *self, mh_entity entity)
{
    if (mh_key_down("space"))
        self->speed = -self->speed;
    mh_rotate(entity, mh_v3_scale(self->axis, self->speed * mh_delta_time()), true);
}

MH_SCRIPT(Spinner, MH_FIELDS(Spinner_fields), MH_UPDATE(Spinner_update));
