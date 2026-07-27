// Ignore Spelling: Ble

using MPHCore.Models;

namespace MPHEditor.Services;

/// <summary>
/// Service for managing Bluetooth Low Energy (BLE) communications with Dream Machine devices.
/// Handles device discovery, connection management, and command transmission.
/// </summary>
public interface IBleService
{
    bool IsConnected { get; }
    bool IsConnecting { get; }
    string Status { get; }
    bool IsBluetoothEnabled { get; }

    event EventHandler<bool> ConnectedChanged;
    event EventHandler<bool> ConnectingChanged;
    event EventHandler<string> StatusChanged;

    // Async methods
    Task<bool> CheckBluetoothStatusAsync();
    Task ConnectAsync();
    Task DisconnectAsync();
    Task ForceDisconnectAsync();
    Task PlaySequenceAsync(Sequence sequence);
    Task StopAsync();
    Task SendBrightnessAsync(int brightness);
    Task PauseSequenceAsync();
    Task PlayStepAsync(Step step);

    // Sync methods (originally from MPEditor)
    void PlaySequence(Sequence sequence);
    void PauseSequence();
    void Stop();
    void SendBrightness(int brightness);
    void WriteBuffer(byte[] buffer);
    void PlayStep(Step step);
}
