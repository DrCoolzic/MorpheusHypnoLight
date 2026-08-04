using System;
using System.Collections.Generic;
using System.Text;
using MPHCore.Models;

namespace MPHCore.Utilities;

/// <summary>
/// Encodes <see cref="Sequence"/> and <see cref="Step"/> models into the MHL compact
/// sequence wire format version 1.0.0, as defined in <c>doc/compact_sequence_format.md</c>.
/// The same byte layout is used for embedded tests, binary files, and BLE transfer.
/// </summary>
public static class CompactSequenceEncoder
{
    private const int HeaderSize = 14;
    private const int MinStepCount = 1;
    private const int MaxStepCount = 128;
    private const int OscillatorsPerStep = 5;
    private const double MaxMainFrequencyHz = 100.0;

    private static readonly uint[] Crc32Table = BuildCrc32Table();

    /// <summary>
    /// Encodes a complete sequence into the compact wire format, including the 14-byte header.
    /// </summary>
    /// <param name="sequence">The sequence to encode.</param>
    /// <returns>The complete compact sequence bytes (header + payload).</returns>
    /// <exception cref="InvalidOperationException">Thrown when the sequence or one of its steps is invalid.</exception>
    public static byte[] EncodeSequence(Sequence sequence)
    {
        ArgumentNullException.ThrowIfNull(sequence);

        if (sequence.Steps.Count < MinStepCount || sequence.Steps.Count > MaxStepCount)
            throw new InvalidOperationException(
                $"Sequence must have between {MinStepCount} and {MaxStepCount} steps, found {sequence.Steps.Count}.");

        var payload = new List<byte>();
        foreach (var step in sequence.Steps)
            payload.AddRange(EncodeStep(step));

        var payloadBytes = payload.ToArray();
        var crc = ComputeCrc32(payloadBytes);

        var buffer = new List<byte>(HeaderSize + payloadBytes.Length);
        buffer.AddRange(Encoding.ASCII.GetBytes("MHLS"));
        buffer.Add(1); // version_major
        buffer.Add(0); // version_minor
        buffer.Add(0); // version_patch
        buffer.Add((byte)sequence.Steps.Count);
        WriteUInt16Le(buffer, (ushort)payloadBytes.Length);
        WriteUInt32Le(buffer, crc);
        buffer.AddRange(payloadBytes);
        return buffer.ToArray();
    }

    /// <summary>
    /// Encodes a single step into its compact wire payload (no sequence header).
    /// </summary>
    /// <param name="step">The step to encode.</param>
    /// <returns>The encoded step bytes: duration followed by exactly five oscillators.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the step is invalid.</exception>
    public static byte[] EncodeStep(Step step)
    {
        ArgumentNullException.ThrowIfNull(step);

        if (step.DurationSeconds < 0)
            throw new InvalidOperationException("Step duration must be greater than or equal to zero.");

        if (step.Oscillators.Count != OscillatorsPerStep)
            throw new InvalidOperationException(
                $"Step must have exactly {OscillatorsPerStep} oscillators, found {step.Oscillators.Count}.");

        var buffer = new List<byte>();
        WriteUInt16Le(buffer, QuantizeDecisecond(step.DurationSeconds));
        foreach (var oscillator in step.Oscillators)
            WriteOscillator(buffer, oscillator);
        return buffer.ToArray();
    }

    private static void WriteOscillator(List<byte> buffer, Oscillator oscillator)
    {
        ArgumentNullException.ThrowIfNull(oscillator);

        buffer.Add(EncodeWaveform(oscillator.Waveform));
        buffer.Add(EncodePhase(oscillator.PhaseDegrees));
        WriteFrequencyModulator(buffer, oscillator.Frequency);
        WriteLevelModulator(buffer, oscillator.Brightness, "Brightness");
        WriteLevelModulator(buffer, oscillator.Duty, "Duty");
    }

