/* Audio, in the scripting runtime, with miniaudio (Engine/ThirdParty/miniaudio; its implementation is
   compiled once on its own, see menhir_miniaudio.c): the AudioSource component (a script type of its
   own), one-shot sounds, and the listener at the main camera. The device opens with the first sound;
   without one (or with MENHIR_NO_AUDIO, the tests' headless players) the game runs silent. */
#include <stdlib.h>
#include <string.h>
#include "menhir.h"
#include "menhir_internal.h"

typedef struct {
    const char *clip;
    float volume, pitch;
    bool loop, play_on_awake, spatial;
    float min_distance, max_distance;
    void *sound; /* ma_sound, once loaded */
} AudioSource;

static const mh_field AudioSource_fields[] = {
    MH_STRING(AudioSource, clip, "", MH_TOOLTIP("A file of the Assets folder (WAV, MP3, FLAC), e.g. Sounds/jump.wav")),
    MH_FLOAT(AudioSource, volume, 1, MH_RANGE(0, 1)),
    MH_FLOAT(AudioSource, pitch, 1, MH_RANGE(0.1, 3)),
    MH_BOOL(AudioSource, loop, 0),
    MH_BOOL(AudioSource, play_on_awake, 1),
    MH_BOOL(AudioSource, spatial, 0, MH_TOOLTIP("Sounds from where it is, heard from the main camera")),
    MH_FLOAT(AudioSource, min_distance, 1),
    MH_FLOAT(AudioSource, max_distance, 50),
};

#ifdef MENHIR_NO_AUDIO

static void AudioSource_start(AudioSource *self, mh_entity entity) { (void)self; (void)entity; }
static void AudioSource_destroy(AudioSource *self, mh_entity entity) { (void)self; (void)entity; }
void mh_play_sound(const char *clip, float volume) { (void)clip; (void)volume; }
void mh_play_sound_at(const char *clip, mh_vec3 position, float volume) { (void)clip; (void)position; (void)volume; }
void mh_audio_play(mh_entity entity) { (void)entity; }
void mh_audio_stop(mh_entity entity) { (void)entity; }
void mh_audio_pause(mh_entity entity) { (void)entity; }
bool mh_audio_is_playing(mh_entity entity) { (void)entity; return false; }
void mh_audio_set_volume(mh_entity entity, float volume) { (void)entity; (void)volume; }
void mh_audio_set_pitch(mh_entity entity, float pitch) { (void)entity; (void)pitch; }
void mh_set_master_volume(float volume) { (void)volume; }
void mh_audio_update(void) {}
void mh_audio_shutdown(void) {}

#else

#include "../../ThirdParty/miniaudio/miniaudio.h"

static ma_engine mh_audio_engine;
static int mh_audio_state; /* 0 not opened yet, 1 open, -1 failed */

static bool mh_audio_open(void)
{
    if (mh_audio_state == 0) {
        ma_result result = ma_engine_init(NULL, &mh_audio_engine);
        mh_audio_state = result == MA_SUCCESS ? 1 : -1;
        if (mh_audio_state < 0)
            mh_log_warning("No audio device (%s): the game runs without sound.", ma_result_description(result));
    }
    return mh_audio_state > 0;
}

/* Opens a sound into memory that stays where it is (miniaudio keeps pointers to it). */
static bool mh_sound_init(ma_sound *sound, const char *clip, ma_uint32 flags)
{
    if (!clip || !*clip || !mh_audio_open())
        return false;
    if (ma_sound_init_from_file(&mh_audio_engine, mh_asset_path(clip), flags, NULL, NULL, sound) != MA_SUCCESS) {
        mh_log_error("The sound %s could not be opened (a WAV, MP3 or FLAC file of the Assets folder).", clip);
        return false;
    }
    return true;
}

/* ---- AudioSource ---------------------------------------------------------------------------------- */

/* Every started AudioSource, for the update (spatial ones follow their entity). */
typedef struct {
    AudioSource *source;
    mh_entity entity;
} mh_source_ref;

