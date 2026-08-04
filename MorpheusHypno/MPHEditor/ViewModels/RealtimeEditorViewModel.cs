using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MPHCore.Models;
using MPHEditor.Services;

namespace MPHEditor.ViewModels;

/// <summary>
/// ViewModel for the realtime step editor page.
/// Loads a single zero-duration step onto the device and exposes play/stop controls.
/// </summary>
public partial class RealtimeEditorViewModel : ObservableObject
{
    private readonly IBleService _bleService;
    private readonly ILogger<RealtimeEditorViewModel> _logger;

    [ObservableProperty]
    public partial Step CurrentStep { get; set; }

    public RealtimeEditorViewModel(IBleService bleService, ILogger<RealtimeEditorViewModel> logger)
    {
        _bleService = bleService;
        _logger = logger;
        CurrentStep = CreateSampleStep();
        _ = LoadSequenceAsync();
    }

    private async Task LoadSequenceAsync()
    {
        if (!_bleService.IsConnected)
        {
            _logger.LogWarning("Not connected, skipping realtime sequence load");
            return;
        }

        var sequence = new Sequence
        {
            Name = "Realtime",
            Steps = new List<Step> { CurrentStep }
        };

        try
        {
            _logger.LogInformation("Loading realtime sequence");
            await _bleService.LoadSequenceAsync(sequence);
            await _bleService.SendBrightnessAsync(80);
            _logger.LogInformation("Realtime sequence loaded");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load realtime sequence");
        }
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        _logger.LogInformation("Sending realtime PLAY command");
        await _bleService.PlayAsync();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        _logger.LogInformation("Sending realtime STOP command");
        await _bleService.StopAsync();
    }

    private static Step CreateSampleStep()
    {
        var step = new Step { DurationSeconds = 0.0 };

        for (int i = 0; i < 5; i++)
        {
            step.Oscillators.Add(new Oscillator
            {
                Waveform = OscillatorWaveform.Sine,
                PhaseDegrees = i * 90.0,
                Frequency = new Modulator { Mode = ModulatorMode.Static, Value = 10.0 + i },
                Brightness = new Modulator { Mode = ModulatorMode.Static, Value = 0.5 },
                Duty = new Modulator { Mode = ModulatorMode.Static, Value = 0.5 },
            });
        }

        return step;
    }
}
