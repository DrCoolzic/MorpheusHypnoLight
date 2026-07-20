/*
 * Simple LED hardware test for the Morpheus HypnoLight prototype.
 *
 * This program initializes all LED control outputs and runs a short visual
 * test sequence. It uses ESP-IDF LEDC for 5 groups: 4 peripheral LED banks
 * (PB1..PB4) plus 1 central group (CG). Each bank shares one LEDC channel,
 * driving its two sub-groups with the same signal.
 */

#include "esp_err.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h" // IWYU pragma: keep
#include "freertos/task.h"
#include "led_control.h"
#include "oscillator.h"
#include "sequence.h"

static const char *TAG = "led_test";

#define TEST_RUN_COUNT 1
#define TEST_PAUSE_MS 5000
#define OSCILLATOR_TICK_PERIOD_US 1000
#define SEQUENCE_TICK_PERIOD_US (SEQUENCE_TICK_PERIOD_MS * 1000U)
#define OSCILLATOR_TEST_BRIGHTNESS 0.5f
#define OSCILLATOR_TEST_SHORT_DURATION_MS 4000
#define OSCILLATOR_TEST_LONG_DURATION_MS 8000
#define LINEAR_TEST_DURATION_MS 4000

/**
 * @brief Evaluate realtime parameter controls at the fixed sequence rate.
 *
 * @param[in] arg Unused esp_timer callback argument.
 */
static void sequence_timer_callback(void *arg) {
  (void)arg;

  ESP_ERROR_CHECK(sequence_tick());
}

/**
 * @brief Generate waveform samples and apply them to the LED outputs.
 *
 * This callback uses the default esp_timer task dispatch method, allowing it
 * to call the standard LEDC driver APIs through led_control.
 *
 * @param[in] arg Unused esp_timer callback argument.
 */
static void oscillator_timer_callback(void *arg) {
  (void)arg;

  float osc_values[OSCILLATOR_COUNT];
  float brightnesses[OSCILLATOR_COUNT];
  ESP_ERROR_CHECK(oscillator_tick(osc_values));
  ESP_ERROR_CHECK(sequence_get_realtime_brightness(brightnesses));

  for (uint8_t oscillator_id = 0; oscillator_id < OSCILLATOR_COUNT;
       oscillator_id++) {
    ESP_ERROR_CHECK(led_control_update(oscillator_id, osc_values[oscillator_id],
                                       brightnesses[oscillator_id]));
  }
}

/**
 * @brief Reset oscillator state and disable every visual test output.
 *
 * The caller must stop the oscillator timer before calling this function.
 */
static void reset_oscillator_visual_stage(void) {
  ESP_ERROR_CHECK(sequence_init());
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
      .duty_cycle = duty_cycle,
      .phase_degrees = phase_degrees,
      .custom_lut = NULL,
  };

  ESP_ERROR_CHECK(sequence_realtime_set_static(oscillator_id, &config));
  ESP_ERROR_CHECK(sequence_realtime_set_frequency(oscillator_id, frequency_hz));
  ESP_ERROR_CHECK(sequence_realtime_set_brightness(oscillator_id, brightness));
}

/**
 * @brief Run the current oscillator configuration for a fixed duration.
 *
 * The function waits one FreeRTOS tick after stopping the timer so an already
 * dispatched callback completes before the next stage changes oscillator state.
 *
 * @param[in] oscillator_timer Periodic esp_timer that advances DDS state.
 * @param[in] sequence_timer Periodic esp_timer that evaluates parameter ramps.
 * @param[in] duration_ms Visual test duration in milliseconds.
 */
static void run_oscillator_visual_stage(esp_timer_handle_t oscillator_timer,
                                        esp_timer_handle_t sequence_timer,
                                        uint32_t duration_ms) {
  ESP_ERROR_CHECK(
      esp_timer_start_periodic(sequence_timer, SEQUENCE_TICK_PERIOD_US));
  ESP_ERROR_CHECK(
      esp_timer_start_periodic(oscillator_timer, OSCILLATOR_TICK_PERIOD_US));
  vTaskDelay(pdMS_TO_TICKS(duration_ms));
  ESP_ERROR_CHECK(esp_timer_stop(oscillator_timer));
  ESP_ERROR_CHECK(esp_timer_stop(sequence_timer));
  vTaskDelay(1);
  ESP_ERROR_CHECK(led_control_all_off());
}

