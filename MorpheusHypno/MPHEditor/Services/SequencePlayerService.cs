// Ignore Spelling: ble MPH

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using MPHCore.Models;
using MPHCore.Services;
using MPHCore.Utilities;
using MPHEditor.Utilities;
using Plugin.Maui.Audio;

namespace MPHEditor.Services;

/// <summary>
/// Implementation of the sequence player service that handles
/// - playback of sequences to the Dream Machine device
/// - audio playback using Plugin.Maui.Audio
/// </summary>
public partial class SequencePlayerService : ISequencePlayerService, IDisposable
{
    #region Private Fields
    private readonly IBleService _bleService;
    private readonly ILogger _logger;
    private IAudioPlayer? _audioPlayer;
    private IAudioManager _audioManager;
    private FileStream? _audioFileStream;
    private Sequence? _sequence;
    private Sequence? _sequenceToPlay = null;
    private int _duration = 0;
    private CancellationTokenSource? _cancellationTokenSource;
    private Stopwatch? _stopwatch;
    private bool _sequenceLoadedOnDevice = false;

    // Add timeout constant for sequence changes
    private const int SequenceChangeTimeoutMs = 5000; // 5 seconds timeout
    #endregion

    #region Properties
    /// <inheritdoc />
    public PlayerStateEnum PlayerState { get; private set; } = PlayerStateEnum.STOPPED;

    /// <inheritdoc />
    public double CurrentPosition { get; private set; }

    // /// <summary>
    // /// Current position rounded to the nearest integer second.
    // /// This should be used for all BLE/Dream Machine operations since the device
    // /// only operates on second boundaries. Ensures audio and light stay synchronized.
    // /// </summary>
    // public int CurrentPositionSeconds => (int)Math.Round(CurrentPosition);

    /// <inheritdoc />
    public bool LoopMode { get; set; }

    #endregion

    #region Events
    /// <inheritdoc />
    public event EventHandler<PlayerStateEnum>? PlayerStateChanged;

    /// <inheritdoc />
    public event EventHandler<double>? PositionChanged;

    /// <inheritdoc />
    public event EventHandler? SequenceCompleted;

    #endregion

    #region Constructor
    /// <summary>
    /// Creates a new instance of the SequencePlayerService
    /// </summary>
    /// <param name="bleService">The BLE service for Dream Machine communication</param>
    /// <param name="logger">Logger for diagnostic information</param>
    public SequencePlayerService(IBleService bleService, IAudioManager audioManager, ILogger<SequencePlayerService> logger)
    {
        _bleService = bleService;
        _audioManager = audioManager;
        _logger = logger;
    }
    #endregion

    #region Interface Methods

