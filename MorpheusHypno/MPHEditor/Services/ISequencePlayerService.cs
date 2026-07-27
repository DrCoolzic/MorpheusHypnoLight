// Ignore Spelling: dm

using MPHCore.Models;
using Plugin.Maui.Audio;

namespace MPHEditor.Services;

/// <summary>
/// Service for managing sequence playback to both audio and Dream Machine devices.
/// Implements a state machine (Playing/Paused/Stopped) and handles timing/synchronization.
/// </summary>
public interface ISequencePlayerService
{
    /// <summary>
    /// Current state of the player (Playing, Paused, Stopped)
    /// </summary>
    PlayerStateEnum PlayerState { get; }

    /// <summary>
    /// Current position in seconds within the sequence
    /// </summary>
    double CurrentPosition { get; }

    // Events
    /// <summary>
    /// Event raised when the player state changes
    /// </summary>
    event EventHandler<PlayerStateEnum> PlayerStateChanged;

    /// <summary>
    /// Event raised when the current position changes
    /// </summary>
    event EventHandler<double> PositionChanged;

    /// <summary>
    /// Event raised when a sequence has completed playback naturally (reached its end)
    /// </summary>
    event EventHandler? SequenceCompleted;


    /// <summary>
    /// Set the Sequence and potentially the Audio of the Player.
    /// Return the Audio path if sequence has audio, otherwise null
    /// </summary>
    string SetPlayer(DmSequence dmSequence);


    /// <summary>
    /// Start playback of the current sequence
    /// </summary>
    Task StartPlayerAsync();

    /// <summary>
    /// Pause the current playback
    /// </summary>
    void PausePlayer();

    /// <summary>
    /// Stop playback and reset position to beginning
    /// </summary>
    Task StopPlayerAsync();

    /// <summary>
    /// Seek to a specific position in the sequence
    /// </summary>
    Task SeekToPositionAsync(double positionInSeconds);

    /// <summary>
    /// Set audio on/off
    /// </summary>
    void SetAudio(bool on);

    /// <summary>
    /// Release audio resources (close file streams) without disposing the entire service
    /// </summary>
    void ReleaseAudioResources();

    /// <summary>
    /// Get the current sequence being played (may be truncated if seek was used)
    /// </summary>
    Sequence? GetSequenceToPlay();

    /// <summary>
    /// Dispose of resources used by the player
    /// </summary>
    void Dispose();
}

/// <summary>
/// Represents the possible states of the sequence player
/// </summary>
public enum PlayerStateEnum
{
    /// <summary>
    /// Player is stopped, position is reset to beginning
    /// </summary>
    STOPPED,
    
    /// <summary>
    /// Player is actively playing the sequence
    /// </summary>
    PLAYING,
    
    /// <summary>
    /// Player is paused at the current position
    /// </summary>
    PAUSED
}
