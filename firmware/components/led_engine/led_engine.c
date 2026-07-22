/**
 * @file led_engine.c
 * @brief Per-oscillator LED control chain implementation.
 */

#include "led_engine.h"

#include <math.h>
#include <stdint.h>

#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "led_control.h"
#include "modulator.h"
#include "oscillator.h"

/** @brief Serialized access to modulator configuration state. */
static portMUX_TYPE led_engine_lock = portMUX_INITIALIZER_UNLOCKED;

/** @brief Runtime state for one oscillator channel. */
typedef struct {
  modulator_state_t frequency_modulator;
  modulator_state_t brightness_modulator;
  float current_frequency;
  float current_brightness;
} led_engine_oscillator_t;

/** @brief State indexed by fixed oscillator ID. */
static led_engine_oscillator_t led_engine_oscillators[OSCILLATOR_COUNT];

/**
 * @brief Clamp a frequency to the range accepted by the oscillator.
 *
 * @param[in] frequency_hz Frequency value to clamp.
 *
 * @return Clamped frequency.
 */
static float clamp_frequency(float frequency_hz) {
  if (frequency_hz < 0.0f) {
    return 0.0f;
  }
  if (frequency_hz > OSCILLATOR_MAX_FREQUENCY_HZ) {
    return OSCILLATOR_MAX_FREQUENCY_HZ;
  }
  return frequency_hz;
}

/**
 * @brief Clamp a brightness value to the normalized range.
 *
 * @param[in] brightness Brightness value to clamp.
 *
 * @return Clamped brightness.
 */
static float clamp_brightness(float brightness) {
  if (brightness < 0.0f) {
    return 0.0f;
  }
  if (brightness > 1.0f) {
    return 1.0f;
  }
  return brightness;
}

esp_err_t led_engine_init(void) {
  const esp_err_t error = oscillator_init();
  if (error != ESP_OK) {
    return error;
  }

  taskENTER_CRITICAL(&led_engine_lock);
  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    led_engine_oscillator_t *osc = &led_engine_oscillators[oscillator_id];
    modulator_init(&osc->frequency_modulator);
    modulator_init(&osc->brightness_modulator);
    osc->current_frequency = 0.0f;
    osc->current_brightness = 0.0f;
  }
  taskEXIT_CRITICAL(&led_engine_lock);

  return ESP_OK;
}

esp_err_t led_engine_set_static(uint8_t oscillator_id,
                                const oscillator_static_config_t *config) {
  return oscillator_set_static(oscillator_id, config);
}

esp_err_t led_engine_set_frequency(uint8_t oscillator_id, float frequency_hz) {
  if (oscillator_id >= OSCILLATOR_COUNT || !isfinite(frequency_hz) ||
      frequency_hz < 0.0f || frequency_hz > OSCILLATOR_MAX_FREQUENCY_HZ) {
    return ESP_ERR_INVALID_ARG;
  }

  const modulator_config_t config = {
      .mode = MODULATOR_MODE_STATIC,
      .static_config = {.value = frequency_hz},
  };

  return led_engine_set_frequency_modulator(oscillator_id, &config);
}

esp_err_t led_engine_set_brightness(uint8_t oscillator_id, float brightness) {
  if (oscillator_id >= OSCILLATOR_COUNT || !isfinite(brightness) ||
      brightness < 0.0f || brightness > 1.0f) {
    return ESP_ERR_INVALID_ARG;
  }

  const modulator_config_t config = {
      .mode = MODULATOR_MODE_STATIC,
      .static_config = {.value = brightness},
  };

  return led_engine_set_brightness_modulator(oscillator_id, &config);
}

esp_err_t led_engine_linear_frequency(uint8_t oscillator_id, float end_value,
                                      uint32_t duration_ms) {
  if (oscillator_id >= OSCILLATOR_COUNT || !isfinite(end_value) ||
      end_value < 0.0f || end_value > OSCILLATOR_MAX_FREQUENCY_HZ ||
      duration_ms == 0U) {
    return ESP_ERR_INVALID_ARG;
  }

  float start_value = 0.0f;

  taskENTER_CRITICAL(&led_engine_lock);
  start_value = led_engine_oscillators[oscillator_id].current_frequency;
  taskEXIT_CRITICAL(&led_engine_lock);

  const modulator_config_t config = {
      .mode = MODULATOR_MODE_LINEAR,
      .linear_config =
          {
              .start_value = start_value,
              .end_value = end_value,
              .duration_ms = duration_ms,
          },
  };

  return led_engine_set_frequency_modulator(oscillator_id, &config);
}