static mh_source_ref *mh_sources;
static int32_t mh_source_count, mh_source_capacity;

static AudioSource *mh_audio_source(mh_entity entity)
{
    AudioSource *source = mh_get_script(entity, "AudioSource");
    if (!source)
        mh_log_error("%s has no AudioSource.", mh_is_alive(entity) ? mh_get_name(entity) : "The entity");
    return source;
}

static void mh_source_apply(AudioSource *self, mh_entity entity)
{
    ma_sound *sound = self->sound;
    ma_sound_set_volume(sound, mh_clamp(self->volume, 0, 1));
    ma_sound_set_pitch(sound, fmaxf(self->pitch, 0.01f));
    ma_sound_set_looping(sound, self->loop);
    ma_sound_set_spatialization_enabled(sound, self->spatial);
    if (self->spatial) {
        ma_sound_set_min_distance(sound, fmaxf(self->min_distance, 0.01f));
        ma_sound_set_max_distance(sound, fmaxf(self->max_distance, self->min_distance));
        mh_vec3 p = mh_get_position(entity);
        ma_sound_set_position(sound, p.x, p.y, p.z);
    }
}

static void AudioSource_start(AudioSource *self, mh_entity entity)
{
    if (mh_source_count == mh_source_capacity) {
        int32_t capacity = mh_source_capacity ? mh_source_capacity * 2 : 16;
        mh_source_ref *grown = realloc(mh_sources, sizeof(*grown) * (size_t)capacity);
        if (!grown)
            return;
        mh_sources = grown;
        mh_source_capacity = capacity;
    }
    mh_sources[mh_source_count++] = (mh_source_ref){ self, entity };
    if (self->play_on_awake)
        mh_audio_play(entity);
}

static void mh_source_release(AudioSource *self)
{
    if (self->sound) {
        ma_sound_uninit(self->sound);
        free(self->sound);
        self->sound = NULL;
    }
}

static void AudioSource_destroy(AudioSource *self, mh_entity entity)
{
    (void)entity;
    for (int32_t i = 0; i < mh_source_count; i++)
        if (mh_sources[i].source == self) {
            mh_sources[i] = mh_sources[--mh_source_count];
            break;
        }
    mh_source_release(self);
}

void mh_audio_play(mh_entity entity)
{
    AudioSource *source = mh_audio_source(entity);
    if (!source)
        return;
    if (!source->sound) {
        ma_sound *sound = malloc(sizeof(*sound));
        if (!sound)
            return;
        if (!mh_sound_init(sound, source->clip, source->loop ? MA_SOUND_FLAG_STREAM : MA_SOUND_FLAG_DECODE)) {
            free(sound);
            return;
        }
        source->sound = sound;
    }
    mh_source_apply(source, entity);
    ma_sound_seek_to_pcm_frame(source->sound, 0);
    ma_sound_start(source->sound);
}

void mh_audio_stop(mh_entity entity)
{
    AudioSource *source = mh_audio_source(entity);
    if (source && source->sound) {
        ma_sound_stop(source->sound);
        ma_sound_seek_to_pcm_frame(source->sound, 0);
    }
}

void mh_audio_pause(mh_entity entity)
{
    AudioSource *source = mh_audio_source(entity);
    if (source && source->sound)
        ma_sound_stop(source->sound);
}

bool mh_audio_is_playing(mh_entity entity)
{
    AudioSource *source = mh_get_script(entity, "AudioSource");
    return source && source->sound && ma_sound_is_playing(source->sound);
}

void mh_audio_set_volume(mh_entity entity, float volume)
{
    AudioSource *source = mh_audio_source(entity);
    if (!source)
        return;
    source->volume = volume;
    if (source->sound)
        ma_sound_set_volume(source->sound, mh_clamp(volume, 0, 1));
}

void mh_audio_set_pitch(mh_entity entity, float pitch)
{
    AudioSource *source = mh_audio_source(entity);
    if (!source)
        return;
    source->pitch = pitch;
    if (source->sound)
        ma_sound_set_pitch(source->sound, fmaxf(pitch, 0.01f));
}

