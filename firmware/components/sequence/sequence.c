/**
 * @file sequence.c
 * @brief Realtime parameter implementation for the HypnoLight sequence engine.
 */
#include "sequence.h"

#include <math.h>
#include <stdbool.h>

#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

/** @brief Serialized access to realtime parameter state. */
static portMUX_TYPE sequence_lock = portMUX_INITIALIZER_UNLOCKED;

/** @brief Current mode until sequence playback is implemented. */
static sequence_mode_t sequence_mode = SEQUENCE_MODE_REALTIME;

/** @brief Realtime state indexed by fixed oscillator ID. */
static sequence_realtime_oscillator_t realtime_oscillators[OSCILLATOR_COUNT];

/** @brief Evaluation state for one linear realtime parameter control. */
typedef struct {
  float start_value;
  float target_value;
  uint32_t elapsed_ticks;
  uint32_t total_ticks;
} sequence_linear_control_t;

/** @brief Linear frequency controls indexed by fixed oscillator ID. */
static sequence_linear_control_t frequency_controls[OSCILLATOR_COUNT];

/** @brief Linear brightness controls indexed by fixed oscillator ID. */
static sequence_linear_control_t brightness_controls[OSCILLATOR_COUNT];

/**
 * @brief Check whether a value is a normalized finite brightness factor.
 *
 * @param[in] brightness Value to validate.
 *
 * @return true when brightness is in the inclusive range [0.0, 1.0].
 */
static bool brightness_is_valid(float brightness) {
  return isfinite(brightness) && brightness >= 0.0f && brightness <= 1.0f;
}

/**
 * @brief Check whether a duration is representable by the fixed tick period.
 *
 * @param[in] duration_ms Requested ramp duration in milliseconds.
 *
 * @return true when duration_ms produces at least one whole evaluation tick.
 */
static bool duration_is_valid(uint32_t duration_ms) {
  return duration_ms > 0U && duration_ms % SEQUENCE_TICK_PERIOD_MS == 0U;
}

/**
 * @brief Configure linear evaluation state for a parameter.
 *
 * @param[out] control Control state to initialize.
 * @param[in] start_value Value active at ramp start.
 * @param[in] target_value Value to reach.
 * @param[in] duration_ms Ramp duration in milliseconds.
 */
static void configure_linear_control(sequence_linear_control_t *control,
                                     float start_value, float target_value,
                                     uint32_t duration_ms) {
  control->start_value = start_value;
  control->target_value = target_value;
  control->elapsed_ticks = 0U;
  control->total_ticks = duration_ms / SEQUENCE_TICK_PERIOD_MS;
}

/** @copydoc sequence_init */
esp_err_t sequence_init(void) {
  const oscillator_static_config_t default_static_config = {
      .waveform = OSCILLATOR_WAVEFORM_SINE,
      .duty_cycle = 0.5f,
      .phase_degrees = 0.0f,
      .custom_lut = NULL,
  };

  esp_err_t error = oscillator_init();
  if (error != ESP_OK) {
    return error;
  }

  taskENTER_CRITICAL(&sequence_lock);
  sequence_mode = SEQUENCE_MODE_REALTIME;
  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    realtime_oscillators[oscillator_id].static_config = default_static_config;
    realtime_oscillators[oscillator_id].frequency_hz = 0.0f;
    realtime_oscillators[oscillator_id].brightness = 0.0f;
    realtime_oscillators[oscillator_id].frequency_mode =
        SEQUENCE_PARAMETER_CONTROL_CONSTANT;
    realtime_oscillators[oscillator_id].brightness_mode =
        SEQUENCE_PARAMETER_CONTROL_CONSTANT;
    frequency_controls[oscillator_id] = (sequence_linear_control_t){0};
    brightness_controls[oscillator_id] = (sequence_linear_control_t){0};
  }
  taskEXIT_CRITICAL(&sequence_lock);

  return ESP_OK;
}

/** @copydoc sequence_get_mode */
sequence_mode_t sequence_get_mode(void) {
  taskENTER_CRITICAL(&sequence_lock);
  const sequence_mode_t mode = sequence_mode;
  taskEXIT_CRITICAL(&sequence_lock);

  return mode;
}

/** @copydoc sequence_realtime_set_static */
esp_err_t
sequence_realtime_set_static(uint8_t oscillator_id,
                             const oscillator_static_config_t *config) {
  if (oscillator_id >= OSCILLATOR_COUNT || config == NULL) {
    return ESP_ERR_INVALID_ARG;
  }

  const esp_err_t error = oscillator_set_static(oscillator_id, config);
  if (error != ESP_OK) {
    return error;
  }

  taskENTER_CRITICAL(&sequence_lock);
  realtime_oscillators[oscillator_id].static_config = *config;
  taskEXIT_CRITICAL(&sequence_lock);

  return ESP_OK;
}

/** @copydoc sequence_realtime_set_frequency */
esp_err_t sequence_realtime_set_frequency(uint8_t oscillator_id,
                                          float frequency_hz) {
  if (oscillator_id >= OSCILLATOR_COUNT) {
    return ESP_ERR_INVALID_ARG;
  }

  const esp_err_t error = oscillator_set_frequency(oscillator_id, frequency_hz);
  if (error != ESP_OK) {
    return error;
  }

  taskENTER_CRITICAL(&sequence_lock);
  realtime_oscillators[oscillator_id].frequency_hz = frequency_hz;
  realtime_oscillators[oscillator_id].frequency_mode =
      SEQUENCE_PARAMETER_CONTROL_CONSTANT;
  frequency_controls[oscillator_id] = (sequence_linear_control_t){0};
  taskEXIT_CRITICAL(&sequence_lock);

  return ESP_OK;
}

