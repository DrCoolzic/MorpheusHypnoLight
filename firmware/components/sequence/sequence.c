/**
 * @file sequence.c
 * @brief Step-based playback engine for HypnoLight sequences.
 */
#include "sequence.h"

#include <math.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>

#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

#include "led_engine.h"
#include "modulator.h"

/** @brief Serialized access to sequence playback state. */
static portMUX_TYPE sequence_lock = portMUX_INITIALIZER_UNLOCKED;

/** @brief Loaded sequence steps. */
static sequence_step_t sequence_steps[SEQUENCE_MAX_STEPS];

/** @brief Number of valid steps currently loaded. */
static uint32_t sequence_step_count = 0U;

/** @brief Index of the step currently being played. */
static uint32_t sequence_current_step = 0U;

/** @brief Elapsed time inside the current step, in milliseconds. */
static uint32_t sequence_elapsed_ms = 0U;

/** @brief Whether playback is currently active. */
static bool sequence_playing = false;

/**
 * @brief Validate a static oscillator configuration without modifying hardware.
 *
 * @param[in] config Static configuration to validate.
 *
 * @return true when the configuration can be applied to the oscillator.
 */
static bool static_config_is_valid(const oscillator_static_config_t *config) {
  if (config == NULL || !isfinite(config->phase_degrees) ||
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

  for (uint8_t i = 0; i < OSCILLATOR_LUT_SIZE; i++) {
    if (!isfinite(config->custom_lut[i])) {
      return false;
    }
  }

  return true;
}

/**
 * @brief Validate one per-oscillator step entry.
 *
 * The modulator configurations are checked by attempting to apply them to a
 * scratch modulator state.
 *
 * @param[in] oscillator_step Per-oscillator step data.
 *
 * @return true when all fields are valid.
 */
static bool
oscillator_step_is_valid(const sequence_oscillator_step_t *oscillator_step) {
  if (oscillator_step == NULL) {
    return false;
  }

  if (!static_config_is_valid(&oscillator_step->static_config)) {
    return false;
  }

  modulator_state_t scratch;
  if (modulator_init(&scratch) != ESP_OK) {
    return false;
  }

  if (modulator_set_config(&scratch, &oscillator_step->frequency_modulator) !=
          ESP_OK ||
      modulator_set_config(&scratch, &oscillator_step->brightness_modulator) !=
          ESP_OK ||
      modulator_set_config(&scratch, &oscillator_step->duty_modulator) !=
          ESP_OK) {
    return false;
  }

  return true;
}

/**
 * @brief Validate a complete sequence step.
 *
 * @param[in] step Step to validate.
 *
 * @return true when the step can be loaded.
 */
static bool step_is_valid(const sequence_step_t *step) {
  if (step == NULL || step->duration_ms == 0U) {
    return false;
  }

  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    if (!oscillator_step_is_valid(&step->oscillators[oscillator_id])) {
      return false;
    }
  }

  return true;
}

/**
 * @brief Apply one step's configuration to the led_engine.
 *
 * This function must be called outside the sequence critical section because it
 * acquires its own locks inside led_engine and oscillator.
 *
 * @param[in] step_index Zero-based index of the step to apply.
 *
 * @return ESP_OK on success, or an error propagated from led_engine.
 */
static esp_err_t apply_step(uint32_t step_index) {
  if (step_index >= sequence_step_count) {
    return ESP_ERR_INVALID_ARG;
  }

  const sequence_step_t *step = &sequence_steps[step_index];

  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    const sequence_oscillator_step_t *oscillator_step =
        &step->oscillators[oscillator_id];

    esp_err_t error =
        led_engine_set_static(oscillator_id, &oscillator_step->static_config);
    if (error != ESP_OK) {
      return error;
    }

    error = led_engine_set_frequency_modulator(
        oscillator_id, &oscillator_step->frequency_modulator);
    if (error != ESP_OK) {
      return error;
    }

    error = led_engine_set_brightness_modulator(
        oscillator_id, &oscillator_step->brightness_modulator);
    if (error != ESP_OK) {
      return error;
    }

    error = led_engine_set_duty_cycle_modulator(
        oscillator_id, &oscillator_step->duty_modulator);
    if (error != ESP_OK) {
      return error;
    }
  }

  return ESP_OK;
}

