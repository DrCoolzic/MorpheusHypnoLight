// Ignore Spelling: MHL

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MPHCore.Models;

/// <summary>
/// Supported oscillator waveform shapes.
/// </summary>
[JsonConverter(typeof(StringEnumConverter))]
public enum OscillatorWaveform
{
    Sine,
    Square,
    Triangle,
    Custom
}

/// <summary>
/// Supported LFO waveforms for modulators.
/// </summary>
public enum LfoWaveform
{
    Sine,
    Square
}

/// <summary>
/// Modulator control modes.
/// </summary>
public enum ModulatorMode
{
    Static,
    Linear,
    Lfo
}

/// <summary>
/// JSON converter for a modulator, whose shape depends on its mode.
/// </summary>
public class ModulatorConverter : JsonConverter<Modulator>
{
    public override void WriteJson(JsonWriter writer, Modulator? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        writer.WriteStartObject();
        writer.WritePropertyName("mode");
        writer.WriteValue(value.Mode.ToString().ToLowerInvariant());

        switch (value.Mode)
        {
            case ModulatorMode.Static:
                writer.WritePropertyName("value");
                writer.WriteValue(value.Value);
                break;
            case ModulatorMode.Linear:
                writer.WritePropertyName("start");
                writer.WriteValue(value.Start);
                writer.WritePropertyName("end");
                writer.WriteValue(value.End);
                break;
            case ModulatorMode.Lfo:
                writer.WritePropertyName("waveform");
                writer.WriteValue(value.LfoWaveform.ToString()?.ToLowerInvariant());
                writer.WritePropertyName("lfo_frequency");
                writer.WriteValue(value.LfoFrequency);
                writer.WritePropertyName("low");
                writer.WriteValue(value.Low);
                writer.WritePropertyName("high");
                writer.WriteValue(value.High);
                break;
        }

        writer.WriteEndObject();
    }

    public override Modulator? ReadJson(JsonReader reader, Type objectType, Modulator? existingValue, bool hasExistingValue, JsonSerializer serializer)
    {
        var jObject = JObject.Load(reader);
        var modeToken = jObject["mode"];
        if (modeToken is null || !Enum.TryParse<ModulatorMode>(modeToken.ToString(), true, out var mode))
            throw new JsonSerializationException("Modulator requires a valid 'mode' value (static, linear, or lfo).");

        var modulator = new Modulator { Mode = mode };

        switch (mode)
        {
            case ModulatorMode.Static:
                modulator.Value = GetRequiredDouble(jObject, "value");
                break;
            case ModulatorMode.Linear:
                modulator.Start = GetRequiredDouble(jObject, "start");
                modulator.End = GetRequiredDouble(jObject, "end");
                break;
            case ModulatorMode.Lfo:
                var waveformToken = jObject["waveform"];
                if (waveformToken is null || !Enum.TryParse<LfoWaveform>(waveformToken.ToString(), true, out var lfoWaveform))
                    throw new JsonSerializationException("LFO modulator requires a valid 'waveform' value (sine or square).");
                modulator.LfoWaveform = lfoWaveform;
                modulator.LfoFrequency = GetRequiredDouble(jObject, "lfo_frequency");
                modulator.Low = GetRequiredDouble(jObject, "low");
                modulator.High = GetRequiredDouble(jObject, "high");
                break;
        }

        return modulator;
    }

    private static double GetRequiredDouble(JObject jObject, string propertyName)
    {
        var token = jObject[propertyName];
        if (token is null)
            throw new JsonSerializationException($"Modulator requires '{propertyName}' to be a number.");
        return token.Value<double>();
    }
}

/// <summary>
/// A modulated value: static, linear ramp, or LFO.
/// </summary>
[JsonConverter(typeof(ModulatorConverter))]
public class Modulator : JsonBase
{
    /// <summary>
    /// Modulator mode.
    /// </summary>
    public ModulatorMode Mode { get; set; }

    /// <summary>
    /// Static mode value.
    /// </summary>
    public double? Value { get; set; }

    /// <summary>
    /// Linear ramp start value.
    /// </summary>
    public double? Start { get; set; }

    /// <summary>
    /// Linear ramp end value.
    /// </summary>
    public double? End { get; set; }

    /// <summary>
    /// LFO waveform.
    /// </summary>
    public LfoWaveform? LfoWaveform { get; set; }

    /// <summary>
    /// LFO frequency in hertz.
    /// </summary>
    [JsonProperty("lfo_frequency")]
    public double? LfoFrequency { get; set; }

    /// <summary>
    /// LFO output low value.
    /// </summary>
    public double? Low { get; set; }

    /// <summary>
    /// LFO output high value.
    /// </summary>
    public double? High { get; set; }
}

/// <summary>
/// Represents a single oscillator (one of the 5 fixed outputs PB1..PB4 / CG).
/// </summary>
public class Oscillator : JsonBase
{
    /// <summary>
    /// Waveform shape for this oscillator.
    /// </summary>
    [JsonProperty("waveform")]
    public OscillatorWaveform Waveform { get; set; } = OscillatorWaveform.Square;

    /// <summary>
    /// Initial phase in degrees.
    /// </summary>
    [JsonProperty("phase_degrees")]
    public double PhaseDegrees { get; set; }

    /// <summary>
    /// Frequency modulator in hertz.
    /// </summary>
    [JsonProperty("frequency")]
    public Modulator Frequency { get; set; } = new Modulator { Mode = ModulatorMode.Static, Value = 0.0 };

    /// <summary>
    /// Brightness modulator (0.0 to 1.0).
    /// </summary>
    [JsonProperty("brightness")]
    public Modulator Brightness { get; set; } = new Modulator { Mode = ModulatorMode.Static, Value = 0.0 };

    /// <summary>
    /// Duty cycle modulator (0.0 to 1.0).
    /// </summary>
    [JsonProperty("duty")]
    public Modulator Duty { get; set; } = new Modulator { Mode = ModulatorMode.Static, Value = 0.5 };
}

/// <summary>
/// Represents one step in an MHL sequence.
/// </summary>
public class Step : JsonBase
{
    /// <summary>
    /// Step duration in seconds.
    /// </summary>
    [JsonProperty("duration")]
    public double DurationSeconds { get; set; }

    /// <summary>
    /// Step duration in milliseconds, derived from <see cref="DurationSeconds"/>.
    /// </summary>
    [JsonIgnore]
    public int DurationMs => (int)Math.Round(DurationSeconds * 1000.0);

    /// <summary>
    /// Oscillator settings for this step (5 oscillators).
    /// </summary>
    [JsonProperty("oscillators")]
    public List<Oscillator> Oscillators { get; set; } = new List<Oscillator>(5);

    public Step()
    {
        Oscillators = new List<Oscillator>();
    }
}

/// <summary>
/// A complete MHL sequence loaded from sequence.json.
/// </summary>
public class Sequence : JsonBase
{
    [JsonProperty("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("author")]
    public string? Author { get; set; }

    [JsonProperty("createdAt")]
    public DateTime? CreatedAt { get; set; }

    [JsonProperty("steps")]
    public List<Step> Steps { get; set; } = new List<Step>();

    /// <summary>
    /// Total duration in milliseconds, derived from all steps.
    /// </summary>
    [JsonIgnore]
    public int DurationMs => Steps.Sum(s => s.DurationMs);

    public Sequence()
    {
        Steps = new List<Step>();
    }

    public override string ToString()
    {
        return $"{Name} ({DurationMs}ms)";
    }
}
