# Plan: Reduce Step Size and Prepare the Bluetooth Protocol

Analyze the current footprint of a step (≈ 604 bytes), define a compact wire format using fixed-point integers with one decimal place and optional fields, add a dedicated loader/parser, then expose the control commands (play/pause/stop/brightness) and sequence transfer over BLE.

## Implementation Roadmap

### Phase 1: Finalize the Application JSON Format

**Status: complete**

- Use `sequence_format.md` as the authoritative format shared by Morpheus Player, Morpheus Editor, and firmware tools.
- Require `version`, `name`, and `steps`; keep `author` and `createdAt` optional.
- Store relative step `duration` in seconds and derive total sequence duration from all steps.
- Require exactly five oscillators per step.
- Use `lfo_frequency` as the JSON field name and `square` as the default main waveform.
- `demo.json` and `generate_sequence.py` have been migrated from `duration_ms` to the application JSON format.
- Strict JSON validation runs before any output is generated.
- Python unit tests cover the valid demo, metadata, required and unknown fields, version, duration resolution, counts, defaults, waveforms, phases, modulator fields, ranges, and LFO ordering.
- Generated C output is compared with the checked-in reference to prevent playback-data regressions.

**Acceptance criteria: met.** The demo conforms to `sequence_format.md`, invalid fields and ranges are rejected, and the generated C output remains unchanged.

### Phase 2: Finalize the Compact Binary Layout

**Status: complete**

- `compact_sequence_format.md` is the authoritative binary format specification.
- The 14-byte sequence header contains magic, semantic version, step count, payload length, and payload CRC-32.
- All multi-byte values use little-endian encoding.
- The exact layout of steps, oscillators, and every mode-specific modulator payload is defined.
- Only fields used by the selected modulator mode are encoded.
- Custom LUT support is excluded from format version 1.
- Numeric enum codes are explicitly defined and independent of C enum ordinals.
- An annotated 61-byte golden sequence vector is published.

**Acceptance criteria: met for the wire specification.** Every byte has a documented meaning and message length can be validated safely. Cross-encoder golden-vector verification is part of Phase 3.

### Phase 3: Add Compact Encoding to the Python Tool

**Status: complete**

- The current C source output remains available as the reference path.
- `generate_sequence.py` implements the complete version 1 compact encoder and 14-byte header.
- `--compact-output` writes the compact binary file.
- `--compact-c-output` writes an equivalent C byte-array header for firmware tests.
- Compact output reports per-step, payload, and complete sequence sizes.
- Duration, frequency, phase, brightness, and duty are quantized according to `compact_sequence_format.md`.
- Unit tests verify the published golden vector byte-for-byte, header fields, size accounting, phase canonicalization, and unchanged C generation.
- The current four-step demo encodes to step sizes 48, 52, 71, and 71 bytes: 242 bytes of payload and 256 bytes including the header.

**Acceptance criteria: met.** The same validated JSON data produces C structures, compact binary bytes, and equivalent embedded C data.

### Phase 4: Implement the Firmware Decoder

**Status: complete**

- `sequence_decode_compact()` decodes into a caller-provided `sequence_step_t` array.
- Header, version, length, step count, CRC, modes, waveforms, ranges, truncation, and complete payload consumption are validated.
- Decoding uses bounded readers and no dynamic allocation.
- A validation pass completes before any caller-provided output is modified.
- `sequence_load_compact()` validates first, stops playback, decodes into the existing internal sequence buffer, and applies the first step.
- The decoder and loader compile successfully in the ESP-IDF firmware build.
- On-device startup completed on ESP32-S3 and reported `Compact sequence decoder validation passed`.

**Acceptance criteria: met.** Valid vectors decode successfully, malformed vectors are rejected, and failed decoding leaves output unchanged.

### Phase 5: Validate the Compact Round Trip

**Status: complete**

