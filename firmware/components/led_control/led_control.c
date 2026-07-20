/**
 * @file led_control.c
 * @brief LEDC-backed implementation of the fixed HypnoLight LED outputs.
 */
#include "led_control.h"

#include <math.h>

#include "driver/ledc.h"

/** @brief LEDC timer shared by all five LED output channels. */
#define LED_CONTROL_TIMER LEDC_TIMER_0

/** @brief LEDC speed mode supported by the ESP32-S3. */
#define LED_CONTROL_MODE LEDC_LOW_SPEED_MODE

/** @brief Fixed PWM carrier frequency for all LED control outputs. */
#define LED_CONTROL_FREQUENCY_HZ 20000

/** @brief LEDC duty resolution used for brightness control. */
#define LED_CONTROL_RESOLUTION LEDC_TIMER_10_BIT

/** @brief Maximum duty value represented by LED_CONTROL_RESOLUTION. */
#define LED_CONTROL_MAX_DUTY ((1U << LED_CONTROL_RESOLUTION) - 1U)

/** @brief GPIO mapping indexed by fixed oscillator ID. */
static const int led_control_gpios[LED_CONTROL_OSCILLATOR_COUNT] = {
    4, 5, 6, 7, 15,
};

/**
 * @brief Clamp a finite value to the normalized brightness range.
 *
 * @param[in] value Value to clamp.
 *
 * @return value constrained to the inclusive range [0.0, 1.0].
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

/** @copydoc led_control_init */
esp_err_t led_control_init(void) {
  const ledc_timer_config_t timer_config = {
      .speed_mode = LED_CONTROL_MODE,
      .duty_resolution = LED_CONTROL_RESOLUTION,
      .timer_num = LED_CONTROL_TIMER,
      .freq_hz = LED_CONTROL_FREQUENCY_HZ,
      .clk_cfg = LEDC_AUTO_CLK,
  };

  esp_err_t error = ledc_timer_config(&timer_config);
  if (error != ESP_OK) {
    return error;
  }

  for (uint8_t oscillator_id = 0; oscillator_id < LED_CONTROL_OSCILLATOR_COUNT;
       oscillator_id++) {
    const ledc_channel_config_t channel_config = {
        .gpio_num = led_control_gpios[oscillator_id],
        .speed_mode = LED_CONTROL_MODE,
        .channel = (ledc_channel_t)oscillator_id,
        .timer_sel = LED_CONTROL_TIMER,
        .duty = 0,
        .hpoint = 0,
    };

    error = ledc_channel_config(&channel_config);
    if (error != ESP_OK) {
      return error;
    }
  }

  return ESP_OK;
}

/** @copydoc led_control_update */
esp_err_t led_control_update(uint8_t oscillator_id, float osc_value,
                             float current_brightness) {
  if (oscillator_id >= LED_CONTROL_OSCILLATOR_COUNT || !isfinite(osc_value) ||
      !isfinite(current_brightness)) {
    return ESP_ERR_INVALID_ARG;
  }

  const float final_brightness =
      clamp_unit(osc_value) * clamp_unit(current_brightness);
  const uint32_t duty =
      (uint32_t)(final_brightness * LED_CONTROL_MAX_DUTY + 0.5f);

  esp_err_t error =
      ledc_set_duty(LED_CONTROL_MODE, (ledc_channel_t)oscillator_id, duty);
  if (error != ESP_OK) {
    return error;
  }

  return ledc_update_duty(LED_CONTROL_MODE, (ledc_channel_t)oscillator_id);
}

/** @copydoc led_control_all_off */
esp_err_t led_control_all_off(void) {
  for (uint8_t oscillator_id = 0; oscillator_id < LED_CONTROL_OSCILLATOR_COUNT;
       oscillator_id++) {
    const esp_err_t error = led_control_update(oscillator_id, 0.0f, 0.0f);
    if (error != ESP_OK) {
      return error;
    }
  }

  return ESP_OK;
}
