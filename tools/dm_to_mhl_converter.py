#!/usr/bin/env python3
"""Convert Dream Machine sequences to Morpheus HypnoLight (MHL) format.

Usage (single file):
    python dm_to_mhl_converter.py path/to/sequence.json

Usage (batch):
    python dm_to_mhl_converter.py path/to/Programmes

For each converted sequence:
- The original Dream Machine ``sequence.json`` is renamed to ``sequence.dm``.
- A new ``sequence.json`` is written in the MHL format.
- If ``son.mp3`` exists, it is copied to ``sound.mp3``.
"""

import argparse
import json
import shutil
import sys
from datetime import datetime, timezone
from pathlib import Path

# Brightness correction factors per LED.
LED_FACTORS = {
    "A1": 1,
    "A2": 4,
    "A3": 4,
    "A4": 2,
    "A5": 2,
    "B1": 3,
    "B2": 3,
    "B3": 3,
    "B4": 3,
    "B5": 3,
    "B6": 3,
    "B7": 3,
}
BCOEF_DIVISOR = 21


def compute_bcoef(leds: list[str]) -> float:
    """Compute the average brightness correction factor for a list of LEDs."""
    if not leds:
        return 0.0
    total = sum(LED_FACTORS.get(led.upper(), 1) for led in leds)
    return total / BCOEF_DIVISOR


def make_static_modulator(value: float) -> dict:
    return {"mode": "static", "value": value}


def make_linear_modulator(start: float, end: float) -> dict:
    return {"mode": "linear", "start": start, "end": end}


def make_default_oscillator() -> dict:
    """Return an oscillator with brightness off, suitable for unused slots."""
    return {
        "waveform": "square",
        "phase_degrees": 0,
        "frequency": make_static_modulator(0.0),
        "brightness": make_static_modulator(0.0),
        "duty": make_static_modulator(0.5),
    }


def convert_oscillator(dm_osc: dict) -> dict:
    """Convert a Dream Machine oscillator to an MHL oscillator."""
    leds = dm_osc.get("led", [])
    if not leds:
        return make_default_oscillator()

    bcoef = compute_bcoef(leds)

    return {
        "waveform": "square",
        "phase_degrees": 0,
        "frequency": make_linear_modulator(
            dm_osc.get("frequencyStart", 0.0),
            dm_osc.get("frequencyEnd", 0.0),
        ),
        "brightness": make_linear_modulator(
            round(dm_osc.get("brightnessStart", 0.0) * bcoef / 100.0, 4),
            round(dm_osc.get("brightnessEnd", 0.0) * bcoef / 100.0, 4),
        ),
        "duty": make_linear_modulator(
            round(dm_osc.get("dutyStart", 0.0) / 100.0, 4),
            round(dm_osc.get("dutyEnd", 0.0) / 100.0, 4),
        ),
    }


def convert_step(dm_step: dict) -> dict:
    """Convert a Dream Machine step to an MHL step."""
    time_start = dm_step.get("timeStart", 0)
    time_end = dm_step.get("timeEnd", time_start)

    dm_oscillators = dm_step.get("oscillators", [])
    mhl_oscillators: list[dict] = []

    # Map the first 4 Dream Machine oscillators to MHL slots 0..3.
    for dm_osc in dm_oscillators[:4]:
        mhl_oscillators.append(convert_oscillator(dm_osc))

    # Fill remaining slots (up to 4) with default "off" oscillators.
    while len(mhl_oscillators) < 4:
        mhl_oscillators.append(make_default_oscillator())

    # Slot 4 (oscillator 5) is always off.
    mhl_oscillators.append(make_default_oscillator())

    return {
        "duration": time_end - time_start,
        "oscillators": mhl_oscillators,
    }


def convert_sequence(dm_data: dict) -> dict:
    """Convert a Dream Machine sequence dictionary to an MHL sequence dictionary."""
    created_at = dm_data.get("createdAt")
    if not created_at:
        created_at = datetime.now(timezone.utc).isoformat()

    return {
        "version": dm_data.get("version", "1.0.0"),
        "name": dm_data.get("name", ""),
        "author": dm_data.get("author", ""),
        "createdAt": created_at,
        "steps": [convert_step(step) for step in dm_data.get("steps", [])],
    }


def convert_single_sequence(sequence_path: Path) -> Path:
    """Convert one ``sequence.json`` file and copy the audio file if present."""
    sequence_path = Path(sequence_path)
    if not sequence_path.exists():
        raise FileNotFoundError(f"Sequence file not found: {sequence_path}")

    dm_data = json.loads(sequence_path.read_text(encoding="utf-8"))
    mhl_data = convert_sequence(dm_data)

    # Preserve the original Dream Machine file.
    original_path = sequence_path.with_suffix(".dm")
    if not original_path.exists():
        sequence_path.rename(original_path)
    else:
        # If .dm already exists, keep the existing original and overwrite
        # sequence.json with the converted version.
        pass

    output_path = sequence_path
    output_path.write_text(
        json.dumps(mhl_data, indent=2, ensure_ascii=False),
        encoding="utf-8",
    )

    # Copy the audio file to the name expected by Morpheus HypnoLight.
    # Prefer an exact ``son.mp3`` match, otherwise fall back to the first
    # ``son_*.mp3`` variant found in the same directory.
    audio_dst = sequence_path.with_name("sound.mp3")
    audio_src = sequence_path.with_name("son.mp3")
    if not audio_src.exists():
        variants = sorted(sequence_path.parent.glob("son_*.mp3"))
        if variants:
            audio_src = variants[0]
    if audio_src.exists():
        shutil.copy2(audio_src, audio_dst)

    return output_path


def is_dream_machine_sequence(data: dict) -> bool:
    """Heuristic check that the JSON is a Dream Machine sequence."""
    steps = data.get("steps", [])
    if not isinstance(steps, list) or not steps:
        return False
    first_step = steps[0]
    if not isinstance(first_step, dict):
        return False
    oscillators = first_step.get("oscillators", [])
    if not isinstance(oscillators, list) or not oscillators:
        return False
    first_osc = oscillators[0]
    return isinstance(first_osc, dict) and (
        "frequencyStart" in first_osc
        or "brightnessStart" in first_osc
        or "dutyStart" in first_osc
    )


def batch_convert(root: Path) -> int:
    """Convert every Dream Machine ``sequence.json`` found recursively under ``root``."""
    root = Path(root)
    converted = 0

    for seq_file in root.rglob("sequence.json"):
        try:
            data = json.loads(seq_file.read_text(encoding="utf-8"))
            if not is_dream_machine_sequence(data):
                print(f"Skipping (already MHL or unknown format): {seq_file}")
                continue
            convert_single_sequence(seq_file)
            converted += 1
            print(f"Converted: {seq_file}")
        except Exception as exc:  # noqa: BLE001
            print(f"Error converting {seq_file}: {exc}", file=sys.stderr)

    return converted


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Convert Dream Machine sequences to Morpheus HypnoLight format."
    )
    parser.add_argument(
        "path",
        help="Path to a sequence.json file or to a Programmes directory.",
    )
    args = parser.parse_args()

    path = Path(args.path)
    if not path.exists():
        print(f"Path not found: {path}", file=sys.stderr)
        return 1

    if path.is_file():
        output = convert_single_sequence(path)
        print(f"Converted: {output}")
    else:
        count = batch_convert(path)
        print(f"Converted {count} sequence(s).")

    return 0


if __name__ == "__main__":
    sys.exit(main())
