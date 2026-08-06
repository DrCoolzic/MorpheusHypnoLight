// Ignore Spelling: ble Dm Unmute mvm hh ss

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MPCore.Models;
using MPCore.Services;
using MPCore.Utilities;
using MPEditor.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Plugin.Maui.Audio;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows.Input;
using MPEditor.ViewModel;
using MPEditor.View;
using MPMaui.Services;
using MPMaui.Utilities;
using CommunityToolkit.Maui.Extensions;


namespace MPEditor.ViewModel;

/* Explanation:
The [QueryProperty] attribute is used in .NET MAUI applications to enable passing data between pages. 
Here's a brief explanation:
- It's part of the Shell navigation system in MAUI.
- This attribute allows the DetailViewModel to receive the dmSequence object when navigating 
  to the page associated with this view model.
*/
[QueryProperty(nameof(DmSequence), "DmSequence")]

public partial class PlayerViewModel : BaseViewModel
{
    private readonly ILogger<PlayerViewModel> _logger;
    private readonly IBleService _bleService;
    private readonly ILanguageService _languageService;
    private readonly MainViewModel _mvm;
    private readonly DmElementService _des;
    private readonly MetadataService _metadataService;
    private readonly ISequencePlayerService _sequencePlayerService;

    private TaskCompletionSource<bool>? _sequenceChangeCompletion;
    private DmSequence? _currentDmSequence;

