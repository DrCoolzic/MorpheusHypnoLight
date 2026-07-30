# Dream Machine to Morpheus HypnoLight converter

This document describes a Python script that converts Dream Machine sequences into the Morpheus HypnoLight (MHL) format.

## Overview

Two conversion modes are required:

1. **Single-sequence mode** – convert one `sequence.json` file.
2. **Batch mode** – convert an entire Dream Machine `Programmes` tree, including every collection and the sequences it contains.

For each converted sequence directory:

- The output file is written as `sequence.json` in the same directory (Morpheus HypnoLight format, immediately usable).
- If a `son.mp3` audio file is present, it is renamed to `sound.mp3`.

## Field conversion

### Sequence metadata

- **Convert:** `version`, `author`, `createdAt`, `name`
- **Ignore:** `duration`, `gradient`

### Steps

- **Convert:** `duration` = `timeEnd - timeStart`
- **Ignore:** `index`, `runtimeType`

### Oscillators (1 to 5)

For each of the 4 Dream Machine oscillators:

| MHL field | Mode | Start value | End value |
|---|---|---|---|
| `frequency` | `linear` | `min(frequencyStart, 100)` | `min(frequencyEnd, 100)` |
| `brightness` | `linear` | `brightnessStart * Bcoef / 100` | `brightnessEnd * Bcoef / 100` |
| `duty` | `linear` | `dutyStart / 100` | `dutyEnd / 100` |
| `waveform` | `square` | — | — |

- `runtimeType` is ignored.
- A missing oscillator, or an oscillator with no LEDs, becomes an oscillator with `brightness` mode = `static` and value = `0`.
- Oscillator 5 is always converted to `brightness` mode = `static`, value = `0`.

## Brightness correction (Bcoef)

In the Dream Machine, each LED has its own brightness factor. The correction coefficient is the average of the factors for the LEDs used in the step.

LED factors:

- `A1` = 1
- `A2`-`A3` = 4
- `A4`–`A5` = 2
- `B1`–`B4` = 3

Formula:

```text
Bcoef = sum of the LED factors used in the step / 21
```

Example:

```text
LEDs = ["A1", "A4", "B3"]
Bcoef = (1 + 2 + 3) / 21 = 0.2857...
brightnessStart = 50 => start = 50 * 0.2857 / 100 = 0.14285 => 0.143
```

## File name and location

### Single-sequence conversion

The script takes a Dream Machine `sequence.json` file as input and:

1. Renames the original Dream Machine file to `sequence.dm` (preserving the source data).
2. Writes the converted Morpheus HypnoLight data as `sequence.json` in the same directory.
3. If a `son.mp3` audio file is present, copies it to `sound.mp3` (the original `son.mp3` is kept so the source remains intact).

### Batch conversion (`Programmes` tree)

The script takes a Dream Machine `Programmes` directory as input and recursively converts every `sequence.json` file found in the tree using the single-sequence conversion rules:

- Rename the original `sequence.json` to `sequence.dm`.
- Write the converted file as `sequence.json`.
- Copy `son.mp3` to `sound.mp3` if it exists.

Files that are not in Dream Machine format (e.g., already converted MHL files) are skipped.
