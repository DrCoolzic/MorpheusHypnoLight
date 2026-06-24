# Morpheus HypnoLight Project

## Objective

The objective of this project is to create a device for visual and auditory stimulation. This type of device is often referred to as AVS (Audio-Visual Stimulation) or FLS (Flicker Light Stimulation). Flicker Light Stimulation is a non-invasive technique that uses rhythmic light variation to influence brain activity and the state of consciousness. It involves emitting rhythmic light pulses at specific frequencies, typically ranging from 3 Hz to 50 Hz. These light pulses stimulate the visual system, leading to a phenomenon called "visual entrainment," in which brain activity synchronizes with the frequency of the light. This synchronization can induce altered states of consciousness, like those experienced during deep meditation or under the influence of psychedelics, often accompanied by striking visual patterns and sensations beyond conscious control.

---

## Main Features

### Must have (in V1)

- **Remote Sequence Mode of Operation**: The sequences to play are selected and controlled remotely from an application that communicates using BLE or Wi-Fi. This application can control multiple machines simultaneously.
- **Remote Real-time Mode of Operation**: Developers use this mode to control all machine parameters in real time without using sequences.
- Bluetooth Low Energy connectivity, used to connect to the control application and potentially to a sound system
- 8 outer LED groups (cold white) driven by 4 independent oscillators via 8 LEDC hardware channels
- 1 central LED group (warm white) driven by a dedicated Sigma-Delta Modulator (SDM) channel with linear or LFO brightness control
- 4 oscillators with sine, triangle, square, and custom waveforms, each with controllable frequency, duty cycle, brightness, and phase
- Per-step oscillator-to-group assignment (each step can route oscillators to different LED groups)
- Low Frequency Oscillator (LFO) modulation of oscillator parameters, including frequency modulation for rich FLS effects
- Step-based sequence engine with linear interpolation and LFO modulation modes
- Capability to pause and seek loaded sequences
- Low-cost hardware (target: under $100)
- Low-noise (large PWM-controlled fan)
- Compact and easy to carry with tripod mounting threads
- WiFi web server interface

### Could have (in V2)

- **Local Sequence Mode of Operation**: Sequence selection and control is done locally, without the need for an external application. The sequences are stored locally on an SD card, and control is done using knobs, the touch screen, etc.
- **Local Real-Time Mode of Operation**: This mode allows developers to control all machine parameters in real time without using sequences. In Sequence mode, control is done using knobs, the touch screen, etc.
- Input devices: parameter controller (rotary knobs and switches), joystick, mouse, Touch screen
- Output devices: sound output direct through plug or remote via BLE
- Store sequences locally on SD card

---

## LED PCB

The LEDs are placed on a front panel PCB with an aluminum substrate for heat dissipation. In addition to the LEDs, the PCB contains temperature sensors that automatically activate the fan if the device overheats.

Numerous experiments have shown that the position of the LEDs on the PCB has no effect on the user's perception. For instance, a diode that lights up in the top left or bottom right corner of the board is perceived by the user in exactly the same way. Consequently, the LEDs have been arranged into nine groups of four as follows:

<img src="images/pcbled2.png" alt="Led" style="zoom: 50%;" />

The eight peripheral groups are connected to four oscillators, each of which has adjustable frequency, brightness, and duty cycle settings. They use cold white LEDs with a power rating of approximately one watt each. Therefore, each group can provide a maximum power output of four watts, for a total output of about 32 watts for all eight groups combined.

The central group (group 9) uses warm white LEDs (potentially with a power of two watts), and serves a different purpose than the outer groups (groups 1-8). It provides ambient background light without flickering. Its brightness can be smoothly varied over time, though it is not driven by an oscillator.

---

## LED Control Architecture

### Overview

The device drives 9 LED groups using two distinct control paths:

- **Groups 1-8 (cold white, outer ring)**: driven by the 4 independent software oscillators via the 8 LEDC hardware channels of the ESP32-S3. Each group produces a flickering light signal with fully controllable frequency, duty cycle, brightness, and phase.