    // /////////////////////////////////////////////////
    // Constructor
    // /////////////////////////////////////////////////
    public PlayerViewModel(
        IBleService bleService,
        ILanguageService languageService,
        ILogger<PlayerViewModel> logger,
        MainViewModel mvm,
        DmElementService des,
        MetadataService metadataService,
        ISequencePlayerService sequencePlayerService)
    {
        _bleService = bleService;
        _languageService = languageService;
        _logger = logger;
        _mvm = mvm;
        _des = des;
        _metadataService = metadataService;
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
                    _sequencePlayerService.PausePlayer();
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

        // Initialize current language from service
        CurrentLanguage = _languageService.CurrentLanguage;

        // Subscribe to language changes from the language service
        _languageService.LanguageChanged += OnLanguageServiceChanged;

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

            // get frequencies at current position
            // Use integer position to match BLE behavior and ensure audio/light sync
            int positionSeconds = (int)Math.Round(PlayerCurrentPosition);
            var (_, _, oscValues) = DmDSP.ParametersAtPos(positionSeconds, Sequence!);
            string f0 = oscValues[0].frequency == -1 ? string.Empty : oscValues[0].frequency.ToString("F1");
            string f1 = oscValues[1].frequency == -1 ? string.Empty : oscValues[1].frequency.ToString("F1");
            string f2 = oscValues[2].frequency == -1 ? string.Empty : oscValues[2].frequency.ToString("F1");
            string f3 = oscValues[3].frequency == -1 ? string.Empty : oscValues[3].frequency.ToString("F1");
            Frequencies = f0 + "  " + f1 + "  " + f2 + "  " + f3;
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
    public partial string Detail { get; set; } = "Detail";

    [ObservableProperty]
    public partial DmSequence DmSequence { get; set; } = new();

    [ObservableProperty]
    public partial List<string> GradientStops { get; set; } = [];

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string CategoryName { get; set; } = "";

    [ObservableProperty]
    public partial string LevelName { get; set; } = "";

    // [ObservableProperty]
    // public partial string AudioName { get; set; } = "";

    [ObservableProperty]
    public partial bool HasAudio { get; set; } = false;

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

    // [ObservableProperty]
    // public partial string? AudioFilePath { get; set; }

    [ObservableProperty]
    // [NotifyPropertyChangedFor(nameof(FormattedPlayerCurrentTime))]
    // [NotifyPropertyChangedFor(nameof(FormattedPlayerRemainingTime))]
    public partial double PlayerCurrentPosition { get; set; } = 0.0;

    // Format time values locally since they're not part of the service interface
    public string FormattedPlayerCurrentTime => TimeSpan.FromSeconds(PlayerCurrentPosition).ToString(@"hh\:mm\:ss");
    public string FormattedPlayerRemainingTime => TimeSpan.FromSeconds(PlayerDuration - PlayerCurrentPosition).ToString(@"hh\:mm\:ss");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FormattedEndTime))]
    public partial int PlayerDuration { get; set; }
    public string FormattedEndTime => TimeSpan.FromSeconds(PlayerDuration).ToString(@"hh\:mm\:ss");

    [ObservableProperty]
    public partial string BleStatus { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DmIcon))]
    public partial bool IsConnected { get; set; } = false;
    public object DmIcon => IsConnected ? "dm_on.png" : "dm_off.png";

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
    public partial bool AudioOn { get; set; } = true;
    public object AudioIcon => AudioOn ? "sound.png" : "nosound.png";

    [ObservableProperty]
    public partial int Category { get; set; } = -1;

    [ObservableProperty]
    public partial int Level { get; set; } = -1;

    [ObservableProperty]
    public partial int Rating { get; set; } = -1;

    [ObservableProperty]
    public partial string Frequencies { get; set; } = string.Empty;

    /// <summary>
    /// Current language for the sequence
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentLanguageIcon))]
    public partial string CurrentLanguage { get; set; } = "en";

    /// <summary>
    /// Gets the language flag icon based on current language
    /// </summary>
    public string CurrentLanguageIcon => CurrentLanguage == "fr" ? "france.png" : "usa.png";

    #endregion

    #region Commands


    [RelayCommand]
    private async Task MinusRatingAsync()
    {
        if (Rating > 0 && _currentDmSequence is not null && (_currentDmSequence.Userdata is Userdata ud))
        {
            Rating--;
            ud.Rating = Rating;
            var userdataFile = Path.Combine(_currentDmSequence.DirPath, "userdata.json");
            await ud.SaveJsonFileAsync(userdataFile);

            // Set the flag on MainPage to indicate a refresh is needed when it appears
            MainPage.NeedsRefresh = true;
        }

    }

    [RelayCommand]
    private async Task PlusRatingAsync()
    {
        if (Rating < 5 && _currentDmSequence is not null && (_currentDmSequence.Userdata is Userdata ud))
        {
            Rating++;
            ud.Rating = Rating;
            var userdataFile = Path.Combine(_currentDmSequence.DirPath, "userdata.json");
            await ud.SaveJsonFileAsync(userdataFile);
            MainPage.NeedsRefresh = true;
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

    [RelayCommand]
    private async Task ShowLanguagePicker()
    {
        var items = new List<MPCore.Models.PickerItem>
        {
            new MPCore.Models.PickerItem { Value = 0, DisplayText = "English" },
            new() { Value = 1, DisplayText = "Français" }
        };

        await ShowCustomPicker("Select Language", items, "Language");
    }

    /// <summary>
    /// Starts the player with optional delay if delay mode is enabled and starting from stopped state
    /// </summary>
    [RelayCommand]
    private async Task StartPlayerAsync()
    {
        _logger.LogInformation("DetailViewModel: Starting player via SequencePlayerService");
        
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
    private void PausePlayer()
    {
        // Delegate to the sequence player service
        _logger.LogInformation("DetailViewModel: Pausing player via SequencePlayerService");
        _sequencePlayerService.PausePlayer();
    }


    [RelayCommand]
    public async Task StopPlayerAsync()
    {
        // Delegate to the sequence player service
        _logger.LogInformation("DetailViewModel: Stopping player via SequencePlayerService");
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


    partial void OnDmSequenceChanged(DmSequence value)
    {
        if (value == null)
        {
            _logger.LogError("OnDmSequenceChanged called with null sequence");
            _sequenceChangeCompletion?.TrySetResult(false);
            return;
        }

        try
        {
            _sequenceChangeCompletion = new TaskCompletionSource<bool>();
            _currentDmSequence = value;
            Sequence = value.Sequence;
            
            // Populate metadata fields from sequence
            Author = Sequence?.Author;
            Version = Sequence?.Version;
            CreatedAt = Sequence?.CreatedAt;

            if (Sequence == null || Sequence.Duration <= 0)
            {
                _logger.LogError("Invalid sequence data for: {}, Duration: {}",
                    value.Metadata.NameItems[_languageService.CurrentLanguage],
                    Sequence?.Duration ?? 0);
                _sequenceChangeCompletion.TrySetResult(false);
                return;
            }

            _logger.LogInformation("Setting _sequenceToPlay in OnDmSequenceChanged: {} ({}ms)",
                value.Metadata.NameItems[_languageService.CurrentLanguage], Sequence.Duration);

            GradientStops = DmSequence.GradientStops;
            Name = DmSequence.Metadata.NameItems[_languageService.CurrentLanguage];
            if (value.Metadata is SequenceMetadata smd)
            {
                CategoryName = LocalizedNamesInstances.CategoryName[NameType.Category, _languageService.CurrentLanguage][smd.Category + 1];
                LevelName = LocalizedNamesInstances.LevelName[NameType.Level, _languageService.CurrentLanguage][smd.Level + 1];
                Detail = smd.DetailItems.TryGetValue(_languageService.CurrentLanguage, out var detailText)
                    ? detailText
                    : smd.DetailItems.TryGetValue("en", out var englishText) // Fallback to English
                        ? englishText
                        : "No description available"; // Default if no text available

                // Update observable properties
                Category = smd.Category;
                Level = smd.Level;
                // We don't need to set duration as the player service will handle this
                _logger.LogInformation("Sequence metadata duration: {}", smd.Duration);
            }
            Rating = value.Userdata?.Rating ?? 0; // Use Userdata if available, otherwise default to 0

            // Set the sequence into the player service
            HasAudio = _sequencePlayerService.SetPlayer(value) != string.Empty;

            // Set duration from sequence data (this should now be managed by the service)
            PlayerDuration = Sequence.Duration;
            // _logger.LogInformation("Set player duration to {} seconds from sequence", PlayerDuration);
            PlayerCurrentPosition = 0; // TODO needed?

            _sequenceChangeCompletion.TrySetResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in OnDmSequenceChanged");
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

            var nextSequence = _des.DmRoot.PlaylistElements.SkipWhile(n => n != DmSequence).Skip(1).FirstOrDefault() ?? _des.DmRoot.PlaylistElements.First();
            if (nextSequence.Sequence == null)
            {
                var sequenceJsonPath = Path.Combine(nextSequence.DirPath, "sequence.json");
                var sequence = await Sequence.LoadJsonFileAsync<Sequence>(sequenceJsonPath) ?? throw new FileNotFoundException($"sequence.json not found in {nextSequence.DirPath}");
                nextSequence.Sequence = sequence;
                _logger.LogInformation("Next sequence {} loaded", sequence.Name);
            }

            // Update sequence on UI thread
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                try
                {
                    DmSequence = nextSequence;
                    _logger.LogInformation("Sequence set to: {}", DmSequence?.Metadata.NameItems[_languageService.CurrentLanguage]);
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

    // Command to toggle play/pause state using the sequence player service
    [RelayCommand]
    private async Task TogglePlaybackAsync()
    {
        _logger.LogInformation("TogglePlayback command executed");
        await PlayPausePlayerCommandHandlerAsync();
    }

    // Separate handler method to avoid ambiguity
    private async Task PlayPausePlayerCommandHandlerAsync()
    {
        if (_sequencePlayerService.PlayerState == PlayerStateEnum.PLAYING)
            _sequencePlayerService.PausePlayer();
        else
        {
            // Apply delay only if starting from stopped state
            if (DelayEnabled && PlayerState == PlayerStateEnum.STOPPED)
            {
                _logger.LogInformation("Delay mode enabled, waiting 5 seconds before starting playback from stopped state");
                await StartDelayCountdownAsync();
            }
            await _sequencePlayerService.StartPlayerAsync();
        }
    }

    public ICommand PlayPausePlayerCommand { get; private set; }

    partial void OnBrightnessValueChanged(int value)
    {
        try
        {
            _bleService.SendBrightness(value);
            _logger.LogInformation("Brightness changed to: {value}", value);
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
            // if we are in playlist mode we change the current DmSession to the next one
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

        /// <summary>
        /// Handles language service changes to update the current language
        /// </summary>
        private void OnLanguageServiceChanged(object? sender, string newLanguage)
        {
            _logger.LogInformation("Language service changed from {} to {}", CurrentLanguage, newLanguage);
            CurrentLanguage = newLanguage;
            
            // Update sequence display information and audio when language changes
            if (_currentDmSequence != null)
            {
                // Update sequence display information for new language
                Name = _currentDmSequence.Metadata.NameItems[newLanguage];
                
                if (_currentDmSequence.Metadata is SequenceMetadata smd)
                {
                    CategoryName = LocalizedNamesInstances.CategoryName[NameType.Category, newLanguage][smd.Category + 1];
                    LevelName = LocalizedNamesInstances.LevelName[NameType.Level, newLanguage][smd.Level + 1];
                    Detail = smd.DetailItems.TryGetValue(newLanguage, out var detailText)
                        ? detailText
                        : smd.DetailItems.TryGetValue("en", out var englishText) // Fallback to English
                            ? englishText
                            : "No description available"; // Default if no text available
                }
                
                // Update the sequence player service with new language audio
                HasAudio = _sequencePlayerService.SetPlayer(_currentDmSequence) != string.Empty;
                _logger.LogInformation("Updated sequence display info and audio for new language: {}", newLanguage);
            }
        }

        private async Task ShowCustomPicker(string title, List<MPCore.Models.PickerItem> items, string type)
        {
            try
            {
                var popup = new MPMaui.Controls.CustomPickerPopup();
                popup.Initialize(title, items, type);

                // Show the popup and wait for it to be dismissed
                var popupResult = await Shell.Current.ShowPopupAsync(popup);

                // Check if popup was dismissed by tapping outside or if a selection was made
                if (!popupResult.WasDismissedByTappingOutsideOfPopup && popup.SelectedValue.HasValue)
                {
                    var result = popup.SelectedValue.Value;
                    switch (type)
                    {
                        case "Language":
                            CurrentLanguage = result == 0 ? "en" : "fr";
                            // Update the language service to keep it synchronized
                            if (_languageService.CurrentLanguage != CurrentLanguage)
                            {
                                _logger.LogInformation("Updating language service from {} to {}", _languageService.CurrentLanguage, CurrentLanguage);
                                _ = _languageService.SetLanguageAsync(CurrentLanguage);
                            }
                            _logger.LogInformation("Language changed to: {Language}", CurrentLanguage);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing custom picker for {Type}", type);
            }
        }
    }