    private static void WriteFrequencyModulator(List<byte> buffer, Modulator modulator)
    {
        ArgumentNullException.ThrowIfNull(modulator);

        switch (modulator.Mode)
        {
            case ModulatorMode.Static:
                buffer.Add(0);
                WriteUInt16Le(buffer, QuantizeFrequency(RequireValue(modulator, "Frequency")));
                break;

            case ModulatorMode.Linear:
                buffer.Add(1);
                WriteUInt16Le(buffer, QuantizeFrequency(RequireStart(modulator, "Frequency")));
                WriteUInt16Le(buffer, QuantizeFrequency(RequireEnd(modulator, "Frequency")));
                break;

            case ModulatorMode.Lfo:
                buffer.Add(2);
                buffer.Add(EncodeLfoWaveform(RequireLfoWaveform(modulator, "Frequency")));
                double lfoFrequency = RequireLfoFrequency(modulator, "Frequency");
                if (lfoFrequency <= 0)
                    throw new InvalidOperationException("Frequency LFO modulator requires a positive 'LfoFrequency'.");
                WriteUInt16Le(buffer, QuantizeFrequency(lfoFrequency));
                double low = RequireLow(modulator, "Frequency");
                double high = RequireHigh(modulator, "Frequency");
                if (low > high)
                    throw new InvalidOperationException("Frequency LFO modulator requires 'Low' <= 'High'.");
                WriteUInt16Le(buffer, QuantizeFrequency(low));
                WriteUInt16Le(buffer, QuantizeFrequency(high));
                break;

            default:
                throw new InvalidOperationException($"Unsupported modulator mode '{modulator.Mode}'.");
        }
    }

    private static void WriteLevelModulator(List<byte> buffer, Modulator modulator, string fieldName)
    {
        ArgumentNullException.ThrowIfNull(modulator);

        switch (modulator.Mode)
        {
            case ModulatorMode.Static:
                buffer.Add(0);
                buffer.Add(QuantizeNormalized(RequireValue(modulator, fieldName)));
                break;

            case ModulatorMode.Linear:
                buffer.Add(1);
                buffer.Add(QuantizeNormalized(RequireStart(modulator, fieldName)));
                buffer.Add(QuantizeNormalized(RequireEnd(modulator, fieldName)));
                break;

            case ModulatorMode.Lfo:
                buffer.Add(2);
                buffer.Add(EncodeLfoWaveform(RequireLfoWaveform(modulator, fieldName)));
                double lfoFrequency = RequireLfoFrequency(modulator, fieldName);
                if (lfoFrequency <= 0)
                    throw new InvalidOperationException($"{fieldName} LFO modulator requires a positive 'LfoFrequency'.");
                WriteUInt16Le(buffer, QuantizeFrequency(lfoFrequency));
                double low = RequireLow(modulator, fieldName);
                double high = RequireHigh(modulator, fieldName);
                if (low > high)
                    throw new InvalidOperationException($"{fieldName} LFO modulator requires 'Low' <= 'High'.");
                buffer.Add(QuantizeNormalized(low));
                buffer.Add(QuantizeNormalized(high));
                break;

            default:
                throw new InvalidOperationException($"Unsupported modulator mode '{modulator.Mode}'.");
        }
    }

    private static double RequireValue(Modulator m, string fieldName) =>
        m.Value ?? throw new InvalidOperationException($"{fieldName} static modulator requires 'Value'.");

    private static double RequireStart(Modulator m, string fieldName) =>
        m.Start ?? throw new InvalidOperationException($"{fieldName} linear modulator requires 'Start'.");

    private static double RequireEnd(Modulator m, string fieldName) =>
        m.End ?? throw new InvalidOperationException($"{fieldName} linear modulator requires 'End'.");

    private static double RequireLow(Modulator m, string fieldName) =>
        m.Low ?? throw new InvalidOperationException($"{fieldName} LFO modulator requires 'Low'.");

    private static double RequireHigh(Modulator m, string fieldName) =>
        m.High ?? throw new InvalidOperationException($"{fieldName} LFO modulator requires 'High'.");

    private static double RequireLfoFrequency(Modulator m, string fieldName) =>
        m.LfoFrequency ?? throw new InvalidOperationException($"{fieldName} LFO modulator requires 'LfoFrequency'.");

