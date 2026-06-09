/* SPDX-License-Identifier: MIT */

#pragma once

#include <stdint.h>

#ifdef _WIN32
#define EXPORT __declspec(dllexport)
#else
#define EXPORT __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct ae_engine ae_engine;

typedef enum ae_result : int32_t {
	AE_OK = 0,
	AE_ERR_NULL = -1,
	AE_ERR_BAD_STATE = -2,
	AE_ERR_JACK_OPEN_FAILED = -3,
	AE_ERR_JACK_PORT_FAILED = -4,
	AE_ERR_JACK_ACTIVATE_FAILED = -5,
	AE_ERR_NO_OUTPUT_PORTS = -6,
	AE_ERR_COMMAND_QUEUE_FULL = -7,
	AE_ERR_LOCK_POISONED = -8,
	AE_ERR_BAD_COMMAND = -9,
} ae_result;

typedef struct ae_config {
	int32_t channels;
	int32_t command_capacity;
	int32_t max_voices;
} ae_config;

typedef struct ae_stats {
	int64_t frame_cursor;
	int32_t sample_rate;
	int32_t quantum_frames;
	int32_t channel_count;
	int64_t xrun_count;
	int32_t running;
	float cpu_load;
	int64_t dropped_command_count;
} ae_stats;

typedef enum ae_command_kind : uint32_t {
	AE_COMMAND_NONE = 0,
	AE_COMMAND_SET_TEST_TONE = 1,
} ae_command_kind;

typedef struct ae_set_test_tone_command {
	int32_t enabled;
	float hz;
	float gain;
	int32_t _reserved0;
} ae_set_test_tone_command;

typedef union ae_command_data {
	ae_set_test_tone_command set_test_tone;
	uint64_t raw[8];
} ae_command_data;

typedef struct ae_command {
	ae_command_kind kind;
	uint32_t size;
	int64_t target_frame;
	ae_command_data data;
} ae_command;

#define AE_COMMAND_IMMEDIATE ((int64_t)INT64_MIN)

EXPORT ae_engine *ae_create(const ae_config *config);
EXPORT void ae_destroy(ae_engine *engine);

EXPORT ae_result ae_start_jack(ae_engine *engine, const char *client_name, int32_t autoconnect);
EXPORT ae_result ae_stop(ae_engine *engine);

EXPORT ae_result ae_get_stats(ae_engine *engine, ae_stats *out_stats);
EXPORT ae_result ae_enqueue_command(ae_engine *engine, const ae_command *command);

#ifdef __cplusplus
}
#endif
