/*
 * Simple LED hardware tests for the Morpheus HypnoLight prototype.
 *
 * These tests are kept here so that main.c can focus on the sequence playback
 * demonstration while the original bring-up tests remain available.
 */

#include "test.h"

#include "test_sequence.h"
#include "test_sequence_compact_data.h"

#include "esp_err.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "led_control.h"
#include "led_engine.h"
#include "oscillator.h"
#include "sequence.h"

#include <math.h>
#include <stdbool.h>
#include <stdint.h>
#include <string.h>

static const char *TAG = "led_test";

#define TEST_PAUSE_MS 5000
#define OSCILLATOR_TICK_PERIOD_US 1000
#define OSCILLATOR_TEST_BRIGHTNESS 1.0f
#define OSCILLATOR_TEST_SHORT_DURATION_MS 4000
#define OSCILLATOR_TEST_LONG_DURATION_MS 8000
#define LINEAR_TEST_DURATION_MS 4000

static const uint8_t compact_golden_sequence[] = {
    0x4D, 0x48, 0x4C, 0x53, 0x01, 0x00, 0x00, 0x01, 0x2F, 0x00, 0x3F,
    0xF5, 0xDD, 0xB1, 0x0A, 0x00, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00,
    0x32, 0x00, 0x32, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x32, 0x00,
    0x32, 0x00, 0x00, 0x00, 0x64, 0x00, 0x00, 0x32, 0x00, 0x32, 0x00,
    0x00, 0x00, 0x64, 0x00, 0x00, 0x32, 0x00, 0x32, 0x00, 0x00, 0x00,
    0x64, 0x00, 0x00, 0x32, 0x00, 0x32,
};

/**
 * @brief Reset oscillator state before a visual test stage.
 */
static void reset_oscillator_visual_stage(void) {
  ESP_ERROR_CHECK(led_engine_init());
}

/**
 * @brief Configure one oscillator used by the visual hardware test.
 *
 * @param[in] oscillator_id Fixed oscillator ID.
 * @param[in] waveform Waveform shape to generate.
 * @param[in] duty_cycle Duty cycle for square or triangle waveforms.
 * @param[in] phase_degrees Initial phase in degrees.
 * @param[in] frequency_hz Dynamic frequency in hertz.
 * @param[in] brightness Constant test brightness factor.
 */
static void configure_oscillator_visual_test(
    uint8_t oscillator_id, oscillator_waveform_t waveform, float duty_cycle,
    float phase_degrees, float frequency_hz, float brightness) {
  const oscillator_static_config_t config = {
      .waveform = waveform,
      .phase_degrees = phase_degrees,
      .custom_lut = NULL,
  };

  ESP_ERROR_CHECK(led_engine_set_static(oscillator_id, &config));
  ESP_ERROR_CHECK(led_engine_set_duty_cycle(oscillator_id, duty_cycle));
  ESP_ERROR_CHECK(led_engine_set_frequency(oscillator_id, frequency_hz));
  ESP_ERROR_CHECK(led_engine_set_brightness(oscillator_id, brightness));
}

/**
 * @brief Run the current oscillator configuration for a fixed duration.
 *
 * @param[in] oscillator_timer Periodic esp_timer that advances DDS state.
 * @param[in] duration_ms Visual test duration in milliseconds.
 */
static void run_oscillator_visual_stage(esp_timer_handle_t oscillator_timer,
                                        uint32_t duration_ms) {
  ESP_ERROR_CHECK(
      esp_timer_start_periodic(oscillator_timer, OSCILLATOR_TICK_PERIOD_US));
  vTaskDelay(pdMS_TO_TICKS(duration_ms));
  ESP_ERROR_CHECK(esp_timer_stop(oscillator_timer));
  vTaskDelay(1);
  ESP_ERROR_CHECK(led_engine_all_off());
}

/**
 * @brief Exercise oscillator waveforms and phase behavior on the LED hardware.
 */
