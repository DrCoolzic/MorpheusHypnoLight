/**
 * @file ble.c
 * @brief NimBLE peripheral GATT server for HypnoLight sequence transfer.
 */

#include "ble.h"

#include <stdbool.h>
#include <stdint.h>
#include <string.h>

#include "esp_err.h"
#include "esp_log.h"
#include "host/ble_gap.h"
#include "host/ble_gatt.h"
#include "host/ble_hs.h"
#include "host/ble_hs_mbuf.h"
#include "host/ble_uuid.h"
#include "led_control.h"
#include "led_engine.h"
#include "nimble/nimble_port.h"
#include "nimble/nimble_port_freertos.h"
#include "nvs_flash.h"
#include "os/os_mbuf.h"
#include "sequence.h"
#include "services/gap/ble_svc_gap.h"
#include "services/gatt/ble_svc_gatt.h"

/** @brief Log tag for the BLE component. */
#define MHL_TAG "mhl_ble"

/** @brief 128-bit service and characteristic UUID base. */
#define MHL_SVC_UUID128                                                        \
  0x00, 0x00, 0xa6, 0xc2, 0xb5, 0xa1, 0x15, 0x8f, 0x02, 0xaf, 0x25, 0x4f,      \
      0xc0, 0x8b, 0xc3, 0xd4
#define MHL_CMD_UUID128                                                        \
  0x01, 0x00, 0xa6, 0xc2, 0xb5, 0xa1, 0x15, 0x8f, 0x02, 0xaf, 0x25, 0x4f,      \
      0xc0, 0x8b, 0xc3, 0xd4
#define MHL_STATUS_UUID128                                                     \
  0x02, 0x00, 0xa6, 0xc2, 0xb5, 0xa1, 0x15, 0x8f, 0x02, 0xaf, 0x25, 0x4f,      \
      0xc0, 0x8b, 0xc3, 0xd4

/** @brief Maximum GATT write length (ATT maximum payload). */
#define MHL_BLE_MAX_WRITE_LEN 517U

/** @brief Buffer size for a complete compact sequence transfer. */
#define MHL_BLE_TRANSFER_BUFFER_SIZE (14U + 112U * SEQUENCE_MAX_STEPS)

/** @brief Buffer size for a single-step compact update (header + one step). */
#define MHL_BLE_UPDATE_BUFFER_SIZE (14U + 112U)

static const ble_uuid128_t mhl_service_uuid = BLE_UUID128_INIT(MHL_SVC_UUID128);
static const ble_uuid128_t mhl_command_uuid = BLE_UUID128_INIT(MHL_CMD_UUID128);
static const ble_uuid128_t mhl_status_uuid =
    BLE_UUID128_INIT(MHL_STATUS_UUID128);

static uint16_t status_attr_handle;
static uint16_t command_attr_handle;

static uint8_t command_rx_buffer[MHL_BLE_MAX_WRITE_LEN];
static uint8_t transfer_buffer[MHL_BLE_TRANSFER_BUFFER_SIZE];
static uint8_t update_buffer[MHL_BLE_UPDATE_BUFFER_SIZE];

static uint32_t transfer_expected = 0U;
static uint8_t update_step_index = 0U;
static uint16_t update_expected = 0U;
static bool update_in_progress = false;

static uint8_t last_status[2] = {0x00U, 0x00U};

/**
 * @brief Decode a little-endian uint16_t from the given buffer.
 */
static uint16_t read_u16_le(const uint8_t *data) {
  return (uint16_t)data[0] | ((uint16_t)data[1] << 8U);
}

/**
 * @brief Decode a little-endian uint32_t from the given buffer.
 */
static uint32_t read_u32_le(const uint8_t *data) {
  return (uint32_t)data[0] | ((uint32_t)data[1] << 8U) |
         ((uint32_t)data[2] << 16U) | ((uint32_t)data[3] << 24U);
}

/**
 * @brief Send a 2-byte status notification to the connected central.
 *
 * @param[in] op Opcode echoed in the notification.
 * @param[in] code Result code for that opcode.
 */
