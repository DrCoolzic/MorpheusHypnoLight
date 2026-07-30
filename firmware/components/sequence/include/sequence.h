/**
 * @file sequence.h
 * @brief Step-based playback engine for HypnoLight sequences.
 *
 * The `sequence` component stores a fixed-size array of steps and advances
 * playback. Each step defines, for every oscillator, a static waveform
 * configuration and three independent modulators for frequency, brightness, and
 * duty cycle. The actual realtime evaluation of those modulators is performed
 * by the `led_engine` component.
 */
#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "esp_err.h"
#include "modulator.h"
#include "oscillator.h"

/** @brief Maximum number of steps stored internally by the sequence engine. */
#define SEQUENCE_MAX_STEPS 128U

/** @brief Fixed interval at which sequence_tick() must be called. */
#define SEQUENCE_STEP_TICK_PERIOD_MS 100U

/**
 * @brief Per-oscillator data for one sequence step.
 *
 * The modulator configurations are evaluated by `led_engine` at 1 kHz. The
 * static configuration is applied once at step entry.
 */
typedef struct {
  oscillator_static_config_t static_config;
  modulator_config_t frequency_modulator;
  modulator_config_t brightness_modulator;
  modulator_config_t duty_modulator;
} sequence_oscillator_step_t;

/**
 * @brief One sequence step.
 *
 * The duration is common to all oscillators. Each oscillator carries its own
 * static waveform and modulator settings.
 */
typedef struct {
  uint32_t duration_ms;
  sequence_oscillator_step_t oscillators[OSCILLATOR_COUNT];
} sequence_step_t;

/**
 * @brief Initialize the sequence playback state.
 *
 * All step data, playback position, and playback state are reset. This function
 * does not initialize the `led_engine`; it must be initialized before playback
 * starts.
 *
 * @return ESP_OK on success.
 */
esp_err_t sequence_init(void);

/**
 * @brief Load a sequence into internal RAM.
 *
 * The steps are copied into an internal buffer limited by
 * SEQUENCE_MAX_STEPS. All static configurations and modulator configurations
 * are validated. Playback is reset to the first step and paused.
 *
 * @param[in] steps Array of sequence steps.
 * @param[in] step_count Number of steps to copy.
 *
 * @return ESP_OK on success, or ESP_ERR_INVALID_ARG for invalid input.
 */
esp_err_t sequence_load(const sequence_step_t *steps, uint32_t step_count);

esp_err_t sequence_decode_compact(const uint8_t *data, size_t data_length,
                                  sequence_step_t *steps,
                                  uint32_t steps_capacity,
                                  uint32_t *step_count);

esp_err_t sequence_load_compact(const uint8_t *data, size_t data_length);

/**
 * @brief Replace a single step in the loaded sequence.
 *
 * The replacement is atomic: the step is validated, the current playback is
 * paused if needed, and the step is copied into internal storage. If the
 * replaced step is the one currently playing, its modulators are re-applied
 * before playback resumes.
 *
 * @param[in] step_index Zero-based index of the step to replace.
 * @param[in] step Pointer to the new step data.
 *
 * @return ESP_OK on success, or ESP_ERR_INVALID_ARG for an invalid index or
 * step.
 */
esp_err_t sequence_replace_step(uint32_t step_index,
                                const sequence_step_t *step);

/**
 * @brief Start or resume playback from the current step.
 *
 * If playback is already running this function has no effect.
 *
 * @return ESP_OK on success.
 */
esp_err_t sequence_play(void);

/**
 * @brief Pause playback without changing the current step.
 *
 * Linear ramps are frozen at their current value. Static and LFO modulators
 * keep running, so the LEDs retain their current dynamic output.
 *
 * @return ESP_OK on success.
 */
esp_err_t sequence_pause(void);

/**
 * @brief Stop playback, reset the cursor to the beginning and turn all LEDs
 * off.
 *
 * @return ESP_OK on success.
 */
esp_err_t sequence_stop(void);

/**
 * @brief Jump to a position in the sequence and apply the corresponding step.
 *
 * The position is resolved to a step and an offset inside that step. The
 * modulators in `led_engine` are seeked to the exact value at the requested
 * position.
 *
 * @param[in] position_ms Absolute position in the sequence, in milliseconds.
 *
 * @return ESP_OK on success, or ESP_ERR_INVALID_ARG for an out-of-range
 * position.
 */
esp_err_t sequence_seek(uint32_t position_ms);

/**
 * @brief Return whether the sequence engine is currently playing.
 *
 * @return true when playback is active.
 */
bool sequence_is_playing(void);

/**
 * @brief Return the zero-based index of the current step.
 *
 * @return Current step index.
 */
uint32_t sequence_get_current_step(void);

/**
 * @brief Return the elapsed time inside the current step.
 *
 * @return Elapsed time in milliseconds.
 */
uint32_t sequence_get_elapsed_ms(void);

/**
 * @brief Return the number of steps currently loaded.
 *
 * @return Step count.
 */
uint32_t sequence_get_step_count(void);

/**
 * @brief Return the absolute elapsed time since the start of the sequence.
 *
 * This is the sum of the durations of all previous steps plus the elapsed time
 * inside the current step.
 *
 * @return Total elapsed time in milliseconds.
 */
uint32_t sequence_get_total_elapsed_ms(void);