static void run_oscillator_visual_test(esp_timer_handle_t oscillator_timer) {
  ESP_LOGI(TAG, "Oscillator test: PB1 square 2 Hz, 50%% duty");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(0, OSCILLATOR_WAVEFORM_SQUARE, 0.5f, 0.0f,
                                   2.0f, OSCILLATOR_TEST_BRIGHTNESS);
  run_oscillator_visual_stage(oscillator_timer,
                              OSCILLATOR_TEST_SHORT_DURATION_MS);

  ESP_LOGI(TAG, "Oscillator test: PB2 square 2 Hz, 25%% duty");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(1, OSCILLATOR_WAVEFORM_SQUARE, 0.25f, 0.0f,
                                   2.0f, OSCILLATOR_TEST_BRIGHTNESS);
  run_oscillator_visual_stage(oscillator_timer,
                              OSCILLATOR_TEST_SHORT_DURATION_MS);

  ESP_LOGI(TAG, "Oscillator test: PB3 triangle 0.25 Hz");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(2, OSCILLATOR_WAVEFORM_TRIANGLE, 0.5f, 0.0f,
                                   0.25f, OSCILLATOR_TEST_BRIGHTNESS);
  run_oscillator_visual_stage(oscillator_timer,
                              OSCILLATOR_TEST_LONG_DURATION_MS);

  ESP_LOGI(TAG, "Oscillator test: PB4 sine 0.25 Hz");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(3, OSCILLATOR_WAVEFORM_SINE, 0.5f, 0.0f,
                                   0.25f, OSCILLATOR_TEST_BRIGHTNESS);
  run_oscillator_visual_stage(oscillator_timer,
                              OSCILLATOR_TEST_LONG_DURATION_MS);

  ESP_LOGI(TAG, "Oscillator test: CG fixed output at 0 Hz");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(LED_CONTROL_CG_OSCILLATOR_ID,
                                   OSCILLATOR_WAVEFORM_SINE, 0.5f, 180.0f, 0.0f,
                                   OSCILLATOR_TEST_BRIGHTNESS);
  run_oscillator_visual_stage(oscillator_timer,
                              OSCILLATOR_TEST_SHORT_DURATION_MS);

  ESP_LOGI(TAG, "Oscillator test: PB1/PB2 square 2 Hz, 180 degree offset");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(0, OSCILLATOR_WAVEFORM_SQUARE, 0.5f, 0.0f,
                                   2.0f, OSCILLATOR_TEST_BRIGHTNESS);
  configure_oscillator_visual_test(1, OSCILLATOR_WAVEFORM_SQUARE, 0.5f, 180.0f,
                                   2.0f, OSCILLATOR_TEST_BRIGHTNESS);
  run_oscillator_visual_stage(oscillator_timer,
                              OSCILLATOR_TEST_SHORT_DURATION_MS);

  ESP_LOGI(TAG, "Sequence test: PB1 linear brightness 0%% to 100%% over 4 s");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(0, OSCILLATOR_WAVEFORM_SINE, 0.5f, 0.0f,
                                   0.0f, 0.0f);
  ESP_ERROR_CHECK(
      led_engine_linear_brightness(0, 0.0f, 1.0f, LINEAR_TEST_DURATION_MS));
  run_oscillator_visual_stage(oscillator_timer, LINEAR_TEST_DURATION_MS);

  ESP_LOGI(TAG, "Sequence test: PB2 linear frequency 0 Hz to 2 Hz over 4 s");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(1, OSCILLATOR_WAVEFORM_SINE, 0.5f, 0.0f,
                                   0.0f, OSCILLATOR_TEST_BRIGHTNESS);
  ESP_ERROR_CHECK(
      led_engine_linear_frequency(1, 0.0f, 2.0f, LINEAR_TEST_DURATION_MS));
  run_oscillator_visual_stage(oscillator_timer, LINEAR_TEST_DURATION_MS);
}

static bool compact_float_equal(float left, float right, float tolerance) {
  return fabsf(left - right) <= tolerance;
}

static bool compact_phase_equal(float left, float right) {
  float difference = fabsf(left - right);
  if (difference > 180.0f) {
    difference = 360.0f - difference;
  }
  return difference <= 5.8f;
}

