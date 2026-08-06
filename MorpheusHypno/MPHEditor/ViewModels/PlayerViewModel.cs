// Ignore Spelling: ble Unmute mvm hh ss

using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MPHCore.Models;
using MPHCore.Services;
using MPHEditor.Services;

namespace MPHEditor.ViewModels;

/* Explanation:
The [QueryProperty] attribute is used in .NET MAUI applications to enable passing data between pages.
Here's a brief explanation:
- It's part of the Shell navigation system in MAUI.
- This attribute allows the PlayerViewModel to receive the MPHSequence object when navigating
  to the page associated with this view model.
*/
[QueryProperty(nameof(MphSequence), "MPHSequence")]

public partial class PlayerViewModel : ObservableObject
{
    private readonly ILogger<PlayerViewModel> _logger;
    private readonly IBleService _bleService;
    private readonly MainViewModel _mvm;
    private readonly IMPHElementService _mes;
    private readonly ISequencePlayerService _sequencePlayerService;

    private TaskCompletionSource<bool>? _sequenceChangeCompletion;
    private MPHSequence? _currentMphSequence;
    private CancellationTokenSource? _brightnessDebounceCts;

    // /////////////////////////////////////////////////
    // Constructor
    // /////////////////////////////////////////////////
    public PlayerViewModel(
        IBleService bleService,
        ILogger<PlayerViewModel> logger,
        MainViewModel mvm,
        IMPHElementService mes,
        ISequencePlayerService sequencePlayerService)
    {
        _bleService = bleService;
        _logger = logger;
        _mvm = mvm;
        _mes = mes;
        _sequencePlayerService = sequencePlayerService;

        // Wire up event handlers for the sequence player service
        _sequencePlayerService.PlayerStateChanged += (sender, state) => SequencePlayerService_PlayerStateChanged(sender, state);
        _sequencePlayerService.PositionChanged += (sender, position) => SequencePlayerService_PositionChanged(sender, position);
        _sequencePlayerService.SequenceCompleted += async (sender, args) => await SequenceEnded();

        // Initialize the play/pause command
        // defining command: 1st param is an async lambda, 2nd param return true (ie cmd always enable)
        PlayPausePlayerCommand = new Command(
            async () =>
            {
                if (PlayerState == PlayerStateEnum.PLAYING)
                    await _sequencePlayerService.PausePlayerAsync();
                else
                {
                    // Apply delay only if starting from stopped state
                    if (DelayEnabled && PlayerState == PlayerStateEnum.STOPPED)
                    {
                        _logger.LogInformation("Delay mode enabled, waiting 5 seconds before starting playback");
                        await StartDelayCountdownAsync();
                    }
                    await _sequencePlayerService.StartPlayerAsync();
                }
            },
            () => true
        );

        BleStatus = _bleService.Status; // Initial status
        _bleService.StatusChanged += (sender, status) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                BleStatus = status;
            });
        };

        IsConnected = _bleService.IsConnected;
        _bleService.ConnectedChanged += (sender, isConnected) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsConnected = isConnected;
            });
        };

        IsConnecting = _bleService.IsConnecting;
        _bleService.ConnectingChanged += (sender, isConnecting) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                IsConnecting = isConnecting;
            });
        };
    }

    #region SequencePlayerService Event Handlers

    /// <summary>
    /// Handler for player state changes from the SequencePlayerService
    /// </summary>
    /// <param name="sender">The sequence player service</param>
    /// <param name="e">The new player state</param>
    private void SequencePlayerService_PlayerStateChanged(object? sender, PlayerStateEnum e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Update UI bindings based on the new player state
            _logger.LogInformation("Player state changed to: {}", e);

            // Update UI properties that depend on player state
            // Using nameof for compile-time safety
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsPaused));
            OnPropertyChanged(nameof(DisplayClock));
            OnPropertyChanged(nameof(DisplayText));
            OnPropertyChanged(nameof(PlayerState)); // For any remaining bindings
        });
    }

    /// <summary>
    /// Handler for position changes from the SequencePlayerService
    /// </summary>
    /// <param name="sender">The sequence player service</param>
    /// <param name="e">The new position in seconds</param>
    private void SequencePlayerService_PositionChanged(object? sender, double e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // No need to store position locally as we get it from the service
            // Just update the UI using nameof for compile-time safety
            OnPropertyChanged(nameof(FormattedPlayerCurrentTime));
            OnPropertyChanged(nameof(FormattedPlayerRemainingTime));
            PlayerCurrentPosition = e; // Update the current position property
        });
    }

    #endregion

    #region Properties

    // Player state UI binding properties that use the SequencePlayerService
    /// <summary>
    /// Gets whether the player is currently in playing state
    /// </summary>
    public bool IsPlaying => _sequencePlayerService.PlayerState == PlayerStateEnum.PLAYING;

    /// <summary>
    /// Gets whether the player is currently in paused state
    /// </summary>
    public bool IsPaused => _sequencePlayerService.PlayerState == PlayerStateEnum.PAUSED;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoopIcon))]
    public partial bool LoopMode { get; set; } = false;
    public string LoopIcon => LoopMode ? "loop.png" : "no_loop.png";

    /// <summary>
    /// Gets or sets whether the delay mode is enabled
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DelayIcon))]
    public partial bool DelayEnabled { get; set; } = false;

    /// <summary>
    /// Gets the delay icon based on the delay mode state
    /// </summary>
    public string DelayIcon => DelayEnabled ? "delay_on.png" : "delay_off.png";

    /// <summary>
    /// Gets or sets the countdown value displayed during delay
    /// </summary>
    [ObservableProperty]
    public partial string DelayCountdown { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Detail { get; set; } = string.Empty;

    [ObservableProperty]
    public partial MPHSequence MphSequence { get; set; } = new();

    [ObservableProperty]
    public partial string Name { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Sequence? Sequence { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetadata))]
    public partial string? Author { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetadata))]
    public partial string? Version { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMetadata))]
    [NotifyPropertyChangedFor(nameof(FormattedCreatedAt))]
    public partial DateTime? CreatedAt { get; set; }

    public string FormattedCreatedAt => CreatedAt?.ToString("yyyy-MM-dd") ?? "";

    // Show metadata line only if at least one field has a value
    public bool HasMetadata => !string.IsNullOrWhiteSpace(Author) || !string.IsNullOrWhiteSpace(Version) || CreatedAt.HasValue;

    [ObservableProperty]
    public partial double PlayerCurrentPosition { get; set; } = 0.0;

    // Format time values locally since they're not part of the service interface
    public string FormattedPlayerCurrentTime => TimeSpan.FromSeconds(PlayerCurrentPosition).ToString(@"hh\:mm\:ss");
    public string FormattedPlayerRemainingTime => TimeSpan.FromSeconds(PlayerDuration - PlayerCurrentPosition).ToString(@"hh\:mm\:ss");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedEndTime))]
    public partial double PlayerDuration { get; set; }
    public string FormattedEndTime => TimeSpan.FromSeconds(PlayerDuration).ToString(@"hh\:mm\:ss");

    [ObservableProperty]
    public partial string BleStatus { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DmIcon))]
    public partial bool IsConnected { get; set; } = false;
    public object DmIcon => IsConnected ? "ble_on.png" : "ble_off.png";

    [ObservableProperty]
    public partial bool IsConnecting { get; set; } = false;

    [ObservableProperty]
    public partial int BrightnessValue { get; set; } = 80;

    public PlayerStateEnum PlayerState
    {
        get => _sequencePlayerService.PlayerState;
        set => _logger.LogInformation("PlayerState setter called, but we now use service state directly");
    }
    public bool DisplayClock => PlayerState == PlayerStateEnum.PLAYING || PlayerState == PlayerStateEnum.PAUSED;
    public bool DisplayText => !DisplayClock;

    [ObservableProperty]
    public partial bool DraggingInPlayMode { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AudioIcon))]
    public partial bool HasAudio { get; set; } = false;
    public object AudioIcon => HasAudio ? "sound.png" : "nosound.png";

    [ObservableProperty]
    public partial int Rating { get; set; } = -1;

    #endregion

    #region Commands

    [RelayCommand]
    private async Task MinusRatingAsync()
    {
        if (Rating > 0 && _currentMphSequence is not null)
        {
            Rating--;
            _currentMphSequence.Userdata.Rating = Rating;
            var userdataFile = Path.Combine(_currentMphSequence.DirPath, "userdata.json");
            await _currentMphSequence.Userdata.SaveJsonFileAsync(userdataFile);
        }
    }

    [RelayCommand]
    private async Task PlusRatingAsync()
    {
        if (Rating < 5 && _currentMphSequence is not null)
        {
            Rating++;
            _currentMphSequence.Userdata.Rating = Rating;
            var userdataFile = Path.Combine(_currentMphSequence.DirPath, "userdata.json");
            await _currentMphSequence.Userdata.SaveJsonFileAsync(userdataFile);
        }
    }

    [RelayCommand]
    private void Loop()
    {
        LoopMode = !LoopMode;
        _logger.LogInformation("Set loop mode to {}", LoopMode);
    }

    /// <summary>
    /// Toggles the delay mode on/off
    /// </summary>
    [RelayCommand]
    private void ToggleDelay()
    {
        DelayEnabled = !DelayEnabled;
        _logger.LogInformation("Delay mode: {}", DelayEnabled);
    }

    /// <summary>
    /// Starts the player with optional delay if delay mode is enabled and starting from stopped state
    /// </summary>
    [RelayCommand]
    private async Task StartPlayerAsync()
    {
        _logger.LogInformation("PlayerViewModel: Starting player via SequencePlayerService");

        // Apply delay only if starting from stopped state
        if (DelayEnabled && PlayerState == PlayerStateEnum.STOPPED)
        {
            _logger.LogInformation("Delay mode enabled, waiting 5 seconds before starting playback from stopped state");
            await StartDelayCountdownAsync();
        }

        // Simply delegate to the sequence player service
        await _sequencePlayerService.StartPlayerAsync();
    }

    /// <summary>
    /// Displays a countdown from 5 to 1 before starting playback
    /// </summary>
    private async Task StartDelayCountdownAsync()
    {
        for (int i = 5; i >= 1; i--)
        {
            await MainThread.InvokeOnMainThreadAsync(() => DelayCountdown = i.ToString());
            await Task.Delay(1000);
        }

        await MainThread.InvokeOnMainThreadAsync(() => DelayCountdown = string.Empty);
    }

    [RelayCommand]
    private async Task PausePlayerAsync()
    {
        // Delegate to the sequence player service
        _logger.LogInformation("PlayerViewModel: Pausing player via SequencePlayerService");
        await _sequencePlayerService.PausePlayerAsync();
    }

    [RelayCommand]
    public async Task StopPlayerAsync()
    {
        // Delegate to the sequence player service
        _logger.LogInformation("PlayerViewModel: Stopping player via SequencePlayerService");
        await _sequencePlayerService.StopPlayerAsync();
    }

    #endregion

    // Add timeout constant for sequence changes
    private const int SequenceChangeTimeoutMs = 5000; // 5 seconds timeout

    private async Task<bool> WaitForSequenceChangeAsync()
    {
        if (_sequenceChangeCompletion == null) return true;

        try
        {
            // Wait for completion with timeout
            return await _sequenceChangeCompletion.Task.WaitAsync(TimeSpan.FromMilliseconds(SequenceChangeTimeoutMs));
        }
        catch (TimeoutException)
        {
            _logger.LogError("Sequence change timed out after {}ms", SequenceChangeTimeoutMs);
            return false;
        }
    }

    partial void OnMphSequenceChanged(MPHSequence value)
    {
        _ = OnMphSequenceChangedAsync(value);
    }

    private async Task OnMphSequenceChangedAsync(MPHSequence value)
    {
        if (value == null)
        {
            _logger.LogError("OnMphSequenceChanged called with null sequence");
            _sequenceChangeCompletion?.TrySetResult(false);
            return;
        }

        try
        {
            _sequenceChangeCompletion = new TaskCompletionSource<bool>();
            _currentMphSequence = value;

            // Load the full sequence content if it hasn't been loaded yet
            value.Sequence ??= await _mes.LoadSequenceAsync(value.DirPath);
            Sequence = value.Sequence;

            if (Sequence == null || Sequence.DurationSeconds <= 0)
            {
                _logger.LogError("Invalid sequence data for: {}, Duration: {}",
                    value.DisplayName, Sequence?.DurationSeconds ?? 0);
                _sequenceChangeCompletion.TrySetResult(false);
                return;
            }

            // Populate metadata fields from sequence
            Author = Sequence.Author;
            Version = Sequence.Version;
            CreatedAt = Sequence.CreatedAt;

            _logger.LogInformation("Setting sequence in OnMphSequenceChanged: {} ({}s)",
                value.DisplayName, Sequence.DurationSeconds);

            Name = value.DisplayName;
            Detail = value.DisplayDetail;
            Rating = value.Userdata.Rating;
            HasAudio = value.HasAudio;
            PlayerDuration = Sequence.DurationSeconds;

            // Set the sequence into the player service
            _ =await _sequencePlayerService.SetPlayerAsync(value) != string.Empty;
            PlayerCurrentPosition = 0;
            _sequenceChangeCompletion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnMphSequenceChanged");
            _sequenceChangeCompletion?.TrySetResult(false);
        }
    }

    private async Task ChangeToNextPlaylistSequenceAsync()
    {
        // Check if a sequence change is already in progress
        if (_sequenceChangeCompletion?.Task.IsCompleted == false)
        {
            _logger.LogWarning("Sequence change already in progress, waiting for completion");
            if (!await WaitForSequenceChangeAsync())
            {
                _logger.LogWarning("Previous sequence change did not complete successfully, proceeding with new change");
            }
        }

        // Create new completion source for this change
        _sequenceChangeCompletion = new TaskCompletionSource<bool>();

        try
        {
            await StopPlayerAsync();

            var nextSequence = _mes.MPHRoot.PlaylistElements.SkipWhile(n => n != MphSequence).Skip(1).FirstOrDefault() ?? _mes.MPHRoot.PlaylistElements.First();
            nextSequence.Sequence ??= await _mes.LoadSequenceAsync(nextSequence.DirPath);

            // Update sequence on UI thread
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                try
                {
                    MphSequence = nextSequence;
                    _logger.LogInformation("Sequence set to: {}", MphSequence.DisplayName);
                    _sequenceChangeCompletion?.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error setting next sequence");
                    _sequenceChangeCompletion?.TrySetResult(false);
                }
            });

            // Wait for sequence change to complete before starting playback
            if (await WaitForSequenceChangeAsync())
            {
                await StartPlayerAsync();
            }
            else
            {
                _logger.LogError("Failed to start playback - sequence change did not complete successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during sequence change");
            _sequenceChangeCompletion?.TrySetResult(false);
            throw;
        }
    }

    public ICommand PlayPausePlayerCommand { get; private set; }

    /// <summary>
    /// Debounce delay before sending a BLE brightness command, to avoid flooding
    /// the device while the user drags the slider.
    /// </summary>
    private const int BrightnessDebounceMs = 200;

    partial void OnBrightnessValueChanged(int value)
    {
        _brightnessDebounceCts?.Cancel();
        _brightnessDebounceCts?.Dispose();
        _brightnessDebounceCts = new CancellationTokenSource();

        _ = DebouncedSendBrightnessAsync(value, _brightnessDebounceCts.Token);
    }

    private async Task DebouncedSendBrightnessAsync(int value, CancellationToken token)
    {
        try
        {
            await Task.Delay(BrightnessDebounceMs, token);

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

    public void MuteAudio() => _sequencePlayerService.SetAudio(false);

    public void UnmuteAudio() => _sequencePlayerService.SetAudio(true);

    public async Task SequenceEnded()
    {
        // Handle playback completion
        if (LoopMode)
        {
            _logger.LogInformation("Playback completed with LoopMode enabled");
            // if we are in playlist mode we change the current sequence to the next one
            if (_mvm.PlaylistMode)
            {
                _logger.LogInformation("Playlist mode active, changing sequence");
                await ChangeToNextPlaylistSequenceAsync();
                return;
            }
            else
            {
                // Non-playlist loop mode
                _logger.LogInformation("Single sequence loop mode, restarting current sequence");
                await _sequencePlayerService.SeekToPositionAsync(0);
                await _sequencePlayerService.StartPlayerAsync();
            }
        }
        else
        {
            _logger.LogInformation("Playback completed in non loop mode");
        }
    }
}
