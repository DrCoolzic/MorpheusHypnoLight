# MPHEditor Step Editor GUI Specification

## Target Platform

Windows first. Tablet may be supported later. Phone is out of scope.

## Rotary Button Behavior

A rotary button behaves like a volume knob:

- Click and drag up or right to rotate clockwise (increase).
- Click and drag down or left to rotate counterclockwise (decrease).
- The current value or selected label is shown in the center.
- Numeric knobs support direct value entry: click the center, type, confirm with Enter, cancel with Escape.
- Keyboard modifiers:
  - **Shift** : coarse step (e.g. +10).
  - **Alt** : fine step (e.g. +0.1).

Numeric values are displayed **below** the rotary button.

## Parameter Ranges

| Parameter     | Range          | Notes                                     |
|---------------|----------------|-------------------------------------------|
| Frequency     | 0.0 – 100.0 Hz | 0 Hz is valid and produces static output  |
| Brightness    | 0.0 – 100.0 %  | Mapped to 0.0 – 1.0 for the firmware      |
| Duty cycle    | 0.0 – 100.0 %  | Mapped to 0.0 – 1.0 for the firmware      |
| Phase         | 0 – 360 deg    |                                           |
| LFO frequency | 0.1 – 10.0 Hz  |                                           |

## Modulator Modes

Modes use short labels: **FIX**, **LIN**, **LFO**.

- **FIX** (static): one rotary button for the fixed value.
- **LIN** (linear): two rotary buttons for start and end values.
- **LFO** : one rotary selector for waveform (sine/square), plus three rotary buttons for LFO frequency, low value, and high value.

When the mode changes, the controls inside the modulator update automatically. Values from the previous mode may be reused where it makes sense (e.g. the current value becomes the FIX value or the LIN start value).

## Oscillator Display

Each oscillator shows, from left to right:

1. **Waveform selector** rotary button.
2. **Phase** rotary button.
3. **Frequency** modulator display.
4. **Brightness** modulator display.
5. **Duty cycle** modulator display.

## Step Display

A step is visualized with 5 oscillator panels stacked vertically.

The step duration is **not** edited here; it is set in the sequence timeline view where the current step position and duration are already shown.