/** @copydoc sequence_realtime_set_brightness */
esp_err_t sequence_realtime_set_brightness(uint8_t oscillator_id,
                                           float brightness) {
  if (oscillator_id >= OSCILLATOR_COUNT || !brightness_is_valid(brightness)) {
    return ESP_ERR_INVALID_ARG;
  }

  taskENTER_CRITICAL(&sequence_lock);
  realtime_oscillators[oscillator_id].brightness = brightness;
  realtime_oscillators[oscillator_id].brightness_mode =
      SEQUENCE_PARAMETER_CONTROL_CONSTANT;
  brightness_controls[oscillator_id] = (sequence_linear_control_t){0};
  taskEXIT_CRITICAL(&sequence_lock);

  return ESP_OK;
}

/** @copydoc sequence_realtime_linear_frequency */
esp_err_t sequence_realtime_linear_frequency(uint8_t oscillator_id,
                                             float target_frequency_hz,
                                             uint32_t duration_ms) {
  if (oscillator_id >= OSCILLATOR_COUNT || !isfinite(target_frequency_hz) ||
      target_frequency_hz < 0.0f ||
      target_frequency_hz > OSCILLATOR_MAX_FREQUENCY_HZ ||
      !duration_is_valid(duration_ms)) {
    return ESP_ERR_INVALID_ARG;
  }

  taskENTER_CRITICAL(&sequence_lock);
  configure_linear_control(&frequency_controls[oscillator_id],
                           realtime_oscillators[oscillator_id].frequency_hz,
                           target_frequency_hz, duration_ms);
  realtime_oscillators[oscillator_id].frequency_mode =
      SEQUENCE_PARAMETER_CONTROL_LINEAR;
  taskEXIT_CRITICAL(&sequence_lock);

  return ESP_OK;
}

/** @copydoc sequence_realtime_linear_brightness */
esp_err_t sequence_realtime_linear_brightness(uint8_t oscillator_id,
                                              float target_brightness,
                                              uint32_t duration_ms) {
  if (oscillator_id >= OSCILLATOR_COUNT ||
      !brightness_is_valid(target_brightness) ||
      !duration_is_valid(duration_ms)) {
    return ESP_ERR_INVALID_ARG;
  }

  taskENTER_CRITICAL(&sequence_lock);
  configure_linear_control(&brightness_controls[oscillator_id],
                           realtime_oscillators[oscillator_id].brightness,
                           target_brightness, duration_ms);
  realtime_oscillators[oscillator_id].brightness_mode =
      SEQUENCE_PARAMETER_CONTROL_LINEAR;
  taskEXIT_CRITICAL(&sequence_lock);

  return ESP_OK;
}

/** @copydoc sequence_tick */
esp_err_t sequence_tick(void) {
  float frequency_updates[OSCILLATOR_COUNT];
  bool frequency_changed[OSCILLATOR_COUNT] = {false};

  taskENTER_CRITICAL(&sequence_lock);
  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    if (realtime_oscillators[oscillator_id].frequency_mode ==
        SEQUENCE_PARAMETER_CONTROL_LINEAR) {
      sequence_linear_control_t *control = &frequency_controls[oscillator_id];
      control->elapsed_ticks++;
      const float progress =
          (float)control->elapsed_ticks / (float)control->total_ticks;
      const float frequency =
          control->elapsed_ticks >= control->total_ticks
              ? control->target_value
              : control->start_value +
                    (control->target_value - control->start_value) * progress;
      realtime_oscillators[oscillator_id].frequency_hz = frequency;
      frequency_updates[oscillator_id] = frequency;
      frequency_changed[oscillator_id] = true;
      if (control->elapsed_ticks >= control->total_ticks) {
        realtime_oscillators[oscillator_id].frequency_mode =
            SEQUENCE_PARAMETER_CONTROL_CONSTANT;
      }
    }

    if (realtime_oscillators[oscillator_id].brightness_mode ==
        SEQUENCE_PARAMETER_CONTROL_LINEAR) {
      sequence_linear_control_t *control = &brightness_controls[oscillator_id];
      control->elapsed_ticks++;
      const float progress =
          (float)control->elapsed_ticks / (float)control->total_ticks;
      realtime_oscillators[oscillator_id].brightness =
          control->elapsed_ticks >= control->total_ticks
              ? control->target_value
              : control->start_value +
                    (control->target_value - control->start_value) * progress;
      if (control->elapsed_ticks >= control->total_ticks) {
        realtime_oscillators[oscillator_id].brightness_mode =
            SEQUENCE_PARAMETER_CONTROL_CONSTANT;
      }
    }
  }
  taskEXIT_CRITICAL(&sequence_lock);

  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    if (frequency_changed[oscillator_id]) {
      const esp_err_t error = oscillator_set_frequency(
          oscillator_id, frequency_updates[oscillator_id]);
      if (error != ESP_OK) {
        return error;
      }
    }
  }

  return ESP_OK;
}

/** @copydoc sequence_get_realtime_brightness */
esp_err_t
sequence_get_realtime_brightness(float brightnesses[OSCILLATOR_COUNT]) {
  if (brightnesses == NULL) {
    return ESP_ERR_INVALID_ARG;
  }

  taskENTER_CRITICAL(&sequence_lock);
  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    brightnesses[oscillator_id] =
        realtime_oscillators[oscillator_id].brightness;
  }
  taskEXIT_CRITICAL(&sequence_lock);

  return ESP_OK;
}