- **Group 9 (warm white, central)**: driven by a dedicated Sigma-Delta Modulator (SDM) hardware channel. It does not flicker; its brightness is controlled smoothly via linear interpolation or LFO modulation.

The 4 oscillators produce signal values that are dispatched to the 8 LEDC channels via a **dispatch table**, which is a step-level parameter: each step defines which oscillator drives which groups.

A given LED group can be driven by at most one oscillator at a time. Unassigned groups remain off for the duration of the step.

---

### LED Driver

After examining several LED drivers, the AL8860 was chosen as it perfectly suits the project's needs. With the AL8860, the brightness of the connected LEDs can be controlled either with an analog or a PWM signal.

Using the analog control input of the AL8860 has serious limitations:

- **Limited range**: only 0.5 V to 2.5 V
- **Potential nonlinearity**
- **Additional complexity** requiring D/A converters

Therefore, brightness is controlled by pulse width modulation (PWM) applied to the CTRL pin of the AL8860. The same AL8860 circuit is used for all 9 groups, including the central group; only the signal source differs.

The schematic for one LED group is as follows:

<img src="images/leddriver.png" style="zoom:130%;" />

The AL8860 is a buck (step-down) constant-current LED driver. Its role is to regulate the current through the LEDs at a fixed value determined by the sense resistor R1. With R1 = 300 mOhm, the regulated LED current is:

$$I_{LED} = \frac{V_{SET}}{R_1} = \frac{0.1\,V}{0.300\,\Omega} \approx 333\,mA$$

The 4 LEDs in each group are connected in series, giving a total forward voltage of approximately 13.6 V (4 x 3.4 V). The driver circuit is powered by a 24 V supply, which provides a comfortable margin.

The CTRL pin of the AL8860 operates in digital mode:

- **CTRL > 2.5 V**: driver active, regulated current flows through LEDs
- **CTRL < 0.4 V**: driver shut down, LED off

This makes the CTRL pin ideal for direct connection to a PWM signal generated by the ESP32, through a 1 kOhm series resistor (R2) for protection.

---

### LED Control: Outer Groups (1-8)

Each group of LEDs is controlled by an oscillator, which allows the frequency, brightness and duty cycle parameters to be adjusted. The oscillator's output is connected to an LED driver that powers a group of four LEDs.

<img src="images/LED Control.png" alt="Led Control" style="zoom:80%;" />

#### Two-Level PWM Modulation

The oscillator parameters are: frequency (1-100 Hz), duty cycle (0-100%), brightness (0-100%), and phase (0-360 degrees).

Since the signal driving the AL8860 CTRL pin must be a digital PWM signal, a two-level **PWM modulation** technique is used to independently control both brightness and the visible flicker frequency and duty cycle:

- **Carrier signal**: High-frequency PWM (1 kHz), generated by the ESP32 LEDC hardware peripheral, controls the LED brightness via its duty cycle.
- **Modulating signal**: Low-frequency waveform (1-100 Hz), generated in ESP32 software, creates the visible flashing effect. Its frequency and duty cycle determine the flicker parameters.

The two levels are combined as follows:

```text
final_duty = osc_value(t) * brightness
ledcWrite(channel, final_duty)
```

When `osc_value` is at its peak, the LEDC runs at full brightness duty cycle. When `osc_value` is zero, the LEDC output is zero and the LED is off. This gives fully independent control of all three parameters.

![](images/oscillator.png)

#### Brightness Modulator: ESP32 LEDC Peripheral

The ESP32-S3 LEDC peripheral provides exactly 8 independent hardware PWM channels, one per outer LED group. Each channel is initialized at a fixed carrier frequency (1 kHz) with 10-bit resolution (0-1023 duty range).

Brightness is set by calling `ledcWrite(channel, duty)` where `duty` is proportional to the desired brightness level. Since the carrier frequency (1 kHz) is well above the flicker range and above the threshold of visual persistence, it is invisible to the eye: only the average brightness is perceived.

