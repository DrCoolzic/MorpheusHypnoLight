/**
 * @file modulator.h
 * @brief Generic time-varying value generator for frequency and brightness.
 *
 * The modulator component supports three control modes: static, linear, and
 * LFO. It is used by `led_engine` to produce the current frequency and
 * brightness values for each oscillator.
 */

#pragma once

#include <stdint.h>

#include "esp_err.h"

/** @brief Modulator operating modes. */
typedef enum {
  MODULATOR_MODE_STATIC,
  MODULATOR_MODE_LINEAR,
  MODULATOR_MODE_LFO,
} modulator_mode_t;

/** @brief LFO waveforms supported by the modulator. */
typedef enum {
  MODULATOR_LFO_WAVEFORM_SINE,
  MODULATOR_LFO_WAVEFORM_SQUARE,
} modulator_lfo_waveform_t;

/** @brief Static mode configuration: a constant value. */
typedef struct {
  float value;
} modulator_static_config_t;

/** @brief Linear mode configuration: ramp from start to end over a duration. */
typedef struct {
  float start_value;
  float end_value;
  uint32_t duration_ms;
} modulator_linear_config_t;

/** @brief LFO mode configuration. */
typedef struct {
  modulator_lfo_waveform_t waveform;
  float frequency_hz;
  float low;
  float high;
} modulator_lfo_config_t;

/** @brief Complete modulator configuration. */
typedef struct {
  modulator_mode_t mode;
  modulator_static_config_t static_config;
  modulator_linear_config_t linear_config;
  modulator_lfo_config_t lfo_config;
} modulator_config_t;

/** @brief Modulator runtime state. */
typedef struct {
  modulator_config_t config;
  float current_value;
  float start_value;
  uint32_t elapsed_ms;
  float lfo_phase;
} modulator_state_t;

/**
 * @brief Initialize a modulator to static zero.
 *
 * @param[in,out] state Modulator state to initialize.
 *
 * @return ESP_OK on success, or ESP_ERR_INVALID_ARG if state is NULL.
 */
esp_err_t modulator_init(modulator_state_t *state);

/**
 * @brief Apply a new modulator configuration.
 *
 * The modulator captures the current value as the start point for linear ramps.
 *
 * @param[in,out] state Modulator state to configure.
 * @param[in] config New configuration to apply.
 *
 * @return ESP_OK on success, or an error if the configuration is invalid.
 */
esp_err_t modulator_set_config(modulator_state_t *state,
                               const modulator_config_t *config);

/**
 * @brief Compute the next value after delta_time_ms.
 *
 * For static mode the configured value is returned. For linear mode the value
 * advances toward the end value and switches to static once the duration
 * expires. For LFO mode the value is sampled from a sine or square low
 * frequency oscillator.
 *
 * @param[in,out] state Modulator state to evaluate.
 * @param[in] delta_time_ms Time elapsed since the last evaluation, in
 * milliseconds. Must be non-negative and finite.
 * @param[out] value Computed modulator value.
 *
 * @return ESP_OK on success, or an error if arguments are invalid.
 */
esp_err_t modulator_evaluate(modulator_state_t *state, float delta_time_ms,
                             float *value);