esp_err_t led_engine_linear_brightness(uint8_t oscillator_id, float end_value,
                                       uint32_t duration_ms) {
  if (oscillator_id >= OSCILLATOR_COUNT || !isfinite(end_value) ||
      end_value < 0.0f || end_value > 1.0f || duration_ms == 0U) {
    return ESP_ERR_INVALID_ARG;
  }

  float start_value = 0.0f;

  taskENTER_CRITICAL(&led_engine_lock);
  start_value = led_engine_oscillators[oscillator_id].current_brightness;
  taskEXIT_CRITICAL(&led_engine_lock);

  const modulator_config_t config = {
      .mode = MODULATOR_MODE_LINEAR,
      .linear_config =
          {
              .start_value = start_value,
              .end_value = end_value,
              .duration_ms = duration_ms,
          },
  };

  return led_engine_set_brightness_modulator(oscillator_id, &config);
}

esp_err_t led_engine_set_frequency_modulator(uint8_t oscillator_id,
                                             const modulator_config_t *config) {
  if (oscillator_id >= OSCILLATOR_COUNT || config == NULL) {
    return ESP_ERR_INVALID_ARG;
  }

  modulator_state_t temp_state;
  const esp_err_t error = modulator_init(&temp_state);
  if (error != ESP_OK) {
    return error;
  }

  if (modulator_set_config(&temp_state, config) != ESP_OK) {
    return ESP_ERR_INVALID_ARG;
  }

  taskENTER_CRITICAL(&led_engine_lock);
  led_engine_oscillator_t *osc = &led_engine_oscillators[oscillator_id];
  osc->frequency_modulator = temp_state;
  if (config->mode == MODULATOR_MODE_STATIC) {
    osc->current_frequency = config->static_config.value;
  } else if (config->mode == MODULATOR_MODE_LINEAR) {
    osc->current_frequency = config->linear_config.start_value;
  }
  taskEXIT_CRITICAL(&led_engine_lock);

  return ESP_OK;
}

esp_err_t
led_engine_set_brightness_modulator(uint8_t oscillator_id,
                                    const modulator_config_t *config) {
  if (oscillator_id >= OSCILLATOR_COUNT || config == NULL) {
    return ESP_ERR_INVALID_ARG;
  }

  modulator_state_t temp_state;
  const esp_err_t error = modulator_init(&temp_state);
  if (error != ESP_OK) {
    return error;
  }

  if (modulator_set_config(&temp_state, config) != ESP_OK) {
    return ESP_ERR_INVALID_ARG;
  }

  taskENTER_CRITICAL(&led_engine_lock);
  led_engine_oscillator_t *osc = &led_engine_oscillators[oscillator_id];
  osc->brightness_modulator = temp_state;
  if (config->mode == MODULATOR_MODE_STATIC) {
    osc->current_brightness = config->static_config.value;
  } else if (config->mode == MODULATOR_MODE_LINEAR) {
    osc->current_brightness = config->linear_config.start_value;
  }
  taskEXIT_CRITICAL(&led_engine_lock);

  return ESP_OK;
}

esp_err_t led_engine_tick(void) {
  float frequencies[OSCILLATOR_COUNT];
  float brightnesses[OSCILLATOR_COUNT];

  taskENTER_CRITICAL(&led_engine_lock);
  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    led_engine_oscillator_t *osc = &led_engine_oscillators[oscillator_id];
    modulator_evaluate(&osc->frequency_modulator, 1.0f,
                       &frequencies[oscillator_id]);
    modulator_evaluate(&osc->brightness_modulator, 1.0f,
                       &brightnesses[oscillator_id]);

    frequencies[oscillator_id] = clamp_frequency(frequencies[oscillator_id]);
    brightnesses[oscillator_id] = clamp_brightness(brightnesses[oscillator_id]);

    osc->current_frequency = frequencies[oscillator_id];
    osc->current_brightness = brightnesses[oscillator_id];
  }
  taskEXIT_CRITICAL(&led_engine_lock);

  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    const esp_err_t error =
        oscillator_set_frequency(oscillator_id, frequencies[oscillator_id]);
    if (error != ESP_OK) {
      return error;
    }
  }

  float osc_values[OSCILLATOR_COUNT];
  const esp_err_t error = oscillator_tick(osc_values);
  if (error != ESP_OK) {
    return error;
  }

  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    const esp_err_t update_error = led_control_update(
        oscillator_id, osc_values[oscillator_id], brightnesses[oscillator_id]);
    if (update_error != ESP_OK) {
      return update_error;
    }
  }

  return ESP_OK;
}

esp_err_t led_engine_all_off(void) { return led_control_all_off(); }
