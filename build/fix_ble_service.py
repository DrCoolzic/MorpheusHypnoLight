#!/usr/bin/env python3
"""Patch BleService.cs for the new MHL Sequence/Oscillator/Modulator model."""

import re
from pathlib import Path

PATH = Path(r"d:\Projects\DreamMachine\MorpheusHypnoLight\MorpheusHypno\MPHEditor\Services\BleService.cs")

text = PATH.read_text(encoding="utf-8")

# Remove obsolete "if (oscillator.LEDs.Count == 0) / continue" blocks.
lines = text.splitlines(keepends=True)
out = []
i = 0
while i < len(lines):
    if lines[i].strip() == "if (oscillator.LEDs.Count == 0)":
        if i + 1 < len(lines) and lines[i + 1].strip() == "continue;   // skip empty":
            i += 2
            continue
    out.append(lines[i])
    i += 1
text = "".join(out)

# Update duration references for the new model.
text = text.replace("sequence.Duration", "sequence.DurationSeconds")
text = re.sub(r"\bstep\.Duration\b", "(int)(step.DurationSeconds * 10)", text)

# Replace the legacy Dream Machine OscToCommand with a stub.
# The MHL compact wire encoder will be implemented later.
text = re.sub(
    r"    public static byte\[\] OscToCommand\(int stepIndex, Oscillator osc, int oscIndex, int duration\)\s*\{[\s\S]*?return \[\.\. command\];\s*\}",
    r'''    public static byte[] OscToCommand(int stepIndex, Oscillator osc, int oscIndex, int duration)
    {
        // TODO: implement MHL compact wire encoder once the BLE protocol is finalized.
        _ = stepIndex;
        _ = osc;
        _ = oscIndex;
        _ = duration;
        return [];
    }''',
    text,
)

PATH.write_text(text, encoding="utf-8")
print("BleService.cs patched for MHL model.")