/**
 * @brief Exercise oscillator waveforms and phase behavior on the LED hardware.
 *
 * The test runs one stage at a time so each waveform is visually identifiable.
 * The timer callback advances DDS state at 1 kHz and passes the generated
 * values to led_control.
 *
 * @param[in] oscillator_timer Periodic esp_timer used for DDS generation.
 * @param[in] sequence_timer Periodic esp_timer used for parameter evaluation.
 */
static void run_oscillator_visual_test(esp_timer_handle_t oscillator_timer,
                                       esp_timer_handle_t sequence_timer) {
  ESP_LOGI(TAG, "Oscillator test: PB1 square 2 Hz, 50%% duty");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(0, OSCILLATOR_WAVEFORM_SQUARE, 0.5f, 0.0f,
                                   2.0f, OSCILLATOR_TEST_BRIGHTNESS);
  run_oscillator_visual_stage(oscillator_timer, sequence_timer,
                              OSCILLATOR_TEST_SHORT_DURATION_MS);

  ESP_LOGI(TAG, "Oscillator test: PB2 square 2 Hz, 25%% duty");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(1, OSCILLATOR_WAVEFORM_SQUARE, 0.25f, 0.0f,
                                   2.0f, OSCILLATOR_TEST_BRIGHTNESS);
  run_oscillator_visual_stage(oscillator_timer, sequence_timer,
                              OSCILLATOR_TEST_SHORT_DURATION_MS);

  ESP_LOGI(TAG, "Oscillator test: PB3 triangle 0.25 Hz");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(2, OSCILLATOR_WAVEFORM_TRIANGLE, 0.5f, 0.0f,
                                   0.25f, OSCILLATOR_TEST_BRIGHTNESS);
  run_oscillator_visual_stage(oscillator_timer, sequence_timer,
                              OSCILLATOR_TEST_LONG_DURATION_MS);

  ESP_LOGI(TAG, "Oscillator test: PB4 sine 0.25 Hz");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(3, OSCILLATOR_WAVEFORM_SINE, 0.5f, 0.0f,
                                   0.25f, OSCILLATOR_TEST_BRIGHTNESS);
  run_oscillator_visual_stage(oscillator_timer, sequence_timer,
                              OSCILLATOR_TEST_LONG_DURATION_MS);

  ESP_LOGI(TAG, "Oscillator test: CG fixed output at 0 Hz");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(LED_CONTROL_CG_OSCILLATOR_ID,
                                   OSCILLATOR_WAVEFORM_SINE, 0.5f, 180.0f, 0.0f,
                                   OSCILLATOR_TEST_BRIGHTNESS);
  run_oscillator_visual_stage(oscillator_timer, sequence_timer,
                              OSCILLATOR_TEST_SHORT_DURATION_MS);

  ESP_LOGI(TAG, "Oscillator test: PB1/PB2 square 2 Hz, 180 degree offset");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(0, OSCILLATOR_WAVEFORM_SQUARE, 0.5f, 0.0f,
                                   2.0f, OSCILLATOR_TEST_BRIGHTNESS);
  configure_oscillator_visual_test(1, OSCILLATOR_WAVEFORM_SQUARE, 0.5f, 180.0f,
                                   2.0f, OSCILLATOR_TEST_BRIGHTNESS);
  run_oscillator_visual_stage(oscillator_timer, sequence_timer,
                              OSCILLATOR_TEST_SHORT_DURATION_MS);

  ESP_LOGI(TAG, "Sequence test: PB1 linear brightness 0%% to 100%% over 4 s");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(0, OSCILLATOR_WAVEFORM_SINE, 0.5f, 0.0f,
                                   0.0f, 0.0f);
  ESP_ERROR_CHECK(
      sequence_realtime_linear_brightness(0, 1.0f, LINEAR_TEST_DURATION_MS));
  run_oscillator_visual_stage(oscillator_timer, sequence_timer,
                              LINEAR_TEST_DURATION_MS);

  ESP_LOGI(TAG, "Sequence test: PB2 linear frequency 0 Hz to 2 Hz over 4 s");
  reset_oscillator_visual_stage();
  configure_oscillator_visual_test(1, OSCILLATOR_WAVEFORM_SINE, 0.5f, 0.0f,
                                   0.0f, OSCILLATOR_TEST_BRIGHTNESS);
  ESP_ERROR_CHECK(
      sequence_realtime_linear_frequency(1, 2.0f, LINEAR_TEST_DURATION_MS));
  run_oscillator_visual_stage(oscillator_timer, sequence_timer,
                              LINEAR_TEST_DURATION_MS);
}