- Firmware startup tests decode the independent published golden vector.
- The generated demo compact bytes are decoded and compared semantically with directly generated reference `sequence_step_t` values.
- Comparisons account for duration, frequency, brightness/duty, and phase quantization.
- Embedded negative tests cover invalid magic, CRC corruption, truncation, and unchanged output after failure.
- Python tests cover JSON validation, the golden encoder vector, header and size accounting, defaults, and phase canonicalization.

**Acceptance criteria: met.** Python tests pass and all embedded round-trip assertions completed successfully on the ESP32-S3.

### Phase 6: Validate Playback without BLE

**Status: complete**

- The build generates and embeds `test_sequence_compact_data.h` from `demo.json`.
- Firmware startup validates the compact decoder and loads the demo through `sequence_load_compact()`.
- Direct C generation remains as the semantic reference used by the round-trip test.
- The firmware builds successfully; compact demo size is 256 bytes including its header.
- Compact playback started successfully on the ESP32-S3 and was visually confirmed as equivalent.
- On-device `size` command produced the following measurements:
  - `sequence_step_t` = 604 bytes.
  - `sequence_oscillator_step_t` = 120 bytes.
  - `modulator_config_t` = 36 bytes.
  - 4-step demo in RAM = 2 416 bytes.
  - Maximum sequence in RAM = 77 312 bytes.
  - Compact demo on wire = 256 bytes.

**Acceptance criteria: met.** Playback, build integration, and memory footprint are validated. The compact representation is about **9.4× smaller** than the decoded 4-step demo and about **302× smaller** than the worst-case in-RAM sequence.

### Phase 7: Add the BLE Transport

**Status: complete**

- NimBLE enabled in `sdkconfig.defaults` and a new `ble` component created.
- GATT service with 128-bit UUIDs registered for command and status characteristics.
- `ble_transfer.py` successfully connects, uploads `build/demo_compact.bin`, and sends `PLAY`.
- Advertising restarts automatically after each disconnect so the device remains discoverable.
- Full-sequence transfer validated end-to-end on ESP32-S3; the single-step update protocol is defined but not yet exercised.

**Acceptance criteria:** full-sequence transfer and playback commands validated on hardware; device reconnects and re-transfers without a reboot.

### Phase 8: Integrate Morpheus Player and Morpheus Editor

**Status: pending**

- Read and validate the application JSON format.
- Reuse an encoder that produces bytes identical to the Python golden vectors.
- Send complete sequences when loading and only modified steps during real-time editing.
- Surface validation, connection, transfer, and firmware rejection errors to the user.

**Acceptance criteria:** application-generated bytes pass the firmware golden-vector tests and real-time step updates do not require retransmitting the complete sequence.

## Decisions Recorded

- Compact format implementation comes before BLE transport.
- JSON is the persistent application format; compact binary is the BLE and firmware-loading format.
- Duration and every frequency-related value use `uint16_t` ×10.
- Brightness and duty use normalized values encoded as `uint8_t` ×100.
- Phase uses radians ×10 in `uint8_t`, with canonical codes 0–62 and modulo-63 wraparound.
- Custom LUT support is deferred beyond format version 1.
- Compact sequences use a 14-byte `MHLS` header, explicit semantic version, payload length, and CRC-32/ISO-HDLC.
- All multi-byte wire values are little-endian, and wire enums are independent of C enum ordinals.
- A currently playing step should be updated immediately when this can be done atomically.

## Remaining Decisions

- Exact BLE message envelope, fragmentation, acknowledgement, and retry rules.

## Compact Format Size

- Use `uint8_t` wherever possible. Only duration and frequency-related values require `uint16_t`.
  - Duration: seconds ×10 in `uint16_t`; range 0–6 553.5 s, 100 ms resolution.
  - All frequency-related values, including LFO frequency: Hz ×10 in `uint16_t`; range 0–6 553.5 Hz, 0.1 Hz resolution.
  - Brightness and duty-cycle values: normalized firmware value ×100 in `uint8_t`; range 0–100%, 1% resolution.
  - Phase: radians ×10 in `uint8_t`; canonical encoded range 0–62 with modulo-63 wraparound, approximately 5.7° resolution.
  - Waveform and mode selections: `uint8_t`.
