// Ignore Spelling: Ble

using MPHCore.Models;

namespace MPHEditor.Services;

/// <summary>
/// Service for managing Bluetooth Low Energy (BLE) communications with Morpheus HypnoLight devices.
/// Handles device discovery, connection management, and command transmission.
/// </summary>
public interface IBleService
{
    /// <summary>
    /// Gets a value indicating whether a Morpheus HypnoLight device is currently connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets a value indicating whether the service is currently trying to connect to a device.
    /// </summary>
    bool IsConnecting { get; }

    /// <summary>
    /// Gets the current status message for the user.
    /// </summary>
    string Status { get; }

    /// <summary>
    /// Gets a value indicating whether Bluetooth is available and enabled on the device.
    /// </summary>
    bool IsBluetoothEnabled { get; }

    /// <summary>
    /// Raised when the connection state changes.
    /// </summary>
    event EventHandler<bool> ConnectedChanged;

    /// <summary>
    /// Raised when the connecting state changes.
    /// </summary>
    event EventHandler<bool> ConnectingChanged;

    /// <summary>
    /// Raised when the status text changes.
    /// </summary>
    event EventHandler<string> StatusChanged;

    /// <summary>
    /// Raised when a status notification is received from the device's Status characteristic.
    /// </summary>
    /// <remarks>
    /// The tuple contains the echoed opcode and the result code, as described in
    /// <c>doc/ble_protocol.md</c>.
    /// </remarks>
    event EventHandler<(byte Opcode, byte ResultCode)> CommandStatusReceived;

    /// <summary>
    /// Checks whether Bluetooth is enabled and all required permissions are granted.
    /// </summary>
    /// <returns>True if Bluetooth can be used, false otherwise.</returns>
    Task<bool> CheckBluetoothStatusAsync();
    /// <summary>
    /// Starts scanning for and connects to a Morpheus HypnoLight device.
    /// </summary>
    Task ConnectAsync();

    /// <summary>
    /// Disconnects from the current Morpheus HypnoLight device and restarts auto-connect.
    /// </summary>
    Task DisconnectAsync();

    /// <summary>
    /// Disconnects from the current Morpheus HypnoLight device without restarting auto-connect.
    /// </summary>
    /// <remarks>Primarily used for testing.</remarks>
    Task ForceDisconnectAsync();

    /// <summary>
    /// Transfers a full sequence to the device.
    /// </summary>
    /// <param name="seq">The sequence to encode and transfer.</param>
    /// <remarks>
    /// Internally encodes <paramref name="seq"/> into the MHL compact wire format and drives the
    /// <c>LOAD_START</c> (<c>0x10</c>), <c>LOAD_CHUNK</c> (<c>0x11</c>), and <c>LOAD_COMMIT</c> (<c>0x12</c>)
    /// opcodes, fragmenting the payload according to the negotiated ATT MTU.
    /// See <c>doc/ble_protocol.md</c> for the wire protocol.
    /// </remarks>
    Task LoadSequenceAsync(Sequence seq);

    /// <summary>
    /// Transfers an updated single step to the device.
    /// </summary>
    /// <param name="stepIndex">The index of the step to update.</param>
    /// <param name="step">The step to encode and transfer.</param>
    /// <remarks>
    /// Internally encodes <paramref name="step"/> into the MHL compact wire format and drives the
    /// <c>UPDATE_STEP_START</c> (<c>0x20</c>), <c>UPDATE_STEP_CHUNK</c> (<c>0x21</c>), and
    /// <c>UPDATE_STEP_COMMIT</c> (<c>0x22</c>) opcodes, fragmenting the payload according to the
    /// negotiated ATT MTU. See <c>doc/ble_protocol.md</c> for the wire protocol.
    /// </remarks>
    Task UpdateStepAsync(int stepIndex, Step step);

    /// <summary>
    /// Sends the <c>PLAY</c> command (opcode <c>0x01</c>) to start or resume playback.
    /// </summary>
    /// <remarks>See <c>doc/ble_protocol.md</c> for the wire protocol.</remarks>
    Task PlayAsync();

    /// <summary>
    /// Sends the <c>SEEK</c> command (opcode <c>0x04</c>) to jump to an absolute position.
    /// </summary>
    /// <param name="positionMs">The target position, in milliseconds.</param>
    /// <remarks>See <c>doc/ble_protocol.md</c> for the wire protocol.</remarks>
    Task SeekAsync(int positionMs);

    /// <summary>
    /// Sends the stop command to the device.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Sends a pause command to the device.
    /// </summary>
    Task PauseAsync();

    /// <summary>
    /// Sends a global brightness value (0-100) to the device.
    /// </summary>
    /// <param name="brightness">The brightness percentage.</param>
    Task SendBrightnessAsync(int brightness);

    /// <summary>
    /// Writes a raw byte buffer to the command characteristic.
    /// </summary>
    /// <param name="buffer">The bytes to send.</param>
    void WriteBuffer(byte[] buffer);
}