static void mhl_send_status(uint16_t conn, uint8_t op, uint8_t code) {
  if (conn == BLE_HS_CONN_HANDLE_NONE) {
    ESP_LOGI(MHL_TAG, "status dropped, no conn");
    return;
  }
  last_status[0] = op;
  last_status[1] = code;
  struct os_mbuf *om = ble_hs_mbuf_from_flat(last_status, sizeof(last_status));
  if (om == NULL) {
    ESP_LOGI(MHL_TAG, "status mbuf alloc failed");
    return;
  }
  const int rc = ble_gatts_notify_custom(conn, status_attr_handle, om);
  if (rc != 0) {
    os_mbuf_free_chain(om);
  }
  ESP_LOGI(MHL_TAG, "notify op=0x%02x code=0x%02x rc=%d", op, code, rc);
}

/**
 * @brief Map an ESP-IDF error to a BLE status result byte.
 *
 * @param[in] err Error returned by a sequence or control call.
 * @param[in] validation_code Code used for validation/argument failures.
 * @param[in] apply_code Code used for load/apply failures.
 * @return Result byte to send in the status notification.
 */
static uint8_t mhl_map_esp_err(esp_err_t err, uint8_t validation_code,
                               uint8_t apply_code) {
  if (err == ESP_OK) {
    return 0x00U;
  }
  if (err == ESP_ERR_INVALID_ARG) {
    return validation_code;
  }
  return apply_code;
}

/**
 * @brief Stop playback, seek to zero, and turn all LEDs off.
 */
static void mhl_stop_playback(void) { (void)sequence_stop(); }

/**
 * @brief Process a validated command payload.
 *
 * @param[in] conn Connection handle for status replies.
 * @param[in] data Command payload.
 * @param[in] len Command payload length.
 */
static void mhl_process_command(uint16_t conn, const uint8_t *data,
                                uint16_t len) {
  const uint8_t op = data[0];
  uint8_t result = 0x00U;

  switch (op) {
  case 0x01U: /* PLAY */
    result = mhl_map_esp_err(sequence_play(), 0xFFU, 0xFFU);
    break;

  case 0x02U: /* PAUSE */
    result = mhl_map_esp_err(sequence_pause(), 0xFFU, 0xFFU);
    break;

  case 0x03U: /* STOP */
    mhl_stop_playback();
    result = 0x00U;
    break;

  case 0x04U: /* SEEK */
    if (len < 5U) {
      result = 0x02U;
    } else {
      const uint32_t position_ms = read_u32_le(&data[1]);
      result = mhl_map_esp_err(sequence_seek(position_ms), 0x02U, 0xFFU);
    }
    break;

  case 0x05U: /* BRIGHTNESS */
    if (len < 2U || data[1] > 100U) {
      result = 0x02U;
    } else {
      const float brightness = (float)data[1] / 100.0f;
      result = mhl_map_esp_err(led_control_set_global_brightness(brightness),
                               0x02U, 0xFFU);
    }
    break;

  case 0x10U: /* LOAD_START */
    if (len < 5U) {
      result = 0x02U;
    } else {
      const uint32_t size = read_u32_le(&data[1]);
      if (size > MHL_BLE_TRANSFER_BUFFER_SIZE) {
        result = 0x03U;
      } else {
        transfer_expected = size;
        update_in_progress = false;
        result = 0x00U;
      }
    }
    break;

  case 0x11U: { /* LOAD_CHUNK */
    if (len < 3U) {
      result = 0x02U;
      break;
    }
    const uint16_t offset = read_u16_le(&data[1]);
    const uint16_t payload_len = len - 3U;
    if ((uint32_t)offset + payload_len > transfer_expected) {
      result = 0x03U;
    } else {
      memcpy(&transfer_buffer[offset], &data[3], payload_len);
      result = 0x00U;
    }
    break;
  }

  case 0x12U: /* LOAD_COMMIT */
    if (transfer_expected == 0U) {
      result = 0x04U;
    } else {
      const esp_err_t err =
          sequence_load_compact(transfer_buffer, transfer_expected);
      result = mhl_map_esp_err(err, 0x04U, 0x05U);
    }
    break;

  case 0x20U: /* UPDATE_STEP_START */
    if (len < 4U) {
      result = 0x02U;
    } else {
      const uint16_t size = read_u16_le(&data[2]);
      if (size > MHL_BLE_UPDATE_BUFFER_SIZE) {
        result = 0x03U;
      } else {
        update_step_index = data[1];
        update_expected = size;
        update_in_progress = true;
        result = 0x00U;
      }
    }
    break;

  case 0x21U: { /* UPDATE_STEP_CHUNK */
    if (len < 3U) {
      result = 0x02U;
      break;
    }
    const uint16_t offset = read_u16_le(&data[1]);
    const uint16_t payload_len = len - 3U;
    if (!update_in_progress ||
        (uint32_t)offset + payload_len > update_expected) {
      result = 0x03U;
    } else {
      memcpy(&update_buffer[offset], &data[3], payload_len);
      result = 0x00U;
    }
    break;
  }

  case 0x22U: { /* UPDATE_STEP_COMMIT */
    if (!update_in_progress) {
      result = 0x02U;
      break;
    }
    sequence_step_t temp;
    uint32_t count = 0U;
    esp_err_t err = sequence_decode_compact(update_buffer, update_expected,
                                            &temp, 1U, &count);
    if (err != ESP_OK || count != 1U) {
      result = 0x04U;
    } else {
      err = sequence_replace_step(update_step_index, &temp);
      result = mhl_map_esp_err(err, 0x04U, 0x05U);
    }
    update_in_progress = false;
    break;
  }

  default:
    result = 0x01U;
    break;
  }

  mhl_send_status(conn, op, result);
}

