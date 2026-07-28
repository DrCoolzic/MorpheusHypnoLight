using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;

namespace MPHCore.Models;

/// <summary>
/// Represents a single oscillator in a sequence.
/// </summary>
public class Oscillator : JsonBase
{
    [JsonProperty("led")]
    public List<string> LEDs { get; set; } = [];

    [JsonProperty("frequencyStart")]
    public double FrequencyStart { get; set; }

    [JsonProperty("dutyStart")]
    public double DutyStart { get; set; }

    [JsonProperty("brightnessStart")]
    public double BrightnessStart { get; set; }

    [JsonProperty("frequencyEnd")]
    public double FrequencyEnd { get; set; }

    [JsonProperty("dutyEnd")]
    public double DutyEnd { get; set; }

    [JsonProperty("brightnessEnd")]
    public double BrightnessEnd { get; set; }

    [JsonProperty("runtimeType")]
    public string RuntimeType { get; set; } = "legacy";

    public Oscillator()
    {
        LEDs = [];
        FrequencyStart = 0;
        DutyStart = 0;
        BrightnessStart = 0;
        FrequencyEnd = 0;
        DutyEnd = 0;
        BrightnessEnd = 0;
        RuntimeType = "legacy";
    }

    public override string ToString()
    {
        var message = "[";
        foreach (var led in LEDs)
        {
            message += $"{led},";
        }
        message += $"] Freq=({FrequencyStart},{FrequencyEnd}), Duty=({DutyStart},{DutyEnd}), Brightness=({BrightnessStart},{BrightnessEnd})";
        return message;
    }

    /// <summary>
    /// Creates a deep copy of the oscillator.
    /// </summary>
    /// <returns>A new Oscillator instance with the same values.</returns>
    public Oscillator Clone()
    {
        return new Oscillator
        {
            LEDs = new List<string>(LEDs),
            FrequencyStart = FrequencyStart,
            DutyStart = DutyStart,
            BrightnessStart = BrightnessStart,
            FrequencyEnd = FrequencyEnd,
            DutyEnd = DutyEnd,
            BrightnessEnd = BrightnessEnd,
            RuntimeType = RuntimeType
        };
    }
}

/// <summary>
/// Gradient associated with a sequence.
/// </summary>
public class Gradient : JsonBase
{
    [JsonProperty("orientation")]
    public required int Orientation { get; set; }

    [JsonProperty("colors")]
    public required int[] Colors { get; set; }

    public override string ToString()
    {
        string message = $"[{Orientation} (";
        foreach (var color in Colors)
        {
            message += $"{color:X4} ";
        }
        return message + ")]";
    }
}

/// <summary>
/// Represents a step in a sequence.
/// </summary>
public class Step : JsonBase
{
    [JsonProperty("index")]
    public int Index { get; set; }

    [JsonProperty("timeStart")]
    public int TimeStart { get; set; }

    [JsonProperty("timeEnd")]
    public int TimeEnd { get; set; }

    [JsonIgnore]
    public int Duration { get { return TimeEnd > TimeStart ? TimeEnd - TimeStart : 0; } }

    [JsonProperty("oscillators")]
    [JsonConverter(typeof(NonEmptyLedOscillatorListConverter))]
    public List<Oscillator> Oscillators { get; set; }

    [JsonProperty("runtimeType")]
    public string RuntimeType { get; set; } = "legacy";

    public Step()
    {
        Oscillators = new List<Oscillator>();
    }

    public Step(int index, int timeStart, int timeEnd, List<Oscillator> oscillators)
    {
        Index = index;
        TimeStart = timeStart;
        TimeEnd = timeEnd;
        Oscillators = oscillators;
        RuntimeType = "legacy";
    }

    public override string ToString()
    {
        var message = $"  Step {Index}: Time=({TimeStart}, {TimeEnd}), Duration=({Duration})";
        foreach (var oscillator in Oscillators)
        {
            message += $"\n{oscillator.ToString()}";
        }
        return message;
    }

    /// <summary>
    /// Creates a deep copy of the step.
    /// </summary>
    /// <returns>A new Step instance with the same values.</returns>
    public Step Clone()
    {
        var clonedOscillators = new List<Oscillator>();
        foreach (var osc in Oscillators)
        {
            clonedOscillators.Add(osc.Clone());
        }

        return new Step(Index, TimeStart, TimeEnd, clonedOscillators)
        {
            RuntimeType = RuntimeType
        };
    }
}


/// <summary>
/// A sequence is a collection of steps with associated parameters such as duration, gradient, etc.
/// This class only contains data from sequence.json file.
/// </summary>
public class Sequence : JsonBase
{
    [JsonProperty("version")]
    public string? Version { get; set; }

    [JsonProperty("author")]
    public string? Author { get; set; }

    [JsonProperty("createdAt")]
    public DateTime? CreatedAt { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("gradient")]
    public Gradient? Gradient { get; set; }

    [JsonProperty("duration")]
    public int Duration { get; set; }

    [JsonProperty("steps")]
    public List<Step> Steps { get; set; } = [];

    public override string ToString()
    {
        return $"{Name} ({Duration}ms)";
    }

    public Sequence()
    {
        Steps = new List<Step>();
    }

    public Sequence(string name, int duration, List<Step> steps) { Name = name; Duration = duration; Steps = steps; }

    public Sequence(string name, int duration, Gradient gradient, List<Step> steps) { Name = name; Duration = duration; Gradient = gradient; Steps = steps; }

}

