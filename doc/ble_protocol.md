# Morpheus HypnoLight BLE Transport Protocol

## Overview

This document defines the Bluetooth Low Energy message protocol used to transfer compact sequences and to control the Morpheus HypnoLight device.

- The physical protocol is BLE 4.2/5.0.
- The host stack is ESP-IDF NimBLE.
- The application protocol is command/response over a single write characteristic with a separate notification characteristic for status.
- All multi-byte integer fields are little-endian, matching the compact sequence format.

## GATT Service

| Item | Value |
| --- | --- |
| Service UUID | `D4C38BC0-4F25-AF02-8F15-A1B5C2A60000` (128-bit) |
| Command characteristic UUID | `D4C38BC0-4F25-AF02-8F15-A1B5C2A60001` (write, no response) |
| Status characteristic UUID | `D4C38BC0-4F25-AF02-8F15-A1B5C2A60002` (notify, read) |

The device name advertised is `HypnoLight`.

## Command Messages

All command messages are written to the command characteristic. The first byte is the opcode and is followed by the opcode-specific payload.

| Opcode | Name | Payload | Description |
| --- | --- | --- | --- |
| `0x01` | `PLAY` | none | Start or resume playback. |
| `0x02` | `PAUSE` | none | Pause playback. |
| `0x03` | `STOP` | none | Stop and return to the beginning. |
| `0x04` | `SEEK` | 4 bytes: position_ms | Jump to the absolute position in milliseconds. |
| `0x05` | `BRIGHTNESS` | 1 byte: 0–100 | Set the global brightness multiplier. |
| `0x06` | `SET_MODE` | 1 byte: `0x00` player, `0x01` editor | Set the device operating mode. In player mode `PAUSE` turns the LEDs off; in editor mode `PAUSE` freezes the current LED state. |
| `0x10` | `LOAD_START` | 4 bytes: total_size | Start a new full-sequence transfer. |
| `0x11` | `LOAD_CHUNK` | 2 bytes: offset + data | Place data bytes at the given offset in the transfer buffer. |
| `0x12` | `LOAD_COMMIT` | none | Validate and load the transferred full sequence. |
| `0x20` | `UPDATE_STEP_START` | 1 byte: step_index, 2 bytes: step_size | Start a single-step update transfer. |
| `0x21` | `UPDATE_STEP_CHUNK` | 2 bytes: offset + data | Place data bytes at the given offset in the step update buffer. |
| `0x22` | `UPDATE_STEP_COMMIT` | none | Validate and apply the updated step. |

Every `*_START` command resets the corresponding transfer state. The central should send chunks in increasing offset order. GATT writes are acknowledged at the ATT level, so no additional per-fragment acknowledgement is used.

## Status Notifications

The status characteristic emits a 2-byte notification after each command completes:

| Byte | Meaning |
| --- | --- |
| 0 | Echoed opcode that produced the status. |
| 1 | Result code: `0x00` success, `0x01` invalid command, `0x02` invalid argument, `0x03` transfer out of range, `0x04` sequence validation failed, `0x05` load failed, `0xFF` internal error. |

A notification is also emitted when the central subscribes, echoing `0x00` with result `0x00`.

## Full-Sequence Transfer Flow

1. `LOAD_START` with the exact compact sequence size.
2. One or more `LOAD_CHUNK` messages. The payload begins with the 2-byte offset from the start of the compact buffer. The remaining bytes are the raw sequence data.
3. `LOAD_COMMIT` to request validation and loading.
4. The firmware validates the complete buffer, then calls `sequence_load_compact()`. A status notification is emitted with the `LOAD_COMMIT` opcode and the result.

The firmware rejects any transfer that would overflow the fixed 16 KiB transfer buffer.

## Single-Step Update Flow

1. `UPDATE_STEP_START` with the target step index and the exact step payload size.
2. One or more `UPDATE_STEP_CHUNK` messages with the 2-byte step-relative offset and raw step bytes.
3. `UPDATE_STEP_COMMIT` to validate and apply the step.

The firmware validates the step, decodes it into a temporary `sequence_step_t`, and then copies it into the active sequence if the current loaded sequence has at least that many steps. The updated step is applied immediately if it is the currently playing step.

## Fragmentation Example

With a default ATT MTU of 23 bytes, a 256-byte compact demo sequence fits in approximately 14 `LOAD_CHUNK` writes of 19 data bytes each plus a final shorter chunk. The central should not exceed the negotiated MTU minus three bytes for the opcode and offset fields. When using a 128-byte step payload, a single `UPDATE_STEP_CHUNK` can carry up to MTU-3 bytes per fragment.

A larger ATT MTU can be negotiated between the central and the peripheral. In that case, the chunk size can be increased beyond the default 17 bytes used by `ble_transfer.py`; the practical limit for `LOAD_CHUNK` data is the negotiated ATT MTU minus the application header (`opcode` + `offset`) and the BLE write command header. `ble_transfer.py` exposes this through the `--chunk` option.

## Error Handling

- Unknown opcodes immediately emit `0x01` invalid command.
- Messages shorter than the required opcode payload emit `0x02` invalid argument.
- Chunk offsets that do not match the expected next write or that overflow the declared transfer size emit `0x03` transfer out of range and abort the current transfer.
- If validation fails on `*_COMMIT`, the active sequence is not modified and `0x04` is emitted.
