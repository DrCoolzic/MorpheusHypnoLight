/**
 * @file ble.h
 * @brief BLE GATT interface for Morpheus HypnoLight.
 */

#pragma once

#include "esp_err.h"

/**
 * @brief Initialize the NimBLE peripheral and start advertising.
 *
 * This function starts the NimBLE host task, registers the Morpheus GATT
 * service, and begins connectable advertising under the name "HypnoLight".
 * It must be called after the sequence engine is initialized.
 *
 * @return ESP_OK on success.
 */
esp_err_t ble_init(void);
