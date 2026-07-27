using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MPHCore.Models;

namespace MPHEditor.Utilities;

public static class DmDSP
{
    // Linear generator function
    /// <summary>
    /// Generates an array of linearly spaced values over a specified range.
    /// <para>
    /// The function generates an array of <paramref name="samples"/> evenly spaced values
    /// between <paramref name="start"/> and <paramref name="end"/> (inclusive).
    /// </para>
    /// <para>
    /// The generation is done by linear interpolation between the start and end values,
    /// with the step size determined by the number of samples.
    /// </para>
    /// </summary>
    /// <param name="start">The starting value of the range.</param>
    /// <param name="end">The ending value of the range.</param>
    /// <param name="samples">The number of samples to generate.</param>
    /// <returns>An array of <paramref name="samples"/> evenly spaced double values.</returns>
    public static double[] LinearGenerator(double start, double end, int samples)
    {
        double[] result = new double[samples];
        double step = (end - start) / (samples - 1);

        for (int i = 0; i < samples; i++)
        {
            result[i] = start + step * i;
        }

        return result;
    }

    /// <summary>
    /// Calculates the value of a linear function at a given time.
    /// <para>
    /// The function calculates the value of a linear function at a given time
    /// within a specified duration.
    /// </para>
    /// <para>
    /// The calculation is done by linear interpolation between the start and end values,
    /// with the time as the parameter.
    /// </para>
    /// </summary>
    /// <param name="atTime">The time at which to calculate the value.</param>
    /// <param name="startValue">The starting value of the signal.</param>
    /// <param name="endValue">The ending value of the signal.</param>
    /// <param name="duration">The duration of the signal.</param>
    /// <returns>The calculated value at the given time.</returns>
    public static double LinearValue(double atTime, double startValue, double endValue, double duration)
    {
        return startValue + (endValue - startValue) * (atTime / duration);
    }

    // Step position in sequence
    /// <summary>
    /// Calculates the step index and position in the step of a given time in a sequence.
    /// <para>
    /// The function calculates the step index and position in the step of a given time
    /// in a sequence.
    /// </para>
    /// <para>
    /// The calculation is done by iterating over the steps in the sequence and comparing
    /// the given time with the start and end times of each step.
    /// </para>
    /// </summary>
    /// <param name="atTime">The time at which to calculate the step index and position.</param>
    /// <param name="sequence">The sequence.</param>
    /// <returns>A tuple containing the step index and position in the step.</returns>
    public static (int stepIndex, int posInStep) InStepPos(double atTime, Sequence sequence)
    {
        if (atTime > sequence.Duration)
        {
            return (-1, 0);
        }

        for (int stepIndex = 0; stepIndex < sequence.Steps.Count; stepIndex++)
        {
            var step = sequence.Steps[stepIndex];
            // Use <= for TimeEnd to handle positions exactly at step boundaries
            if (step.TimeStart <= atTime && atTime < step.TimeEnd)
            {
                int posInStep = (int)Math.Round(atTime - step.TimeStart);
                return (stepIndex, posInStep);
            }
        }

        return (-1, 0);
    }

    /// <summary>
    /// Calculates parameters at a given absolute position in a sequence.
    /// <para>
    /// The calculation is done by iterating over the steps in the sequence and comparing
    /// the given time with the start and end times of each step. Then, it iterates over
    /// the oscillators in the step and compares the given position with the start and end
    /// positions of each oscillator.
    /// </para>
    /// </summary>
    /// <param name="atTime">The time in second at which to calculate the parameters.</param>
    /// <param name="sequence">The sequence.</param>
    /// <returns>
    /// A tuple containing the step index, position in the step, and the values of the
    /// oscillators parameters at that position.
    /// </returns>
    public static (int stepIndex, int posInStep, List<(double frequency, double brightness, double dutyCycle)> oscValues)
        ParametersAtPos(double atTime, Sequence sequence)
    {
        var oscillatorValues = new List<(double, double, double)>
        {
            (-1.0, 0.0, 0.0),
            (-1.0, 0.0, 0.0),
            (-1.0, 0.0, 0.0),
            (-1.0, 0.0, 0.0)
        };

        var (stepIndex, posInStep) = InStepPos(atTime, sequence);

        if (stepIndex == -1)
            return (stepIndex, posInStep, oscillatorValues);

        var step = sequence.Steps[stepIndex];
        double duration = step.TimeEnd - step.TimeStart;

        for (int oscIndex = 0; oscIndex < step.Oscillators.Count; oscIndex++)
        {
            var osc = step.Oscillators[oscIndex];
            if (osc.LEDs.Count == 0) continue; // Skip if no LEDs

            double fValue = Math.Round(LinearValue(posInStep, osc.FrequencyStart, osc.FrequencyEnd, duration), 1);
            double bValue = Math.Round(LinearValue(posInStep, osc.BrightnessStart, osc.BrightnessEnd, duration), 1);
            double dValue = Math.Round(LinearValue(posInStep, osc.DutyStart, osc.DutyEnd, duration), 1);

            oscillatorValues[oscIndex] = (fValue, bValue, dValue);
        }

        return (stepIndex, posInStep, oscillatorValues);
    }

    // PWM Generator
    /// <summary>
    /// Generates an array of PWM (Pulse Width Modulation) values corresponding to the specified
    /// time array <paramref name="t"/>, with frequency starting at <paramref name="f0"/>,
    /// ending at <paramref name="f1"/>, and duty cycle array <paramref name="d"/>.
    /// </summary>
    /// <para>
    /// frequency-swept and duty_cycle-swept pulse width generator where "f0*t + 0.5*((f1-f0)/t[-1])*t*t" 
    /// is the integral of "f0+(f1-f0)/t[-1])*t" from t[0] to t[-1]
    /// </para>
    /// <param name="t">The time array.</param>
    /// <param name="f0">The starting frequency.</param>
    /// <param name="f1">The ending frequency.</param>
    /// <param name="d">The duty cycle array.</param>
    /// <returns>An array of PWM values.</returns>
    public static int[] PWMGen(double[] t, double f0, double f1, double[] d)
    {
        int[] pwm = new int[t.Length];
        double tEnd = t[^1];
        for (int i = 0; i < t.Length; i++)
        {
            double p = f0 * t[i] + 0.5 * ((f1 - f0) / tEnd) * t[i] * t[i];
            pwm[i] = p % 1 < d[i] ? 1 : 0;
        }
        return pwm;
    }
}


