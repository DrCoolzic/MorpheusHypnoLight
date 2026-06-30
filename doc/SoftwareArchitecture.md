# Software Architecture

## Overview

The Morpheus HypnoLight firmware is an [ESP-IDF](https://docs.espressif.com/projects/esp-idf/en/latest/esp32s3/index.html) project located in the `firmware/` directory of the repository. It is structured as a set of ESP-IDF components, each responsible for a distinct part of the device behavior.

The firmware implements two main operating modes described in the product specification:

- **Sequence Mode**: the device plays ordered steps stored in memory.
- **Real-Time Mode**: parameters are controlled live without a sequence.

Both modes share the same low-level signal generation pipeline.

## Repository Layout

```text
firmware/
├── CMakeLists.txt          # Project declaration
├── sdkconfig               # Generated menuconfig, committed to git
├── sdkconfig.defaults      # Optional baseline configuration
├── main/                   # Application entry point
│   ├── CMakeLists.txt
│   └── main.c              # app_main(): initialization
└── components/             # Application components
    ├── oscillator/         # Software oscillator engine (LUT + DDS)
    ├── led_control/        # LEDC + SDM dispatch and output
    ├── sequence/           # Sequence engine (steps, interpolation, LFO)
    └── comms/              # BLE / Wi-Fi communication layer
```

## Components

### `oscillator`

Responsible for generating the four low-frequency flicker signals that drive the eight outer LED groups.

- **Waveforms**: sine, square, triangle, custom (via LUT).
- **Parameters per oscillator**: frequency, duty cycle, brightness, phase.
- **Implementation**: pre-computed Look-Up Table (LUT) rebuilt at step start, Direct Digital Synthesis (DDS) phase accumulator updated in a 1 kHz timer callback.
- **Public API** (to be defined as the code is written):
  - `oscillator_init()`
  - `oscillator_set_waveform(id, shape, duty)`
  - `oscillator_set_frequency(id, hz)`
  - `oscillator_set_brightness(id, percent)`
  - `oscillator_set_phase(id, degrees)`
  - `oscillator_tick()` — returns the current value of all oscillators

### `led_control`

Routes oscillator values to the LEDC hardware channels and drives the central group via the SDM peripheral.

- **Outer groups 1–8**: 8 LEDC channels at 1 kHz carrier, 10-bit resolution.
- **Central group 9**: 1 SDM channel at ~1 MHz, 8-bit effective brightness.
- **Dispatch table**: per-step mapping `channel → oscillator_id`.
- **Public API** (to be defined):
  - `led_control_init()`
  - `led_control_set_dispatch_table(mapping[8])`
  - `led_control_update_outer(ledc_channel, value)`
  - `led_control_set_central_brightness(percent)`

### `sequence`

Implements the sequence engine: steps, timing, parameter interpolation and LFO modulation.

- A **step** defines duration, per-oscillator waveform/duty/phase, dynamic frequency/brightness (linear or LFO), and central group brightness.
- The sequencer advances steps, rebuilds oscillator LUTs, updates the dispatch table, and ticks the parameter layer every 10–50 ms.
- **Public API** (to be defined):
  - `sequence_load(const sequence_t *seq)`
  - `sequence_play()`, `sequence_pause()`, `sequence_seek(position)`
  - `sequence_tick()` — called from the main loop or a FreeRTOS task

### `comms`

Communication layer for remote control and configuration.

- **Bluetooth Low Energy (BLE)**: primary control channel from the mobile/desktop application.
- **Wi-Fi**: optional web server interface for configuration and control.
- **Protocol**: to be defined (commands for play/pause/seek, parameter updates, sequence upload).
- **Public API** (to be defined):
  - `comms_init()`
  - `comms_register_command_handler(...)`
  - `comms_send_status(...)`

## Data Flow

The full signal flow is shown in the product specification; at firmware level it can be summarized as:

```text
Sequencer (step advance every few seconds)
    ↓
Parameter layer (linear / LFO update every 10–50 ms)
    ↓
Oscillator timer callback (1 kHz)
    ↓
Dispatch table (channel → oscillator)
    ↓
LEDC peripheral (8 channels) → outer LED groups 1–8
SDM peripheral (1 channel)   → central LED group 9
```

## Runtime Architecture (FreeRTOS)

ESP-IDF includes FreeRTOS as the default real-time kernel. The firmware does not require a different RTOS such as Zephyr; FreeRTOS is sufficient for all timing, peripheral, and communication needs.

Proposed FreeRTOS tasks and timers:

|Task / Timer|Period|Responsibility|
|------------|------|--------------|
|`oscillator_timer`|1 ms (1 kHz)|Update DDS phase accumulators, compute oscillator values, dispatch to LEDC channels.|
|`sequencer_task`|10–50 ms|Advance sequence steps, update parameter layer, rebuild LUTs, apply LFOs.|
|`input_task`|50–100 ms|Poll I2C rotary encoders and update parameters or sequence.|
|`display_task`|100–250 ms|Refresh the OLED display with current status.|
|`comms_task`|event-driven|Handle BLE/Wi-Fi events and dispatch commands.|
|`fan_task`|1 s|Read TMP36 temperature, update fan PWM.|
|`watchdog_task`|1 s|Feed the watchdog and monitor safety thresholds (temperature, stack).|

**Inter-task communication:**

- `oscillator` publishes current values directly to `led_control` from the timer callback (shared state protected by critical section).
- `sequence` writes target parameters to a shared structure read by `oscillator` and `led_control`.
- `comms` posts commands to a FreeRTOS queue consumed by `sequence` or `input_task`.

## Build, Flash and Monitor

All commands are run from the `firmware/` directory.

```bash
idf.py set-target esp32s3
idf.py build
idf.py -p PORT flash
idf.py -p PORT monitor
```

For Visual Studio Code, use the ESP-IDF extension commands:

- **ESP-IDF: Build your Project**
- **ESP-IDF: Flash your Project**
- **ESP-IDF: Monitor your Device**

> **Important:** Always use the **`UART`** USB port of the DevKitC-1 for flashing and monitoring. See [SoftwareSetup.md](SoftwareSetup.md) for the full environment setup.

## Important `sdkconfig` Settings

The following ESP-IDF configuration options will be required or important for the project. They are typically set via `idf.py menuconfig` and committed in `sdkconfig`:

- **LEDC**: 8 channels, 10-bit resolution, 1 kHz frequency.
- **SDM**: 1 channel for the central group.
- **I2C**: master mode on GPIO1 (SDA) / GPIO2 (SCL) for the QWIIC bus.
- **FreeRTOS**: 1 kHz tick rate for the oscillator timer callback.
- **BLE / Wi-Fi**: enable as needed for the `comms` component.

## TBD / Future Sections

- Sequence file format (JSON or binary).
- BLE/Wi-Fi command protocol.
- Error handling and safety (thermal shutdown, watchdog).
- Fan control algorithm.
- Calibration and factory settings.
