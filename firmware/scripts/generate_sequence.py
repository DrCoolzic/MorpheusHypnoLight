#!/usr/bin/env python3
"""Generate firmware/main/test_sequence.c and .h from a JSON sequence.

Usage (from the firmware/ directory):
    python scripts/generate_sequence.py [path/to/sequence.json]

The default input file is sequences/demo.json.
"""

import argparse
import json
import math
import struct
import sys
import zlib
from datetime import datetime
from pathlib import Path

OSCILLATOR_COUNT = 5
SEQUENCE_MAX_STEPS = 128
SUPPORTED_VERSION = "1.0.0"
ROOT_FIELDS = {"version", "name", "author", "createdAt", "steps"}
STEP_FIELDS = {"duration", "oscillators"}
OSCILLATOR_FIELDS = {
    "waveform",
    "phase_degrees",
    "frequency",
    "brightness",
    "duty",
}
MODULATOR_FIELDS = {
    "static": {"mode", "value"},
    "linear": {"mode", "start", "end"},
    "lfo": {"mode", "waveform", "lfo_frequency", "low", "high"},
}
COMPACT_MAGIC = b"MHLS"
COMPACT_VERSION = (1, 0, 0)
COMPACT_HEADER_SIZE = 14
COMPACT_WAVEFORM_CODES = {"square": 0, "sine": 1, "triangle": 2}
COMPACT_MODE_CODES = {"static": 0, "linear": 1, "lfo": 2}
COMPACT_LFO_WAVEFORM_CODES = {"sine": 0, "square": 1}

WAVEFORM_MAP = {
    "sine": "OSCILLATOR_WAVEFORM_SINE",
    "square": "OSCILLATOR_WAVEFORM_SQUARE",
    "triangle": "OSCILLATOR_WAVEFORM_TRIANGLE",
    "custom": "OSCILLATOR_WAVEFORM_CUSTOM",
}

LFO_WAVEFORM_MAP = {
    "sine": "MODULATOR_LFO_WAVEFORM_SINE",
    "square": "MODULATOR_LFO_WAVEFORM_SQUARE",
}

MODE_MAP = {
    "static": "MODULATOR_MODE_STATIC",
    "linear": "MODULATOR_MODE_LINEAR",
    "lfo": "MODULATOR_MODE_LFO",
}


def require_object(value: object, path: str) -> dict:
    if not isinstance(value, dict):
        raise ValueError(f"{path}: expected an object")
    return value


def require_exact_fields(
    value: dict, allowed: set[str], required: set[str], path: str
) -> None:
    unknown = set(value) - allowed
    if unknown:
        raise ValueError(f"{path}: unknown field(s): {', '.join(sorted(unknown))}")
    missing = required - set(value)
    if missing:
        raise ValueError(f"{path}: missing field(s): {', '.join(sorted(missing))}")


