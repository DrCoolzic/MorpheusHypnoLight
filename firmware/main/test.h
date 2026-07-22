/**
 * @file test.h
 * @brief Hardware tests for the Morpheus HypnoLight prototype.
 */

#pragma once

#include "esp_timer.h"

/**
 * @brief Run the static LED bank test.
 *
 * Lights up each peripheral bank and the central group in sequence without
 * using the oscillator engine.
 */
void test_run_static_led_test(void);

/**
 * @brief Run the oscillator/visual hardware tests.
 *
 * Exercises waveforms, duty cycles, phases, and linear ramps on the LED
 * hardware using the provided 1 kHz oscillator timer.
 *
 * @param[in] oscillator_timer Periodic esp_timer that calls led_engine_tick().
 */
void test_run_oscillator_visual_test(esp_timer_handle_t oscillator_timer);

/**
 * @brief Run all stored hardware tests.
 *
 * @param[in] oscillator_timer Periodic esp_timer that calls led_engine_tick().
 */
void test_run_all(esp_timer_handle_t oscillator_timer);
