/*
 * Simple LED hardware test for the Morpheus HypnoLight prototype.
 *
 * This program initializes all LED control outputs and runs a short visual
 * test sequence. It uses ESP-IDF LEDC for 5 groups: 4 peripheral LED banks
 * (PB1..PB4) plus 1 central group (CG). Each bank shares one LEDC channel,
 * driving its two sub-groups with the same signal.
 */

#include "driver/ledc.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h" // IWYU pragma: keep
#include "freertos/task.h"

static const char *TAG = "led_test";

/* GPIO mapping for the 4 peripheral LED banks (PB1..PB4) and the central group
 * (CG). These pins drive the DIM inputs of the PicoBuck LED drivers.
 * Each bank has 2 LED sub-groups wired in parallel to the same GPIO. */
static const int pb_gpios[4] = {4, 5, 6, 7};
static const int cg_gpio = 15;
#define NUM_OUTER_GROUPS 4

/* LEDC configuration.
 * 20 kHz carrier frequency, 10-bit resolution -> duty range 0..1023.
 * 20 kHz is above visible range and keeps the central group flicker-free. */
#define LEDC_TIMER LEDC_TIMER_0
#define LEDC_MODE LEDC_LOW_SPEED_MODE
#define LEDC_FREQUENCY 20000
#define LEDC_RESOLUTION LEDC_TIMER_10_BIT
#define LEDC_MAX_DUTY 1023

#define CENTRAL_LEDC_CHANNEL 4
#define TEST_RUN_COUNT 3
#define TEST_PAUSE_MS 5000

/* Configure one LEDC timer and bind 5 group GPIOs to channels. */
static void ledc_init(void) {
  ledc_timer_config_t ledc_timer = {
      .speed_mode = LEDC_MODE,
      .duty_resolution = LEDC_RESOLUTION,
      .timer_num = LEDC_TIMER,
      .freq_hz = LEDC_FREQUENCY,
      .clk_cfg = LEDC_AUTO_CLK,
  };
  ESP_ERROR_CHECK(ledc_timer_config(&ledc_timer));

  /* Bind each outer bank GPIO to a distinct LEDC channel with duty = 0. */
  for (int i = 0; i < NUM_OUTER_GROUPS; i++) {
    ledc_channel_config_t ledc_channel = {
        .gpio_num = pb_gpios[i],
        .speed_mode = LEDC_MODE,
        .channel = (ledc_channel_t)i,
        .timer_sel = LEDC_TIMER,
        .duty = 0,
        .hpoint = 0,
    };
    ESP_ERROR_CHECK(ledc_channel_config(&ledc_channel));
  }

  /* Bind the central group GPIO to its own LEDC channel with duty = 0. */
  ledc_channel_config_t central_channel = {
      .gpio_num = cg_gpio,
      .speed_mode = LEDC_MODE,
      .channel = (ledc_channel_t)CENTRAL_LEDC_CHANNEL,
      .timer_sel = LEDC_TIMER,
      .duty = 0,
      .hpoint = 0,
  };
  ESP_ERROR_CHECK(ledc_channel_config(&central_channel));
}

/* Update the LEDC duty for an outer bank or the central group. */
static void set_brightness(int channel, int duty) {
  ESP_ERROR_CHECK(ledc_set_duty(LEDC_MODE, (ledc_channel_t)channel, duty));
  ESP_ERROR_CHECK(ledc_update_duty(LEDC_MODE, (ledc_channel_t)channel));
}

/* Turn off all groups by setting their LEDC duty to zero. */
static void all_groups_off(void) {
  for (int i = 0; i < NUM_OUTER_GROUPS; i++) {
    set_brightness(i, 0);
  }
  set_brightness(CENTRAL_LEDC_CHANNEL, 0);
}

/* Main test sequence.
 * The sequence runs five times, with all LEDs off for five seconds between
 * runs. If the 24 V LED supply is not yet connected when the test starts,
 * press the RESET button to replay it. */
void app_main(void) {
  ESP_LOGI(TAG, "Initializing LED test outputs");
  ledc_init();

  for (int test_run = 0; test_run < TEST_RUN_COUNT; test_run++) {
    ESP_LOGI(TAG, "Starting test run %d/%d", test_run + 1, TEST_RUN_COUNT);

    ESP_LOGI(TAG, "Sequential test: each peripheral bank ON for 2 s");
    /* Light up each outer bank one at a time at 50% brightness. */
    for (int i = 0; i < NUM_OUTER_GROUPS; i++) {
      set_brightness(i, LEDC_MAX_DUTY / 2);
      vTaskDelay(pdMS_TO_TICKS(2000));
      set_brightness(i, 0);
      vTaskDelay(pdMS_TO_TICKS(200));
    }

    ESP_LOGI(TAG, "Central group ON for 2 s");
    /* Light up the central group at 50% brightness using LEDC. */
    set_brightness(CENTRAL_LEDC_CHANNEL, LEDC_MAX_DUTY / 2);
    vTaskDelay(pdMS_TO_TICKS(2000));
    set_brightness(CENTRAL_LEDC_CHANNEL, 0);

    ESP_LOGI(TAG, "Global fade in/out on all LEDs");
    /* Fade all LEDs from off to full brightness and back to off. */
    for (int step = 0; step <= 10; step++) {
      int duty = (LEDC_MAX_DUTY * step) / 10;
      for (int i = 0; i < NUM_OUTER_GROUPS; i++) {
        set_brightness(i, duty);
      }
      set_brightness(CENTRAL_LEDC_CHANNEL, duty);
      vTaskDelay(pdMS_TO_TICKS(100));
    }
    for (int step = 10; step >= 0; step--) {
      int duty = (LEDC_MAX_DUTY * step) / 10;
      for (int i = 0; i < NUM_OUTER_GROUPS; i++) {
        set_brightness(i, duty);
      }
      set_brightness(CENTRAL_LEDC_CHANNEL, duty);
      vTaskDelay(pdMS_TO_TICKS(100));
    }

    all_groups_off();
    if (test_run < TEST_RUN_COUNT - 1) {
      ESP_LOGI(TAG, "LEDs off; waiting 5 s before the next test run");
      vTaskDelay(pdMS_TO_TICKS(TEST_PAUSE_MS));
    }
  }

  ESP_LOGI(TAG, "All test runs complete");
}