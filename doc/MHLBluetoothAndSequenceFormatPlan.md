# Plan: Reduce Step Size and Prepare the Bluetooth Protocol

Analyze the current footprint of a step (≈ 604 bytes), define a compact wire format using fixed-point integers with one decimal place and optional fields, add a dedicated loader/parser, then expose the control commands (play/pause/stop/brightness) and sequence transfer over BLE.

## Planned Steps

1. **Measure the Current Footprint**
   - `sequence_step_t` = `uint32_t duration_ms` + 5× `sequence_oscillator_step_t`
   - Each `sequence_oscillator_step_t` = `oscillator_static_config_t` (12 bytes) + 3× `modulator_config_t` (3×36 bytes)
   - Estimated total: **604 bytes/step**, i.e. ~77 KB for 128 steps.
   - Verify with `idf.py size` or a small test program once the structs are frozen.

2. **Define the Compact Format**
   - Encode duration in deciseconds as `uint16_t`: seconds ×10, 100 ms resolution, maximum 6 553.5 s (≈109 min).
   - Encode every frequency value, including LFO frequency, as `uint16_t`: Hz ×10, 0.1 Hz resolution, maximum 6 553.5 Hz.
   - Encode phase as radians ×10 in `uint8_t`: values 0–63 represent 0–2π with a resolution of approximately 0.1 rad (5.7°).
   - Encode normalized brightness and duty-cycle values as integer percentages in `uint8_t`: firmware float ×100, range 0–100, 1% resolution.
   - Encode modes and waveforms as `uint8_t` enums.
   - Let each modulator mode determine its payload length so only parameters used by that mode are transmitted; no presence mask is required for these fields.
   - Replace the `custom_lut` pointer with a predefined LUT index on the firmware side (or a separate transmission if needed).
   - Resulting size: 47–112 bytes/step for five oscillators, excluding any sequence-level header or checksum.

3. **Add a Compact Parser/Loader**
   - Create `sequence_load_compact(const uint8_t *data, uint32_t len)` to decode the wire format and fill the engine.
   - Keep the existing `sequence_load()` C-array loader if embedded code generation is still desired.
   - Validate ranges before applying.

4. **Prepare the Bluetooth Protocol**
   - Control commands: `play`, `pause`, `stop`, `brightness <x10>`.
   - Full sequence transfer command: packet-based send with acknowledgements.
   - Single-step update command for real-time editing: transmit only the step index and its compact payload instead of retransmitting the complete sequence.
   - Apply a received step atomically. Prefer immediate application when updating the currently playing step, provided this can be done safely without exposing a partially decoded configuration.
   - Coalesce rapid editor changes before transmission to avoid flooding the BLE link.
   - Decide whether the format is raw binary or encapsulated in typed messages (id + len + payload).

5. **Update the Python Tool**
   - Adapt `generate_sequence.py` to emit the compact binary format in addition to the C source.
   - Add a “BLE preview” mode that reports the total sequence size.

6. **Tests and Validation**
   - Compare generated sequences before/after compacting.
   - Verify playback on the device.
   - Measure actual BLE throughput/transfer time.

## Open Decisions

- Priority order: compacting steps or BLE commands first?
- Custom LUT handling: predefined index, separate transmission, or no custom LUT for now?
- Desired resolution: ×10 (1 decimal place) or finer?
- BLE packet format: raw binary, lightweight JSON, or structured messages like Nordic UART?

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