#### Frequency Generator: ESP32 Software Oscillator

The low-frequency modulating signal is generated entirely in ESP32 software using a high-precision hardware timer (`esp_timer`), which provides microsecond-level accuracy independent of the FreeRTOS task scheduler.

**Waveform shapes supported:**

All waveforms are normalized to the range 0.0-1.0. Each waveform has a **frequency** and a **duty cycle** parameter. The meaning of duty cycle depends on the waveform type:

| Waveform | Duty Cycle Meaning | FLS Effect |
| -------- | ------------------ | ---------- |
| Square | Fraction of period at HIGH level (classic PWM) | Sharp, stroboscopic pulses |
| Triangle | Fraction of period in ascending phase (0% or 100% gives a sawtooth variant) | Linear fade in/out, asymmetry controllable |
| Sine | Not applicable (fixed shape) | Gentle, organic stimulation |
| Custom | Encoded in the LUT (user-defined shape) | Any specific FLS therapeutic profile |

Note: sawtooth is not a separate waveform type. It is a degenerate case of the triangle waveform obtained by setting duty cycle to 0% (instant rise then full fall ramp) or 100% (full rise ramp then instant fall).

**Implementation: LUT + Phase Accumulator (DDS)**

Waveform shapes are pre-computed at step startup into a **Look-Up Table (LUT)** of N samples (e.g., N = 64). This avoids expensive floating-point trigonometric calculations inside the time-critical timer callback. The duty cycle parameter is applied during LUT generation, so changing the duty cycle triggers a LUT rebuild for that oscillator.

To handle continuously changing frequencies smoothly, a **Direct Digital Synthesis (DDS)** phase accumulator is used. Rather than stepping through the LUT at a fixed integer rate, a fractional phase value advances by an amount proportional to the current frequency on every timer tick:

```c
// Timer callback - runs at CALLBACK_RATE Hz (e.g. 1000 Hz)
void osc_callback(void* arg) {
    // 1. Compute all 4 oscillator values
    for (int i = 0; i < NUM_OSC; i++) {
        osc_phase[i] += (LUT_SIZE * current_freq[i]) / CALLBACK_RATE_HZ;
        if (osc_phase[i] >= LUT_SIZE) osc_phase[i] -= LUT_SIZE;
        osc_value[i] = lut[i][(int)osc_phase[i]];
    }

    // 2. Dispatch to LEDC channels (groups 1-8) via dispatch table
    for (int ch = 0; ch < 8; ch++) {
        int osc_id = dispatch_table[ch];
        if (osc_id < 0) { ledcWrite(ch, 0); continue; }  // unassigned -> off
        ledcWrite(ch, (int)(osc_value[osc_id] * current_brightness[osc_id] * MAX_DUTY));
    }
    // Group 9 (central) is handled independently by the SDM peripheral
}
```

This approach ensures:

- Frequency changes are **instantaneous and glitch-free** for all 4 oscillators
- Any waveform shape is supported by simply loading a different LUT
- The dispatch is a cheap integer lookup per channel per tick
- Custom therapeutic waveform profiles can be loaded as arbitrary LUTs

**Parameter update constraints:**

| Parameter | Dynamic during step? | Mechanism |
| --------- | -------------------- | --------- |
| Frequency | Yes | DDS phase increment updated each parameter tick |
| Brightness | Yes | Multiplied at callback time, no LUT rebuild |
| Duty cycle | No (fixed per step) | LUT rebuilt once at step start |
| Phase | Set once at step start | Initializes the phase accumulator |
| Group assignment | Set once at step start | Updates the dispatch table |

Duty cycle is kept fixed per step because changing it requires rebuilding the LUT. For FLS purposes this is not a limitation: the perceptual difference between smoothly swept duty cycle and step-wise updated duty cycle is negligible.

The **phase** parameter is useful when multiple oscillators are used, as it allows you to adjust the relative positions of the waveforms.