static bool compact_modulator_equal(const modulator_config_t *left,
                                    const modulator_config_t *right,
                                    float tolerance) {
  if (left->mode != right->mode) {
    return false;
  }
  switch (left->mode) {
  case MODULATOR_MODE_STATIC:
    return compact_float_equal(left->static_config.value,
                               right->static_config.value, tolerance);
  case MODULATOR_MODE_LINEAR:
    return left->linear_config.duration_ms ==
               right->linear_config.duration_ms &&
           compact_float_equal(left->linear_config.start_value,
                               right->linear_config.start_value, tolerance) &&
           compact_float_equal(left->linear_config.end_value,
                               right->linear_config.end_value, tolerance);
  case MODULATOR_MODE_LFO:
    return left->lfo_config.waveform == right->lfo_config.waveform &&
           compact_float_equal(left->lfo_config.frequency_hz,
                               right->lfo_config.frequency_hz, 0.051f) &&
           compact_float_equal(left->lfo_config.low, right->lfo_config.low,
                               tolerance) &&
           compact_float_equal(left->lfo_config.high, right->lfo_config.high,
                               tolerance);
  default:
    return false;
  }
}

static bool compact_step_equal(const sequence_step_t *left,
                               const sequence_step_t *right) {
  if (left->duration_ms != right->duration_ms) {
    return false;
  }
  for (uint8_t oscillator = 0U; oscillator < OSCILLATOR_COUNT; oscillator++) {
    const sequence_oscillator_step_t *left_osc = &left->oscillators[oscillator];
    const sequence_oscillator_step_t *right_osc =
        &right->oscillators[oscillator];
    if (left_osc->static_config.waveform != right_osc->static_config.waveform ||
        !compact_phase_equal(left_osc->static_config.phase_degrees,
                             right_osc->static_config.phase_degrees) ||
        !compact_modulator_equal(&left_osc->frequency_modulator,
                                 &right_osc->frequency_modulator, 0.051f) ||
        !compact_modulator_equal(&left_osc->brightness_modulator,
                                 &right_osc->brightness_modulator, 0.0051f) ||
        !compact_modulator_equal(&left_osc->duty_modulator,
                                 &right_osc->duty_modulator, 0.0051f)) {
      return false;
    }
  }
  return true;
}

esp_err_t test_validate_compact_sequence(void) {
  static sequence_step_t reference[SEQUENCE_DEMO_STEP_COUNT];
  static sequence_step_t decoded[SEQUENCE_DEMO_STEP_COUNT];
  static uint8_t corrupted[sizeof(demo_sequence_compact)];

  build_demo_sequence(reference);
  uint32_t step_count = 0U;
  sequence_step_t golden_step;
  esp_err_t error = sequence_decode_compact(compact_golden_sequence,
                                            sizeof(compact_golden_sequence),
                                            &golden_step, 1U, &step_count);
  if (error != ESP_OK || step_count != 1U || golden_step.duration_ms != 1000U) {
    return ESP_FAIL;
  }
  for (uint8_t oscillator = 0U; oscillator < OSCILLATOR_COUNT; oscillator++) {
    const sequence_oscillator_step_t *golden_oscillator =
        &golden_step.oscillators[oscillator];
    if (golden_oscillator->static_config.waveform !=
            OSCILLATOR_WAVEFORM_SQUARE ||
        golden_oscillator->frequency_modulator.mode != MODULATOR_MODE_STATIC ||
        golden_oscillator->frequency_modulator.static_config.value != 10.0f ||
        golden_oscillator->brightness_modulator.static_config.value != 0.5f ||
        golden_oscillator->duty_modulator.static_config.value != 0.5f) {
      return ESP_FAIL;
    }
  }

  error =
      sequence_decode_compact(demo_sequence_compact, demo_sequence_compact_size,
                              decoded, SEQUENCE_DEMO_STEP_COUNT, &step_count);
  if (error != ESP_OK || step_count != SEQUENCE_DEMO_STEP_COUNT) {
    return ESP_FAIL;
  }
  for (uint32_t step = 0U; step < step_count; step++) {
    if (!compact_step_equal(&reference[step], &decoded[step])) {
      return ESP_FAIL;
    }
  }

  memcpy(corrupted, demo_sequence_compact, sizeof(corrupted));
  corrupted[0] ^= 0x01U;
  if (sequence_decode_compact(corrupted, sizeof(corrupted), NULL, 0U,
                              &step_count) == ESP_OK) {
    return ESP_FAIL;
  }

  memcpy(corrupted, demo_sequence_compact, sizeof(corrupted));
  corrupted[sizeof(corrupted) - 1U] ^= 0x01U;
  if (sequence_decode_compact(corrupted, sizeof(corrupted), NULL, 0U,
                              &step_count) == ESP_OK ||
      sequence_decode_compact(demo_sequence_compact,
                              demo_sequence_compact_size - 1U, NULL, 0U,
                              &step_count) == ESP_OK) {
    return ESP_FAIL;
  }

  memset(decoded, 0xA5, sizeof(decoded));
  const sequence_step_t unchanged = decoded[0];
  if (sequence_decode_compact(corrupted, sizeof(corrupted), decoded,
                              SEQUENCE_DEMO_STEP_COUNT,
                              &step_count) == ESP_OK ||
      memcmp(&decoded[0], &unchanged, sizeof(unchanged)) != 0) {
    return ESP_FAIL;
  }

  ESP_LOGI(TAG, "Compact sequence decoder validation passed");
  return ESP_OK;
}

