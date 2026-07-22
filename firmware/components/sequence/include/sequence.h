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

/**
 * @brief Start or resume playback from the current step.
 *
 * If playback is already running this function has no effect.
 *
 * @return ESP_OK on success.
 */
esp_err_t sequence_play(void);

/**
 * @brief Pause playback without changing the current step or LED state.
 *
 * The modulators configured in `led_engine` continue to run while paused, so
 * the LEDs keep their current dynamic output.
 *
 * @return ESP_OK on success.
 */
esp_err_t sequence_pause(void);

/**
 * @brief Jump to a specific step and apply its configuration.
 *
 * If the sequence is playing, playback continues from the new position. If it
 * is paused, the configuration is applied but playback remains paused.
 *
 * @param[in] step_index Zero-based step index.
 *
 * @return ESP_OK on success, or ESP_ERR_INVALID_ARG for an out-of-range index.
 */
esp_err_t sequence_seek(uint32_t step_index);

/**
 * @brief Advance the sequence timeline by one tick.
 *
 * Call this function every SEQUENCE_STEP_TICK_PERIOD_MS milliseconds. It
 * advances the current step timer and, when the duration expires, moves to the
 * next step and applies its configuration to `led_engine`.
 *
 * @return ESP_OK on success, or an error propagated from `led_engine`.
 */
esp_err_t sequence_tick(void);

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
