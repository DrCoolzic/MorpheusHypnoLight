/**
 * @file led_control.h
 * @brief Fixed LEDC output control for the Morpheus HypnoLight LED groups.
 *
 * This module maps five oscillator IDs to the fixed PWM outputs PB1 through
 * PB4 and CG. Callers provide normalized waveform and brightness values; the
 * module combines them and writes the resulting duty cycle to LEDC.
 */
#pragma once

#include <stdint.h>

#include "esp_err.h"

/** @brief Number of peripheral LED banks controlled by PB1 through PB4. */
#define LED_CONTROL_PERIPHERAL_BANK_COUNT 4

/** @brief Total number of LED groups and fixed LEDC output channels. */
#define LED_CONTROL_OSCILLATOR_COUNT 5

/** @brief Oscillator ID assigned to the central group (CG). */
#define LED_CONTROL_CG_OSCILLATOR_ID 4

/**
 * @brief Configure the LEDC timer and all five fixed LED output channels.
 *
 * The function initializes each channel with a zero duty cycle. It must be
 * called before any call to led_control_update() or led_control_all_off().
 *
 * @return ESP_OK on success; otherwise, an error returned by the LEDC driver.
 */
esp_err_t led_control_init(void);

/**
 * @brief Update one LED group's duty cycle from normalized signal values.
 *
 * The output duty is calculated as osc_value multiplied by current_brightness.
 * Finite values outside the normalized range are clamped to [0.0, 1.0]. This
 * function uses standard LEDC driver APIs and must not be called from an ISR.
 *
 * @param[in] oscillator_id Fixed channel ID in the range 0 to 4.
 * @param[in] osc_value Instantaneous waveform value.
 * @param[in] current_brightness Current brightness factor after any global
 *                                brightness scaling.
 *
 * @return ESP_OK on success, ESP_ERR_INVALID_ARG for an invalid ID or a
 *         non-finite input value, or an error returned by the LEDC driver.
 */
esp_err_t led_control_update(uint8_t oscillator_id, float osc_value,
                             float current_brightness);

/**
 * @brief Turn off all five LED groups.
 *
 * This function writes a zero duty cycle to every fixed LEDC channel.
 *
 * @return ESP_OK on success; otherwise, the first error returned by the LEDC
 *         driver.
 */
esp_err_t led_control_all_off(void);
