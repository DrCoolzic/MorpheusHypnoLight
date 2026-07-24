# Plan: Reduce Step Size and Prepare the Bluetooth Protocol

Analyze the current footprint of a step (≈ 604 bytes), define a compact wire format using fixed-point integers with one decimal place and optional fields, add a dedicated loader/parser, then expose the control commands (play/pause/stop/brightness) and sequence transfer over BLE.

## Implementation Roadmap

### Phase 1: Finalize the Application JSON Format

**Status: mostly complete**

- Use `sequence_format.md` as the authoritative format shared by Morpheus Player, Morpheus Editor, and firmware tools.
- Require `version`, `name`, and `steps`; keep `author` and `createdAt` optional.
- Store relative step `duration` in seconds and derive total sequence duration from all steps.
- Require exactly five oscillators per step.
- Use `lfo_frequency` as the JSON field name and `square` as the default main waveform.
- Migrate `demo.json` and `generate_sequence.py` from `duration_ms` to the application JSON format.
- Add strict JSON validation before any output is generated.

**Acceptance criteria:** the demo sequence conforms to `sequence_format.md`, invalid fields and ranges are rejected, and the existing C output still produces equivalent playback.

### Phase 2: Finalize the Compact Binary Layout

**Status: partially defined**

- Define a sequence header containing at least the format version and step count.
- Define the exact byte order and payload layout for sequences, steps, oscillators, and all modulator modes.
- Use little-endian encoding for every `uint16_t` value unless portability requirements dictate otherwise.
- Encode only fields used by the selected modulator mode.
- Reserve no custom LUT support in format version 1.
- Decide whether the format includes total payload length and CRC.
- Publish at least one annotated binary example and golden byte vector.

**Acceptance criteria:** every byte has a documented meaning, message length can be validated without reading beyond the buffer, and independent encoders produce the same golden vector.

### Phase 3: Add Compact Encoding to the Python Tool

**Status: pending**

- Keep the current C source output as a temporary reference path.
- Add compact binary output, for example `demo.seq`.
- Add C byte-array output for firmware tests without BLE.
- Report encoded sizes per step and for the complete sequence.
- Quantize duration, frequency, phase, brightness, and duty according to the compact format rules.

**Acceptance criteria:** the same validated JSON input can generate C structures, a binary file, and an identical embedded C byte array.

### Phase 4: Implement the Firmware Decoder

**Status: pending**

- Add a pure `sequence_decode_compact()` function that decodes into a caller-provided `sequence_step_t` array.
- Check version, lengths, step count, modes, waveforms, numeric ranges, and truncation before accepting the sequence.
- Avoid dynamic allocation and out-of-bounds reads.
- Decode into temporary storage so invalid input cannot partially modify the active sequence.
- Add `sequence_load_compact()` as the validated bridge to the existing sequence engine.

**Acceptance criteria:** valid golden vectors decode successfully, malformed inputs return explicit errors, and the active sequence remains unchanged after a decoding failure.

### Phase 5: Validate the Compact Round Trip

**Status: pending**

- Generate reference `sequence_step_t` values directly from JSON.
- Encode the same JSON to compact bytes and decode it back to `sequence_step_t`.
- Compare fields semantically instead of using `memcmp()`.
- Account for quantization tolerances: 100 ms duration, 0.1 Hz frequency, 1% brightness/duty, and approximately 5.7° phase.
- Test every mode and waveform, minimum and maximum values, one-step and 128-step sequences, truncated buffers, unknown values, and invalid ranges.

**Acceptance criteria:** all decoded values equal their quantized reference values and all malformed test vectors are rejected.

### Phase 6: Validate Playback without BLE

**Status: pending**

- Embed the generated compact demo byte array in the firmware.
- Load it through `sequence_load_compact()`.
- Verify playback behavior against the existing directly generated C sequence.
- Measure the actual current and compact memory footprints.

**Acceptance criteria:** both loading paths produce equivalent visible playback and the measured compact size matches the encoder report.

### Phase 7: Add the BLE Transport

**Status: pending**

- Transfer the exact compact bytes already validated without BLE.
- Define typed control and transfer messages with identifier, length, payload, and integrity information.
- Support full-sequence transfer and an atomic single-step update command.
- Prefer immediate application when updating the currently playing step.
- Coalesce rapid editor updates and avoid a blocking acknowledgement after every BLE fragment.
- Add sequence numbering, final acknowledgement, and selective retry or complete retry behavior.

**Acceptance criteria:** full sequences and individual steps transfer reliably, corrupted or incomplete transfers are rejected, and a typical 1 600-byte sequence transfers in roughly 200 ms under normal conditions.

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
- Phase uses radians ×10 in `uint8_t`.
- Custom LUT support is deferred beyond format version 1.
- A currently playing step should be updated immediately when this can be done atomically.

## Remaining Decisions

- Final sequence header fields and numeric enum values.
- CRC algorithm and whether a payload-length field is included.
- Exact BLE message envelope, fragmentation, acknowledgement, and retry rules.

## Compact Format Size

- Use `uint8_t` wherever possible. Only duration and frequency-related values require `uint16_t`.
  - Duration: seconds ×10 in `uint16_t`; range 0–6 553.5 s, 100 ms resolution.
  - All frequency-related values, including LFO frequency: Hz ×10 in `uint16_t`; range 0–6 553.5 Hz, 0.1 Hz resolution.
  - Brightness and duty-cycle values: normalized firmware value ×100 in `uint8_t`; range 0–100%, 1% resolution.
  - Phase: radians ×10 in `uint8_t`; encoded range 0–63 for 0–2π, approximately 5.7° resolution.
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