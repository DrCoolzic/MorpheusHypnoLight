# Morpheus HypnoLight Sequence File Format

## Purpose

The JSON sequence format is the persistent interchange format used by Morpheus Player, Morpheus Editor, and the firmware generation tools. It is human-readable and independent of the compact binary format used for Bluetooth Low Energy transport.

`generate_sequence.py` will use this JSON format as its source when generating either C test data or compact binary sequence data.

## JSON Format (`.json`)

### File Structure

```json
{
  "version": "1.0.0",
  "name": "Morpheus Demo",
  "author": "Author Name",
  "createdAt": "2026-07-23T18:30:00+02:00",
  "steps": [
    {
      "duration": 5.0,
      "oscillators": [
        {
          "waveform": "sine",
          "phase_degrees": 0,
          "frequency": { "mode": "static", "value": 10.0 },
          "brightness": { "mode": "static", "value": 0.5 },
          "duty": { "mode": "static", "value": 0.5 }
        },
        {
          "waveform": "sine",
          "phase_degrees": 90,
          "frequency": { "mode": "linear", "start": 8.0, "end": 12.0 },
          "brightness": { "mode": "linear", "start": 0.2, "end": 0.8 },
          "duty": { "mode": "static", "value": 0.5 }
        },
        {
          "waveform": "triangle",
          "phase_degrees": 180,
          "frequency": {
            "mode": "lfo",
            "waveform": "sine",
            "lfo_frequency": 0.5,
            "low": 6.0,
            "high": 9.0
          },
          "brightness": { "mode": "static", "value": 0.5 },
          "duty": { "mode": "static", "value": 0.5 }
        },
        {
          "waveform": "square",
          "phase_degrees": 270,
          "frequency": { "mode": "static", "value": 5.0 },
          "brightness": {
            "mode": "lfo",
            "waveform": "square",
            "lfo_frequency": 1.0,
            "low": 0.2,
            "high": 0.7
          },
          "duty": { "mode": "linear", "start": 0.2, "end": 0.8 }
        },
        {
          "waveform": "sine",
          "phase_degrees": 0,
          "frequency": { "mode": "static", "value": 0.5 },
          "brightness": { "mode": "static", "value": 0.4 },
          "duty": { "mode": "static", "value": 0.5 }
        }
      ]
    }
  ]
}
```

## Root Object

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `version` | string | Yes | Sequence format version using semantic versioning. Version 1 is written as `"1.0.0"`. |
| `name` | string | Yes | Non-empty sequence name displayed by Morpheus Player and Morpheus Editor. |
| `author` | string | No | Sequence author name. |
| `createdAt` | string | No | ISO 8601 creation timestamp including a timezone or UTC suffix when available. |
| `steps` | array | Yes | Ordered, non-empty array of sequence steps. |

The root object does not contain a duration field. Total sequence duration is the sum of all step durations.

The format does not contain a gradient. Display colors and other editor presentation settings are application concerns and are not part of the playback sequence.

## Step Object

A step defines the state and modulation of all five HypnoLight oscillators for a relative period of time. Array order defines playback order, so no step index is stored.

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `duration` | number | Yes | Step duration in seconds. Resolution is 0.1 s; valid compact-format range is 0.1 to 6 553.5 s. |
| `oscillators` | array | Yes | Exactly five oscillator objects, ordered by firmware oscillator ID from 0 to 4. |

Steps use a relative duration instead of absolute `timeStart` and `timeEnd` timestamps. Applications calculate an absolute position by accumulating the durations of preceding steps.

## Oscillator Object

Each oscillator object configures one of the five physical HypnoLight LED outputs.

| Field | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `waveform` | string | No | `"square"` | Main oscillator waveform: `"sine"`, `"square"`, or `"triangle"`. |
| `phase_degrees` | number | No | `0` | Initial phase in degrees in the range 0 inclusive to 360 exclusive. |
| `frequency` | object | Yes | - | Frequency modulator in hertz. |
| `brightness` | object | Yes | - | Normalized brightness modulator in the range 0.0 to 1.0. |
| `duty` | object | No | Static value `0.5` | Normalized duty-cycle modulator in the range 0.0 to 1.0. It affects square and triangle waveforms. |

