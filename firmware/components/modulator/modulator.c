/**
 * @file modulator.c
 * @brief Generic time-varying value generator implementation.
 */

#include "modulator.h"

#include <math.h>
#include <stdint.h>
#include <string.h>

/** @brief One complete normalized LFO cycle. */
#define MODULATOR_LFO_CYCLE 1.0f

/**
 * @brief Clamp a value to the inclusive range [0.0, 1.0].
 *
 * @param[in] value Value to clamp.
 *
 * @return Clamped value.
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

esp_err_t modulator_init(modulator_state_t *state) {
  if (state == NULL) {
    return ESP_ERR_INVALID_ARG;
  }

  memset(state, 0, sizeof(*state));
  state->config.mode = MODULATOR_MODE_STATIC;
  state->current_value = 0.0f;
  state->lfo_phase = 0.0f;
  state->paused = false;
  state->paused_elapsed_ms = 0U;

  return ESP_OK;
}

esp_err_t modulator_set_config(modulator_state_t *state,
                               const modulator_config_t *config) {
  if (state == NULL || config == NULL) {
    return ESP_ERR_INVALID_ARG;
  }

  switch (config->mode) {
  case MODULATOR_MODE_STATIC:
    if (!isfinite(config->static_config.value)) {
      return ESP_ERR_INVALID_ARG;
    }
    break;

  case MODULATOR_MODE_LINEAR:
    if (!isfinite(config->linear_config.start_value) ||
        !isfinite(config->linear_config.end_value) ||
        config->linear_config.duration_ms == 0U) {
      return ESP_ERR_INVALID_ARG;
    }
    break;

  case MODULATOR_MODE_LFO:
    if (config->lfo_config.waveform != MODULATOR_LFO_WAVEFORM_SINE &&
        config->lfo_config.waveform != MODULATOR_LFO_WAVEFORM_SQUARE) {
      return ESP_ERR_INVALID_ARG;
    }
    if (!isfinite(config->lfo_config.frequency_hz) ||
        config->lfo_config.frequency_hz <= 0.0f ||
        !isfinite(config->lfo_config.low) ||
        !isfinite(config->lfo_config.high) ||
        config->lfo_config.low > config->lfo_config.high) {
      return ESP_ERR_INVALID_ARG;
    }
    break;

  default:
    return ESP_ERR_INVALID_ARG;
  }

  state->config = *config;
  state->start_value = state->current_value;
  state->elapsed_ms = 0U;
  state->lfo_phase = 0.0f;

  if (config->mode == MODULATOR_MODE_STATIC) {
    state->current_value = config->static_config.value;
  } else if (config->mode == MODULATOR_MODE_LINEAR) {
    state->current_value = config->linear_config.start_value;
  }

  return ESP_OK;
}

esp_err_t modulator_evaluate(modulator_state_t *state, float delta_time_ms,
                             float *value) {
  if (state == NULL || value == NULL || delta_time_ms < 0.0f ||
      !isfinite(delta_time_ms)) {
    return ESP_ERR_INVALID_ARG;
  }

  switch (state->config.mode) {
  case MODULATOR_MODE_STATIC:
    state->current_value = state->config.static_config.value;
    break;

  case MODULATOR_MODE_LINEAR: {
    const uint32_t duration_ms = state->config.linear_config.duration_ms;
    state->elapsed_ms += (uint32_t)delta_time_ms;

    if (state->elapsed_ms >= duration_ms) {
      state->current_value = state->config.linear_config.end_value;
      state->config.mode = MODULATOR_MODE_STATIC;
      state->config.static_config.value = state->current_value;
    } else {
      const float progress = (float)state->elapsed_ms / (float)duration_ms;
      state->current_value = state->config.linear_config.start_value +
                             (state->config.linear_config.end_value -
                              state->config.linear_config.start_value) *
                                 progress;
    }
    break;
  }

  case MODULATOR_MODE_LFO: {
    const float period_ms = 1000.0f / state->config.lfo_config.frequency_hz;
    state->lfo_phase += delta_time_ms / period_ms;
    while (state->lfo_phase >= MODULATOR_LFO_CYCLE) {
      state->lfo_phase -= MODULATOR_LFO_CYCLE;
    }

    float lfo_value;
    if (state->config.lfo_config.waveform == MODULATOR_LFO_WAVEFORM_SQUARE) {
      lfo_value = (state->lfo_phase < 0.5f) ? 1.0f : 0.0f;
    } else {
      lfo_value = (sinf(state->lfo_phase * 2.0f * (float)M_PI) + 1.0f) * 0.5f;
    }
    lfo_value = clamp_unit(lfo_value);

    state->current_value =
        state->config.lfo_config.low +
        (state->config.lfo_config.high - state->config.lfo_config.low) *
            lfo_value;
    break;
  }

  default:
    return ESP_ERR_INVALID_STATE;
  }

  *value = state->current_value;
  return ESP_OK;
}

esp_err_t modulator_pause(modulator_state_t *state) {
  if (state == NULL) {
    return ESP_ERR_INVALID_ARG;
  }

  if (state->paused) {
    return ESP_OK;
  }

  if (state->config.mode == MODULATOR_MODE_LINEAR) {
    state->paused_config = state->config;
    state->paused_elapsed_ms = state->elapsed_ms;
    state->config.mode = MODULATOR_MODE_STATIC;
    state->config.static_config.value = state->current_value;
  }
  state->paused = true;

  return ESP_OK;
}

esp_err_t modulator_resume(modulator_state_t *state) {
  if (state == NULL) {
    return ESP_ERR_INVALID_ARG;
  }

  if (!state->paused) {
    return ESP_OK;
  }

  if (state->paused_config.mode == MODULATOR_MODE_LINEAR &&
      state->config.mode == MODULATOR_MODE_STATIC) {
    const uint32_t remaining_ms =
        (state->paused_elapsed_ms >=
         state->paused_config.linear_config.duration_ms)
            ? 0U
            : state->paused_config.linear_config.duration_ms -
                  state->paused_elapsed_ms;

    state->config = state->paused_config;
    state->config.linear_config.start_value = state->current_value;
    state->config.linear_config.duration_ms = remaining_ms;
    state->start_value = state->current_value;
    state->elapsed_ms = 0U;
  }
  state->paused = false;

  return ESP_OK;
}

esp_err_t modulator_seek(modulator_state_t *state, uint32_t elapsed_ms) {
  if (state == NULL) {
    return ESP_ERR_INVALID_ARG;
  }

  const bool paused = state->paused;
  const modulator_config_t *config =
      paused ? &state->paused_config : &state->config;
  float value = 0.0f;

  switch (config->mode) {
  case MODULATOR_MODE_STATIC:
    value = config->static_config.value;
    break;

  case MODULATOR_MODE_LINEAR: {
    const uint32_t duration_ms = config->linear_config.duration_ms;
    if (duration_ms == 0U) {
      return ESP_ERR_INVALID_STATE;
    }
    if (elapsed_ms >= duration_ms) {
      value = config->linear_config.end_value;
    } else {
      const float progress = (float)elapsed_ms / (float)duration_ms;
      value = config->linear_config.start_value +
              (config->linear_config.end_value -
               config->linear_config.start_value) *
                  progress;
    }
    if (!paused) {
      state->elapsed_ms = elapsed_ms;
    }
    break;
  }

  case MODULATOR_MODE_LFO: {
    const float period_ms = 1000.0f / config->lfo_config.frequency_hz;
    float phase = fmodf((float)elapsed_ms / period_ms, MODULATOR_LFO_CYCLE);
    if (phase < 0.0f) {
      phase += MODULATOR_LFO_CYCLE;
    }

    float lfo_value;
    if (config->lfo_config.waveform == MODULATOR_LFO_WAVEFORM_SQUARE) {
      lfo_value = (phase < 0.5f) ? 1.0f : 0.0f;
    } else {
      lfo_value = (sinf(phase * 2.0f * (float)M_PI) + 1.0f) * 0.5f;
    }
    lfo_value = clamp_unit(lfo_value);

    value = config->lfo_config.low +
            (config->lfo_config.high - config->lfo_config.low) * lfo_value;
    state->lfo_phase = phase;
    break;
  }

  default:
    return ESP_ERR_INVALID_STATE;
  }

  if (state->config.mode == MODULATOR_MODE_STATIC) {
    state->config.static_config.value = value;
  }
  state->current_value = value;
  if (paused) {
    state->paused_elapsed_ms = elapsed_ms;
  }

  return ESP_OK;
}