def require_number(value: object, minimum: float, maximum: float, path: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise ValueError(f"{path}: expected a number")
    number = float(value)
    if not math.isfinite(number) or number < minimum or number > maximum:
        raise ValueError(f"{path}: expected a finite value in [{minimum}, {maximum}]")
    return number


def duration_to_ms(value: object, path: str) -> int:
    duration = require_number(value, 0.1, 6553.5, path)
    deciseconds = round(duration * 10.0)
    if not math.isclose(duration * 10.0, deciseconds, abs_tol=1e-6):
        raise ValueError(f"{path}: expected 100 ms resolution")
    return deciseconds * 100


def validate_modulator(modulator: object, target: str, path: str) -> None:
    mod = require_object(modulator, path)
    mode = mod.get("mode")
    if not isinstance(mode, str) or mode not in MODULATOR_FIELDS:
        raise ValueError(f"{path}.mode: expected static, linear, or lfo")
    require_exact_fields(mod, MODULATOR_FIELDS[mode], MODULATOR_FIELDS[mode], path)

    value_maximum = 100.0 if target == "frequency" else 1.0
    if mode == "static":
        require_number(mod["value"], 0.0, value_maximum, f"{path}.value")
        return
    if mode == "linear":
        require_number(mod["start"], 0.0, value_maximum, f"{path}.start")
        require_number(mod["end"], 0.0, value_maximum, f"{path}.end")
        return

    if not isinstance(mod["waveform"], str) or mod["waveform"] not in LFO_WAVEFORM_MAP:
        raise ValueError(f"{path}.waveform: expected sine or square")
    require_number(mod["lfo_frequency"], 0.1, 6553.5, f"{path}.lfo_frequency")
    low = require_number(mod["low"], 0.0, value_maximum, f"{path}.low")
    high = require_number(mod["high"], 0.0, value_maximum, f"{path}.high")
    if low > high:
        raise ValueError(f"{path}: low must be less than or equal to high")


def validate_sequence(data: object) -> dict:
    root = require_object(data, "sequence")
    require_exact_fields(root, ROOT_FIELDS, {"version", "name", "steps"}, "sequence")

    if root["version"] != SUPPORTED_VERSION:
        raise ValueError(f"sequence.version: expected {SUPPORTED_VERSION}")
    if not isinstance(root["name"], str) or not root["name"].strip():
        raise ValueError("sequence.name: expected a non-empty string")
    if "author" in root and not isinstance(root["author"], str):
        raise ValueError("sequence.author: expected a string")
    if "createdAt" in root:
        if not isinstance(root["createdAt"], str):
            raise ValueError("sequence.createdAt: expected an ISO 8601 string")
        try:
            datetime.fromisoformat(root["createdAt"].replace("Z", "+00:00"))
        except ValueError as exc:
            raise ValueError("sequence.createdAt: expected an ISO 8601 string") from exc

    steps = root["steps"]
    if not isinstance(steps, list) or not 1 <= len(steps) <= SEQUENCE_MAX_STEPS:
        raise ValueError(f"sequence.steps: expected 1 to {SEQUENCE_MAX_STEPS} steps")

    for step_index, step_value in enumerate(steps):
        step_path = f"sequence.steps[{step_index}]"
        step = require_object(step_value, step_path)
        require_exact_fields(step, STEP_FIELDS, STEP_FIELDS, step_path)
        duration_to_ms(step["duration"], f"{step_path}.duration")

        oscillators = step["oscillators"]
        if not isinstance(oscillators, list) or len(oscillators) != OSCILLATOR_COUNT:
            raise ValueError(
                f"{step_path}.oscillators: expected {OSCILLATOR_COUNT} oscillators"
            )

        for oscillator_index, oscillator_value in enumerate(oscillators):
            oscillator_path = f"{step_path}.oscillators[{oscillator_index}]"
            oscillator = require_object(oscillator_value, oscillator_path)
            require_exact_fields(
                oscillator,
                OSCILLATOR_FIELDS,
                {"frequency", "brightness"},
                oscillator_path,
            )
            waveform = oscillator.get("waveform", "square")
            if not isinstance(waveform, str) or waveform not in {
                "sine",
                "square",
                "triangle",
            }:
                raise ValueError(
                    f"{oscillator_path}.waveform: expected sine, square, or triangle"
                )
            phase = require_number(
                oscillator.get("phase_degrees", 0.0),
                0.0,
                360.0,
                f"{oscillator_path}.phase_degrees",
            )
            if phase >= 360.0:
                raise ValueError(
                    f"{oscillator_path}.phase_degrees: expected a value in [0, 360)"
                )
            validate_modulator(
                oscillator["frequency"],
                "frequency",
                f"{oscillator_path}.frequency",
            )
            validate_modulator(
                oscillator["brightness"],
                "brightness",
                f"{oscillator_path}.brightness",
            )
            validate_modulator(
                oscillator.get("duty", {"mode": "static", "value": 0.5}),
                "duty",
                f"{oscillator_path}.duty",
            )

    return root


def quantize(value: float, scale: float) -> int:
    return math.floor(value * scale + 0.5)


def encode_u16(value: int, path: str) -> bytes:
    if not 0 <= value <= 0xFFFF:
        raise ValueError(f"{path}: value {value} does not fit in uint16_t")
    return struct.pack("<H", value)


def encode_modulator(modulator: dict, target: str, path: str) -> bytes:
    mode = modulator["mode"]
    encoded = bytearray([COMPACT_MODE_CODES[mode]])
    scale = 10.0 if target == "frequency" else 100.0

    def encode_value(value: float, value_path: str) -> bytes:
        code = quantize(float(value), scale)
        if target == "frequency":
            return encode_u16(code, value_path)
        if not 0 <= code <= 100:
            raise ValueError(
                f"{value_path}: value {code} does not fit the target range"
            )
        return bytes([code])

    if mode == "static":
        encoded.extend(encode_value(modulator["value"], f"{path}.value"))
    elif mode == "linear":
        encoded.extend(encode_value(modulator["start"], f"{path}.start"))
        encoded.extend(encode_value(modulator["end"], f"{path}.end"))
    else:
        encoded.append(COMPACT_LFO_WAVEFORM_CODES[modulator["waveform"]])
        encoded.extend(
            encode_u16(
                quantize(float(modulator["lfo_frequency"]), 10.0),
                f"{path}.lfo_frequency",
            )
        )
        encoded.extend(encode_value(modulator["low"], f"{path}.low"))
        encoded.extend(encode_value(modulator["high"], f"{path}.high"))
    return bytes(encoded)


def encode_oscillator(oscillator: dict, path: str) -> bytes:
    waveform = oscillator.get("waveform", "square")
    phase_degrees = float(oscillator.get("phase_degrees", 0.0))
    phase_radians = math.radians(phase_degrees)
    phase_code = quantize(phase_radians, 10.0) % 63
    duty = oscillator.get("duty", {"mode": "static", "value": 0.5})

    encoded = bytearray([COMPACT_WAVEFORM_CODES[waveform], phase_code])
    encoded.extend(
        encode_modulator(oscillator["frequency"], "frequency", f"{path}.frequency")
    )
    encoded.extend(
        encode_modulator(oscillator["brightness"], "brightness", f"{path}.brightness")
    )
    encoded.extend(encode_modulator(duty, "duty", f"{path}.duty"))
    return bytes(encoded)


def encode_step(step: dict, step_index: int) -> bytes:
    path = f"sequence.steps[{step_index}]"
    duration_deciseconds = duration_to_ms(step["duration"], f"{path}.duration") // 100
    encoded = bytearray(encode_u16(duration_deciseconds, f"{path}.duration"))
    for oscillator_index, oscillator in enumerate(step["oscillators"]):
        encoded.extend(
            encode_oscillator(oscillator, f"{path}.oscillators[{oscillator_index}]")
        )
    return bytes(encoded)


def generate_compact(data: dict) -> tuple[bytes, list[int]]:
    data = validate_sequence(data)
    encoded_steps = [
        encode_step(step, step_index) for step_index, step in enumerate(data["steps"])
    ]
    payload = b"".join(encoded_steps)
    header = bytearray(COMPACT_MAGIC)
    header.extend(COMPACT_VERSION)
    header.append(len(encoded_steps))
    header.extend(encode_u16(len(payload), "sequence.payload_length"))
    header.extend(struct.pack("<I", zlib.crc32(payload)))
    if len(header) != COMPACT_HEADER_SIZE:
        raise RuntimeError("invalid compact header size")
    return bytes(header) + payload, [len(step) for step in encoded_steps]


def generate_compact_c(compact: bytes) -> str:
    lines = [
        "#pragma once",
        "",
        "#include <stddef.h>",
        "#include <stdint.h>",
        "",
        "static const uint8_t demo_sequence_compact[] = {",
    ]
    for offset in range(0, len(compact), 12):
        chunk = compact[offset : offset + 12]
        lines.append("  " + ", ".join(f"0x{value:02X}" for value in chunk) + ",")
    lines.extend(
        [
            "};",
            "",
            "static const size_t demo_sequence_compact_size =",
            "    sizeof(demo_sequence_compact);",
            "",
        ]
    )
    return "\n".join(lines)


def emit_static_modulator(mod: dict, target: str, lines: list[str]) -> None:
    lines.append(
        f"      steps[step].oscillators[osc].{target}.mode = {MODE_MAP['static']};"
    )
    lines.append(
        f"      steps[step].oscillators[osc].{target}.static_config.value = "
        f"{float(mod['value'])}f;"
    )


def emit_linear_modulator(mod: dict, target: str, lines: list[str]) -> None:
    lines.append(
        f"      steps[step].oscillators[osc].{target}.mode = {MODE_MAP['linear']};"
    )
    lines.append(
        f"      steps[step].oscillators[osc].{target}.linear_config.start_value = "
        f"{float(mod['start'])}f;"
    )
    lines.append(
        f"      steps[step].oscillators[osc].{target}.linear_config.end_value = "
        f"{float(mod['end'])}f;"
    )
    lines.append(
        f"      steps[step].oscillators[osc].{target}.linear_config.duration_ms = "
        f"{int(mod['duration_ms'])}U;"
    )


def emit_lfo_modulator(mod: dict, target: str, lines: list[str]) -> None:
    lines.append(
        f"      steps[step].oscillators[osc].{target}.mode = {MODE_MAP['lfo']};"
    )
    waveform = LFO_WAVEFORM_MAP[mod["waveform"]]
    lines.append(
        f"      steps[step].oscillators[osc].{target}.lfo_config.waveform = {waveform};"
    )
    lines.append(
        f"      steps[step].oscillators[osc].{target}.lfo_config.frequency_hz = "
        f"{float(mod['lfo_frequency'])}f;"
    )
    lines.append(
        f"      steps[step].oscillators[osc].{target}.lfo_config.low = "
        f"{float(mod['low'])}f;"
    )
    lines.append(
        f"      steps[step].oscillators[osc].{target}.lfo_config.high = "
        f"{float(mod['high'])}f;"
    )


MODULATOR_DISPATCH = {
    "static": emit_static_modulator,
    "linear": emit_linear_modulator,
    "lfo": emit_lfo_modulator,
}


def generate_c(source_name: str, data: dict) -> str:
    data = validate_sequence(data)
    lines: list[str] = [
        f"/* Auto-generated by generate_sequence.py from {source_name}. */",
        '#include "test_sequence.h"',
        "",
        '#include "modulator.h"',
        '#include "oscillator.h"',
        '#include "sequence.h"',
        "",
        "#include <stdint.h>",
        "#include <string.h>",
        "",
        "void build_demo_sequence(sequence_step_t *steps) {",
        "  (void)steps;",
        "",
    ]

    lines.append(
        "  memset(steps, 0, sizeof(sequence_step_t) * SEQUENCE_DEMO_STEP_COUNT);"
    )
    lines.append("")

    steps = data["steps"]
    for step_index, step in enumerate(steps):
        oscillators = step["oscillators"]
        step_duration_ms = duration_to_ms(
            step["duration"], f"sequence.steps[{step_index}].duration"
        )

        lines.append(f"  /* Step {step_index} */")
        lines.append("  {")
        lines.append(f"    const uint32_t step = {step_index}U;")
        lines.append(f"    steps[step].duration_ms = {step_duration_ms}U;")

        for osc_index, osc in enumerate(oscillators):
            waveform = WAVEFORM_MAP[osc.get("waveform", "square")]
            phase_degrees = float(osc.get("phase_degrees", 0.0))
            frequency = osc["frequency"]
            brightness = osc["brightness"]
            duty = osc.get("duty", {"mode": "static", "value": 0.5})

            lines.append("    {")
            lines.append(f"      const uint8_t osc = {osc_index}U;")

            lines.append(
                f"      steps[step].oscillators[osc].static_config.waveform = {waveform};"
            )
            lines.append(
                f"      steps[step].oscillators[osc].static_config.phase_degrees = "
                f"{phase_degrees}f;"
            )
            lines.append(
                "      steps[step].oscillators[osc].static_config.custom_lut = NULL;"
            )

            for target, mod in (
                ("frequency", frequency),
                ("brightness", brightness),
                ("duty", duty),
            ):
                mode = mod["mode"]
                if mode not in MODULATOR_DISPATCH:
                    raise ValueError(
                        f"Step {step_index}, oscillator {osc_index}: "
                        f"unknown modulator mode '{mode}'"
                    )
                emitted_mod = mod
                if mode == "linear":
                    emitted_mod = {**mod, "duration_ms": step_duration_ms}
                MODULATOR_DISPATCH[mode](emitted_mod, f"{target}_modulator", lines)

            lines.append("    }")

        lines.append("  }")

    lines.append("}")
    return "\n".join(lines) + "\n"


def generate_h(source_name: str, step_count: int) -> str:
    guard = "TEST_SEQUENCE_H"
    return f"""/* Auto-generated by generate_sequence.py from {source_name}. */
#ifndef {guard}
#define {guard}

#include "sequence.h"

#ifdef __cplusplus
extern "C" {{
#endif

/** @brief Number of steps in the generated demo sequence. */
#define SEQUENCE_DEMO_STEP_COUNT {step_count}U

/** @brief Populate the provided array with the demo sequence steps. */
void build_demo_sequence(sequence_step_t *steps);

#ifdef __cplusplus
}}
#endif

#endif /* {guard} */
"""


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Generate test_sequence.c/h from a JSON sequence."
    )
    parser.add_argument(
        "input",
        nargs="?",
        default="sequences/demo.json",
        help="JSON input file (default: sequences/demo.json)",
    )
    parser.add_argument(
        "--compact-output",
        help="Optional compact binary output path, relative to firmware/",
    )
    parser.add_argument(
        "--compact-c-output",
        help="Optional compact C header output path, relative to firmware/",
    )
    args = parser.parse_args()

    firmware_dir = Path(__file__).resolve().parent.parent
    input_path = firmware_dir / args.input
    if not input_path.is_file():
        print(f"Error: input file not found: {input_path}", file=sys.stderr)
        return 1

    try:
        with open(input_path, "r", encoding="utf-8") as f:
            data = json.load(f)
        data = validate_sequence(data)
        c_source = generate_c(input_path.name, data)
        h_source = generate_h(input_path.name, len(data["steps"]))
        compact, step_sizes = generate_compact(data)
    except (json.JSONDecodeError, KeyError, ValueError) as exc:
        print(f"Error: {exc}", file=sys.stderr)
        return 1

    main_dir = firmware_dir / "main"
    main_dir.mkdir(parents=True, exist_ok=True)

    c_path = main_dir / "test_sequence.c"
    h_path = main_dir / "test_sequence.h"

    with open(c_path, "w", encoding="utf-8") as f:
        f.write(c_source)
    with open(h_path, "w", encoding="utf-8") as f:
        f.write(h_source)

    print(f"Generated {c_path}")
    print(f"Generated {h_path}")

    if args.compact_output:
        compact_path = firmware_dir / args.compact_output
        compact_path.parent.mkdir(parents=True, exist_ok=True)
        compact_path.write_bytes(compact)
        print(f"Generated {compact_path}")
    if args.compact_c_output:
        compact_c_path = firmware_dir / args.compact_c_output
        compact_c_path.parent.mkdir(parents=True, exist_ok=True)
        compact_c_path.write_text(generate_compact_c(compact), encoding="utf-8")
        print(f"Generated {compact_c_path}")
    if args.compact_output or args.compact_c_output:
        print(
            f"Compact step sizes: {', '.join(str(size) for size in step_sizes)} bytes"
        )
        print(
            f"Compact sequence size: {len(compact)} bytes "
            f"({len(compact) - COMPACT_HEADER_SIZE} bytes payload)"
        )
    return 0


if __name__ == "__main__":
    sys.exit(main())
