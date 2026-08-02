#!/usr/bin/env python3
"""Convert Dream Machine sequences to Morpheus HypnoLight (MHL) format.

Usage (single file):
    python dm_to_mhl_converter.py path/to/sequence.json
    python dm_to_mhl_converter.py --spread path/to/sequence.json

Usage (batch):
    python dm_to_mhl_converter.py path/to/Programmes
    python dm_to_mhl_converter.py --spread path/to/Programmes

For each converted sequence:
- The original Dream Machine ``sequence.json`` is renamed to ``sequence.dm``.
- A new ``sequence.json`` is written in the MHL format.
- If ``son.mp3`` exists, it is copied to ``sound.mp3``.
"""

import argparse
import copy
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
}
BCOEF_DIVISOR = 21


def compute_bcoef(leds: list[str]) -> float:
    """Compute the average brightness correction factor for a list of LEDs.

    Unknown LED names are ignored (factor 0) rather than contributing a
    default factor, so that malformed or unexpected LED names cannot push
    the coefficient above 1.0.
    """
    if not leds:
        return 0.0
    total = 0
    for led in leds:
        factor = LED_FACTORS.get(led.upper())
        if factor is None:
            print(
                f"Warning: unknown LED '{led}', ignored in Bcoef computation.",
                file=sys.stderr,
            )
            continue
        total += factor
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
            min(dm_osc.get("frequencyStart", 0.0), 100.0),
            min(dm_osc.get("frequencyEnd", 0.0), 100.0),
        ),
        "brightness": make_linear_modulator(
            min(round(dm_osc.get("brightnessStart", 0.0) * bcoef / 100.0, 4), 1.0),
            min(round(dm_osc.get("brightnessEnd", 0.0) * bcoef / 100.0, 4), 1.0),
        ),
        "duty": make_linear_modulator(
            round(dm_osc.get("dutyStart", 0.0) / 100.0, 4),
            round(dm_osc.get("dutyEnd", 0.0) / 100.0, 4),
        ),
    }


def _scale_brightness(osc: dict, scale: float) -> dict:
    """Scale the brightness modulator of ``osc`` by ``scale``."""
    scaled = copy.deepcopy(osc)
    modulator = scaled["brightness"]
    if modulator["mode"] == "static":
        modulator["value"] = round(modulator["value"] * scale, 4)
    elif modulator["mode"] == "linear":
        modulator["start"] = round(modulator["start"] * scale, 4)
        modulator["end"] = round(modulator["end"] * scale, 4)
    return scaled


def _replicate_oscillator(
    base_osc: dict, count: int, brightness_scale: float
) -> list[dict]:
    """Create ``count`` copies of ``base_osc`` with brightness scaled and phase offset."""
    replicated: list[dict] = []
    for i in range(count):
        osc = _scale_brightness(base_osc, brightness_scale)
        osc["phase_degrees"] = round(i * (360.0 / count), 1)
        replicated.append(osc)
    return replicated


def _active_oscillators(dm_oscillators: list[dict]) -> list[dict]:
    """Return Dream Machine oscillators that actually drive LEDs."""
    return [osc for osc in dm_oscillators if osc.get("led")]


def convert_step(
    dm_step: dict, *, spread: bool = False, spread_scale: float = 1.0
) -> dict:
    """Convert a Dream Machine step to an MHL step."""
    time_start = dm_step.get("timeStart", 0)
    time_end = dm_step.get("timeEnd", time_start)

    dm_oscillators = dm_step.get("oscillators", [])
    mhl_oscillators: list[dict] = []

    # When only one Dream Machine oscillator is actually used (i.e. has LEDs) and
    # the spread option is enabled, replicate it across the first 4 MHL
    # oscillators so the brightness is distributed over 4 LED groups instead of
    # lighting only one.
    active_oscillators = _active_oscillators(dm_oscillators)
    if spread and len(active_oscillators) == 1:
        base_osc = convert_oscillator(active_oscillators[0])
        mhl_oscillators = _replicate_oscillator(base_osc, 4, spread_scale)
    else:
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


def convert_sequence(
    dm_data: dict, *, spread: bool = False, spread_scale: float = 1.0
) -> dict:
    """Convert a Dream Machine sequence dictionary to an MHL sequence dictionary."""
    created_at = dm_data.get("createdAt")
    if not created_at:
        created_at = datetime.now(timezone.utc).isoformat()

    return {
        "version": dm_data.get("version", "1.0.0"),
        "name": dm_data.get("name", ""),
        "author": dm_data.get("author", ""),
        "createdAt": created_at,
        "steps": [
            convert_step(step, spread=spread, spread_scale=spread_scale)
            for step in dm_data.get("steps", [])
        ],
    }


def convert_single_sequence(
    sequence_path: Path, *, spread: bool = False, spread_scale: float = 1.0
) -> Path:
    """Convert one ``sequence.json`` file and copy the audio file if present."""
    sequence_path = Path(sequence_path)
    if not sequence_path.exists():
        raise FileNotFoundError(f"Sequence file not found: {sequence_path}")

    dm_data = json.loads(sequence_path.read_text(encoding="utf-8"))
    mhl_data = convert_sequence(dm_data, spread=spread, spread_scale=spread_scale)

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


def batch_convert(
    root: Path, *, spread: bool = False, spread_scale: float = 1.0
) -> int:
    """Convert every Dream Machine ``sequence.json`` found recursively under ``root``."""
    root = Path(root)
    converted = 0

    for seq_file in root.rglob("sequence.json"):
        try:
            data = json.loads(seq_file.read_text(encoding="utf-8"))
            if not is_dream_machine_sequence(data):
                print(f"Skipping (already MHL or unknown format): {seq_file}")
                continue
            convert_single_sequence(seq_file, spread=spread, spread_scale=spread_scale)
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
    parser.add_argument(
        "--spread",
        action="store_true",
        help=(
            "When a Dream Machine step contains a single active oscillator, replicate it "
            "across the first 4 MHL oscillators so the output is distributed over "
            "4 LED groups instead of a single one."
        ),
    )
    parser.add_argument(
        "--spread-scale",
        type=float,
        default=1.0,
        metavar="FACTOR",
        help=(
            "Brightness scale factor applied to each replicated oscillator when "
            "--spread is used. 1.0 keeps the original brightness, 0.5 divides it by 2, "
            "0.25 divides it by 4. Use this to balance sequences where single-oscillator "
            "steps would otherwise be much brighter than multi-oscillator steps."
        ),
    )
    args = parser.parse_args()

    path = Path(args.path)
    if not path.exists():
        print(f"Path not found: {path}", file=sys.stderr)
        return 1

    if path.is_file():
        output = convert_single_sequence(
            path, spread=args.spread, spread_scale=args.spread_scale
        )
        print(f"Converted: {output}")
    else:
        count = batch_convert(path, spread=args.spread, spread_scale=args.spread_scale)
        print(f"Converted {count} sequence(s).")

    return 0


if __name__ == "__main__":
    sys.exit(main())
