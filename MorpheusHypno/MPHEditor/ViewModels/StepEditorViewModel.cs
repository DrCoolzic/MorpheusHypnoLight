using CommunityToolkit.Mvvm.ComponentModel;
using MPHCore.Models;

namespace MPHEditor.ViewModels;

/**
 * @brief ViewModel for the step editor test page.
 *
 * Exposes a sample Step pre-filled with default oscillators so the editor
 * controls can be visualized immediately.
 */
public partial class StepEditorViewModel : ObservableObject
{
    [ObservableProperty]
    public partial Step CurrentStep { get; set; }

    public StepEditorViewModel()
    {
        CurrentStep = CreateSampleStep();
    }

    private static Step CreateSampleStep()
    {
        var step = new Step { DurationSeconds = 4.0 };

        for (int i = 0; i < 5; i++)
        {
            step.Oscillators.Add(new Oscillator
            {
                Waveform = OscillatorWaveform.Sine,
                PhaseDegrees = i * 90.0,
                Frequency = new Modulator { Mode = ModulatorMode.Static, Value = 1.0 + i },
                Brightness = new Modulator { Mode = ModulatorMode.Static, Value = 50.0 },
                Duty = new Modulator { Mode = ModulatorMode.Static, Value = 50.0 },
            });
        }

        return step;
    }
}
