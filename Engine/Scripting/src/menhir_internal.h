/* Between the runtime's files (menhir_runtime.c, menhir_physics.c, menhir_audio.c). */
#ifndef MENHIR_INTERNAL_H
#define MENHIR_INTERNAL_H

#include "menhir.h"

void mh_physics_step(float dt);
void mh_physics_reset(void);
void mh_audio_update(void);
void mh_audio_shutdown(void);

#endif