    private static LfoWaveform RequireLfoWaveform(Modulator m, string fieldName) =>
        m.LfoWaveform ?? throw new InvalidOperationException($"{fieldName} LFO modulator requires 'LfoWaveform'.");

    /// <summary>
    /// Encodes the main oscillator waveform. Wire codes are fixed by the format
    /// specification and must not be derived from the C# enum ordinal.
    /// </summary>
    private static byte EncodeWaveform(OscillatorWaveform waveform) => waveform switch
    {
        OscillatorWaveform.Square => 0,
        OscillatorWaveform.Sine => 1,
        OscillatorWaveform.Triangle => 2,
        _ => throw new InvalidOperationException(
            $"Waveform '{waveform}' is not supported by compact sequence format version 1."),
    };

    /// <summary>
    /// Encodes an LFO waveform. Wire codes are fixed by the format specification.
    /// </summary>
    private static byte EncodeLfoWaveform(LfoWaveform waveform) => waveform switch
    {
        LfoWaveform.Sine => 0,
        LfoWaveform.Square => 1,
        _ => throw new InvalidOperationException(
            $"LFO waveform '{waveform}' is not supported by compact sequence format version 1."),
    };

    /// <summary>
    /// Encodes a phase in degrees to the 1-byte wire code (radians x10, modulo 63).
    /// </summary>
    private static byte EncodePhase(double phaseDegrees)
    {
        double radians = phaseDegrees * Math.PI / 180.0;
        long code = (long)Math.Floor(radians * 10.0 + 0.5);
        int wrapped = (int)(((code % 63) + 63) % 63);
        return (byte)wrapped;
    }

    /// <summary>
    /// Quantizes a step duration in seconds to deciseconds (uint16, seconds x10).
    /// </summary>
    private static ushort QuantizeDecisecond(double seconds)
    {
        double scaled = Math.Floor(seconds * 10.0 + 0.5);
        if (scaled < 0 || scaled > ushort.MaxValue)
            throw new InvalidOperationException($"Step duration {seconds}s is out of the encodable range.");
        return (ushort)scaled;
    }

    /// <summary>
    /// Quantizes a frequency in Hz (0 to 100 Hz) to the uint16 wire code (Hz x10).
    /// </summary>
    private static ushort QuantizeFrequency(double hz)
    {
        if (hz < 0 || hz > MaxMainFrequencyHz)
            throw new InvalidOperationException($"Frequency {hz} Hz must be between 0 and {MaxMainFrequencyHz} Hz.");
        return (ushort)Math.Floor(hz * 10.0 + 0.5);
    }

    /// <summary>
    /// Quantizes a normalized value (0.0 to 1.0) to the uint8 wire code (value x100).
    /// </summary>
    private static byte QuantizeNormalized(double normalized)
    {
        if (normalized < 0.0 || normalized > 1.0)
            throw new InvalidOperationException($"Normalized value {normalized} must be between 0.0 and 1.0.");
        return (byte)Math.Floor(normalized * 100.0 + 0.5);
    }

    private static void WriteUInt16Le(List<byte> buffer, ushort value)
    {
        buffer.Add((byte)(value & 0xFF));
        buffer.Add((byte)((value >> 8) & 0xFF));
    }

    private static void WriteUInt32Le(List<byte> buffer, uint value)
    {
        buffer.Add((byte)(value & 0xFF));
        buffer.Add((byte)((value >> 8) & 0xFF));
        buffer.Add((byte)((value >> 16) & 0xFF));
        buffer.Add((byte)((value >> 24) & 0xFF));
    }

    /// <summary>
    /// Computes the CRC-32/ISO-HDLC checksum used by the compact sequence format
    /// (polynomial 0x04C11DB7 reflected, init 0xFFFFFFFF, final XOR 0xFFFFFFFF).
    /// This matches Python's <c>zlib.crc32()</c>.
    /// </summary>
    private static uint ComputeCrc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }
}
