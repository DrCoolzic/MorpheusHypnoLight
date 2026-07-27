// Ignore Spelling: ble dm

using MPHCore.Models;
using MPHCore.Services;
using MPHCore.Utilities;
using MPHEditor.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls;
using Plugin.Maui.Audio;
using System.Diagnostics;

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
    public string SetPlayer(DmSequence dmSequence)
    {
        if (dmSequence.Sequence is null)
        {
            _logger.LogWarning("Attempted to set player with a null sequence");
            return string.Empty;
        }
        _sequence = dmSequence.Sequence;
        _sequenceToPlay = _sequence;
        _duration = _sequence.Duration;
        _logger.LogInformation("Set player sequence to {} with duration {}", _sequence.Name, _sequence.Duration);

        // Check if sequence has audio and select the appropriate audio file
        string audioPath = string.Empty;
        string audioKey =
            dmSequence.AudioItems.ContainsKey("default") ? "default" :
            dmSequence.AudioItems.ContainsKey("en") ? "en" :
            dmSequence.AudioItems.ContainsKey("fr") ? "fr" :
            string.Empty;

        if (!string.IsNullOrEmpty(audioKey))
        {
            var audioName = audioKey switch
            {
                "en" => "son_en.mp3",
                "fr" => "son_fr.mp3",
                _ => "son.mp3"
            };
            audioPath = Path.Combine(dmSequence.DirPath, audioName);
            _logger.LogInformation("Found audio for key {}: {}", audioKey, audioPath);
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

        // Resume from paused state
        if (PlayerState == PlayerStateEnum.PAUSED)
        {
            _logger.LogInformation("Resuming playback from position: {} seconds", CurrentPosition);
            // Now continue playback
            SetPlayerState(PlayerStateEnum.PLAYING);
            PlayAction();
            _stopwatch?.Start();
            _cancellationTokenSource = new CancellationTokenSource();
            await ContinueTimerAsync();
            return;
        }

        // Starting from the stopped state
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = new CancellationTokenSource();
        SetPlayerState(PlayerStateEnum.PLAYING);
        _logger.LogInformation("Start player from position: {} seconds", CurrentPosition);

        // Reconstruct truncated sequence at current position before playing
        // This ensures BLE starts from the correct position (not always from 0)
        int roundedPosition = (int)Math.Round(CurrentPosition);
        SeekToAction(roundedPosition);

        PlayAction();
        _stopwatch = Stopwatch.StartNew();
        await ContinueTimerAsync();
    }


    /// <inheritdoc />
    public void PausePlayer()
    {
        if (_cancellationTokenSource != null && PlayerState == PlayerStateEnum.PLAYING)
        {
            // Round position to nearest integer before pausing
            // This ensures audio and BLE resume from the same position
            // Dream Machine doesn't support pause, so we create a truncated sequence at integer position
            double originalPosition = CurrentPosition;
            CurrentPosition = Math.Round(CurrentPosition);

            if (originalPosition != CurrentPosition)
            {
                _logger.LogInformation("Pause: Rounded position from {} to {} seconds", originalPosition, CurrentPosition);
            }

            SetPlayerState(PlayerStateEnum.PAUSED);
            PauseAction();
            _stopwatch?.Stop(); // Stop the Stopwatch

            // Notify UI of the rounded position
            PositionChanged?.Invoke(this, CurrentPosition);
        }
    }


    /// <inheritdoc />
    public async Task StopPlayerAsync()
    {
        if (PlayerState == PlayerStateEnum.STOPPED)
            return;

        _cancellationTokenSource?.Cancel();
        SetPlayerState(PlayerStateEnum.STOPPED);
        StopAction();
        await SeekToPositionAsync(0);
        CurrentPosition = 0;
        _stopwatch?.Stop();
        _stopwatch = null; // Reset the stopwatch
    }


    /// <inheritdoc />
    public async Task SeekToPositionAsync(double positionInSeconds)
    {
        // Stop the timer if active
        _cancellationTokenSource?.Cancel();

        // Round to nearest integer to ensure audio and BLE stay synchronized
        // Dream Machine only operates on second boundaries
        int roundedPosition = (int)Math.Round(positionInSeconds);

        // Update CurrentPosition and seek to the new position
        CurrentPosition = roundedPosition;
        PositionChanged?.Invoke(this, CurrentPosition);

        SeekToAction(roundedPosition);
        _logger.LogInformation("Seek requested to {} seconds, rounded to {} seconds", positionInSeconds, roundedPosition);

        // If the player is playing, restart the timer
        if (PlayerState == PlayerStateEnum.PLAYING)
        {
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


    private void PlayAction()
    {
        if (_sequenceToPlay is null)
        {
            _logger.LogInformation("Trying to play a null sequence");
            return;
        }

        if (_bleService.IsConnected)
        {
            Stopwatch stopwatch = new();
            stopwatch.Start();  // Start timing
#if ANDROID
            _bleService.PlaySequenceAsync(_sequenceToPlay);
#else
            _bleService.PlaySequence(_sequenceToPlay);
#endif
            stopwatch.Stop();   // Stop timing
            _logger.LogInformation("Time to send ble sequence: {}ms", stopwatch.ElapsedMilliseconds);
        }

        if (_audioPlayer != null)
        {
            try
            {
                _logger.LogInformation("Starting audio playback Volume={}, Duration={}, CurrentPosition={}",
                    _audioPlayer.Volume, _audioPlayer.Duration, _audioPlayer.CurrentPosition);
                _audioPlayer.Play();
                _logger.LogInformation("Audio play command sent successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start audio playback - attempting to recreate audio player");
            }
        }
        else
        {
            _logger.LogWarning("Audio player is null - no audio playback available");
        }
    }


    private void PauseAction()
    {
        if (_sequenceToPlay is null)
            return;

        // Round to nearest integer to ensure audio and BLE stay synchronized
        // Dream Machine only operates on second boundaries
        int roundedPosition = (int)Math.Round(CurrentPosition);

        // Update CurrentPosition and seek to the new position
        CurrentPosition = roundedPosition;
        PositionChanged?.Invoke(this, CurrentPosition);

        // Pause audio at current position (already rounded in PausePlayer)
        _audioPlayer?.Pause();
        // Seek audio to the rounded position to ensure sync on resume
        if (_audioPlayer != null)
        {
            try
            {
                _audioPlayer.Seek(roundedPosition);
                _logger.LogInformation("Pause: Audio sought to rounded position {} seconds", roundedPosition);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to seek audio during pause");
            }
        }

        _logger.LogInformation("Pause player at position: {} seconds", roundedPosition);

        if (_bleService.IsConnected)
        {
#if ANDROID
            _bleService.PauseSequenceAsync();
#else
            _bleService.PauseSequence();
#endif
            // Create truncated sequence at rounded position for resume
            SeekToAction(roundedPosition);
        }
    }


    private void StopAction()
    {
        _logger.LogInformation("Stop player at position: {} seconds", CurrentPosition);

        _audioPlayer?.Stop();

        if (_bleService.IsConnected)
#if ANDROID
            _bleService.StopAsync();
#else
            _bleService.Stop();
#endif
    }

    private void SeekToAction(int position)
    {
        if (_sequence is null)
            return;

        _logger.LogInformation("Seeking player to position: {} seconds", position);
        _logger.LogInformation("_sequence: Name={}, Duration={}, Steps.Count={}", _sequence.Name, _sequence.Duration, _sequence.Steps.Count);
        if (_sequence.Steps.Count > 0)
        {
            _logger.LogInformation("First step: TimeStart={}, TimeEnd={}", _sequence.Steps[0].TimeStart, _sequence.Steps[0].TimeEnd);
            _logger.LogInformation("Last step: TimeStart={}, TimeEnd={}", _sequence.Steps[^1].TimeStart, _sequence.Steps[^1].TimeEnd);
        }

        // Only seek audio/BLE if player is not stopped
        // When stopped, we just reconstruct the truncated sequence for later playback
        bool shouldSeekAudioAndBle = PlayerState != PlayerStateEnum.STOPPED;

        // Defensive audio seeking with error handling to prevent COMException crashes
        if (_audioPlayer != null && shouldSeekAudioAndBle)
        {
            try
            {
                // Validate seek position is within bounds
                if (position >= 0 && position <= _duration)
                {
                    _audioPlayer.Seek(position);
                    // _logger.LogInformation("Seek players to position: {} seconds", positionInSeconds);
                }
                else
                {
                    _logger.LogWarning("Seek position {} is out of bounds (0-{}), skipping audio seek", position, _duration);
                }
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex is InvalidOperationException || ex is ObjectDisposedException)
            {
                _logger.LogError(ex, "Failed to seek audio player to position {} - audio player may be in invalid state. Attempting to recreate audio player.", position);
            }
        }

        // Always reconstruct truncated sequence, even when stopped or not connected
        // This ensures _sequenceToPlay is ready when StartPlayerAsync is called
        if (position == 0)
            _sequenceToPlay = _sequence;
        else
        {
            //// Use integer position for BLE operations to ensure audio/light sync
            //int positionSeconds = CurrentPositionSeconds;
            var (stepIndex, posInStep, oscValues) = DmDSP.ParametersAtPos(position, _sequence);
            _logger.LogInformation("@{} step:{} pos:{} f0:F({})-D({})-B({}) f1:F({})-D({})-B({}) f2:F({})-D({})-B({}) f3:F({})-D({})-B({})",
                position, stepIndex, posInStep,
                oscValues[0].frequency, oscValues[0].dutyCycle, oscValues[0].brightness,
                oscValues[1].frequency, oscValues[1].dutyCycle, oscValues[1].brightness,
                oscValues[2].frequency, oscValues[2].dutyCycle, oscValues[2].brightness,
                oscValues[3].frequency, oscValues[3].dutyCycle, oscValues[3].brightness
            );

            // Validate stepIndex before accessing Steps collection
            if (stepIndex == -1)
            {
                _logger.LogWarning("Seek position {} is out of sequence bounds (duration: {}), cannot create truncated sequence", position, _sequence.Duration);
                return;
            }

            Step current_step = _sequence.Steps[stepIndex];

            // Calculate remaining duration from seek position to end of current step
            int duration = current_step.TimeEnd - position;

            var oscillators = new List<Oscillator>();
            if (current_step.Oscillators.Count == 0)
                return;
            foreach (var iter in current_step.Oscillators.Select((oscillator, i) => (oscillator, i)))
            {
                Oscillator oscillator = new()
                {
                    LEDs = iter.oscillator.LEDs,
                    FrequencyStart = oscValues[iter.i].frequency,
                    FrequencyEnd = oscValues[iter.i].frequency,
                    DutyStart = oscValues[iter.i].dutyCycle,
                    DutyEnd = oscValues[iter.i].dutyCycle,
                    BrightnessStart = oscValues[iter.i].brightness,
                    BrightnessEnd = oscValues[iter.i].brightness
                };
                oscillators.Add(oscillator);
            }
            Step newStep = new(0, 0, duration, oscillators!);

            // we create a new sequence starting with the new step
            _sequenceToPlay = new Sequence("Dummy Sequence", _sequence.Duration - position, [newStep]);

            // now we add the following steps if any
            var last_time = duration;
            var last_index = 0;
            if (stepIndex != _sequence.Steps.Count - 1)
            {
                foreach (var step in _sequence.Steps.Skip(stepIndex + 1))
                {
                    var clonedStep = step.Clone();
                    // Save duration before modifying times (Duration is a computed property)
                    int stepDuration = clonedStep.Duration;
                    clonedStep.TimeStart = last_time;
                    clonedStep.TimeEnd = last_time + stepDuration;
                    clonedStep.Index = ++last_index;
                    _sequenceToPlay.Steps.Add(clonedStep);
                    last_time += stepDuration;
                }
            }
            _sequenceToPlay.Duration = _sequenceToPlay.Steps[^1].TimeEnd;
            _logger.LogInformation("_sequenceToPlay: {}", _sequenceToPlay);
        }
    }

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
                _bleService.Stop();

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
