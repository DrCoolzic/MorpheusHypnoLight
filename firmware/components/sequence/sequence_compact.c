#include "sequence.h"

#include <math.h>
#include <stddef.h>
#include <stdint.h>
#include <string.h>

#define COMPACT_HEADER_SIZE 14U
#define COMPACT_PHASE_CODE_COUNT 63U
#define COMPACT_MAGIC_0 ((uint8_t)'M')
#define COMPACT_MAGIC_1 ((uint8_t)'H')
#define COMPACT_MAGIC_2 ((uint8_t)'L')
#define COMPACT_MAGIC_3 ((uint8_t)'S')
#define COMPACT_VERSION_MAJOR 1U
#define COMPACT_VERSION_MINOR 0U
#define COMPACT_VERSION_PATCH 0U

typedef struct {
  const uint8_t *data;
  size_t length;
  size_t offset;
} compact_reader_t;

typedef enum {
  COMPACT_TARGET_FREQUENCY,
  COMPACT_TARGET_NORMALIZED,
} compact_target_t;

static bool read_u8(compact_reader_t *reader, uint8_t *value) {
  if (reader == NULL || value == NULL || reader->offset >= reader->length) {
    return false;
  }
  *value = reader->data[reader->offset];
  reader->offset++;
  return true;
}

static bool read_u16(compact_reader_t *reader, uint16_t *value) {
  if (reader == NULL || value == NULL || reader->offset > reader->length ||
      reader->length - reader->offset < 2U) {
    return false;
  }
  *value = (uint16_t)reader->data[reader->offset] |
           ((uint16_t)reader->data[reader->offset + 1U] << 8U);
  reader->offset += 2U;
  return true;
}

static uint32_t read_u32_at(const uint8_t *data) {
  return (uint32_t)data[0] | ((uint32_t)data[1] << 8U) |
         ((uint32_t)data[2] << 16U) | ((uint32_t)data[3] << 24U);
}

static uint16_t read_u16_at(const uint8_t *data) {
  return (uint16_t)data[0] | ((uint16_t)data[1] << 8U);
}

static uint32_t compact_crc32(const uint8_t *data, size_t length) {
  uint32_t crc = UINT32_MAX;
  for (size_t i = 0U; i < length; i++) {
    crc ^= data[i];
    for (uint8_t bit = 0U; bit < 8U; bit++) {
      const uint32_t mask = 0U - (crc & 1U);
      crc = (crc >> 1U) ^ (0xEDB88320U & mask);
    }
  }
  return crc ^ UINT32_MAX;
}

static bool decode_main_waveform(uint8_t code,
                                 oscillator_waveform_t *waveform) {
  if (waveform == NULL) {
    return false;
  }
  switch (code) {
  case 0U:
    *waveform = OSCILLATOR_WAVEFORM_SQUARE;
    return true;
  case 1U:
    *waveform = OSCILLATOR_WAVEFORM_SINE;
    return true;
  case 2U:
    *waveform = OSCILLATOR_WAVEFORM_TRIANGLE;
    return true;
  default:
    return false;
  }
}

static bool decode_lfo_waveform(uint8_t code,
                                modulator_lfo_waveform_t *waveform) {
  if (waveform == NULL) {
    return false;
  }
  switch (code) {
  case 0U:
    *waveform = MODULATOR_LFO_WAVEFORM_SINE;
    return true;
  case 1U:
    *waveform = MODULATOR_LFO_WAVEFORM_SQUARE;
    return true;
  default:
    return false;
  }
}

static bool decode_target_value(compact_reader_t *reader,
                                compact_target_t target, float *value) {
  if (target == COMPACT_TARGET_FREQUENCY) {
    uint16_t code = 0U;
    if (!read_u16(reader, &code) || code > 1000U) {
      return false;
    }
    *value = (float)code / 10.0f;
    return true;
  }

  uint8_t code = 0U;
  if (!read_u8(reader, &code) || code > 100U) {
    return false;
  }
  *value = (float)code / 100.0f;
  return true;
}

static bool decode_modulator(compact_reader_t *reader, compact_target_t target,
                             uint32_t duration_ms,
                             modulator_config_t *config) {
  uint8_t mode = 0U;
  if (config == NULL || !read_u8(reader, &mode)) {
    return false;
  }
  memset(config, 0, sizeof(*config));

  switch (mode) {
  case 0U:
    config->mode = MODULATOR_MODE_STATIC;
    return decode_target_value(reader, target, &config->static_config.value);

  case 1U:
    config->mode = MODULATOR_MODE_LINEAR;
    config->linear_config.duration_ms = duration_ms;
    return decode_target_value(reader, target,
                               &config->linear_config.start_value) &&
           decode_target_value(reader, target,
                               &config->linear_config.end_value);

  case 2U: {
    uint8_t waveform_code = 0U;
    uint16_t frequency_code = 0U;
    config->mode = MODULATOR_MODE_LFO;
    if (!read_u8(reader, &waveform_code) ||
        !decode_lfo_waveform(waveform_code,
                             &config->lfo_config.waveform) ||
        !read_u16(reader, &frequency_code) || frequency_code == 0U) {
      return false;
    }
    config->lfo_config.frequency_hz = (float)frequency_code / 10.0f;
    if (!decode_target_value(reader, target, &config->lfo_config.low) ||
        !decode_target_value(reader, target, &config->lfo_config.high)) {
      return false;
    }
    return config->lfo_config.low <= config->lfo_config.high;
  }

  default:
    return false;
  }
}