**Note on timing constraints:** The LEDC peripheral applies a new duty cycle at the start of each carrier period. At a 1 kHz carrier, updates more frequent than every 1 ms provide no additional benefit. In practice, with a 64-sample LUT, the maximum meaningful flicker frequency is approximately 1000 / 64 = 15 Hz at full waveform resolution. At higher flicker frequencies, a smaller LUT (e.g., 32 or 16 samples) should be used to stay within this constraint.

---

### LED Control: Central Group (Group 9)

#### Role and Constraints

The central group uses warm white LEDs and acts as a background ambient light source. It does not flicker and is never driven by one of the 4 main oscillators. Its only controllable parameter is **brightness**, which can evolve smoothly over the duration of a step using either linear interpolation or LFO modulation.

#### Hardware: Sigma-Delta Modulator (SDM)

The ESP32-S3 Sigma-Delta Modulator (SDM) peripheral is used to drive the central group's AL8860 CTRL pin. The SDM generates a high-frequency 1-bit pulse-density stream (internal clock ~1 MHz) whose average density corresponds to the desired duty cycle. This gives:

- **Zero callback overhead**: the SDM hardware runs autonomously, no timer interrupt needed
- **8-bit resolution**: 256 brightness levels
- **Effective carrier ~78 kHz**: completely invisible, no risk of flicker artifact
- **Simple API**: a single function call sets the brightness

```c
// One-time setup
sdm_channel_handle_t sdm_chan;
sdm_config_t config = {
    .clk_src   = SDM_CLK_SRC_DEFAULT,
    .gpio_num  = CENTRAL_LED_GPIO,
    .sample_rate_hz = 1000000,
};
sdm_new_channel(&config, &sdm_chan);
sdm_channel_enable(sdm_chan);

// Update brightness from parameter layer (every 10-50 ms)
// density range: -128 (off) to +127 (full brightness)
int8_t density = (int8_t)(central_brightness * 255 - 128);
sdm_channel_set_pulse_density(sdm_chan, density);
```

#### Brightness Control Modes

The central brightness is updated by the **parameter layer** every 10-50 ms, independently of the oscillator callback. It supports the same two control modes as the main oscillators:

```c
// Parameter layer tick
if (central_mode == LINEAR) {
    central_brightness = start_b + (end_b - start_b) * (elapsed / duration);
}
else if (central_mode == LFO) {
    float lfo_val  = evaluate_lfo(lfo_form, lfo_freq, t);  // 0.0 to 1.0
    central_brightness = b_min + (b_max - b_min) * lfo_val;
}
sdm_channel_set_pulse_density(sdm_chan, brightness_to_density(central_brightness));
```

Example: a 5 Hz sine LFO between 30% and 50% brightness produces a gentle, slow breathing effect on the warm central light, updated at 100 Hz (every 10 ms = 20 samples per LFO period), giving a visually smooth curve.

---

## Sequence Mode of Operation

### Overview

In normal mode of operation, the device is controlled by playing **sequences** composed of ordered **steps**. Each step has a fixed duration and defines, for each of the 4 oscillators, both the waveform parameters and the oscillator-to-group routing, as well as the brightness of the central group. Steps are played back one after another, with optional looping, pause, and seek capabilities.

### Step Definition

A step has one global parameter, a set of per-oscillator parameters for groups 1-8, and a dedicated parameter block for the central group:

**Global parameter:**

| Parameter | Description |
| --------- | ----------- |
| `duration` | Duration of the step in seconds, common to all oscillators |

**Per-oscillator parameters (repeated for oscillators 1 to 4), groups 1-8 only:**

| Parameter | Description |
| --------- | ----------- |
| `groups` | List of LED groups (1-8) driven by this oscillator during this step |
| `waveform` | Waveform shape: sine, square, triangle, or custom |
| `duty` | Duty cycle: fixed value for the duration of the step (triggers LUT rebuild at step start) |
| `phase` | Starting position in the LUT at the beginning of this step (0-360 degrees) |
| `freq` | Frequency control: linear(start_hz, end_hz) or lfo(form, freq_hz, min_hz, max_hz) |
| `brightness` | Brightness control: linear(start_%, end_%) or lfo(form, freq_hz, min_%, max_%) |

