/**
 * @file sequence.c
 * @brief Step-based playback engine for HypnoLight sequences.
 */
#include "sequence.h"

#include <math.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>

#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

#include "led_engine.h"
#include "modulator.h"

/** @brief Tag used for component log messages. */
static const char *TAG = "sequence";

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

/** @brief Whether playback is currently paused (step state is preserved). */
static bool sequence_paused = false;

/** @brief Whether the loaded sequence is a single zero-duration realtime step.
 */
static bool sequence_realtime = false;

/** @brief Internal periodic timer that drives playback. */
static esp_timer_handle_t sequence_timer = NULL;

static void sequence_tick(void);

/**
 * @brief Timer callback that advances the sequence timeline.
 */
static void IRAM_ATTR sequence_timer_callback(void *arg) {
  (void)arg;
  sequence_tick();
}

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
  if (step == NULL) {
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

/**
 * @brief Seek the loaded modulators to an offset inside the current step.
 *
 * @param[in] step_index Step whose modulators must be seeked.
 * @param[in] offset_ms  Offset inside the step, in milliseconds.
 *
 * @return ESP_OK on success, or an error propagated from led_engine.
 */
static esp_err_t apply_step_offset(uint32_t step_index, uint32_t offset_ms) {
  if (step_index >= sequence_step_count) {
    return ESP_ERR_INVALID_ARG;
  }

  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    const esp_err_t error =
        led_engine_seek_modulators(oscillator_id, offset_ms);
    if (error != ESP_OK) {
      return error;
    }
  }

  return ESP_OK;
}

/**
 * @brief Locate the step and offset matching an absolute position.
 *
 * @param[in]  position_ms Absolute position in the loaded sequence.
 * @param[out] step_index  Zero-based step index containing the position.
 * @param[out] offset_ms   Offset inside that step.
 *
 * @return true when the position is inside the sequence duration.
 */
static bool find_step_for_position(uint32_t position_ms, uint32_t *step_index,
                                   uint32_t *offset_ms) {
  if (step_index == NULL || offset_ms == NULL || sequence_step_count == 0U) {
    return false;
  }

  uint32_t accumulated_ms = 0U;
  for (uint32_t i = 0; i < sequence_step_count; i++) {
    const uint32_t step_duration = sequence_steps[i].duration_ms;
    if (position_ms >= accumulated_ms &&
        position_ms < accumulated_ms + step_duration) {
      *step_index = i;
      *offset_ms = position_ms - accumulated_ms;
      return true;
    }
    accumulated_ms += step_duration;
  }

  return false;
}

esp_err_t sequence_init(void) {
  if (sequence_timer != NULL) {
    esp_timer_stop(sequence_timer);
  }

  taskENTER_CRITICAL(&sequence_lock);
  memset(sequence_steps, 0, sizeof(sequence_steps));
  sequence_step_count = 0U;
  sequence_current_step = 0U;
  sequence_elapsed_ms = 0U;
  sequence_playing = false;
  sequence_paused = false;
  sequence_realtime = false;
  taskEXIT_CRITICAL(&sequence_lock);

  if (sequence_timer == NULL) {
    const esp_timer_create_args_t timer_args = {
        .callback = &sequence_timer_callback,
        .name = "sequence_tick",
    };
    return esp_timer_create(&timer_args, &sequence_timer);
  }

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

  const bool realtime =
      (step_count == 1U && steps != NULL && steps[0].duration_ms == 0U);

  if (!realtime) {
    for (uint32_t i = 0; i < step_count; i++) {
      if (steps[i].duration_ms == 0U) {
        return ESP_ERR_INVALID_ARG;
      }
    }
  }

  if (sequence_timer != NULL) {
    esp_timer_stop(sequence_timer);
  }

  taskENTER_CRITICAL(&sequence_lock);
  if (steps != NULL) {
    memcpy(sequence_steps, steps, step_count * sizeof(sequence_step_t));
  }
  sequence_step_count = step_count;
  sequence_current_step = 0U;
  sequence_elapsed_ms = 0U;
  sequence_playing = false;
  sequence_paused = false;
  sequence_realtime = realtime;
  taskEXIT_CRITICAL(&sequence_lock);

  /* Loading does not start playback. The first step will be applied when
   * sequence_play() is called. */
  return ESP_OK;
}

esp_err_t sequence_load_compact(const uint8_t *data, size_t data_length) {
  uint32_t step_count = 0U;
  esp_err_t error =
      sequence_decode_compact(data, data_length, NULL, 0U, &step_count);
  if (error != ESP_OK) {
    return error;
  }

  if (sequence_timer != NULL) {
    esp_timer_stop(sequence_timer);
  }

  taskENTER_CRITICAL(&sequence_lock);
  sequence_playing = false;
  sequence_paused = false;
  taskEXIT_CRITICAL(&sequence_lock);

  error = sequence_decode_compact(data, data_length, sequence_steps,
                                  SEQUENCE_MAX_STEPS, &step_count);
  if (error != ESP_OK) {
    return error;
  }

  const bool realtime =
      (step_count == 1U && sequence_steps[0].duration_ms == 0U);

  if (!realtime) {
    for (uint32_t i = 0; i < step_count; i++) {
      if (sequence_steps[i].duration_ms == 0U) {
        taskENTER_CRITICAL(&sequence_lock);
        sequence_step_count = 0U;
        taskEXIT_CRITICAL(&sequence_lock);
        return ESP_ERR_INVALID_ARG;
      }
    }
  }

  taskENTER_CRITICAL(&sequence_lock);
  sequence_step_count = step_count;
  sequence_current_step = 0U;
  sequence_elapsed_ms = 0U;
  sequence_realtime = realtime;
  taskEXIT_CRITICAL(&sequence_lock);

  /* Loading does not start playback. The first step will be applied when
   * sequence_play() is called. */
  return ESP_OK;
}

