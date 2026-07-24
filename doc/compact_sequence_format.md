# Morpheus HypnoLight Compact Sequence Format

## Purpose

This document defines compact sequence format version 1.0.0. The format is generated from the application JSON format and is consumed by the firmware sequence decoder. The same bytes are used for embedded tests, binary files, and BLE transfer.

All fields are packed without alignment or padding. Every multi-byte integer uses little-endian byte order.

## Sequence Layout

A complete compact sequence consists of one header followed by `step_count` consecutive step payloads.

```text
sequence = header + step[0] + ... + step[step_count - 1]
```

### Header

| Offset | Size | Field | Value |
| --- | --- | --- | --- |
| 0 | 4 | `magic` | ASCII `MHLS` (`4D 48 4C 53`) |
| 4 | 1 | `version_major` | `1` |
| 5 | 1 | `version_minor` | `0` |
| 6 | 1 | `version_patch` | `0` |
| 7 | 1 | `step_count` | 1 to 128 |
| 8 | 2 | `payload_length` | Number of bytes after the header, `uint16_t` little-endian |
| 10 | 4 | `payload_crc32` | CRC-32/ISO-HDLC of all bytes after the header, `uint32_t` little-endian |

The header size is 14 bytes. `payload_length` excludes the header. A decoder must reject unsupported versions, invalid step counts, length mismatches, trailing data, and CRC mismatches before applying the sequence.

CRC-32/ISO-HDLC uses polynomial `0x04C11DB7`, reflected input and output, initial value `0xFFFFFFFF`, and final XOR `0xFFFFFFFF`. This is the CRC produced by Python `zlib.crc32()`.

## Numeric Encoding

All JSON values are non-negative. Quantization to the nearest integer uses `floor(scaled_value + 0.5)`.

| Value | Encoded type | Encoding | Decoding |
| --- | --- | --- | --- |
| Step duration | `uint16_t` | seconds ×10 | code ×100 ms |
| Main frequency | `uint16_t` | Hz ×10 | code ÷10 Hz |
| LFO frequency | `uint16_t` | Hz ×10 | code ÷10 Hz |
| Brightness | `uint8_t` | normalized value ×100 | code ÷100 |
| Duty cycle | `uint8_t` | normalized value ×100 | code ÷100 |
| Phase | `uint8_t` | radians ×10, rounded modulo 63 | code ÷10 radians |

Canonical phase codes are 0 through 62. Code 63 is rejected. Modulo 63 maps values quantized to one complete turn back to zero and avoids storing two representations of the same phase. The angular resolution is approximately 0.1 radian or 5.7 degrees.

## Enumerations

Enum values are part of the wire format and must not be derived from C enum ordinals.

### Main Waveform

| Code | JSON value |
| --- | --- |
| 0 | `square` |
| 1 | `sine` |
| 2 | `triangle` |

### Modulator Mode

| Code | JSON value |
| --- | --- |
| 0 | `static` |
| 1 | `linear` |
| 2 | `lfo` |

### LFO Waveform

| Code | JSON value |
| --- | --- |
| 0 | `sine` |
| 1 | `square` |

All other enum codes are invalid in format version 1.

## Step Layout

Each step begins with its duration and contains exactly five oscillator payloads in firmware oscillator order.

```text
step = duration + oscillator[0] + ... + oscillator[4]
```

| Field | Size |
| --- | --- |
| Duration | 2 bytes |
| Five oscillators | Variable, 9 to 22 bytes each |

A step therefore occupies 47 to 112 bytes.

## Oscillator Layout

```text
oscillator = waveform + phase + frequency_modulator
             + brightness_modulator + duty_modulator
```

| Field | Size |
| --- | --- |
| Main waveform | 1 byte |
| Phase | 1 byte |
| Frequency modulator | 3, 5, or 8 bytes |
| Brightness modulator | 2, 3, or 6 bytes |
| Duty modulator | 2, 3, or 6 bytes |