The `"custom"` waveform is reserved for a future format revision because version 1 does not define how custom LUT samples are stored or referenced.

A main oscillator frequency of `0` Hz produces a constant waveform output of 1.0. Brightness still controls the final LED output.

## Modulator Objects

The same three modulator modes can control `frequency`, `brightness`, and `duty`. Only fields belonging to the selected mode are allowed.

### Static Mode

Static mode holds a constant value for the complete step.

```json
{ "mode": "static", "value": 10.0 }
```

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `mode` | string | Yes | Must be `"static"`. |
| `value` | number | Yes | Constant output value. |

### Linear Mode

Linear mode interpolates from `start` to `end` over the complete step duration.

```json
{ "mode": "linear", "start": 0.2, "end": 0.8 }
```

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `mode` | string | Yes | Must be `"linear"`. |
| `start` | number | Yes | Value at the beginning of the step. |
| `end` | number | Yes | Value at the end of the step. |

No separate linear duration is stored in version 1. The step duration is always used.

### LFO Mode

LFO mode continuously oscillates between `low` and `high` during the step.

```json
{
  "mode": "lfo",
  "waveform": "sine",
  "lfo_frequency": 0.5,
  "low": 0.2,
  "high": 0.8
}
```

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `mode` | string | Yes | Must be `"lfo"`. |
| `waveform` | string | Yes | LFO waveform: `"sine"` or `"square"`. |
| `lfo_frequency` | number | Yes | LFO frequency in hertz; must be greater than zero. |
| `low` | number | Yes | Minimum generated value. |
| `high` | number | Yes | Maximum generated value; must be greater than or equal to `low`. |

## Parameter Ranges

Ranges apply to every value produced by static, linear, or LFO modulation.

| Target | JSON unit | Valid range | Compact representation |
| --- | --- | --- | --- |
| Step duration | seconds | 0.1 to 6 553.5 | Seconds ×10 in `uint16_t` |
| Main frequency | hertz | 0.0 to 100.0 | Hertz ×10 in `uint16_t` |
| LFO frequency | hertz | 0.1 to 6 553.5 | Hertz ×10 in `uint16_t` |
| Brightness | normalized ratio | 0.0 to 1.0 | Value ×100 in `uint8_t` |
| Duty cycle | normalized ratio | 0.0 to 1.0 | Value ×100 in `uint8_t` |
| Phase | degrees | 0.0 to less than 360.0 | Converted to radians ×10 in `uint8_t` |

Although the compact encoding can represent main frequencies above 100 Hz, firmware version 1 limits the main oscillator output to 100 Hz.

For an LFO modulating frequency, `low` and `high` use the main frequency range. For an LFO modulating brightness or duty, `low` and `high` use the normalized 0.0 to 1.0 range.

## Validation Rules

1. The root object must contain only one supported major format version.
2. `name` must not be empty.
3. `steps` must contain at least one step and no more than the firmware limit of 128 steps.
4. Every step duration must be positive, finite, and representable at 100 ms resolution.
5. Every step must contain exactly five oscillators.
6. Every numeric value must be finite and within the range of its target parameter.
7. Each modulator must contain exactly the fields required by its selected mode.
8. Every LFO frequency must be greater than zero.
9. Every LFO must satisfy `low <= high`.
10. Unknown fields, modes, and waveforms must be rejected to prevent silent format incompatibilities.

## Conversion and Transport

The JSON file is the authoritative application-level sequence representation. It should be saved and exchanged by Morpheus Player and Morpheus Editor.

The compact binary format is a transport and firmware-loading representation derived from the JSON file. It is not intended to replace the JSON file in user storage.

`generate_sequence.py` must validate the complete JSON document before producing output. It will support two output paths:

- C source generation for built-in firmware tests.
- Compact binary generation for BLE transfer and compact loader validation.

The current generator and `demo.json` still use `duration_ms` and omit the root metadata. They must be migrated to this specification before the JSON document can be used unchanged for both output paths.