    /// <inheritdoc />
    public async Task<string> SetPlayerAsync(MPHSequence MPHSequence)
    {
        if (MPHSequence.Sequence is null)
        {
            _logger.LogWarning("Attempted to set player with a null sequence");
            return string.Empty;
        }
        _sequence = MPHSequence.Sequence;
        _sequenceToPlay = _sequence;
        _duration = (int)Math.Ceiling(_sequence.DurationSeconds);
        _sequenceLoadedOnDevice = false;
        _logger.LogInformation("Set player sequence to {} with duration {}", _sequence.Name, _duration);

        // Check if sequence has audio
        string audioPath = string.Empty;
        if (MPHSequence.HasAudio)
        {
            audioPath = Path.Combine(MPHSequence.DirPath, "sound.mp3");
            _logger.LogInformation("Found audio {}", audioPath);
        }
        else
        {
            _logger.LogInformation("No audio found for sequence");
        }

        // Always dispose existing audio resources first to prevent resource leaks
        try
        {
            _audioPlayer?.Dispose();
            _audioFileStream?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing existing audio resources");
        }
        finally
        {
            _audioPlayer = null;
            _audioFileStream = null;
        }

        // Load the sequence onto the device if connected
        if (_bleService.IsConnected)
        {
            try
            {
                await _bleService.LoadSequenceAsync(_sequenceToPlay);
                _sequenceLoadedOnDevice = true;
                _logger.LogInformation("Sequence loaded onto device");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load sequence onto device");
                _sequenceLoadedOnDevice = false;
            }
        }

        // If audioPath is not empty, create the audio player
        if (audioPath != string.Empty && File.Exists(audioPath))
        {
            try
            {
                // If audio file exists, create audio player
                _audioFileStream = File.OpenRead(audioPath);
                _audioPlayer = _audioManager.CreatePlayer(_audioFileStream);
                Task.Delay(250).Wait(); // for windows to settle the audio player state

                // Ensure audio player is in a proper state for playback
                if (_audioPlayer != null)
                {
                    // Set initial volume and ensure it's ready to play
                    _audioPlayer.Volume = 1.0;
                    _logger.LogInformation("Audio player created successfully for path: {} (Duration: {}, Volume: {})",
                        audioPath, _audioPlayer.Duration, _audioPlayer.Volume);
                }
                else
                {
                    _logger.LogWarning("Audio player creation returned null for path: {}", audioPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create audio player for path: {}", audioPath);
                _audioPlayer = null;
                _audioFileStream?.Dispose();
                _audioFileStream = null;
            }
            return audioPath;
        }

        // Return empty string if no audio path was set or file doesn't exist
        _logger.LogInformation("No audio file set or file does not exist - audio resources disposed");
        return string.Empty;
    }


    /// <inheritdoc />
    public async Task StartPlayerAsync()
    {
        if (PlayerState == PlayerStateEnum.PLAYING)
            return; // Already playing

        bool wasPaused = PlayerState == PlayerStateEnum.PAUSED;
        SetPlayerState(PlayerStateEnum.PLAYING);

        // Resume from paused state: the firmware has kept its internal position,
        // so we only need to resume both the device and the audio.
        if (wasPaused)
        {
            _logger.LogInformation("Resuming playback from position: {} seconds", CurrentPosition);
            _cancellationTokenSource = new CancellationTokenSource();
            await ResumeActionAsync();
            _stopwatch?.Start();
            await ContinueTimerAsync();
            return;
        }

        // Starting from the stopped state
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();
        _logger.LogInformation("Start player from position: {} seconds", CurrentPosition);
        await StartActionAsync();
        _stopwatch = Stopwatch.StartNew();
        await ContinueTimerAsync();
    }


    /// <inheritdoc />
    public async Task PausePlayerAsync()
    {
        if (PlayerState != PlayerStateEnum.PLAYING)
            return;

        _logger.LogInformation("Pause player at position: {} seconds", CurrentPosition);
        SetPlayerState(PlayerStateEnum.PAUSED);
        _stopwatch?.Stop();

        await PauseActionAsync();
        PositionChanged?.Invoke(this, CurrentPosition);
    }


    /// <inheritdoc />
    public async Task StopPlayerAsync()
    {
        if (PlayerState == PlayerStateEnum.STOPPED)
            return;

        _cancellationTokenSource?.Cancel();
        SetPlayerState(PlayerStateEnum.STOPPED);
        await StopActionAsync();
        CurrentPosition = 0;
        PositionChanged?.Invoke(this, CurrentPosition);
        _stopwatch?.Stop();
        _stopwatch = null; // Reset the stopwatch
    }


    /// <inheritdoc />
    public async Task SeekToPositionAsync(double positionInSeconds)
    {
        // Clamp to valid range
        double clampedPosition = Math.Max(0.0, Math.Min(positionInSeconds, _duration));
        _logger.LogInformation("Seek requested to {} seconds (clamped to {} seconds)", positionInSeconds, clampedPosition);

        _cancellationTokenSource?.Cancel();
        CurrentPosition = clampedPosition;
        PositionChanged?.Invoke(this, CurrentPosition);

        // Do not send a BLE seek while stopped: the device would apply the step and turn LEDs on.
        // The next Start will seek to this position if needed.
        if (PlayerState != PlayerStateEnum.STOPPED)
        {
            await SeekToActionAsync(clampedPosition);
        }

        // If the player is playing, restart the timer
        if (PlayerState == PlayerStateEnum.PLAYING)
        {
            _stopwatch?.Restart();
            _cancellationTokenSource = new CancellationTokenSource();
            await ContinueTimerAsync();
        }
    }


    /// <inheritdoc />
    public void SetAudio(bool on)
    {
        if (_audioPlayer == null)
            return;

        _audioPlayer.Volume = on ? 1 : 0;
    }

    /// <inheritdoc />
    public Sequence? GetSequenceToPlay()
    {
        return _sequenceToPlay;
    }

    /// <inheritdoc />
    public void ReleaseAudioResources()
    {
        try
        {
            _logger.LogInformation("Releasing audio resources (FileStream and AudioPlayer)");

            // Stop and dispose audio player
            _audioPlayer?.Stop();
            _audioPlayer?.Dispose();
            _audioPlayer = null;

            // Close and dispose file stream
            _audioFileStream?.Close();
            _audioFileStream?.Dispose();
            _audioFileStream = null;

            _logger.LogInformation("Audio resources released successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error releasing audio resources");
        }
    }

    #endregion

    #region Private Methods
    private void SetPlayerState(PlayerStateEnum newState)
    {
        if (PlayerState != newState)
        {
            PlayerState = newState;
            PlayerStateChanged?.Invoke(this, PlayerState);
        }
    }


    /// <summary>
    /// Loads the full sequence onto the device, optionally seeks to the current
    /// position, and starts playback. Used when starting from the stopped state.
    /// </summary>
    private async Task StartActionAsync()
    {
        if (_sequenceToPlay is null)
        {
            _logger.LogWarning("Trying to start playback with a null sequence");
            return;
        }

        if (_bleService.IsConnected)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            if (!_sequenceLoadedOnDevice)
            {
                await _bleService.LoadSequenceAsync(_sequenceToPlay);
                _sequenceLoadedOnDevice = true;
            }

            int positionMs = (int)Math.Round(CurrentPosition * 1000.0);
            if (positionMs > 0)
            {
                await _bleService.SeekAsync(positionMs);
            }

            await _bleService.PlayAsync();
            stopwatch.Stop();
            _logger.LogInformation("Time to send BLE commands: {} ms", stopwatch.ElapsedMilliseconds);
        }

        if (_audioPlayer != null)
        {
            try
            {
                _logger.LogInformation("Starting audio playback Volume={}, Duration={}, CurrentPosition={}",
                    _audioPlayer.Volume, _audioPlayer.Duration, _audioPlayer.CurrentPosition);
                _audioPlayer.Play();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start audio playback");
            }
        }
        else
        {
            _logger.LogWarning("Audio player is null - no audio playback available");
        }
    }

    /// <summary>
    /// Resumes playback on the device and in the audio player. Used when resuming
    /// from a paused state.
    /// </summary>
    private async Task ResumeActionAsync()
    {
        if (_bleService.IsConnected)
        {
            await _bleService.PlayAsync();
        }

        if (_audioPlayer != null)
        {
            try
            {
                _audioPlayer.Play();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to resume audio playback");
            }
        }
    }

    /// <summary>
    /// Pauses playback on the device and in the audio player.
    /// </summary>
    private async Task PauseActionAsync()
    {
        if (_audioPlayer != null)
        {
            try
            {
                _audioPlayer.Pause();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to pause audio playback");
            }
        }

        if (_bleService.IsConnected)
        {
            await _bleService.PauseAsync();
        }
    }

    /// <summary>
    /// Stops playback on the device and in the audio player.
    /// </summary>
    private async Task StopActionAsync()
    {
        _logger.LogInformation("Stop player at position: {} seconds", CurrentPosition);

        _audioPlayer?.Stop();

        if (_bleService.IsConnected)
        {
            await _bleService.StopAsync();
        }
    }

    /// <summary>
    /// Seeks the audio player and the BLE device to the specified position.
    /// The device expects the position in milliseconds, while the audio player
    /// works in seconds.
    /// </summary>
    /// <param name="positionInSeconds">Target position in seconds.</param>
    private async Task SeekToActionAsync(double positionInSeconds)
    {
        if (positionInSeconds < 0.0 || positionInSeconds > _duration)
        {
            _logger.LogWarning("Seek position {} is out of bounds (0-{}), skipping seek", positionInSeconds, _duration);
            return;
        }

        // Audio player works in seconds
        if (_audioPlayer != null)
        {
            try
            {
                _audioPlayer.Seek(positionInSeconds);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex is InvalidOperationException || ex is ObjectDisposedException)
            {
                _logger.LogError(ex, "Failed to seek audio player to position {} seconds", positionInSeconds);
            }
        }

        // BLE device works in milliseconds
        if (_bleService.IsConnected)
        {
            int positionMs = (int)Math.Round(positionInSeconds * 1000.0);
            await _bleService.SeekAsync(positionMs);
        }
    }

    /// <summary>
    /// Converts the current position from seconds to milliseconds for BLE commands.
    /// </summary>
    private int CurrentPositionMs => (int)Math.Round(CurrentPosition * 1000.0);

    /// <summary>
    /// Continues the timer when the player is playing.
    /// </summary>
    /// <remarks>
    /// This method is called when the player is playing and the timer needs to be continued.
    /// It will loop until the player is stopped or paused.
    /// The timer is updated every 100ms with the current position of the player.
    /// If the player is paused or stopped, the method will exit the loop and stop the timer.
    /// </remarks>
    private async Task ContinueTimerAsync()
    {
        try
        {
            while (CurrentPosition < _duration)
            {
                if (_cancellationTokenSource!.Token.IsCancellationRequested || PlayerState == PlayerStateEnum.PAUSED)
                {
                    _stopwatch?.Stop();
                    return; // Exit the loop
                }

                // Update CurrentPosition based on Stopwatch
                if (_stopwatch != null)
                {
                    CurrentPosition += _stopwatch.Elapsed.TotalSeconds;
                    PositionChanged?.Invoke(this, CurrentPosition);
                    _stopwatch.Restart(); // Keep Stopwatch in sync
                }
                await Task.Delay(100, _cancellationTokenSource.Token); // Smooth updates
            }

            // Handle playback completion
            _logger.LogInformation("Playback completed, stopping player and notifying listeners");

            // Cancel token and set state to stopped
            _cancellationTokenSource?.Cancel();
            SetPlayerState(PlayerStateEnum.STOPPED);

            // Stop the BLE sequence but preserve audio player state
            if (_bleService.IsConnected)
                await _bleService.StopAsync();

            // Stop audio but don't dispose it
            _audioPlayer?.Stop();

            // Reset position
            CurrentPosition = 0;
            PositionChanged?.Invoke(this, CurrentPosition); // Notify UI to update slider

            // Trigger the SequenceCompleted event after stopping
            SequenceCompleted?.Invoke(this, EventArgs.Empty);

            // Final cleanup
            _stopwatch?.Stop();
            _stopwatch = null; // Reset the stopwatch
        }
        catch (TaskCanceledException)
        {
            _logger.LogInformation("Timer canceled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in timer loop");
        }
    }

    /// <summary>
    /// Disposes of the SequencePlayerService and cleans up all resources
    /// </summary>
    public void Dispose()
    {
        try
        {
            // Cancel any running operations
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            // Stop and dispose audio resources
            _audioPlayer?.Stop();
            _audioPlayer?.Dispose();
            _audioFileStream?.Dispose();

            // Stop stopwatch
            _stopwatch?.Stop();

            _logger.LogInformation("SequencePlayerService disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during SequencePlayerService disposal");
        }
        finally
        {
            _audioPlayer = null;
            _audioFileStream = null;
            _cancellationTokenSource = null;
            _stopwatch = null;
        }
    }
    #endregion
}
