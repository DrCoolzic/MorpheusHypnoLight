/**
 * @file oscillator.c
 * @brief LUT and DDS implementation for HypnoLight waveform generation.
 */
#include "oscillator.h"

#include <math.h>
#include <stdbool.h>

#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

/** @brief Pi used for the sine-wave LUT calculation. */
#define OSCILLATOR_PI 3.14159265358979323846f

/** @brief One complete normalized waveform cycle. */
#define OSCILLATOR_CYCLE 1.0f

/** @brief Persistent state for one independent DDS oscillator. */
typedef struct {
  float lut[OSCILLATOR_LUT_SIZE];
  float phase;
  float frequency_hz;
} oscillator_state_t;

/** @brief State indexed by fixed oscillator ID. */
static oscillator_state_t oscillator_states[OSCILLATOR_COUNT];

/** @brief Serialized access to LUT, phase, and frequency state. */
static portMUX_TYPE oscillator_lock = portMUX_INITIALIZER_UNLOCKED;

/**
 * @brief Clamp a finite waveform sample to the normalized range.
 *
 * @param[in] value Value to clamp.
 *
 * @return value constrained to [0.0, 1.0].
 */
static float clamp_unit(float value) {
  if (value <= 0.0f) {
    return 0.0f;
  }
  if (value >= 1.0f) {
    return 1.0f;
  }
  return value;
}

/**
 * @brief Normalize a phase expressed in degrees to an internal LUT position.
 *
 * @param[in] phase_degrees Phase in degrees.
 *
 * @return Equivalent LUT position in the range [0, OSCILLATOR_LUT_SIZE).
 */
static float phase_degrees_to_lut_position(float phase_degrees) {
  float normalized_degrees = fmodf(phase_degrees, 360.0f);
  if (normalized_degrees < 0.0f) {
    normalized_degrees += 360.0f;
  }
  return normalized_degrees * OSCILLATOR_LUT_SIZE / 360.0f;
}

/**
 * @brief Generate one normalized triangle or sawtooth waveform sample.
 *
 * @param[in] cycle_position Position in the waveform cycle in [0.0, 1.0).
 * @param[in] duty_cycle Fraction of the cycle spent rising.
 *
 * @return Normalized waveform value.
 */
static float triangle_sample(float cycle_position, float duty_cycle) {
  if (duty_cycle <= 0.0f) {
    return OSCILLATOR_CYCLE - cycle_position;
  }
  if (duty_cycle >= OSCILLATOR_CYCLE) {
    return cycle_position;
  }
  if (cycle_position < duty_cycle) {
    return cycle_position / duty_cycle;
  }
  return (OSCILLATOR_CYCLE - cycle_position) / (OSCILLATOR_CYCLE - duty_cycle);
}

/**
 * @brief Rebuild an oscillator LUT from its static configuration.
 *
 * @param[in,out] state Oscillator state whose LUT is replaced.
 * @param[in] config Validated static configuration.
 */
static void build_lut(oscillator_state_t *state,
                      const oscillator_static_config_t *config) {
  for (uint8_t sample = 0; sample < OSCILLATOR_LUT_SIZE; sample++) {
    const float cycle_position = (float)sample / OSCILLATOR_LUT_SIZE;
    float value = 0.0f;

    switch (config->waveform) {
    case OSCILLATOR_WAVEFORM_SINE:
      value = (sinf((2.0f * OSCILLATOR_PI * cycle_position) -
                    (OSCILLATOR_PI / 2.0f)) +
               1.0f) /
              2.0f;
      break;
    case OSCILLATOR_WAVEFORM_SQUARE:
      value = cycle_position < config->duty_cycle ? 1.0f : 0.0f;
      break;
    case OSCILLATOR_WAVEFORM_TRIANGLE:
      value = triangle_sample(cycle_position, config->duty_cycle);
      break;
    case OSCILLATOR_WAVEFORM_CUSTOM:
      value = config->custom_lut[sample];
      break;
    }

    state->lut[sample] = clamp_unit(value);
  }
}

