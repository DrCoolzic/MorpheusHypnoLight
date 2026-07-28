# MPH File System

This document describes the on-disk layout of the Morpheus HypnoLight editor (`MPHEditor`) user data.

## Application Data Directory

`MPHEditor` stores all user data under the platform-specific application data directory:

| Platform | Path |
| --- | --- |
| Windows | `%LOCALAPPDATA%\com.drcoolzic.mpheditor` |
| Android | `/storage/emulated/0/Android/data/com.drcoolzic.mpheditor/files` |

> The exact Android path may vary depending on the device and Android version.

## Directory Layout

The application data root contains the following entries:

| Path | Description |
| --- | --- |
| `mpheditor.log` | Application log file |
| `settings.json` | User preferences and application settings |
| `collections/` | Root directory containing all sequence collections |

## Collections

Sequences are grouped into collections. Each collection is a folder under `collections/` and contains one or more sequence subfolders.

A sequence folder contains the files that make up a single sequence:

- `metadata.json` — Sequence metadata (name, author, tags, etc.)
- `sequence.json` — Sequence definition
- `sound.mp3` (optional) — Audio track associated with the sequence

## Example

```text
<AppDataDirectory>
├── mpheditor.log
├── settings.json
└── collections/
    ├── collection1/
    │   ├── sequence1/
    │   │   ├── metadata.json
    │   │   ├── sequence.json
    │   │   └── sound.mp3
    │   └── sequence2/
    │       ├── metadata.json
    │       └── sequence.json
    └── collection2/
        ├── sequence_1/
        │   ├── metadata.json
        │   ├── sequence.json
        │   └── sound.mp3
        └── sequence_2/
            ├── metadata.json
            └── sequence.json
```

## Notes

- Logs and settings live directly in the application data root, not in `Logs/` or `Settings/` subdirectories.
- Each collection is an independent folder and can hold any number of sequences.
- The `sound.mp3` file is optional; a sequence may contain only lighting data.
