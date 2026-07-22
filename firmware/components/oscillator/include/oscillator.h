/**
 * @file oscillator.h
 * @brief Waveform generation using LUT or direct phase evaluation for
 * HypnoLight LED groups.
 *
 * The oscillator module generates five independent normalized waveform values.
 * It has no dependency on LEDC or brightness control.
 */
#pragma once

#include <stdint.h>

#include "esp_err.h"

/** @brief Number of independent waveform generators. */
#define OSCILLATOR_COUNT 5

/** @brief Number of samples in every internal waveform LUT. */
#define OSCILLATOR_LUT_SIZE 64

/** @brief Nominal rate at which oscillator_tick() is called. */
#define OSCILLATOR_TICK_HZ 1000.0f

/** @brief Maximum supported waveform frequency in hertz. */
#define OSCILLATOR_MAX_FREQUENCY_HZ 100.0f

/** @brief Supported waveform shapes. */
typedef enum {
  OSCILLATOR_WAVEFORM_SINE,
  OSCILLATOR_WAVEFORM_SQUARE,
  OSCILLATOR_WAVEFORM_TRIANGLE,
  OSCILLATOR_WAVEFORM_CUSTOM,
} oscillator_waveform_t;

/**
 * @brief Static waveform settings applied at a sequence-step boundary.
 *
 * phase_degrees defines the initial LUT position. custom_lut must point to
 * OSCILLATOR_LUT_SIZE normalized samples when waveform is
 * OSCILLATOR_WAVEFORM_CUSTOM. duty_cycle is a dynamic parameter and is set
 * separately through oscillator_set_duty_cycle().
 */
typedef struct {
  oscillator_waveform_t waveform;
  float phase_degrees;
  const float *custom_lut;
} oscillator_static_config_t;

/**
 * @brief Initialize all oscillators with a zero frequency and sine LUT.
 *
 * Each initialized oscillator outputs 1.0 because zero frequency represents a
 * fixed brightness output.
 *
 * @return ESP_OK on success.
 */
esp_err_t oscillator_init(void);

/**
 * @brief Apply static waveform settings to an oscillator.
 *
 * This builds the LUT for sine/custom waveforms and stores parameters for
 * square/triangle, then resets the DDS phase to phase_degrees. Call it at a
 * step boundary, not concurrently with oscillator_tick().
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 * @param[in] config Static waveform configuration.
 *
 * @return ESP_OK on success or ESP_ERR_INVALID_ARG for invalid input.
 */
esp_err_t oscillator_set_static(uint8_t oscillator_id,
                                const oscillator_static_config_t *config);

/**
 * @brief Set the dynamic frequency of an oscillator.
 *
 * A frequency of 0 Hz forces oscillator_tick() to return 1.0 for the selected
 * oscillator regardless of its waveform or phase.
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 * @param[in] frequency_hz Frequency in the inclusive range 0 to 100 Hz.
 *
 * @return ESP_OK on success or ESP_ERR_INVALID_ARG for invalid input.
 */
esp_err_t oscillator_set_frequency(uint8_t oscillator_id, float frequency_hz);

/**
 * @brief Set the dynamic duty cycle for square and triangle waveforms.
 *
 * This value is normalized to [0.0, 1.0]. It has no effect on sine or custom
 * LUT waveforms. The duty cycle can be changed at runtime without rebuilding a
 * LUT.
 *
 * @param[in] oscillator_id Oscillator ID in the range 0 to 4.
 * @param[in] duty_cycle Duty cycle in the inclusive range [0.0, 1.0].
 *
 * @return ESP_OK on success or ESP_ERR_INVALID_ARG for invalid input.
 */
esp_err_t oscillator_set_duty_cycle(uint8_t oscillator_id, float duty_cycle);

/**
 * @brief Advance DDS state by one nominal tick and return all waveform values.
 *
 * This function is intended for a periodic task callback at OSCILLATOR_TICK_HZ.
 * It does not update LED hardware and must not run concurrently with
 * oscillator_set_static().
 *
 * @param[out] osc_values Array with OSCILLATOR_COUNT normalized output values.
 *
 * @return ESP_OK on success or ESP_ERR_INVALID_ARG when osc_values is NULL.
 */
esp_err_t oscillator_tick(float osc_values[OSCILLATOR_COUNT]);