esp_err_t test_load_compact_demo_sequence(void) {
  return sequence_load_compact(demo_sequence_compact,
                               demo_sequence_compact_size);
}

void test_run_static_led_test(void) {
  ESP_LOGI(TAG, "Sequential test: each peripheral bank ON for 2 s");
  for (uint8_t oscillator_id = 0;
       oscillator_id < LED_CONTROL_PERIPHERAL_BANK_COUNT; oscillator_id++) {
    ESP_ERROR_CHECK(led_control_update(oscillator_id, 1.0f, 1.0f));
    vTaskDelay(pdMS_TO_TICKS(2000));
    ESP_ERROR_CHECK(led_control_update(oscillator_id, 0.0f, 0.0f));
    vTaskDelay(pdMS_TO_TICKS(200));
  }

  ESP_LOGI(TAG, "Central group ON for 2 s");
  ESP_ERROR_CHECK(led_control_update(LED_CONTROL_CG_OSCILLATOR_ID, 1.0f, 1.0f));
  vTaskDelay(pdMS_TO_TICKS(2000));
  ESP_ERROR_CHECK(led_control_update(LED_CONTROL_CG_OSCILLATOR_ID, 0.0f, 0.0f));

  ESP_LOGI(TAG, "Global fade in/out on all LEDs");
  for (int step = 0; step <= 10; step++) {
    const float brightness = (float)step / 10.0f;
    for (uint8_t oscillator_id = 0;
         oscillator_id < LED_CONTROL_OSCILLATOR_COUNT; oscillator_id++) {
      ESP_ERROR_CHECK(led_control_update(oscillator_id, 1.0f, brightness));
    }
    vTaskDelay(pdMS_TO_TICKS(100));
  }
  for (int step = 10; step >= 0; step--) {
    const float brightness = (float)step / 10.0f;
    for (uint8_t oscillator_id = 0;
         oscillator_id < LED_CONTROL_OSCILLATOR_COUNT; oscillator_id++) {
      ESP_ERROR_CHECK(led_control_update(oscillator_id, 1.0f, brightness));
    }
    vTaskDelay(pdMS_TO_TICKS(100));
  }
}

void test_run_oscillator_visual_test(esp_timer_handle_t oscillator_timer) {
  run_oscillator_visual_test(oscillator_timer);
}

void test_run_all(esp_timer_handle_t oscillator_timer) {
  test_run_static_led_test();
  run_oscillator_visual_test(oscillator_timer);
}