**Central group parameters (group 9, always present):**

| Parameter | Description |
| --------- | ----------- |
| `central_brightness` | Brightness control: linear(start_%, end_%) or lfo(form, freq_hz, min_%, max_%) |

### Parameter Control Modes

The two dynamic parameters for the oscillators (freq and brightness) and the central brightness can independently use one of two control modes during a step:

#### Linear Mode

The parameter interpolates smoothly and linearly from a start value to an end value over the duration of the step. Setting start and end to the same value keeps the parameter constant.

```text
Example step - oscillator 1:
  groups:     1, 5, 7
  duration:   20 s
  waveform:   sine
  duty:       50%
  phase:      0 degrees
  freq:       linear  10 Hz -> 12 Hz
  brightness: linear  80%   -> 50%

Central group:
  central_brightness: linear  40% -> 40%   (constant)
```

At each parameter update tick, the current value is:

```text
current_value = start + (end - start) * (elapsed / duration)
```

#### LFO Mode (Low Frequency Oscillator Modulation)

Inspired by classic analog synthesizers (e.g., Korg MS-20/MS-50), a dedicated **Low Frequency Oscillator (LFO)** modulates the parameter rhythmically between a minimum and maximum value at a given LFO frequency and waveform shape. This creates organic, evolving parameter motion within a step.

```text
Example step - oscillator 2:
  groups:     2, 4, 6
  duration:   10 s
  waveform:   sine
  duty:       50%
  phase:      90 degrees
  freq:       lfo  sine  2 Hz  10 Hz - 20 Hz
  brightness: lfo  triangle  0.5 Hz  60% - 90%

Central group:
  central_brightness: lfo  sine  5 Hz  30% - 50%
```

At each parameter update tick, the LFO value is evaluated and mapped to the parameter range:

```text
lfo_value     = evaluate_lfo(lfo_waveform, lfo_freq, t)   // 0.0 to 1.0
current_value = param_min + (param_max - param_min) * lfo_value
```

**Frequency Modulation and its FLS effect:**

When the LFO is applied to the frequency parameter, the result is a **frequency-modulated (FM) flicker signal**: the visible flicker rate itself oscillates rhythmically between two values. This produces a rich, continuously varying stimulation that differs markedly from a fixed-frequency flash. The perceived effect is a "breathing" or "pulsating" quality to the light, and the range and speed of frequency modulation give precise control over the intensity of this effect. Narrow LFO ranges (e.g., 10-12 Hz) produce subtle vibrato-like variation; wide ranges (e.g., 5-30 Hz) produce dramatic sweeping effects that cross multiple brainwave frequency bands within a single step.

### Sequence Architecture

The full signal flow from the sequence engine down to all LED groups is:

```mermaid
flowchart TD
    SEQ["**SEQUENCER**\nStep 1 -> Step 2 -> ..."]

    SEQ --> P1["**Param Layer - Osc 1**\nfreq / brightness\nlinear or LFO"]
    SEQ --> P2["**Param Layer - Osc 2**\nfreq / brightness\nlinear or LFO"]
    SEQ --> P3["**Param Layer - Osc 3**\nfreq / brightness\nlinear or LFO"]
    SEQ --> P4["**Param Layer - Osc 4**\nfreq / brightness\nlinear or LFO"]
    SEQ --> PC["**Param Layer - Central**\nbrightness only\nlinear or LFO"]

    P1 --> O1["**OSC 1**\nLUT + DDS\nphase accumulator"]
    P2 --> O2["**OSC 2**\nLUT + DDS\nphase accumulator"]
    P3 --> O3["**OSC 3**\nLUT + DDS\nphase accumulator"]
    P4 --> O4["**OSC 4**\nLUT + DDS\nphase accumulator"]
    PC --> SDM["**SDM peripheral**\n~1 MHz hardware\n8-bit resolution"]

    O1 --> DT["**DISPATCH TABLE**\nstep parameter\nch -> oscillator mapping"]
    O2 --> DT
    O3 --> DT
    O4 --> DT

    DT --> LEDC["**LEDC - 8 channels**\n1 kHz carrier / 10-bit\ngroups 1-8"]
    SDM --> AL9["**AL8860**\n333 mA - group 9"]

    LEDC --> AL18["**AL8860 x8**\n333 mA each\ngroups 1-8"]

    AL18 --> G18["**LED groups 1-8**\ncold white / flickering"]
    AL9  --> G9["**LED group 9**\nwarm white / ambient"]

    style SEQ      fill:#4A90D9,color:#fff,stroke:#2c6fad
    style DT       fill:#E8A838,color:#fff,stroke:#b07820
    style SDM      fill:#7B68EE,color:#fff,stroke:#5040cc
    style LEDC     fill:#7B68EE,color:#fff,stroke:#5040cc
    style AL18     fill:#5BAD6F,color:#fff,stroke:#3a8050
    style AL9      fill:#5BAD6F,color:#fff,stroke:#3a8050
    style G18      fill:#2E7D32,color:#fff,stroke:#1a5c1e
    style G9       fill:#E65100,color:#fff,stroke:#bf360c
```

### Timing Layers Summary

| Layer | Update Rate | Responsibility |
| ----- | ----------- | -------------- |
| Sequencer | Every few seconds | Advance to next step, rebuild LUTs, update dispatch table |
| Parameter layer | Every 10-50 ms | Linear interp or LFO update for all oscillators and central group |
| Phase accumulator | Every 1 ms (timer callback) | Drive all 4 oscillators, dispatch to 8 LEDC channels |
| LEDC peripheral | Every 1 ms (carrier period) | Output PWM to AL8860 channels 1-8 |
| SDM peripheral | Autonomous (~1 MHz) | Output pulse-density signal to AL8860 channel 9 |

## Real-Time Mode of Operation

In real-time mode, the notion of steps does not exist as it does in sequence mode. Therefore, the linear mode of the oscillator is replaced by a fixed-value mode.

### Parameter Control Modes

The two dynamic parameters for the oscillators (freq and brightness) and the central brightness can independently use one of two control modes:

#### Fix value Mode

The parameters stay constant.

```text
Example step - oscillator 1:
  groups:     1, 5, 7
  waveform:   sine
  duty:       50%
  phase:      0 degrees
  freq:       linear  10 Hz
  brightness: linear  80%

Central group:
  central_brightness: 40%
```

#### LFO Mode (Low Frequency Oscillator Modulation)

Works as described in the `Sequence mode`

```text
Example step - oscillator 2:
  groups:     2, 4, 6
  waveform:   sine
  duty:       50%
  phase:      90 degrees
  freq:       lfo  sine  2 Hz  10 Hz - 20 Hz
  brightness: lfo  triangle  0.5 Hz  60% - 90%

Central group:
  central_brightness: lfo  sine  5 Hz  30% - 50%
```

## Hardware Prototype

We will build a prototype to validate the hardware using the following main components

- Power Supply
- LEDs board
- Electronic board
- Experiment with a PWM Fan controlled by temperature sensors

### Power Supply

