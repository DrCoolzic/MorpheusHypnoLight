/*
 * Simple LED hardware test for the Morpheus HypnoLight prototype.
 *
 * This program initializes all LED control outputs and runs a short visual
 * test sequence. It uses ESP-IDF LEDC for 8 groups (7 outer cold white
 * groups + 1 central warm white group). The ESP32-S3 only has 8 LEDC
 * channels, so the 8th outer group (OG8) is not tested here. The central group
 * normally uses the SDM peripheral in the final architecture, but LEDC is used
 * here temporarily to verify that the hardware wiring and PicoBuck driver
 * respond correctly.
 */

#include "driver/ledc.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

static const char *TAG = "led_test";

/* GPIO mapping for the 7 tested outer LED groups (OG) and the central group
 * (CG). These pins drive the DIM inputs of the PicoBuck LED drivers. OG8
 * (GPIO8) is not tested in this version because all 8 LEDC channels are used by
 * the other groups. */
static const int og_gpios[7] = {4, 5, 6, 7, 16, 17, 18};
static const int cg_gpio = 15;
#define NUM_OUTER_GROUPS 7

/* LEDC configuration.
 * 20 kHz carrier frequency, 10-bit resolution -> duty range 0..1023.
 * 20 kHz is above audible range and keeps the central group flicker-free. */
#define LEDC_TIMER LEDC_TIMER_0
#define LEDC_MODE LEDC_LOW_SPEED_MODE
#define LEDC_FREQUENCY 20000
#define LEDC_RESOLUTION LEDC_TIMER_10_BIT
#define LEDC_MAX_DUTY 1023

#define CENTRAL_LEDC_CHANNEL 7

/* Configure one LEDC timer and bind 8 group GPIOs to channels. */
static void ledc_init(void) {
  ledc_timer_config_t ledc_timer = {
      .speed_mode = LEDC_MODE,
      .duty_resolution = LEDC_RESOLUTION,
      .timer_num = LEDC_TIMER,
      .freq_hz = LEDC_FREQUENCY,
      .clk_cfg = LEDC_AUTO_CLK,
  };
  ESP_ERROR_CHECK(ledc_timer_config(&ledc_timer));

  /* Bind each tested outer group GPIO to a distinct LEDC channel with duty = 0.
   */
  for (int i = 0; i < NUM_OUTER_GROUPS; i++) {
    ledc_channel_config_t ledc_channel = {
        .gpio_num = og_gpios[i],
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

/* Update the LEDC duty for a single outer group or the central group. */
static void set_outer_brightness(int channel, int duty) {
  ESP_ERROR_CHECK(ledc_set_duty(LEDC_MODE, (ledc_channel_t)channel, duty));
  ESP_ERROR_CHECK(ledc_update_duty(LEDC_MODE, (ledc_channel_t)channel));
}

/* Update the LEDC duty for the central group. */
static void set_central_brightness(int duty) {
  ESP_ERROR_CHECK(
      ledc_set_duty(LEDC_MODE, (ledc_channel_t)CENTRAL_LEDC_CHANNEL, duty));
  ESP_ERROR_CHECK(
      ledc_update_duty(LEDC_MODE, (ledc_channel_t)CENTRAL_LEDC_CHANNEL));
}

/* Turn off all tested groups by setting their LEDC duty to zero. */
static void all_groups_off(void) {
  for (int i = 0; i < NUM_OUTER_GROUPS; i++) {
    set_outer_brightness(i, 0);
  }
  set_central_brightness(0);
}

/* Main test sequence.
 * The sequence runs once then returns. If the 24 V LED supply is not yet
 * connected when the test starts, press the RESET button to replay it. */
void app_main(void) {
  ESP_LOGI(TAG, "Initializing LED test outputs");
  ledc_init();

  /* Start with all outputs off so the LEDs do not stay at maximum
   * brightness due to floating/unconfigured GPIOs. */
  all_groups_off();

  ESP_LOGI(TAG, "Sequential test: each group ON for 500 ms");
  /* Light up each tested outer group one at a time at 50% brightness. */
  for (int i = 0; i < NUM_OUTER_GROUPS; i++) {
    set_outer_brightness(i, LEDC_MAX_DUTY / 2);
    vTaskDelay(pdMS_TO_TICKS(500));
    set_outer_brightness(i, 0);
    vTaskDelay(pdMS_TO_TICKS(200));
  }

  ESP_LOGI(TAG, "Central group ON for 1 s");
  /* Light up the central group at 50% brightness using LEDC. */
  set_central_brightness(LEDC_MAX_DUTY / 2);
  vTaskDelay(pdMS_TO_TICKS(1000));
  set_central_brightness(0);

  ESP_LOGI(TAG, "Global fade in/out on all tested outer groups");
  /* Fade all tested outer groups from off to full brightness and back to off.
   */
  for (int step = 0; step <= 10; step++) {
    int duty = (LEDC_MAX_DUTY * step) / 10;
    for (int i = 0; i < NUM_OUTER_GROUPS; i++) {
      set_outer_brightness(i, duty);
    }
    vTaskDelay(pdMS_TO_TICKS(100));
  }
  for (int step = 10; step >= 0; step--) {
    int duty = (LEDC_MAX_DUTY * step) / 10;
    for (int i = 0; i < NUM_OUTER_GROUPS; i++) {
      set_outer_brightness(i, duty);
    }
    vTaskDelay(pdMS_TO_TICKS(100));
  }

  all_groups_off();
  ESP_LOGI(TAG, "Test complete");
}