static bool decode_oscillator(compact_reader_t *reader, uint32_t duration_ms,
                              sequence_oscillator_step_t *oscillator) {
  uint8_t waveform_code = 0U;
  uint8_t phase_code = 0U;
  if (oscillator == NULL || !read_u8(reader, &waveform_code) ||
      !decode_main_waveform(waveform_code,
                            &oscillator->static_config.waveform) ||
      !read_u8(reader, &phase_code) || phase_code >= COMPACT_PHASE_CODE_COUNT) {
    return false;
  }

  oscillator->static_config.phase_degrees =
      ((float)phase_code / 10.0f) * (180.0f / (float)M_PI);
  oscillator->static_config.custom_lut = NULL;

  return decode_modulator(reader, COMPACT_TARGET_FREQUENCY, duration_ms,
                          &oscillator->frequency_modulator) &&
         decode_modulator(reader, COMPACT_TARGET_NORMALIZED, duration_ms,
                          &oscillator->brightness_modulator) &&
         decode_modulator(reader, COMPACT_TARGET_NORMALIZED, duration_ms,
                          &oscillator->duty_modulator);
}

static bool decode_step(compact_reader_t *reader, sequence_step_t *step) {
  uint16_t duration_code = 0U;
  if (step == NULL || !read_u16(reader, &duration_code) ||
      duration_code == 0U) {
    return false;
  }

  memset(step, 0, sizeof(*step));
  step->duration_ms = (uint32_t)duration_code * 100U;
  for (uint8_t oscillator = 0U; oscillator < OSCILLATOR_COUNT; oscillator++) {
    if (!decode_oscillator(reader, step->duration_ms,
                           &step->oscillators[oscillator])) {
      return false;
    }
  }
  return true;
}

static bool decode_payload(const uint8_t *payload, size_t payload_length,
                           uint32_t step_count, sequence_step_t *steps) {
  compact_reader_t reader = {
      .data = payload,
      .length = payload_length,
      .offset = 0U,
  };

  for (uint32_t step_index = 0U; step_index < step_count; step_index++) {
    sequence_step_t scratch;
    sequence_step_t *destination =
        (steps == NULL) ? &scratch : &steps[step_index];
    if (!decode_step(&reader, destination)) {
      return false;
    }
  }
  return reader.offset == reader.length;
}

esp_err_t sequence_decode_compact(const uint8_t *data, size_t data_length,
                                  sequence_step_t *steps,
                                  uint32_t steps_capacity,
                                  uint32_t *step_count) {
  if (data == NULL || step_count == NULL || data_length < COMPACT_HEADER_SIZE) {
    return ESP_ERR_INVALID_ARG;
  }
  if (data[0] != COMPACT_MAGIC_0 || data[1] != COMPACT_MAGIC_1 ||
      data[2] != COMPACT_MAGIC_2 || data[3] != COMPACT_MAGIC_3 ||
      data[4] != COMPACT_VERSION_MAJOR || data[5] != COMPACT_VERSION_MINOR ||
      data[6] != COMPACT_VERSION_PATCH) {
    return ESP_ERR_INVALID_ARG;
  }

  const uint32_t encoded_step_count = data[7];
  const uint16_t payload_length = read_u16_at(&data[8]);
  const uint32_t expected_crc = read_u32_at(&data[10]);
  if (encoded_step_count == 0U || encoded_step_count > SEQUENCE_MAX_STEPS ||
      payload_length != data_length - COMPACT_HEADER_SIZE ||
      compact_crc32(&data[COMPACT_HEADER_SIZE], payload_length) != expected_crc ||
      (steps != NULL && steps_capacity < encoded_step_count)) {
    return ESP_ERR_INVALID_ARG;
  }

  const uint8_t *payload = &data[COMPACT_HEADER_SIZE];
  if (!decode_payload(payload, payload_length, encoded_step_count, NULL)) {
    return ESP_ERR_INVALID_ARG;
  }
  if (steps != NULL &&
      !decode_payload(payload, payload_length, encoded_step_count, steps)) {
    return ESP_ERR_INVALID_STATE;
  }

  *step_count = encoded_step_count;
  return ESP_OK;
}