The prototype will be powered by  a [24V 5A 120W Power Supply with 5.5 x 2.5mm DC Output Jack](https://www.amazon.fr/Alimentation-Adaptateur-Transformateur-Convertisseur-Surveillance/dp/B0FWRG2X67/ref=sr_1_7?th=1).

- This voltage will be use to directly by the PicoBuck LED Bulk Converters to power the LED
- A [5A DC-DC Step Down Power Supply Buck Converter](https://fr.aliexpress.com/item/1005005921557535.html) will be used to provide 5V power to the ESP32

### LEDs Board

- We will use 32 x [1W High Power cool white 6500K LED with 20mm star pcb](https://fr.aliexpress.com/item/1005003381591196.html) for the 8 peripheral groups
- We will use 4 x [3W High Power warm white LED with 20mm star pcb](https://fr.aliexpress.com/item/1005003381591196.html) for the central groups
- The Led will be placed on a [150x150 aluminum plate](https://www.amazon.fr/Plaque-aluminium-5083-150mm/dp/B07NYV688B/ref=sr_1_6?th=1) (or a [150x150x25mm Aluminum Heat Sink radiator for LED heat dissipation](https://fr.aliexpress.com/item/32958588818.html)) using [ARCTIC TP-4 : High Performance Thermal Pad 100x100x0,5 mm](https://www.amazon.fr/ARCTIC-TP-4-thermique-performance-irrégularités/dp/B0FZV47ZNR/ref=sxin_10_pa_sp_search_thematic_sspa?th=1)

### Electronic board

For the prototype the electronic will be placed on a Universal Double-Sided prototype PCB Board or on a breadboard.

The main components will be placed on a [90x150 Prototype board](https://www.amazon.fr/gp/product/B0FDQDNRJ2/ref=ox_sc_act_title_1?smid=A524J1LH0U8IN&th=1):

- An [ESP32-S3-DevKitC-1 8MB PSRAM 16MB FLASH N16R8 42Pin](https://fr.aliexpress.com/item/1005007173771226.html) development board using male and female 2.54mm pitch header connectors
- 3 x [PicoBuck LED Driver](https://www.sparkfun.com/picobuck-led-driver.html) providing 9 outputs for the nine LED groups. Coonection to the led will be done using 12 x [3.5mm Screw Terminals](https://www.sparkfun.com/screw-terminals-3-5mm-pitch-2-pin.html) and connection to power supply will use a [DC Barrel Jack](https://www.sparkfun.com/dc-barrel-jack-adapter-female.html)
- One Qwiic connector connected to connect the following test devices using I2C protocol
  - 1-4 x [Adafruit I2C Quad rotary Encoder](https://learn.adafruit.com/adafruit-i2c-quad-rotary-encoder-breakout/arduino) used to test the oscillator behavior
  - 1 x [SparkFun Qwiic OLED Display 128x128](https://www.sparkfun.com/sparkfun-qwiic-oled-display-1-5-in-128x128.html)
- [DC-DC stepdown buck converter](https://fr.aliexpress.com/item/1005005921557535.html) to provide 5V to ESP32 Dev board

Simplified breadboard schematic

<img src="images/HypnoLight_bb.png" style="zoom:40%;" />

## Software Prototype

TBD

## Enclosure Prototype

TBD

Use FreeCAD with MCP to drive 3D printer

## Useful References

- [10pcs 1W 3W 5W high power red/yellow/blue/green/white/warm white/cool white/UV/full spectrum LED emission lamp + 20mm Star PCB - AliExpress](https://fr.aliexpress.com/item/4001113713002.html)
- [10pcs 1W 3W High Power warm white/cool white /natural white/red/green/Blue/Royal blue IR LED with 20mm star pcb - AliExpress](https://fr.aliexpress.com/item/1005003381591196.html)
- [25pcs/100pcs 1W 3W High Power LED Diodes Warm Cold White/Natural White/Red/Green/Blue/Royal Blue With 20mm White Black PCB - AliExpress](https://fr.aliexpress.com/item/1005002960208410.html)
- [AL8860: 40V 1.5A STEP DOWN LED DRIVER WITH INTERNAL SWITCH (AL8860)](https://www.diodes.com/part/view/AL8860)
- [ESP32 PWM Fan Controller \| DroneBot Workshop](https://dronebotworkshop.com/esp32-pwm-fan/)
- [Unit 8Encoder](https://docs.m5stack.com/en/unit/8Encoder)
- [wagiminator/ATtiny412-I2C-Rotary-Encoder: Rotary Encoder with I²C Interface](https://github.com/wagiminator/ATtiny412-I2C-Rotary-Encoder)
- [MIDI Mix \| Akai Professional](https://www.akaipro.com/midimix/)
- [Alimentation 24V 5A 120W](https://www.amazon.fr/Alimentation-Adaptateur-Transformateur-Convertisseur-Surveillance/dp/B0FWRG2X67/ref=sr_1_7?th=1)
- [5A DC-DC Step Down Power Supply Buck Module Converter Voltage Regulator 3.3V 5V 6V 9V 12V 18V 24V](https://fr.aliexpress.com/item/1005005921557535.html)
- Spacer washers
- [Using Temperature-Sensing Diodes with Remote Thermal Sensors](https://ww1.microchip.com/downloads/en/AppNotes/000001839A.pdf)
- [EMC2101 Fan Controller](https://learn.adafruit.com/emc2101-fan-controller-and-temperature-sensor) [2N3904](https://fr.aliexpress.com/item/1005003384179636.html)
- [TMP35/TMP36/TMP37 (Rev. H) Datasheet](https://www.analog.com/media/en/technical-documentation/data-sheets/TMP35_36_37.pdf)  - [TMP36GT9Z TO92](https://fr.aliexpress.com/item/1005009466938545.html)

## SparkFun References

- [PicoBuck LED Driver - SparkFun Electronics](https://www.sparkfun.com/picobuck-led-driver.html)
- [FemtoBuck LED Driver - SparkFun Electronics](https://www.sparkfun.com/femtobuck-led-driver.html)
- [FemtoBuck: 6-36V 350mA constant-current LED driver - github](https://github.com/sparkfun/FemtoBuck)
- [SparkFun Qwiic OLED - (1.3in., 128x64) - SparkFun Electronics](https://www.sparkfun.com/sparkfun-qwiic-oled-1-3in-128x64.html)
- [SparkFun Qwiic OLED Display (1.5 in., 128x128) - SparkFun Electronics](https://www.sparkfun.com/sparkfun-qwiic-oled-display-1-5-in-128x128.html)
- [GitHub PicoBuck: Three-channel current driver for LEDs](https://github.com/sparkfun/PicoBuck)

## Adafruit References

- [Learn Adafruit I2C Quad Rotary Encoder Breakout](https://learn.adafruit.com/adafruit-i2c-quad-rotary-encoder-breakout)
- [Learn Adafruit I2C QT Rotary Encoder](https://learn.adafruit.com/adafruit-i2c-qt-rotary-encoder/overview)
- [Adafruit I2C Stemma QT Rotary Encoder Breakout with NeoPixel](https://www.adafruit.com/product/4991)
- [Adafruit I2C Stemma QT Rotary Encoder Breakout with Encoder](https://www.adafruit.com/product/5880)
- [Adafruit I2C Quad Rotary Encoder Breakout with NeoPixel](https://www.adafruit.com/product/5752)
- [Black Nylon Machine Screw and Stand-off Set – M2.5 Thread](https://www.adafruit.com/product/3299)
- [Adafruit Swirly Aluminum Mounting Grid for 0.1 Spaced PCBs](https://www.adafruit.com/product/5781)
- [STEMMA QT / Qwiic JST SH 4-pin Cable with Premium Female Sockets - 150mm Long](https://www.adafruit.com/product/4397)
- [Adafruit Mini I2C Gamepad with seesaw](https://www.adafruit.com/product/5743)
- [Adafruit EMC2101 I2C PC Fan Controller and Temperature Sensor](https://www.adafruit.com/product/4808)
- [JST PH 2mm 4-pin Vertical Connector (10-pack)](https://www.adafruit.com/product/4390)
- [2-Axis Joystick : Adafruit Industries, Unique & fun DIY electronics and kits](https://www.adafruit.com/product/245)
- [Analog 2-axis Thumb Joystick with Select Button + Breakout Board](https://www.adafruit.com/product/512)
- [Monochrome 0.96 128x64 OLED Graphic Display](https://www.adafruit.com/product/326)
- [Adafruit Monochrome 1.12 128x128 OLED Graphic Display](https://www.adafruit.com/product/5297)