esp_err_t sequence_replace_step(uint32_t step_index,
                                const sequence_step_t *step) {
  if (step == NULL || !step_is_valid(step) ||
      step_index >= SEQUENCE_MAX_STEPS) {
    return ESP_ERR_INVALID_ARG;
  }

  taskENTER_CRITICAL(&sequence_lock);
  const bool was_playing = sequence_playing;
  const bool is_realtime = sequence_realtime;
  if (step_index >= sequence_step_count) {
    taskEXIT_CRITICAL(&sequence_lock);
    return ESP_ERR_INVALID_ARG;
  }

  if (!is_realtime && step->duration_ms == 0U) {
    taskEXIT_CRITICAL(&sequence_lock);
    return ESP_ERR_INVALID_ARG;
  }

  if (sequence_timer != NULL) {
    esp_timer_stop(sequence_timer);
  }

  sequence_playing = false;
  memcpy(&sequence_steps[step_index], step, sizeof(*step));

  if (is_realtime && sequence_step_count == 1U) {
    sequence_realtime = (step_index == 0U && step->duration_ms == 0U);
  }

  const bool is_current = (step_index == sequence_current_step);
  const bool stay_realtime = sequence_realtime;
  taskEXIT_CRITICAL(&sequence_lock);

  if (is_current) {
    const esp_err_t error = apply_step(step_index);
    if (error != ESP_OK) {
      return error;
    }
  }

  if (was_playing) {
    if (stay_realtime) {
      taskENTER_CRITICAL(&sequence_lock);
      sequence_playing = true;
      taskEXIT_CRITICAL(&sequence_lock);
    } else if (sequence_timer != NULL) {
      const esp_err_t error = esp_timer_start_periodic(
          sequence_timer, (uint64_t)SEQUENCE_STEP_TICK_PERIOD_MS * 1000ULL);
      if (error != ESP_OK) {
        return error;
      }
      taskENTER_CRITICAL(&sequence_lock);
      sequence_playing = true;
      taskEXIT_CRITICAL(&sequence_lock);
    }
  }

  return ESP_OK;
}

esp_err_t sequence_play(void) {
  taskENTER_CRITICAL(&sequence_lock);
  if (sequence_step_count == 0U || sequence_playing) {
    taskEXIT_CRITICAL(&sequence_lock);
    return ESP_OK;
  }
  const bool realtime = sequence_realtime;
  taskEXIT_CRITICAL(&sequence_lock);

  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    led_engine_resume_modulators(oscillator_id);
  }

  /* Re-apply the current step so that the outputs match the cursor position
   * even after a stop or after the sequence has reached its end. */
  taskENTER_CRITICAL(&sequence_lock);
  const uint32_t restart_step = sequence_current_step;
  const uint32_t restart_offset = sequence_elapsed_ms;
  taskEXIT_CRITICAL(&sequence_lock);

  esp_err_t error = apply_step(restart_step);
  if (error != ESP_OK) {
    return error;
  }
  error = apply_step_offset(restart_step, restart_offset);
  if (error != ESP_OK) {
    return error;
  }

  if (!realtime) {
    if (sequence_timer == NULL) {
      return ESP_ERR_INVALID_STATE;
    }

    (void)esp_timer_stop(sequence_timer);

    error = esp_timer_start_periodic(
        sequence_timer, (uint64_t)SEQUENCE_STEP_TICK_PERIOD_MS * 1000ULL);
    if (error != ESP_OK) {
      return error;
    }
  }

  taskENTER_CRITICAL(&sequence_lock);
  sequence_playing = true;
  sequence_paused = false;
  taskEXIT_CRITICAL(&sequence_lock);

  return ESP_OK;
}

esp_err_t sequence_pause(void) {
  taskENTER_CRITICAL(&sequence_lock);
  if (sequence_realtime) {
    taskEXIT_CRITICAL(&sequence_lock);
    return ESP_ERR_INVALID_STATE;
  }
  taskEXIT_CRITICAL(&sequence_lock);

  if (sequence_timer != NULL) {
    esp_timer_stop(sequence_timer);
  }

  taskENTER_CRITICAL(&sequence_lock);
  const bool was_playing = sequence_playing;
  sequence_playing = false;
  sequence_paused = was_playing;
  taskEXIT_CRITICAL(&sequence_lock);

  if (was_playing) {
    for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
         oscillator_id++) {
      led_engine_pause_modulators(oscillator_id);
    }
  }

  return ESP_OK;
}

