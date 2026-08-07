using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using MPHCore.Models;
using MPHEditor.Services;
using MPHEditor.Utilities;

namespace MPHEditor.ViewModels;

/// <summary>
/// ViewModel for the realtime step editor page.
/// Loads a single zero-duration step onto the device and exposes play/stop controls.
/// </summary>
public partial class RealtimeEditorViewModel : ObservableObject
{
    private readonly IBleService _bleService;
    private readonly ILogger<RealtimeEditorViewModel> _logger;
    private CancellationTokenSource? _updateCts;
    private CancellationTokenSource? _brightnessCts;

    [ObservableProperty]
    public partial Step CurrentStep { get; set; }

    [ObservableProperty]
    public partial double Brightness { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    public RealtimeEditorViewModel(IBleService bleService, ILogger<RealtimeEditorViewModel> logger)
    {
        _bleService = bleService;
        _logger = logger;
        CurrentStep = CreateSampleStep();
        Brightness = 80.0;
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
            await _bleService.SendBrightnessAsync((int)Brightness);
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
        IsPlaying = true;
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        _logger.LogInformation("Sending realtime STOP command");
        await _bleService.StopAsync();
        IsPlaying = false;
    }

    [RelayCommand]
    private async Task SaveStepAsync()
    {
        string? name = await Shell.Current.DisplayPromptAsync(
            "Save Step", "Enter a name for this step:", initialValue: "Step");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalidChar, '_');
        }

        string path = Path.Combine(AppDirectories.GetStepsDirectory(), name + ".json");
        if (File.Exists(path))
        {
            bool overwrite = await Shell.Current.DisplayAlertAsync(
                "Save Step", $"'{name}' already exists. Overwrite it?", "Overwrite", "Cancel");
            if (!overwrite)
            {
                return;
            }
        }

        try
        {
            await CurrentStep.SaveJsonFileAsync(path);
            _logger.LogInformation("Step saved to {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save step to {Path}", path);
            await Shell.Current.DisplayAlertAsync("Save Step", $"Failed to save step: {ex.Message}", "OK");
        }
    }

    [RelayCommand]
    private async Task LoadStepAsync()
    {
        string stepsDirectory = AppDirectories.GetStepsDirectory();
        string[] names = Directory.GetFiles(stepsDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (names.Length == 0)
        {
            await Shell.Current.DisplayAlertAsync("Load Step", "No saved steps found.", "OK");
            return;
        }

        string choice = await Shell.Current.DisplayActionSheetAsync("Load Step", "Cancel", null, names);
        if (string.IsNullOrEmpty(choice) || choice == "Cancel")
        {
            return;
        }

        string path = Path.Combine(stepsDirectory, choice + ".json");
        try
        {
            CurrentStep = await JsonBase.LoadJsonFileAsync<Step>(path);
            _logger.LogInformation("Step loaded from {Path}", path);
            await UpdateStepAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load step from {Path}", path);
            await Shell.Current.DisplayAlertAsync("Load Step", $"Failed to load step: {ex.Message}", "OK");
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task UpdateStepAsync()
    {
        _updateCts?.Cancel();
        _updateCts?.Dispose();
        _updateCts = new CancellationTokenSource();
        try
        {
            _logger.LogDebug("Scheduling realtime UPDATE_STEP");
            await Task.Delay(200, _updateCts.Token);
            if (!IsPlaying)
            {
                _logger.LogDebug("Skipping realtime update while stopped");
                return;
            }
            if (!_bleService.IsConnected)
            {
                _logger.LogWarning("Not connected, skipping realtime step update");
                return;
            }
            _logger.LogInformation("Sending realtime UPDATE_STEP");
            await _bleService.UpdateStepAsync(0, CurrentStep);
            _logger.LogInformation("Realtime step updated");
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Realtime step update cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update realtime step");
        }
    }

    partial void OnBrightnessChanged(double value)
    {
        _brightnessCts?.Cancel();
        _brightnessCts?.Dispose();
        _brightnessCts = new CancellationTokenSource();

        _ = DebouncedSendBrightnessAsync((int)value, _brightnessCts.Token);
    }

    private async Task DebouncedSendBrightnessAsync(int value, CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token);

            if (token.IsCancellationRequested)
                return;

            await _bleService.SendBrightnessAsync(value);
            _logger.LogInformation("Brightness changed to: {value}", value);
        }
        catch (OperationCanceledException)
        {
            // Expected when a newer brightness value arrives before the delay expires.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating brightness");
        }
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
