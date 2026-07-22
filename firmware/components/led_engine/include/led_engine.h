/**
 * @file led_engine.h
 * @brief Per-oscillator LED control chain for the HypnoLight project.
 *
 * The `led_engine` component encapsulates the realtime signal chain for each
 * LED channel: a frequency modulator, a brightness modulator, and the
 * oscillator. It is driven by a 1 kHz tick that evaluates the modulators,
 * updates the oscillator, and writes the final duty cycle to `led_control`.
 *
 * In Player mode, the step engine calls the low-level modulator setters. In
 * Editor mode, realtime commands can use the high-level convenience wrappers.
 */

#pragma once

#include <stdint.h>

#include "esp_err.h"
#include "modulator.h"
#include "oscillator.h"

/**
 * @brief Initialize the LED engine and all underlying oscillators.
 *
 * This function calls `oscillator_init()` and resets all per-oscillator
 * frequency and brightness modulators to static zero.
 *
 * @return ESP_OK on success, or an error propagated from oscillator_init().
 */
esp_err_t led_engine_init(void);

/**
 * @brief Apply static waveform settings to one oscillator.
 *
 * This sets the oscillator waveform, duty cycle, and phase. It should be called
 * at step boundaries or when a realtime waveform parameter changes.
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 * @param[in] config Static oscillator configuration.
 *
 * @return ESP_OK on success, or an error from oscillator_set_static().
 */
esp_err_t led_engine_set_static(uint8_t oscillator_id,
                                const oscillator_static_config_t *config);

/**
 * @brief Set a constant oscillator frequency.
 *
 * Convenience wrapper that configures the frequency modulator in static mode.
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 * @param[in] frequency_hz Frequency in the range accepted by the oscillator.
 *
 * @return ESP_OK on success, or ESP_ERR_INVALID_ARG for invalid input.
 */
esp_err_t led_engine_set_frequency(uint8_t oscillator_id, float frequency_hz);

/**
 * @brief Set a constant oscillator brightness.
 *
 * Convenience wrapper that configures the brightness modulator in static mode.
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 * @param[in] brightness Normalized brightness in [0.0, 1.0].
 *
 * @return ESP_OK on success, or ESP_ERR_INVALID_ARG for invalid input.
 */
esp_err_t led_engine_set_brightness(uint8_t oscillator_id, float brightness);

/**
 * @brief Start a linear frequency ramp from an explicit start value.
 *
 * The ramp duration is expressed in milliseconds and must be non-zero.
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 * @param[in] start_value Starting frequency.
 * @param[in] end_value Target frequency.
 * @param[in] duration_ms Ramp duration in milliseconds.
 *
 * @return ESP_OK on success, or ESP_ERR_INVALID_ARG for invalid input.
 */
esp_err_t led_engine_linear_frequency(uint8_t oscillator_id, float start_value,
                                      float end_value, uint32_t duration_ms);

/**
 * @brief Start a linear brightness ramp from an explicit start value.
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 * @param[in] start_value Starting normalized brightness in [0.0, 1.0].
 * @param[in] end_value Target normalized brightness in [0.0, 1.0].
 * @param[in] duration_ms Ramp duration in milliseconds.
 *
 * @return ESP_OK on success, or ESP_ERR_INVALID_ARG for invalid input.
 */
esp_err_t led_engine_linear_brightness(uint8_t oscillator_id, float start_value,
                                       float end_value, uint32_t duration_ms);

/**
 * @brief Configure the frequency modulator for one oscillator.
 *
 * This low-level setter is intended for the step engine. It accepts any
 * valid modulator configuration (static, linear, or LFO).
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 * @param[in] config Modulator configuration to apply.
 *
 * @return ESP_OK on success, or an error for invalid input.
 */
esp_err_t led_engine_set_frequency_modulator(uint8_t oscillator_id,
                                             const modulator_config_t *config);

/**
 * @brief Configure the brightness modulator for one oscillator.
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 * @param[in] config Modulator configuration to apply.
 *
 * @return ESP_OK on success, or an error for invalid input.
 */
esp_err_t led_engine_set_brightness_modulator(uint8_t oscillator_id,
                                              const modulator_config_t *config);

/**
 * @brief Set a constant oscillator duty cycle.
 *
 * Convenience wrapper that configures the duty modulator in static mode.
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 * @param[in] duty_cycle Normalized duty cycle in [0.0, 1.0].
 *
 * @return ESP_OK on success, or ESP_ERR_INVALID_ARG for invalid input.
 */
esp_err_t led_engine_set_duty_cycle(uint8_t oscillator_id, float duty_cycle);

/**
 * @brief Start a linear duty-cycle ramp from an explicit start value.
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 * @param[in] start_value Starting normalized duty cycle in [0.0, 1.0].
 * @param[in] end_value Target normalized duty cycle in [0.0, 1.0].
 * @param[in] duration_ms Ramp duration in milliseconds.
 *
 * @return ESP_OK on success, or ESP_ERR_INVALID_ARG for invalid input.
 */
esp_err_t led_engine_linear_duty_cycle(uint8_t oscillator_id, float start_value,
                                       float end_value, uint32_t duration_ms);

/**
 * @brief Configure the duty cycle modulator for one oscillator.
 *
 * This low-level setter is intended for the step engine. It accepts any
 * valid modulator configuration (static, linear, or LFO).
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 * @param[in] config Modulator configuration to apply.
 *
 * @return ESP_OK on success, or an error for invalid input.
 */
esp_err_t led_engine_set_duty_cycle_modulator(uint8_t oscillator_id,
                                              const modulator_config_t *config);

/**
 * @brief Freeze all linear modulators for one oscillator.
 *
 * Static and LFO modulators continue unchanged. This is intended for pausing
 * sequence playback while keeping LEDs at their current state.
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 *
 * @return ESP_OK on success, or ESP_ERR_INVALID_ARG for an invalid ID.
 */
esp_err_t led_engine_pause_modulators(uint8_t oscillator_id);

/**
 * @brief Resume previously paused linear modulators for one oscillator.
 *
 * Linear ramps continue from the paused value with the remaining duration.
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 *
 * @return ESP_OK on success, or ESP_ERR_INVALID_ARG for an invalid ID.
 */
esp_err_t led_engine_resume_modulators(uint8_t oscillator_id);

/**
 * @brief Seek all modulators for one oscillator to a position in milliseconds.
 *
 * Updates frequency, brightness, and duty modulators so that subsequent
 * evaluations continue from the requested offset inside the current step.
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 * @param[in] elapsed_ms Offset inside the current modulator configuration, in
 * milliseconds.
 *
 * @return ESP_OK on success, or an error for invalid input.
 */
esp_err_t led_engine_seek_modulators(uint8_t oscillator_id,
                                     uint32_t elapsed_ms);

/**
 * @brief Evaluate all modulators and update the LED outputs.
 *
 * Call this function from the 1 kHz timer callback. It evaluates the
 * frequency, brightness, and duty modulators for every oscillator, applies
 * the resulting frequency and duty to the oscillator, advances the waveform,
 * and writes each channel through `led_control_update()`.
 *
 * @return ESP_OK on success, or an error propagated from a called component.
 */
esp_err_t led_engine_tick(void);

/**
 * @brief Turn all LEDs off by writing zero duty cycle to every channel.
 *
 * @return ESP_OK on success, or an error from led_control_all_off().
 */
esp_err_t led_engine_all_off(void);