/**
 * @brief Verify that a static waveform configuration can generate a LUT.
 *
 * @param[in] config Configuration to validate.
 *
 * @return true when valid; otherwise false.
 */
static bool static_config_is_valid(const oscillator_static_config_t *config) {
  if (config == NULL || !isfinite(config->duty_cycle) ||
      !isfinite(config->phase_degrees) || config->duty_cycle < 0.0f ||
      config->duty_cycle > OSCILLATOR_CYCLE ||
      config->waveform < OSCILLATOR_WAVEFORM_SINE ||
      config->waveform > OSCILLATOR_WAVEFORM_CUSTOM) {
    return false;
  }

  if (config->waveform != OSCILLATOR_WAVEFORM_CUSTOM) {
    return true;
  }

  if (config->custom_lut == NULL) {
    return false;
  }

  for (uint8_t sample = 0; sample < OSCILLATOR_LUT_SIZE; sample++) {
    if (!isfinite(config->custom_lut[sample])) {
      return false;
    }
  }

  return true;
}

/** @copydoc oscillator_init */
esp_err_t oscillator_init(void) {
  const oscillator_static_config_t default_config = {
      .waveform = OSCILLATOR_WAVEFORM_SINE,
      .duty_cycle = 0.5f,
      .phase_degrees = 0.0f,
      .custom_lut = NULL,
  };

  taskENTER_CRITICAL(&oscillator_lock);
  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    oscillator_state_t *state = &oscillator_states[oscillator_id];
    build_lut(state, &default_config);
    state->phase = 0.0f;
    state->frequency_hz = 0.0f;
  }
  taskEXIT_CRITICAL(&oscillator_lock);

  return ESP_OK;
}

/** @copydoc oscillator_set_static */
esp_err_t oscillator_set_static(uint8_t oscillator_id,
                                const oscillator_static_config_t *config) {
  if (oscillator_id >= OSCILLATOR_COUNT || !static_config_is_valid(config)) {
    return ESP_ERR_INVALID_ARG;
  }

  taskENTER_CRITICAL(&oscillator_lock);
  oscillator_state_t *state = &oscillator_states[oscillator_id];
  build_lut(state, config);
  state->phase = phase_degrees_to_lut_position(config->phase_degrees);
  taskEXIT_CRITICAL(&oscillator_lock);

  return ESP_OK;
}

/** @copydoc oscillator_set_frequency */
esp_err_t oscillator_set_frequency(uint8_t oscillator_id, float frequency_hz) {
  if (oscillator_id >= OSCILLATOR_COUNT || !isfinite(frequency_hz) ||
      frequency_hz < 0.0f || frequency_hz > OSCILLATOR_MAX_FREQUENCY_HZ) {
    return ESP_ERR_INVALID_ARG;
  }

  taskENTER_CRITICAL(&oscillator_lock);
  oscillator_states[oscillator_id].frequency_hz = frequency_hz;
  taskEXIT_CRITICAL(&oscillator_lock);

  return ESP_OK;
}

/** @copydoc oscillator_tick */
esp_err_t oscillator_tick(float osc_values[OSCILLATOR_COUNT]) {
  if (osc_values == NULL) {
    return ESP_ERR_INVALID_ARG;
  }

  taskENTER_CRITICAL(&oscillator_lock);
  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    oscillator_state_t *state = &oscillator_states[oscillator_id];
    if (state->frequency_hz == 0.0f) {
      osc_values[oscillator_id] = 1.0f;
      continue;
    }

    const uint8_t sample_index = (uint8_t)state->phase;
    osc_values[oscillator_id] = state->lut[sample_index];

    state->phase +=
        OSCILLATOR_LUT_SIZE * state->frequency_hz / OSCILLATOR_TICK_HZ;
    while (state->phase >= OSCILLATOR_LUT_SIZE) {
      state->phase -= OSCILLATOR_LUT_SIZE;
    }
  }
  taskEXIT_CRITICAL(&oscillator_lock);

  return ESP_OK;
}