The mode byte starts each modulator and determines the exact number and interpretation of the following bytes. No presence mask or per-modulator length is stored.

## Frequency Modulator Layout

### Static

| Field | Size |
| --- | --- |
| Mode `0` | 1 byte |
| Value | 2 bytes |

### Linear

The interpolation duration is the containing step duration.

| Field | Size |
| --- | --- |
| Mode `1` | 1 byte |
| Start | 2 bytes |
| End | 2 bytes |

### LFO

| Field | Size |
| --- | --- |
| Mode `2` | 1 byte |
| LFO waveform | 1 byte |
| LFO frequency | 2 bytes |
| Low | 2 bytes |
| High | 2 bytes |

## Brightness and Duty Modulator Layout

Brightness and duty use the same layout.

### Static

| Field | Size |
| --- | --- |
| Mode `0` | 1 byte |
| Value | 1 byte |

### Linear

The interpolation duration is the containing step duration.

| Field | Size |
| --- | --- |
| Mode `1` | 1 byte |
| Start | 1 byte |
| End | 1 byte |

### LFO

| Field | Size |
| --- | --- |
| Mode `2` | 1 byte |
| LFO waveform | 1 byte |
| LFO frequency | 2 bytes |
| Low | 1 byte |
| High | 1 byte |

## Golden Vector

The following application data defines one 1-second step containing five identical default square oscillators. Each oscillator has static 10 Hz frequency, 50% brightness, and 50% duty cycle.

```json
{
  "version": "1.0.0",
  "name": "Golden Static Sequence",
  "steps": [
    {
      "duration": 1.0,
      "oscillators": [
        { "frequency": { "mode": "static", "value": 10.0 }, "brightness": { "mode": "static", "value": 0.5 } },
        { "frequency": { "mode": "static", "value": 10.0 }, "brightness": { "mode": "static", "value": 0.5 } },
        { "frequency": { "mode": "static", "value": 10.0 }, "brightness": { "mode": "static", "value": 0.5 } },
        { "frequency": { "mode": "static", "value": 10.0 }, "brightness": { "mode": "static", "value": 0.5 } },
        { "frequency": { "mode": "static", "value": 10.0 }, "brightness": { "mode": "static", "value": 0.5 } }
      ]
    }
  ]
}
```

The 47-byte payload is:

```text
0A 00
00 00 00 64 00 00 32 00 32
00 00 00 64 00 00 32 00 32
00 00 00 64 00 00 32 00 32
00 00 00 64 00 00 32 00 32
00 00 00 64 00 00 32 00 32
```

- `0A 00`: 10 deciseconds.
- Each 9-byte oscillator is `square`, phase zero, static frequency code 100, static brightness code 50, and static duty code 50.
- The payload CRC-32 is `0xB1DDF53F`.

The complete 61-byte sequence is:

```text
4D 48 4C 53 01 00 00 01 2F 00 3F F5 DD B1
0A 00
00 00 00 64 00 00 32 00 32
00 00 00 64 00 00 32 00 32
00 00 00 64 00 00 32 00 32
00 00 00 64 00 00 32 00 32
00 00 00 64 00 00 32 00 32
```

## Decoder Validation

Before exposing decoded data to the sequence engine, a decoder must validate:

1. Magic, exact supported version, step count, payload length, and CRC.
2. Complete consumption of exactly `payload_length` bytes.
3. Every mode and waveform code.
4. Phase code in the range 0 to 62.
5. Duration greater than zero.
6. Main frequency values no greater than 100.0 Hz.
7. Brightness and duty codes no greater than 100.
8. LFO frequency greater than zero.
9. Every LFO `low` value less than or equal to `high`.
10. No read beyond the supplied buffer for any mode-specific payload.

The decoder must build and validate a complete temporary sequence before replacing the active sequence.

## Single-Step Updates

A single-step BLE update reuses the step payload defined above. The future BLE message envelope supplies the target step index and payload length. BLE framing is not part of this compact sequence format and will be specified separately.
