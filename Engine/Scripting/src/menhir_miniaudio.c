/* miniaudio's implementation, compiled on its own once (it is big) and kept in the cache; see
   menhir_audio.c. Only playback and the decoders the games use. */
#define MINIAUDIO_IMPLEMENTATION
#define MA_NO_ENCODING
#define MA_NO_GENERATION
#include "../../ThirdParty/miniaudio/miniaudio.h"
