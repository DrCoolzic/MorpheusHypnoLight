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
#include "freertos/FreeRTOS.h" // IWYU pragma: keep
#include "freertos/task.h"
#include "led_control.h"
#include "oscillator.h"

static const char *TAG = "led_test";

#define TEST_RUN_COUNT 3
#define TEST_PAUSE_MS 5000

/* Main test sequence.
 * The sequence runs TEST_RUN_COUNT times, with all LEDs off for five seconds
 * between runs. If the 24 V LED supply is not yet connected when the test
 * starts, press the RESET button to replay it. */
void app_main(void) {
  ESP_LOGI(TAG, "Initializing LED test outputs");
  ESP_ERROR_CHECK(oscillator_init());
  ESP_ERROR_CHECK(led_control_init());
  ESP_ERROR_CHECK(led_control_all_off());

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

    ESP_ERROR_CHECK(led_control_all_off());
    if (test_run < TEST_RUN_COUNT - 1) {
      ESP_LOGI(TAG, "LEDs off; waiting 5 s before the next test run");
      vTaskDelay(pdMS_TO_TICKS(TEST_PAUSE_MS));
    }
  }

  ESP_LOGI(TAG, "All test runs complete");
}