- Encode only the parameters used by the selected modulator mode. The mode defines the payload length.

### Size Calculation per Oscillator

- Waveform: 1 byte.
- Phase: 1 byte.
- Static frequency: mode=1, value=2 => 3 bytes.
- Linear frequency: mode=1, start=2, end=2 => 5 bytes.
- Frequency LFO: mode=1, waveform=1, LFO frequency=2, low=2, high=2 => 8 bytes.
- Static brightness/duty: mode=1, value=1 => 2 bytes.
- Linear brightness/duty: mode=1, start=1, end=1 => 3 bytes.
- Brightness/duty LFO: mode=1, waveform=1, LFO frequency=2, low=1, high=1 => 6 bytes.

This gives the following minimum and maximum sizes per oscillator:

- Minimum: 1 + 1 + 3 + 2 + 2 = 9 bytes.
- Maximum: 1 + 1 + 8 + 6 + 6 = 22 bytes.

### Size Calculation per Step

- Minimum: duration=2 + (5 × 9) = 47 bytes.
- Maximum: duration=2 + (5 × 22) = 112 bytes.
- Midpoint estimate: (47 + 112) / 2 ≈ 80 bytes. Actual size depends on the selected modulator modes.
- A 20-step sequence is therefore approximately 940–2 240 bytes, with a midpoint estimate of about 1 600 bytes, excluding any sequence-level header or checksum.

A typical sequence of approximately 1 600 bytes should take on the order of 200 ms to transfer over Bluetooth Low Energy under normal operating conditions.

## Python Tools in `firmware/scripts/`

The `firmware/scripts` directory contains the Python utilities used by the build, tests, and BLE workflow.

### `generate_sequence.py`

Validates a Morpheus application JSON sequence and produces the artifacts needed by the firmware build.

- Default input: `sequences/demo.json`.
- Generated outputs:
  - `main/test_sequence.c` and `main/test_sequence.h`: C representation of the sequence.
  - Optional compact binary (`--compact-output PATH`) and C header (`--compact-c-output PATH`).

Example:

```bash
python scripts/generate_sequence.py
python scripts/generate_sequence.py --compact-output build/demo_compact.bin
```

### `test_generate_sequence.py`

Unit tests for `generate_sequence.py`. Run with:

```bash
python scripts/test_generate_sequence.py
```

### `ble_transfer.py`

Connects to a `HypnoLight` device over BLE, uploads a compact binary sequence, and sends control commands.

- Requires `bleak`: `pip install bleak`.
- Default compact binary: `build/demo_compact.bin`.
- Default chunk size: 17 bytes, safe for a 23-byte ATT MTU.

Common invocations:

```bash
# Scan, connect, upload, and play
python scripts/ble_transfer.py --play --binary build/demo_compact.bin

# Upload and pause
python scripts/ble_transfer.py --binary build/demo_compact.bin --pause

# Upload, then set global brightness to 50%
python scripts/ble_transfer.py --play --binary build/demo_compact.bin --brightness 50

# upload with 200 bytes chunk
python scripts/ble_transfer.py --play --binary build/demo_compact.bin --chunk 200
```

#### Arguments


| Argument       | Default                  | Description                                                 |
| ---------------- | -------------------------- | ------------------------------------------------------------- |
| `--name`       | `HypnoLight`             | Advertised BLE device name.                                 |
| `--address`    | —                       | BLE address; if omitted, scan by name.                      |
| `--binary`     | `build/demo_compact.bin` | Compact binary sequence file to upload.                     |
| `--chunk`      | `17`                     | Sequence bytes per`LOAD_CHUNK`; safe for a 23-byte ATT MTU. |
| `--play`       | flag                     | Send`PLAY` after the transfer.                              |
| `--pause`      | flag                     | Send`PAUSE` after the transfer.                             |
| `--stop`       | flag                     | Send`STOP` after the transfer.                              |
| `--brightness` | —                       | Set global brightness (0–100).                             |

See `doc/ble_protocol.md` for the command opcodes and status notification format.