esp_err_t sequence_init(void) {
  taskENTER_CRITICAL(&sequence_lock);
  memset(sequence_steps, 0, sizeof(sequence_steps));
  sequence_step_count = 0U;
  sequence_current_step = 0U;
  sequence_elapsed_ms = 0U;
  sequence_playing = false;
  taskEXIT_CRITICAL(&sequence_lock);

  return ESP_OK;
}

esp_err_t sequence_load(const sequence_step_t *steps, uint32_t step_count) {
  if (step_count > SEQUENCE_MAX_STEPS || (steps == NULL && step_count > 0U)) {
    return ESP_ERR_INVALID_ARG;
  }

  if (steps != NULL) {
    for (uint32_t i = 0; i < step_count; i++) {
      if (!step_is_valid(&steps[i])) {
        return ESP_ERR_INVALID_ARG;
      }
    }
  }

  taskENTER_CRITICAL(&sequence_lock);
  if (steps != NULL) {
    memcpy(sequence_steps, steps, step_count * sizeof(sequence_step_t));
  }
  sequence_step_count = step_count;
  sequence_current_step = 0U;
  sequence_elapsed_ms = 0U;
  sequence_playing = false;
  taskEXIT_CRITICAL(&sequence_lock);

  return ESP_OK;
}

esp_err_t sequence_play(void) {
  taskENTER_CRITICAL(&sequence_lock);
  if (!sequence_playing && sequence_step_count > 0U) {
    sequence_playing = true;
  }
  taskEXIT_CRITICAL(&sequence_lock);

  return ESP_OK;
}

esp_err_t sequence_pause(void) {
  taskENTER_CRITICAL(&sequence_lock);
  sequence_playing = false;
  taskEXIT_CRITICAL(&sequence_lock);

  return ESP_OK;
}

esp_err_t sequence_seek(uint32_t step_index) {
  taskENTER_CRITICAL(&sequence_lock);
  if (step_index >= sequence_step_count) {
    taskEXIT_CRITICAL(&sequence_lock);
    return ESP_ERR_INVALID_ARG;
  }

  sequence_current_step = step_index;
  sequence_elapsed_ms = 0U;
  const uint32_t target_step = step_index;
  taskEXIT_CRITICAL(&sequence_lock);

  return apply_step(target_step);
}

esp_err_t sequence_tick(void) {
  uint32_t step_to_apply = 0U;
  bool apply = false;

  taskENTER_CRITICAL(&sequence_lock);
  if (!sequence_playing || sequence_step_count == 0U) {
    taskEXIT_CRITICAL(&sequence_lock);
    return ESP_OK;
  }

  sequence_elapsed_ms += SEQUENCE_STEP_TICK_PERIOD_MS;

  const sequence_step_t *current = &sequence_steps[sequence_current_step];
  if (sequence_elapsed_ms >= current->duration_ms) {
    if (sequence_current_step + 1U < sequence_step_count) {
      sequence_current_step++;
      sequence_elapsed_ms = 0U;
      step_to_apply = sequence_current_step;
      apply = true;
    } else {
      sequence_playing = false;
      sequence_elapsed_ms = 0U;
    }
  }
  taskEXIT_CRITICAL(&sequence_lock);

  if (apply) {
    return apply_step(step_to_apply);
  }

  return ESP_OK;
}

bool sequence_is_playing(void) {
  taskENTER_CRITICAL(&sequence_lock);
  const bool playing = sequence_playing;
  taskEXIT_CRITICAL(&sequence_lock);

  return playing;
}

uint32_t sequence_get_current_step(void) {
  taskENTER_CRITICAL(&sequence_lock);
  const uint32_t current = sequence_current_step;
  taskEXIT_CRITICAL(&sequence_lock);

  return current;
}