/**
 * @brief GATT access callback for the command and status characteristics.
 */
static int mhl_access_cb(uint16_t conn, uint16_t attr_handle,
                         struct ble_gatt_access_ctxt *ctxt, void *arg) {
  (void)arg;

  if (attr_handle == command_attr_handle) {
    if (ctxt->op != BLE_GATT_ACCESS_OP_WRITE_CHR) {
      return BLE_ATT_ERR_WRITE_NOT_PERMITTED;
    }

    const uint16_t len = OS_MBUF_PKTLEN(ctxt->om);
    ESP_LOGI(MHL_TAG, "command write conn=%u attr=%u len=%u", conn, attr_handle,
             len);
    if (len == 0U || len > MHL_BLE_MAX_WRITE_LEN) {
      mhl_send_status(conn, 0x00U, 0x02U);
      return BLE_ATT_ERR_INVALID_ATTR_VALUE_LEN;
    }

    const int copy_rc = os_mbuf_copydata(ctxt->om, 0, len, command_rx_buffer);
    if (copy_rc != 0) {
      ESP_LOGE(MHL_TAG, "copydata failed: %d", copy_rc);
      return BLE_ATT_ERR_UNLIKELY;
    }

    mhl_process_command(conn, command_rx_buffer, len);
    return 0;
  }

  if (attr_handle == status_attr_handle) {
    if (ctxt->op != BLE_GATT_ACCESS_OP_READ_CHR) {
      return BLE_ATT_ERR_READ_NOT_PERMITTED;
    }
    if (os_mbuf_append(ctxt->om, last_status, sizeof(last_status)) != 0) {
      return BLE_ATT_ERR_INSUFFICIENT_RES;
    }
    return 0;
  }

  return BLE_ATT_ERR_UNLIKELY;
}

static const struct ble_gatt_svc_def mhl_services[] = {
    {
        .type = BLE_GATT_SVC_TYPE_PRIMARY,
        .uuid = &mhl_service_uuid.u,
        .characteristics =
            (struct ble_gatt_chr_def[]){
                {
                    .uuid = &mhl_command_uuid.u,
                    .access_cb = mhl_access_cb,
                    .flags = BLE_GATT_CHR_F_WRITE | BLE_GATT_CHR_F_WRITE_NO_RSP,
                    .val_handle = &command_attr_handle,
                },
                {
                    .uuid = &mhl_status_uuid.u,
                    .access_cb = mhl_access_cb,
                    .flags = BLE_GATT_CHR_F_READ | BLE_GATT_CHR_F_NOTIFY,
                    .val_handle = &status_attr_handle,
                },
                {0},
            },
    },
    {0},
};

/**
 * @brief Register the Morpheus GATT services with the NimBLE host.
 */