/* ---- One-shot sounds -------------------------------------------------------------------------------- */

static ma_sound **mh_one_shots;
static int32_t mh_one_shot_count, mh_one_shot_capacity;

static void mh_play_one_shot(const char *clip, const mh_vec3 *position, float volume)
{
    if (mh_one_shot_count == mh_one_shot_capacity) {
        int32_t capacity = mh_one_shot_capacity ? mh_one_shot_capacity * 2 : 16;
        ma_sound **grown = realloc(mh_one_shots, sizeof(*grown) * (size_t)capacity);
        if (!grown)
            return;
        mh_one_shots = grown;
        mh_one_shot_capacity = capacity;
    }
    ma_sound *sound = malloc(sizeof(*sound));
    if (!sound)
        return;
    if (!mh_sound_init(sound, clip, MA_SOUND_FLAG_DECODE)) {
        free(sound);
        return;
    }
    ma_sound_set_volume(sound, mh_clamp(volume, 0, 1));
    ma_sound_set_spatialization_enabled(sound, position != NULL);
    if (position)
        ma_sound_set_position(sound, position->x, position->y, position->z);
    ma_sound_start(sound);
    mh_one_shots[mh_one_shot_count++] = sound;
}

void mh_play_sound(const char *clip, float volume) { mh_play_one_shot(clip, NULL, volume); }
void mh_play_sound_at(const char *clip, mh_vec3 position, float volume) { mh_play_one_shot(clip, &position, volume); }

void mh_set_master_volume(float volume)
{
    if (mh_audio_open())
        ma_engine_set_volume(&mh_audio_engine, mh_clamp(volume, 0, 1));
}

/* ---- Each frame, and at the end ------------------------------------------------------------------------ */

void mh_audio_update(void)
{
    if (mh_audio_state <= 0)
        return;
    mh_entity camera = mh_main_camera();
    if (camera != MH_NONE) {
        mh_vec3 p = mh_get_position(camera), f = mh_forward(camera), u = mh_up(camera);
        ma_engine_listener_set_position(&mh_audio_engine, 0, p.x, p.y, p.z);
        ma_engine_listener_set_direction(&mh_audio_engine, 0, f.x, f.y, f.z);
        ma_engine_listener_set_world_up(&mh_audio_engine, 0, u.x, u.y, u.z);
    }
    for (int32_t i = 0; i < mh_source_count; i++)
        if (mh_sources[i].source->sound && mh_sources[i].source->spatial) {
            mh_vec3 p = mh_get_position(mh_sources[i].entity);
            ma_sound_set_position(mh_sources[i].source->sound, p.x, p.y, p.z);
        }
    int32_t kept = 0;
    for (int32_t i = 0; i < mh_one_shot_count; i++) {
        if (ma_sound_at_end(mh_one_shots[i])) {
            ma_sound_uninit(mh_one_shots[i]);
            free(mh_one_shots[i]);
        } else
            mh_one_shots[kept++] = mh_one_shots[i];
    }
    mh_one_shot_count = kept;
}

void mh_audio_shutdown(void)
{
    for (int32_t i = 0; i < mh_one_shot_count; i++) {
        ma_sound_uninit(mh_one_shots[i]);
        free(mh_one_shots[i]);
    }
    free(mh_one_shots);
    mh_one_shots = NULL;
    mh_one_shot_count = mh_one_shot_capacity = 0;
    /* The AudioSources were destroyed with their scenes; any left go with the device. */
    for (int32_t i = 0; i < mh_source_count; i++)
        mh_source_release(mh_sources[i].source);
    free(mh_sources);
    mh_sources = NULL;
    mh_source_count = mh_source_capacity = 0;
    if (mh_audio_state > 0)
        ma_engine_uninit(&mh_audio_engine);
    mh_audio_state = 0;
}

#endif

MH_SCRIPT(AudioSource, MH_FIELDS(AudioSource_fields), MH_START(AudioSource_start), MH_ON_DESTROY(AudioSource_destroy));
