# Morpheus HypnoLight MAUI Player Integration Plan

Create a `MorpheusHypno` folder inside the `MorpheusHypnoLight` repository that hosts a .NET 9 MAUI solution with `MPHCore` (UI-independent logic) and `MPHEditor` (a single application with runtime Player/Editor modes), selectively importing code from `D:\Projects\DreamMachine\Morpheus` and adapting it to the HypnoLight BLE protocol and sequence format.

## Context

The existing `D:\Projects\DreamMachine\Morpheus` project is a multi-project .NET MAUI solution (`MPHCore`, `MPEditor`, `MPPlayer`, `MPHEditor`, `MPManager`, `GPTest`) built for an earlier Dream Machine lamp. For HypnoLight we keep the .NET MAUI approach but simplify the architecture: the shared layer becomes `MPHCore`, `MPPlayer` and `MPEditor` merge into a single `MPHEditor` application, and `MPManager`/`MPHEditor`/`GPTest` are set aside. The new folder is named `MorpheusHypno` to avoid confusion with both the legacy `Morpheus` project and the `MorpheusHypnoLight` repository root.

## Target Structure

```text
MorpheusHypnoLight/
├── firmware/              # existing ESP-IDF firmware
├── doc/                   # existing documentation (protocol, JSON format, etc.)
├── tools/                 # existing scripts/utilities
└── MorpheusHypno/
    ├── MorpheusHypno.sln
    ├── MPHCore/
    │   └── MPHCore.csproj
    └── MPHEditor/
        ├── MPHEditor.csproj
        ├── App.xaml
        ├── AppShell.xaml
        ├── MauiProgram.cs
        ├── Services/
        ├── ViewModels/
        ├── View/
        └── Controls/
```

`MPHCore` is a plain .NET multi-target project (`net9.0;net9.0-android;net9.0-windows10.0.19041.0`). `MPHEditor` is a MAUI project (`<UseMaui>true</UseMaui>`) that references `MPHCore`.

## Scope for the First Deliverable

- **BLE playback**: scan, connect, play/pause/stop, brightness control.
- **Sequence editing**: oscillators, steps, modulation (static/linear/lfo).
- **Audio playback** synchronized with the sequence.

Out of scope initially: `MPManager`, cloud/server features, advanced localization, random generator, advanced waveform editor, gamepad support.

## Phases

### Phase 1 — Preparation and Selective Copy

1. Create `MorpheusHypnoLight/MorpheusHypno/` and a new `MorpheusHypno.sln`.
2. Copy the legacy `MPHCore` into `MPHCore` and clean out Dream Machine-specific dependencies (server API, gamepad, remote DB, etc.).
3. Create a new `MPHEditor` project and import only the useful pages/controls from `MPEditor` and `MPPlayer` (player + sequence editor).
4. Merge `MauiProgram.cs`, `App.xaml`, and `AppShell.xaml` from the legacy player and editor into `MPHEditor`.

### Phase 2 — HypnoLight Models

1. Align the `Sequence`, `Step`, and `Oscillator` classes with `doc/sequence_format.md` (frequency/brightness/duty modulators, waveform, etc.).
2. Keep `Newtonsoft.Json` if serialization constraints require it, otherwise migrate to `System.Text.Json`.

### Phase 3 — HypnoLight BLE Service

1. Create a new `HypnoLightBleService` inspired by `MPHEditor/Services/BleService.cs` (`Plugin.BLE`).
2. Implement the protocol documented in `doc/ble_protocol.md`: `LOAD_START`, `LOAD_CHUNK`, `LOAD_COMMIT`, `PLAY`, `PAUSE`, `STOP`, `SET_BRIGHTNESS`.
3. Handle MTU negotiation and configurable chunk sizes.

### Phase 4 — Runtime Player/Editor Modes

1. Add a configuration toggle (settings file or build constant) to switch between `Player` and `Editor` modes.
2. Hide or disable editing features when running in `Player` mode.
3. Ensure ViewModels are shared between both modes.

### Phase 5 — Audio

1. Port `Plugin.Maui.Audio` integration from `MPHEditor/Services/SequencePlayerService.cs`.
2. Synchronize audio position with the sequence engine.

### Phase 6 — Build and Validation

1. Build for `net9.0-windows10.0.19041.0`.
2. Build for `net9.0-android`.
3. Validate BLE scan, connection, sequence transfer, and playback commands against the HypnoLight firmware.

## Deliverables

- `MorpheusHypno/MorpheusHypno.sln`
- `MorpheusHypno/MPHCore/MPHCore.csproj` cleaned
- `MorpheusHypno/MPHEditor/MPHEditor.csproj` with Player/Editor modes
- `HypnoLightBleService` implementing the documented GATT protocol
- Sequence models aligned with `doc/sequence_format.md`

## Notes and Risks

- The legacy `MPHCore` still contains a lot of Dream Machine code; progressive cleanup will be required inside `MPHCore`.
- The HypnoLight modulation model does not exist in the legacy code and must be recreated accurately.
- `Plugin.BLE` is already used in the legacy project, so scan/connect logic is largely reusable.
- GATT UUIDs and opcodes are already documented; the main risk is adapting the JSON/sequence model correctly.