esp_err_t sequence_stop(void) {
  if (sequence_timer != NULL) {
    esp_timer_stop(sequence_timer);
  }

  taskENTER_CRITICAL(&sequence_lock);
  sequence_playing = false;
  sequence_paused = false;
  sequence_realtime = false;
  sequence_current_step = 0U;
  sequence_elapsed_ms = 0U;
  taskEXIT_CRITICAL(&sequence_lock);

  // led_engine_all_off() only writes the hardware once; the independent
  // oscillator tick timer keeps re-evaluating each modulator and would
  // otherwise overwrite that write on its next cycle. Reset every
  // modulator to a static zero value so the outputs stay off.
  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    (void)led_engine_set_frequency(oscillator_id, 0.0f);
    (void)led_engine_set_brightness(oscillator_id, 0.0f);
  }

  led_engine_all_off();

  return ESP_OK;
}

esp_err_t sequence_seek(uint32_t position_ms) {
  (void)position_ms;

  taskENTER_CRITICAL(&sequence_lock);
  if (sequence_realtime) {
    taskEXIT_CRITICAL(&sequence_lock);
    return ESP_ERR_INVALID_STATE;
  }
  taskEXIT_CRITICAL(&sequence_lock);

  if (sequence_timer != NULL) {
    esp_timer_stop(sequence_timer);
  }

  taskENTER_CRITICAL(&sequence_lock);
  uint32_t step_index = 0U;
  uint32_t offset_ms = 0U;
  if (!find_step_for_position(position_ms, &step_index, &offset_ms)) {
    taskEXIT_CRITICAL(&sequence_lock);
    return ESP_ERR_INVALID_ARG;
  }

  sequence_current_step = step_index;
  sequence_elapsed_ms = offset_ms;
  const bool continue_playing = sequence_playing;
  const bool is_paused = sequence_paused;
  taskEXIT_CRITICAL(&sequence_lock);

  // Apply the step when playing or paused, so the outputs reflect the new
  // cursor position. When stopped, leave the outputs off; the next
  // sequence_play() will apply the correct step and offset before restarting
  // the timer, so the resume position is still correct.
  if (continue_playing || is_paused) {
    esp_err_t error = apply_step(step_index);
    if (error != ESP_OK) {
      return error;
    }

    error = apply_step_offset(step_index, offset_ms);
    if (error != ESP_OK) {
      return error;
    }
  }

  if (continue_playing && sequence_timer != NULL) {
    return esp_timer_start_periodic(
        sequence_timer, (uint64_t)SEQUENCE_STEP_TICK_PERIOD_MS * 1000ULL);
  }

  /* Re-apply step config while paused resets the modulator paused state.
   * Pause them again so the 1 kHz led_engine tick does not keep advancing
   * the modulators while the sequence is paused. */
  if (is_paused) {
    for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
         oscillator_id++) {
      led_engine_pause_modulators(oscillator_id);
    }
  }

  return ESP_OK;
}

static void sequence_tick(void) {
  uint32_t step_to_apply = 0U;
  bool apply = false;
  bool stop_timer = false;

  taskENTER_CRITICAL(&sequence_lock);
  if (!sequence_playing || sequence_step_count == 0U || sequence_realtime) {
    taskEXIT_CRITICAL(&sequence_lock);
    return;
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
      sequence_current_step = 0U;
      sequence_elapsed_ms = 0U;
      stop_timer = true;
    }
  }
  taskEXIT_CRITICAL(&sequence_lock);

  if (apply) {
    const esp_err_t error = apply_step(step_to_apply);
    if (error != ESP_OK) {
      ESP_LOGE(TAG, "apply_step failed: %d", error);
    }
  }

  if (stop_timer && sequence_timer != NULL) {
    esp_timer_stop(sequence_timer);
  }

  if (stop_timer) {
    for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
         oscillator_id++) {
      (void)led_engine_set_frequency(oscillator_id, 0.0f);
      (void)led_engine_set_brightness(oscillator_id, 0.0f);
    }
    (void)led_engine_all_off();
  }
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

uint32_t sequence_get_elapsed_ms(void) {
  taskENTER_CRITICAL(&sequence_lock);
  const uint32_t elapsed = sequence_elapsed_ms;
  taskEXIT_CRITICAL(&sequence_lock);

  return elapsed;
}

uint32_t sequence_get_step_count(void) {
  taskENTER_CRITICAL(&sequence_lock);
  const uint32_t count = sequence_step_count;
  taskEXIT_CRITICAL(&sequence_lock);

  return count;
}

uint32_t sequence_get_total_elapsed_ms(void) {
  taskENTER_CRITICAL(&sequence_lock);
  const uint32_t current = sequence_current_step;
  const uint32_t elapsed = sequence_elapsed_ms;
  const uint32_t count = sequence_step_count;

  uint32_t total = elapsed;
  for (uint32_t i = 0U; i < current && i < count; i++) {
    total += sequence_steps[i].duration_ms;
  }
  taskEXIT_CRITICAL(&sequence_lock);

  return total;
}
