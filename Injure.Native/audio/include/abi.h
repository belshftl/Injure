/*
 * SPDX-FileCopyrightText: 2026 belshftl
 * SPDX-License-Identifier: MIT
 */

#pragma once

#include <stdint.h>

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((__visibility__("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct ae_engine ae_engine;
typedef uint64_t ae_sound_id;
typedef uint64_t ae_voice_id;

typedef enum ae_result : uint32_t {
	AE_OK = 0,

	AE_ERR_NULL                 = 0x80000001,
	AE_ERR_BAD_STATE            = 0x80000002,
	AE_ERR_COMMAND_QUEUE_FULL   = 0x80000003,
	AE_ERR_MUTEX_POISONED       = 0x80000004,
	AE_ERR_BAD_SOUND_DESC       = 0x80000005,
	AE_ERR_SOUND_NOT_FOUND      = 0x80000006,
	AE_ERR_SOUND_BAD_STATE      = 0x80000007,
	AE_ERR_SOUND_IN_USE         = 0x80000008,
	AE_ERR_BAD_VOICE_DESC       = 0x80000009,
	AE_ERR_VOICE_NOT_FOUND      = 0x8000000a,
	AE_ERR_UNSUPPORTED_PLAYBACK = 0x8000000b,
	AE_ERR_ID_EXHAUSTED         = 0x8000000c,

	AE_ERR_JACK_OPEN_FAILED     = 0x81000001,
	AE_ERR_JACK_PORT_FAILED     = 0x81000002,
	AE_ERR_JACK_ACTIVATE_FAILED = 0x81000003,

	AE_ERR_OUT_OF_MEMORY        = 0xffffffff,
} ae_result;

typedef enum ae_sample_format : uint32_t {
	AE_SAMPLE_FORMAT_F32 = 1,
} ae_sample_format;

typedef enum ae_voice_flags : uint32_t {
	AE_VOICE_FLAG_NONE = 0,
	AE_VOICE_FLAG_LOOP = 1 << 0,
} ae_voice_flags;

#define AE_FRAME_IMMEDIATE ((int64_t)INT64_MIN)

typedef struct ae_config {
	int32_t channels;
	int32_t command_capacity;
	int32_t maintenance_capacity;
} ae_config;

typedef struct ae_stats {
	int64_t frame_cursor;
	int32_t sample_rate;
	int32_t quantum_frames;
	int32_t channel_count;
	int64_t xrun_count;
	int32_t running;
	float cpu_load;
	uint64_t active_voice_count;
	uint64_t voice_block_count;
	uint64_t failed_voice_start_count;
	uint64_t allocated_sound_count;
	uint64_t committed_sound_count;
} ae_stats;

typedef struct ae_sound_desc {
	uint32_t channels;
	uint32_t sample_rate;
	uint64_t frame_count;
	ae_sample_format format;
	uint32_t flags;
} ae_sound_desc;

typedef struct ae_sound_mapping {
	void *data;
	uint64_t byte_length;
	uint64_t frame_stride;
} ae_sound_mapping;

typedef struct ae_play_voice_desc {
	ae_sound_id sound;
	int64_t start_frame;
	uint64_t source_frame;
	float gain;
	float playback_rate;
	ae_voice_flags flags;
	uint32_t reserved0;
} ae_play_voice_desc;

EXPORT ae_engine *ae_create(const ae_config *config);
EXPORT void ae_destroy(ae_engine *engine);

EXPORT ae_result ae_start_jack(ae_engine *engine, const char *client_name, int32_t autoconnect);
EXPORT ae_result ae_stop(ae_engine *engine);
EXPORT ae_result ae_get_stats(ae_engine *engine, ae_stats *out_stats);
EXPORT ae_result ae_collect_garbage(ae_engine *engine);

EXPORT ae_result ae_sound_alloc(ae_engine *engine, const ae_sound_desc *desc, ae_sound_id *out_sound);
EXPORT ae_result ae_sound_get_buffer(ae_engine *engine, ae_sound_id sound, ae_sound_mapping *out_mapping);
EXPORT ae_result ae_sound_commit(ae_engine *engine, ae_sound_id sound);
EXPORT ae_result ae_sound_free(ae_engine *engine, ae_sound_id sound);

EXPORT ae_result ae_voice_play(ae_engine *engine, const ae_play_voice_desc *desc, ae_voice_id *out_voice);
EXPORT ae_result ae_voice_stop(ae_engine *engine, ae_voice_id voice);

#ifdef __cplusplus
}
#endif
