import copy
import json
import unittest
from pathlib import Path

from generate_sequence import (
    COMPACT_HEADER_SIZE,
    generate_c,
    generate_compact,
    validate_sequence,
)

FIRMWARE_DIR = Path(__file__).resolve().parent.parent


class GenerateSequenceTest(unittest.TestCase):
    def setUp(self) -> None:
        demo_path = FIRMWARE_DIR / "sequences" / "demo.json"
        self.demo = json.loads(demo_path.read_text(encoding="utf-8"))

    def assert_invalid(self, data: object, message: str) -> None:
        with self.assertRaisesRegex(ValueError, message):
            validate_sequence(data)

    def test_demo_is_valid_and_matches_checked_in_c(self) -> None:
        validate_sequence(self.demo)
        generated = generate_c("demo.json", self.demo)
        expected = (FIRMWARE_DIR / "main" / "test_sequence.c").read_text(
            encoding="utf-8"
        )
        self.assertEqual(generated, expected)

    def test_compact_golden_vector(self) -> None:
        oscillator = {
            "frequency": {"mode": "static", "value": 10.0},
            "brightness": {"mode": "static", "value": 0.5},
        }
        data = {
            "version": "1.0.0",
            "name": "Golden Static Sequence",
            "steps": [
                {
                    "duration": 1.0,
                    "oscillators": [copy.deepcopy(oscillator) for _ in range(5)],
                }
            ],
        }
        compact, step_sizes = generate_compact(data)
        expected = bytes.fromhex(
            "4D 48 4C 53 01 00 00 01 2F 00 3F F5 DD B1 "
            "0A 00 " + "00 00 00 64 00 00 32 00 32 " * 5
        )
        self.assertEqual(compact, expected)
        self.assertEqual(step_sizes, [47])

    def test_compact_demo_header_and_sizes(self) -> None:
        compact, step_sizes = generate_compact(self.demo)
        payload_length = int.from_bytes(compact[8:10], "little")
        self.assertEqual(compact[:8], b"MHLS\x01\x00\x00\x04")
        self.assertEqual(payload_length, len(compact) - COMPACT_HEADER_SIZE)
        self.assertEqual(payload_length, sum(step_sizes))
        self.assertTrue(all(47 <= size <= 112 for size in step_sizes))

    def test_phase_near_full_turn_has_canonical_zero_code(self) -> None:
        data = copy.deepcopy(self.demo)
        data["steps"][0]["oscillators"][0]["phase_degrees"] = 359
        compact, _ = generate_compact(data)
        first_oscillator_phase_offset = COMPACT_HEADER_SIZE + 2 + 1
        self.assertEqual(compact[first_oscillator_phase_offset], 0)

    def test_optional_metadata_is_validated(self) -> None:
        data = copy.deepcopy(self.demo)
        data["author"] = "Test Author"
        data["createdAt"] = "2026-07-24T09:00:00+02:00"
        validate_sequence(data)
        data["createdAt"] = "not-a-date"
        self.assert_invalid(data, "createdAt")

    def test_required_and_unknown_root_fields_are_rejected(self) -> None:
        data = copy.deepcopy(self.demo)
        del data["name"]
        self.assert_invalid(data, "missing field.*name")
        data = copy.deepcopy(self.demo)
        data["duration"] = 20
        self.assert_invalid(data, "unknown field.*duration")

    def test_unsupported_version_is_rejected(self) -> None:
        data = copy.deepcopy(self.demo)
        data["version"] = "2.0.0"
        self.assert_invalid(data, "version")

    def test_old_step_duration_and_invalid_resolution_are_rejected(self) -> None:
        data = copy.deepcopy(self.demo)
        data["steps"][0]["duration_ms"] = data["steps"][0].pop("duration") * 1000
        self.assert_invalid(data, "unknown field.*duration_ms")
        data = copy.deepcopy(self.demo)
        data["steps"][0]["duration"] = 1.25
        self.assert_invalid(data, "100 ms resolution")

    def test_step_and_oscillator_counts_are_validated(self) -> None:
        data = copy.deepcopy(self.demo)
        data["steps"] = []
        self.assert_invalid(data, "1 to 128 steps")
        data = copy.deepcopy(self.demo)
        data["steps"][0]["oscillators"].pop()
        self.assert_invalid(data, "expected 5 oscillators")

    def test_waveform_and_phase_are_validated(self) -> None:
        data = copy.deepcopy(self.demo)
        data["steps"][0]["oscillators"][0]["waveform"] = "custom"
        self.assert_invalid(data, "expected sine, square, or triangle")
        data = copy.deepcopy(self.demo)
        data["steps"][0]["oscillators"][0]["phase_degrees"] = 360
        self.assert_invalid(data, r"\[0, 360\)")

    def test_modulator_fields_and_ranges_are_validated(self) -> None:
        data = copy.deepcopy(self.demo)
        modulator = data["steps"][2]["oscillators"][0]["frequency"]
        modulator["frequency_hz"] = modulator.pop("lfo_frequency")
        self.assert_invalid(data, "unknown field.*frequency_hz")
        data = copy.deepcopy(self.demo)
        data["steps"][0]["oscillators"][0]["brightness"]["value"] = 1.1
        self.assert_invalid(data, r"\[0.0, 1.0\]")

    def test_lfo_order_and_types_are_validated(self) -> None:
        data = copy.deepcopy(self.demo)
        modulator = data["steps"][2]["oscillators"][0]["frequency"]
        modulator["low"] = 10
        modulator["high"] = 5
        self.assert_invalid(data, "low must be less than or equal to high")
        data = copy.deepcopy(self.demo)
        data["steps"][0]["oscillators"][0]["frequency"]["mode"] = []
        self.assert_invalid(data, "expected static, linear, or lfo")

    def test_optional_defaults_generate_square_and_half_duty(self) -> None:
        data = copy.deepcopy(self.demo)
        oscillator = data["steps"][0]["oscillators"][0]
        oscillator.pop("waveform")
        oscillator.pop("phase_degrees")
        oscillator.pop("duty", None)
        generated = generate_c("defaults.json", data)
        self.assertIn("static_config.waveform = OSCILLATOR_WAVEFORM_SQUARE", generated)
        self.assertIn("static_config.phase_degrees = 0.0f", generated)
        self.assertIn("duty_modulator.static_config.value = 0.5f", generated)


if __name__ == "__main__":
    unittest.main()