static int mhl_gatt_svr_init(void) {
  int rc = ble_gatts_count_cfg(mhl_services);
  if (rc != 0) {
    ESP_LOGE(MHL_TAG, "ble_gatts_count_cfg failed %d", rc);
    return rc;
  }
  rc = ble_gatts_add_svcs(mhl_services);
  if (rc != 0) {
    ESP_LOGE(MHL_TAG, "ble_gatts_add_svcs failed %d", rc);
    return rc;
  }
  ESP_LOGI(MHL_TAG, "Morpheus GATT services registered");
  return 0;
}

/**
 * @brief GAP event handler for advertising and connection events.
 */
static int mhl_gap_event(struct ble_gap_event *event, void *arg);

/**
 * @brief Start connectable advertising after host sync.
 */
static void mhl_start_advertise(void) {
  struct ble_hs_adv_fields fields = {0};
  const char *name = "HypnoLight";
  fields.name = (uint8_t *)name;
  fields.name_len = (uint8_t)strlen(name);
  fields.name_is_complete = 1;

  int rc = ble_gap_adv_set_fields(&fields);
  if (rc != 0) {
    ESP_LOGE(MHL_TAG, "adv set fields failed %d", rc);
    return;
  }

  struct ble_gap_adv_params adv_params = {0};
  adv_params.conn_mode = BLE_GAP_CONN_MODE_UND;
  adv_params.disc_mode = BLE_GAP_DISC_MODE_GEN;
  adv_params.itvl_min = 0x20; /* 20 ms */
  adv_params.itvl_max = 0x80; /* 80 ms */

  rc = ble_gap_adv_start(BLE_OWN_ADDR_PUBLIC, NULL, BLE_HS_FOREVER, &adv_params,
                         mhl_gap_event, NULL);
  if (rc != 0) {
    ESP_LOGE(MHL_TAG, "adv start failed %d", rc);
  }
}

/**
 * @brief Restart advertising after disconnect or failed connect.
 */
static int mhl_gap_event(struct ble_gap_event *event, void *arg) {
  (void)arg;

  switch (event->type) {
  case BLE_GAP_EVENT_CONNECT:
    if (event->connect.status != 0) {
      ESP_LOGE(MHL_TAG, "connection failed; restarting advertising");
      mhl_start_advertise();
    }
    break;

  case BLE_GAP_EVENT_DISCONNECT:
    ESP_LOGI(MHL_TAG, "disconnect; restarting advertising");
    mhl_start_advertise();
    break;

  default:
    break;
  }
  return 0;
}

/**
 * @brief NimBLE host reset callback.
 */
static void mhl_on_reset(int reason) {
  ESP_LOGI(MHL_TAG, "reset reason %d", reason);
}

/**
 * @brief NimBLE host sync callback: start advertising.
 */
static void mhl_on_sync(void) { mhl_start_advertise(); }

/**
 * @brief NimBLE host task entry point.
 */
static void mhl_host_task(void *param) {
  (void)param;
  nimble_port_run();
  nimble_port_freertos_deinit();
}

esp_err_t ble_init(void) {
  esp_err_t ret = nvs_flash_init();
  if (ret == ESP_ERR_NVS_NO_FREE_PAGES ||
      ret == ESP_ERR_NVS_NEW_VERSION_FOUND) {
    ESP_ERROR_CHECK(nvs_flash_erase());
    ret = nvs_flash_init();
  }
  ESP_ERROR_CHECK(ret);

  ret = nimble_port_init();
  if (ret != ESP_OK) {
    ESP_LOGE(MHL_TAG, "nimble_port_init failed %d", ret);
    return ret;
  }

  ble_hs_cfg.reset_cb = mhl_on_reset;
  ble_hs_cfg.sync_cb = mhl_on_sync;

  ble_svc_gap_init();
  ble_svc_gatt_init();
  int gatt_rc = mhl_gatt_svr_init();
  if (gatt_rc != 0) {
    ESP_LOGE(MHL_TAG, "GATT server registration failed");
    return ESP_FAIL;
  }

  const int rc = ble_svc_gap_device_name_set("HypnoLight");
  if (rc != 0) {
    ESP_LOGE(MHL_TAG, "device name set failed %d", rc);
    return ESP_FAIL;
  }

  nimble_port_freertos_init(mhl_host_task);
  return ESP_OK;
}