/* Main test sequence.
 * The sequence runs TEST_RUN_COUNT times, with all LEDs off for five seconds
 * between runs. If the 24 V LED supply is not yet connected when the test
 * starts, press the RESET button to replay it. */
void app_main(void) {
  ESP_LOGI(TAG, "Initializing LED test outputs");
  ESP_ERROR_CHECK(sequence_init());
  ESP_ERROR_CHECK(led_control_init());
  ESP_ERROR_CHECK(led_control_all_off());

  const esp_timer_create_args_t oscillator_timer_args = {
      .callback = oscillator_timer_callback,
      .arg = NULL,
      .name = "oscillator_test",
  };
  const esp_timer_create_args_t sequence_timer_args = {
      .callback = sequence_timer_callback,
      .arg = NULL,
      .name = "sequence_test",
  };
  esp_timer_handle_t oscillator_timer;
  esp_timer_handle_t sequence_timer;
  ESP_ERROR_CHECK(esp_timer_create(&oscillator_timer_args, &oscillator_timer));
  ESP_ERROR_CHECK(esp_timer_create(&sequence_timer_args, &sequence_timer));

  for (int test_run = 0; test_run < TEST_RUN_COUNT; test_run++) {
    ESP_LOGI(TAG, "Starting test run %d/%d", test_run + 1, TEST_RUN_COUNT);

    ESP_LOGI(TAG, "Sequential test: each peripheral bank ON for 2 s");
    /* Light up each peripheral bank one at a time at 50% brightness. */
    for (uint8_t oscillator_id = 0;
         oscillator_id < LED_CONTROL_PERIPHERAL_BANK_COUNT; oscillator_id++) {
      ESP_ERROR_CHECK(led_control_update(oscillator_id, 1.0f, 0.5f));
      vTaskDelay(pdMS_TO_TICKS(2000));
      ESP_ERROR_CHECK(led_control_update(oscillator_id, 0.0f, 0.0f));
      vTaskDelay(pdMS_TO_TICKS(200));
    }

    ESP_LOGI(TAG, "Central group ON for 2 s");
    /* Light up the central group at 50% brightness using LEDC. */
    ESP_ERROR_CHECK(
        led_control_update(LED_CONTROL_CG_OSCILLATOR_ID, 1.0f, 0.5f));
    vTaskDelay(pdMS_TO_TICKS(2000));
    ESP_ERROR_CHECK(
        led_control_update(LED_CONTROL_CG_OSCILLATOR_ID, 0.0f, 0.0f));

    ESP_LOGI(TAG, "Global fade in/out on all LEDs");
    /* Fade all LEDs from off to full brightness and back to off. */
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

    run_oscillator_visual_test(oscillator_timer, sequence_timer);

    ESP_ERROR_CHECK(led_control_all_off());
    if (test_run < TEST_RUN_COUNT - 1) {
      ESP_LOGI(TAG, "LEDs off; waiting 5 s before the next test run");
      vTaskDelay(pdMS_TO_TICKS(TEST_PAUSE_MS));
    }
  }

  ESP_ERROR_CHECK(esp_timer_delete(oscillator_timer));
  ESP_ERROR_CHECK(esp_timer_delete(sequence_timer));
  ESP_LOGI(TAG, "All test runs complete");
}