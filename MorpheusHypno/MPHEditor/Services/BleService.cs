// Ignore Spelling: Ble Osc

using System.Linq;
using Microsoft.Extensions.Logging;
using MPHCore.Models;
using MPHCore.Utilities;
using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;

namespace MPHEditor.Services;

/// <summary>
/// Service for managing Bluetooth Low Energy (BLE) communications with Morpheus HypnoLight devices.
/// Handles device discovery, connection management, and command transmission.
/// </summary>
public class BleService : IBleService
{
    private IBluetoothLE? _bluetoothManager;
    private IAdapter? _adapter;
    private readonly ILogger<BleService> _logger;
    private readonly IDispatcherTimer? _connectionCheckTimer;
    private readonly IDispatcherTimer? _autoConnectTimer;
    private const int CONNECTION_CHECK_INTERVAL = 5000; // Check every 5 seconds
    private const int AUTO_CONNECT_INTERVAL = 30000;   // Try to connect every 30 seconds
    private bool _mhlConnected = false;
    private IDevice? MHLIDevice = null;
    private IService? MHLIService { get; set; } = null;
    private ICharacteristic? MHLCommandChannel { get; set; } = null;
    private ICharacteristic? MHLStatusChannel { get; set; } = null;
    private readonly SemaphoreSlim _bleWriteSemaphore = new(1, 1);
    private const int BLE_WRITE_DELAY_MS = 10; // Delay between BLE writes 50?
    // Safe default: fits a 23-byte ATT MTU (1 opcode + 2 offset + 17 data), matches firmware/scripts/ble_transfer.py.
    private const int DEFAULT_BLE_TRANSFER_CHUNK_SIZE = 17;
    private const int REQUESTED_BLE_MTU = 512;
    private int _bleTransferChunkSize = DEFAULT_BLE_TRANSFER_CHUNK_SIZE;
    private const int COMMAND_STATUS_TIMEOUT_MS = 5000;

    #region properties
    private bool _isConnected = false;
    /// <summary>
    /// Gets a value indicating whether a Morpheus HypnoLight device is currently connected.
    /// </summary>
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            _isConnected = value;
            ConnectedChanged?.Invoke(this, value);
        }
    }
    public event EventHandler<bool>? ConnectedChanged;


    private string _status = "MHL not connected";
    /// <summary>
    /// Gets or sets the current status message for the user.
    /// </summary>
    public string Status
    {
        get => _status;
        set
        {
            _status = value;
            StatusChanged?.Invoke(this, value);
        }
    }
    public event EventHandler<string>? StatusChanged;

    /// <summary>
    /// Raised when a status notification (echoed opcode + result code) is received from the device.
    /// </summary>
    public event EventHandler<(byte Opcode, byte ResultCode)>? CommandStatusReceived;


    private bool _isConnecting = false;
    /// <summary>
    /// Gets a value indicating whether the service is currently trying to connect to a device.
    /// </summary>
    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            _isConnecting = value;
            ConnectingChanged?.Invoke(this, value);
        }
    }
    public event EventHandler<bool>? ConnectingChanged;


    private int _currentBrightness = 80;
    /// <summary>
    /// Gets or sets the current global brightness value (0-100).
    /// </summary>
    public int CurrentBrightness
    {
        get => _currentBrightness;
        private set
        {
            if (_currentBrightness != value)
            {
                if (value > 100)
                {
                    _logger.LogError("Brightness {value} must be between 0 and 100", value);
                    value = 100;
                }
                _currentBrightness = value;
                _logger.LogInformation("Current brightness updated to: {value}", value);
            }
        }
    }
    /// <summary>
    /// Gets whether Bluetooth is currently enabled on the device.
    /// </summary>
    public bool IsBluetoothEnabled => _bluetoothManager?.IsAvailable == true && _bluetoothManager?.IsOn == true;

    #endregion

    /// <summary>
    /// Initializes a new instance of the BleService class.
    /// Sets up BLE adapter, scanner configuration, and connection monitoring.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information</param>
    public BleService(ILogger<BleService> logger)
    {
        _logger = logger;

        // Initialize timers first (safe operations)
        _connectionCheckTimer = Application.Current?.Dispatcher.CreateTimer();
        if (_connectionCheckTimer != null)
        {
            _connectionCheckTimer.Interval = TimeSpan.FromMilliseconds(CONNECTION_CHECK_INTERVAL);
            _connectionCheckTimer.Tick += ConnectionCheckTimer_Tick;
        }

        _autoConnectTimer = Application.Current?.Dispatcher.CreateTimer();
        if (_autoConnectTimer != null)
        {
            _autoConnectTimer.Interval = TimeSpan.FromMilliseconds(AUTO_CONNECT_INTERVAL);
            _autoConnectTimer.Tick += AutoConnectTimer_Tick;
        }

        // Initialize Bluetooth (potentially unsafe - may throw exception)
        try
        {
            InitializeBluetooth();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error during Bluetooth initialization - Bluetooth functionality will be disabled");
            _bluetoothManager = null;
            _adapter = null;
        }
    }

    /// <summary>
    /// Initializes the Bluetooth adapter. This is separated from the constructor to prevent
    /// exceptions during dependency injection from crashing the application.
    /// </summary>
    private void InitializeBluetooth()
    {
        // First, try to get the Bluetooth manager
        try
        {
            _bluetoothManager = CrossBluetoothLE.Current;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to access CrossBluetoothLE.Current - Bluetooth functionality will be disabled");
            _bluetoothManager = null;
            _adapter = null;
            return;
        }

        // Then, try to get the adapter
        try
        {
            _adapter = _bluetoothManager?.Adapter;

            if (_adapter == null)
            {
                _logger.LogWarning("Bluetooth adapter is null - Bluetooth may not be available on this device");
                return;
            }

            // Set up scanner
            _adapter.ScanMode = ScanMode.LowLatency;
            _adapter.ScanTimeout = 20000; // ms

            // Register event handlers
            _adapter.DeviceDiscovered += OnDeviceDiscovered;
            _adapter.DeviceConnected += OnDeviceConnected;
            _adapter.DeviceDisconnected += OnDeviceDisconnected;
            _adapter.ScanTimeoutElapsed += Adapter_ScanTimeoutElapsed;

            _logger.LogInformation("Bluetooth adapter initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Bluetooth adapter - Bluetooth functionality will be disabled");
            _adapter = null;
        }
    }

    #region Bluetooth management

    /// <summary>
    /// Checks if Bluetooth is enabled and available and that all required permissions are granted.
    /// </summary>
    /// <returns>True if Bluetooth can be used, false otherwise.</returns>
    public async Task<bool> CheckBluetoothStatusAsync()
    {
        try
        {
            if (_adapter == null)
            {
                _logger.LogError("Bluetooth adapter is null - cannot check Bluetooth status");
                return false;
            }

            // Check if Bluetooth is available and enabled
            if (!_bluetoothManager?.IsAvailable ?? true)
            {
                _logger.LogWarning("Bluetooth is not available on this device");
                return false;
            }

            if (!_bluetoothManager?.IsOn ?? true)
            {
                _logger.LogWarning("Bluetooth is not enabled");
                return false;
            }

            // Check permissions (especially important on Android)
            if (!await CheckAndRequestBluetoothPermissions())
            {
                _logger.LogWarning("Bluetooth permissions not granted");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error checking Bluetooth status: {}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Starts scanning for Morpheus HypnoLight devices and connects to the first one found.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_adapter == null)
        {
            _logger.LogError("Bluetooth adapter is null - cannot connect");
            return;
        }

        if (IsConnected || IsConnecting)
            return;

        IsConnecting = true;
        _autoConnectTimer?.Stop();
        _logger.LogInformation("Auto-connect stopped. Scanning for MHL");

        await ScanForMHL();

        if (MHLIDevice != null)
            await ConnectToDevice();
        IsConnecting = false;
    }

    /// <summary>
    /// Disconnects from the current Morpheus HypnoLight device and restarts the auto-connect timer.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_adapter == null)
        {
            _logger.LogError("Bluetooth adapter is null - cannot disconnect");
            return;
        }

        Status = $"Disconnecting ...";
        StopConnectionCheck();
        if (MHLStatusChannel != null)
        {
            MHLStatusChannel.ValueUpdated -= OnStatusCharacteristicUpdated;
            MHLStatusChannel = null;
        }
        try
        {
            // Try to clean up the connection
            if (MHLIDevice is not null)
                await _adapter.DisconnectDeviceAsync(MHLIDevice);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error during disconnect cleanup: {}", ex.Message);
        }
        _mhlConnected = false;
        IsConnected = false;
        Status = $"Disconnected";
        _logger.LogInformation("connection to the MHL closed - restarting auto-connect");
        _autoConnectTimer?.Start();
    }

    /// <summary>
    /// Disconnects from the current device without restarting the auto-connect timer.
    /// </summary>
    /// <remarks>Primarily used for testing.</remarks>
    public async Task ForceDisconnectAsync()
    {
        await DisconnectAsync();
        _mhlConnected = false;
        IsConnected = false;
        _logger.LogInformation("Force disconnection - stopping auto-connect");
        _autoConnectTimer?.Stop();
    }


    /// <summary>
    /// Periodically checks if the connected device is still reachable.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="e">Event arguments</param>
    /// <remarks>
    /// This method is called by the connection check timer every CONNECTION_CHECK_INTERVAL milliseconds.
    /// If the device becomes unreachable, it initiates the disconnection handling process.
    /// </remarks>
    private async void ConnectionCheckTimer_Tick(object? sender, EventArgs e)
    {
        if (MHLIDevice != null && _mhlConnected)
        {
            try
            {
                // Check if device is still reachable
                var services = await MHLIDevice.GetServicesAsync();
                if (services == null || !services.Any())
                {
                    _logger.LogInformation("ToggleConnectionToMHL lost detected by status check");
                    await DisconnectAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error checking connection: {}", ex.Message);
                if (MHLIDevice != null)
                {
                    await DisconnectAsync();
                }
            }
        }
    }

    /// <summary>
    /// Periodically attempts to auto-connect to a Morpheus HypnoLight device.
    /// </summary>
    private async void AutoConnectTimer_Tick(object? sender, EventArgs e)
    {
        if (!_mhlConnected)
        {
            _logger.LogInformation("Auto-connect: attempting to find and connect to MHL");
            await ConnectAsync();
        }
    }


    /// <summary>
    /// Starts the connection monitoring timer.
    /// </summary>
    private void StartConnectionCheck()
    {
        _connectionCheckTimer?.Start();
        _logger.LogInformation("Started connection monitoring");
    }

    /// <summary>
    /// Stops the connection monitoring timer.
    /// </summary>
    private void StopConnectionCheck()
    {
        _connectionCheckTimer?.Stop();
        _logger.LogInformation("Stopped connection monitoring");
    }

    /// <summary>
    /// Handler for BLE scan timeout events.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="e">Event arguments</param>
    private void Adapter_ScanTimeoutElapsed(object? sender, EventArgs e)
    {
        _logger.LogInformation("Could not find a Morpheus HypnoLight - restarting auto-connection timer");
        Status = "MHL not found";
        _autoConnectTimer?.Start();
    }

    /// <summary>
    /// Handler for device discovery events during BLE scanning.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="args">Device event arguments containing discovered device information</param>
    /// <remarks>
    /// Only processes devices with names starting with "HypnoLight".
    /// When a Morpheus HypnoLight device is found (devices with names starting with "HypnoLight"), 
    /// scanning is stopped and connection can proceed.
    /// </remarks>
    private async void OnDeviceDiscovered(object? sender, DeviceEventArgs args)
    {
        if (args.Device?.Name == null)
        {
            _logger.LogInformation("Skipping device with null name");
            return;
        }

        if (args.Device.Name.StartsWith("HypnoLight"))
        {
            _logger.LogInformation("Found MHL device: {} (ID: {}, RSSI: {})", args.Device.Name, args.Device.Id, args.Device.Rssi);
            _ = new BleDevice(args.Device.Name, args.Device.Id, args.Device.Rssi);
            MHLIDevice = args.Device;
            if (_adapter != null)
                await _adapter.StopScanningForDevicesAsync();
            Status = $"Found {args.Device.Name}";
        }
        else
        {
            _logger.LogInformation("Skipping device: {}", args.Device.Name);
        }
    }

    /// <summary>
    /// Initiates a scan for Morpheus HypnoLight devices.
    /// </summary>
    /// <remarks>
    /// Checks for necessary Bluetooth permissions before starting the scan.
    /// Updates status messages to inform the user of the scanning process.
    /// </remarks>
    private async Task ScanForMHL()
    {
        try
        {
            _logger.LogInformation("Scanning for a Morpheus HypnoLight...");
            Status = "Searching MHL ...";

            if (!await CheckAndRequestBluetoothPermissions())
            {
                _logger.LogError("Bluetooth permission problems");
                Status = "Bluetooth permission problems";
                // Restart auto-connect timer to retry when permissions are granted
                _autoConnectTimer?.Start();
                return;
            }
            if (_bluetoothManager == null || _adapter == null || !(_bluetoothManager.IsAvailable && _bluetoothManager.IsOn))
            {
                _logger.LogError("Bluetooth is not enabled");
                Status = "Bluetooth is not enabled";
                // Restart auto-connect timer to retry when Bluetooth is enabled
                _autoConnectTimer?.Start();
                return;
            }
            if (_adapter.IsScanning)
                await _adapter.StopScanningForDevicesAsync();
            await _adapter.StartScanningForDevicesAsync();
            _logger.LogInformation("Scanning finished ");
        }
        catch (Exception ex)
        {
            _logger.LogError("Scan error: {}", ex.Message);
            // Restart auto-connect timer on scan errors to retry later
            _autoConnectTimer?.Start();
        }
    }

    /// <summary>
    /// Attempts to connect to the discovered Morpheus HypnoLight device.
    /// </summary>
    /// <remarks>
    /// This method is called after a device is discovered during scanning.
    /// It initiates the connection process, including discovering services and characteristics.
    /// </remarks>
    private async Task ConnectToDevice()
    {
        if (MHLIDevice == null)
        {
            _logger.LogError("MHL not found");
            Status = "MHL not found";
            return;
        }

        try
        {
            Status = $"Connecting to {MHLIDevice.Name}...";
            if (_adapter != null)
                await _adapter.ConnectToDeviceAsync(MHLIDevice);
            _logger.LogInformation("Connected to device {}", MHLIDevice.Name);

            Status = $"Connected to {MHLIDevice.Name} looking for MHL Service and Characteristics ...";

            // Give the Windows BLE stack a moment to finish service discovery.
            await Task.Delay(500);

            await GetMHLServiceAndCharacteristics();

            if (MHLIService == null || MHLStatusChannel == null || MHLCommandChannel == null)
            {
                _logger.LogError("Problem getting MHL characteristics");
                Status = "Problem getting characteristics";
                await DisconnectAsync();
                return;
            }

            await NegotiateBleMtuAsync();

            Status = $"{MHLIDevice.Name} ready";
            IsConnected = true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Connection error: {}", ex.Message);
            Status = $"Connection error: {ex.Message}";
            await DisconnectAsync();
        }
    }

    /// <summary>
    /// Requests a larger ATT MTU from the connected device and adjusts the chunk size used for
    /// sequence and step transfers. The firmware supports writes up to 517 bytes, so requesting 512
    /// lets us send payloads close to 200+ bytes per BLE packet.
    /// </summary>
    private async Task NegotiateBleMtuAsync()
    {
        if (MHLIDevice == null)
        {
            _logger.LogWarning("Cannot negotiate MTU: no connected device");
            return;
        }

        try
        {
            int negotiatedMtu = await MHLIDevice.RequestMtuAsync(REQUESTED_BLE_MTU);
            // Reserve 3 bytes for the BLE command header: 1 opcode + 2 offset bytes.
            int mtuChunkSize = negotiatedMtu - 3;
            if (mtuChunkSize < DEFAULT_BLE_TRANSFER_CHUNK_SIZE)
            {
                _logger.LogWarning(
                    "Negotiated MTU {negotiatedMtu} is too small; falling back to default chunk size {defaultChunkSize}",
                    negotiatedMtu, DEFAULT_BLE_TRANSFER_CHUNK_SIZE);
                _bleTransferChunkSize = DEFAULT_BLE_TRANSFER_CHUNK_SIZE;
            }
            else
            {
                _bleTransferChunkSize = mtuChunkSize;
                _logger.LogInformation(
                    "BLE MTU negotiated: {negotiatedMtu}, transfer chunk size: {chunkSize} bytes",
                    negotiatedMtu, _bleTransferChunkSize);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MTU negotiation failed; using default chunk size {defaultChunkSize}", DEFAULT_BLE_TRANSFER_CHUNK_SIZE);
            _bleTransferChunkSize = DEFAULT_BLE_TRANSFER_CHUNK_SIZE;
        }
    }

    /// <summary>
    /// Discovers the necessary services and characteristics for communication with the Morpheus HypnoLight device.
    /// </summary>
    /// <remarks>
    /// This method is called after a device is connected.
    /// It discovers the services and characteristics needed for sending commands and setting brightness.
    /// </remarks>
    private async Task GetMHLServiceAndCharacteristics()
    {
        Guid ServiceUuid = Guid.Parse("d4c38bc0-4f25-af02-8f15-a1b5c2a60000");
        Guid CommandUuid = Guid.Parse("d4c38bc0-4f25-af02-8f15-a1b5c2a60001");
        Guid StatusUuid = Guid.Parse("d4c38bc0-4f25-af02-8f15-a1b5c2a60002");

        if (MHLIDevice == null)
        {
            _logger.LogError("No Morpheus HypnoLight found");
            return;
        }

        const int maxRetries = 3;
        const int retryDelayMs = 500;

        try
        {
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                _logger.LogInformation("Getting service: {} (attempt {})", ServiceUuid, attempt + 1);
                MHLIService = await MHLIDevice.GetServiceAsync(ServiceUuid);
                if (MHLIService != null)
                {
                    break;
                }

                if (attempt < maxRetries - 1)
                {
                    _logger.LogWarning("MHL service not found, retrying...");
                    await Task.Delay(retryDelayMs);
                }
            }

            if (MHLIService == null)
            {
                _logger.LogError("Failed to get MHL service");
                return;
            }

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                _logger.LogInformation("Getting command ch: {} (attempt {})", CommandUuid, attempt + 1);
                MHLCommandChannel = await MHLIService.GetCharacteristicAsync(CommandUuid);
                if (MHLCommandChannel != null)
                {
                    break;
                }

                if (attempt < maxRetries - 1)
                {
                    _logger.LogWarning("Command characteristic not found, retrying...");
                    await Task.Delay(retryDelayMs);
                }
            }

            if (MHLCommandChannel == null)
            {
                _logger.LogError("Failed to get command characteristic");
                return;
            }

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                _logger.LogInformation("Getting status ch: {} (attempt {})", StatusUuid, attempt + 1);
                MHLStatusChannel = await MHLIService.GetCharacteristicAsync(StatusUuid);
                if (MHLStatusChannel != null)
                {
                    break;
                }

                if (attempt < maxRetries - 1)
                {
                    _logger.LogWarning("Status characteristic not found, retrying...");
                    await Task.Delay(retryDelayMs);
                }
            }

            if (MHLStatusChannel != null)
            {
                MHLStatusChannel.ValueUpdated += OnStatusCharacteristicUpdated;
                await MHLStatusChannel.StartUpdatesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting service: {}", ex.Message);
            MHLIService = null;
            MHLCommandChannel = null;
            MHLStatusChannel = null;
        }
    }

    /// <summary>
    /// Handler for status notifications received from the device's Status characteristic.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="e">Characteristic update event arguments containing the 2-byte status payload</param>
    /// <remarks>See <c>doc/ble_protocol.md</c> for the status payload layout.</remarks>
    private void OnStatusCharacteristicUpdated(object? sender, CharacteristicUpdatedEventArgs e)
    {
        var data = e.Characteristic.Value;
        if (data == null || data.Length < 2)
        {
            _logger.LogWarning("Received malformed status notification ({length} bytes)", data?.Length ?? 0);
            return;
        }

        byte opcode = data[0];
        byte resultCode = data[1];
        _logger.LogInformation("Status notification: opcode=0x{opcode:X2} result=0x{resultCode:X2}", opcode, resultCode);
        CommandStatusReceived?.Invoke(this, (opcode, resultCode));
    }

    /// <summary>
    /// Handler for device connection events.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="args">Device event arguments containing connected device information</param>
    private void OnDeviceConnected(object? sender, DeviceEventArgs args)
    {
        _mhlConnected = true;
        _logger.LogInformation("Event: Connected to Device: {}", args.Device.Name);
        Status = $"Connected to {args.Device.Name}";
        StartConnectionCheck();
    }

    /// <summary>
    /// Handler for device disconnection events.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="args">Device event arguments containing disconnected device information</param>
    private void OnDeviceDisconnected(object? sender, DeviceEventArgs args)
    {
        _mhlConnected = false;
        _logger.LogInformation("Event: Disconnected from Device: {}", args.Device.Name);
        Status = $"Disconnected from {args.Device.Name}";
        StopConnectionCheck();
    }

    #endregion

    /// <summary>
    /// Sends the <c>PLAY</c> command (opcode <c>0x01</c>) to start or resume playback.
    /// </summary>
    /// <remarks>See <c>doc/ble_protocol.md</c> for the wire protocol.</remarks>
    public async Task PlayAsync()
    {
        if (!IsConnected || MHLCommandChannel is null)
            return;

        _logger.LogInformation("Sending PLAY command");
        await WriteBufferAsync(MHLCommandChannel, [0x01]);
    }

    /// <summary>
    /// Sends the <c>SEEK</c> command (opcode <c>0x04</c>) to jump to an absolute position.
    /// </summary>
    /// <param name="positionMs">The target position, in milliseconds.</param>
    /// <remarks>See <c>doc/ble_protocol.md</c> for the wire protocol.</remarks>
    public async Task SeekAsync(int positionMs)
    {
        if (!IsConnected || MHLCommandChannel is null)
            return;

        _logger.LogInformation("Sending SEEK command to {positionMs}ms", positionMs);
        var positionBytes = BitConverter.GetBytes((uint)positionMs);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(positionBytes);

        var command = new byte[5];
        command[0] = 0x04;
        Array.Copy(positionBytes, 0, command, 1, 4);
        await WriteBufferAsync(MHLCommandChannel, command);
    }

    /// <summary>
    /// Transfers a full sequence to the device.
    /// </summary>
    /// <param name="seq">The sequence to encode and transfer.</param>
    /// <remarks>
    /// Encodes <paramref name="seq"/> into the MHL compact wire format (see
    /// <see cref="CompactSequenceEncoder"/>) and drives <c>LOAD_START</c> (<c>0x10</c>), one or
    /// more <c>LOAD_CHUNK</c> (<c>0x11</c>) messages fragmented according to the negotiated ATT
    /// MTU, and <c>LOAD_COMMIT</c> (<c>0x12</c>). See <c>doc/ble_protocol.md</c>.
    /// </remarks>
    public async Task LoadSequenceAsync(Sequence seq)
    {
        ArgumentNullException.ThrowIfNull(seq);
        if (!IsConnected || MHLCommandChannel is null)
            return;

        byte[] compact = CompactSequenceEncoder.EncodeSequence(seq);
        _logger.LogInformation("Uploading compact sequence ({size} bytes)", compact.Length);

        var totalSizeBytes = ToLittleEndian(BitConverter.GetBytes((uint)compact.Length));
        await SendChunkedTransferAsync(0x10, totalSizeBytes, 0x11, 0x12, compact);
        _logger.LogInformation("Sequence loaded successfully");
    }

    /// <summary>
    /// Transfers an updated single step to the device.
    /// </summary>
    /// <param name="stepIndex">The index of the step to update.</param>
    /// <param name="step">The step to encode and transfer.</param>
    /// <remarks>
    /// Encodes <paramref name="step"/> into the MHL compact wire format (see
    /// <see cref="CompactSequenceEncoder"/>) and drives <c>UPDATE_STEP_START</c> (<c>0x20</c>), one
    /// or more <c>UPDATE_STEP_CHUNK</c> (<c>0x21</c>) messages fragmented according to the
    /// negotiated ATT MTU, and <c>UPDATE_STEP_COMMIT</c> (<c>0x22</c>).
    /// See <c>doc/ble_protocol.md</c>.
    /// </remarks>
    public async Task UpdateStepAsync(int stepIndex, Step step)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (stepIndex < 0 || stepIndex > byte.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(stepIndex), "Step index must fit in a single byte (0-255).");
        if (!IsConnected || MHLCommandChannel is null)
            return;

        var sequence = new Sequence { Steps = [step] };
        byte[] compact = CompactSequenceEncoder.EncodeSequence(sequence);
        _logger.LogInformation("Uploading step {index} update ({size} bytes)", stepIndex, compact.Length);

        var sizeBytes = ToLittleEndian(BitConverter.GetBytes((ushort)compact.Length));
        var startPayload = new byte[] { (byte)stepIndex, sizeBytes[0], sizeBytes[1] };
        await SendChunkedTransferAsync(0x20, startPayload, 0x21, 0x22, compact);
        _logger.LogInformation("Step {index} updated successfully", stepIndex);
    }

    /// <summary>
    /// Drives a <c>*_START</c> / <c>*_CHUNK</c>* / <c>*_COMMIT</c> transfer sequence, awaiting a
    /// successful status notification after each command.
    /// </summary>
    /// <param name="startOpcode">The opcode that begins the transfer (e.g. <c>LOAD_START</c>).</param>
    /// <param name="startPayload">The payload for the start command.</param>
    /// <param name="chunkOpcode">The opcode used for each data chunk (e.g. <c>LOAD_CHUNK</c>).</param>
    /// <param name="commitOpcode">The opcode that finalizes the transfer (e.g. <c>LOAD_COMMIT</c>).</param>
    /// <param name="data">The raw bytes to fragment and send as chunks.</param>
    private async Task SendChunkedTransferAsync(byte startOpcode, byte[] startPayload, byte chunkOpcode, byte commitOpcode, byte[] data)
    {
        await SendCommandAndAwaitStatusAsync(startOpcode, startPayload);

        int offset = 0;
        while (offset < data.Length)
        {
            int length = Math.Min(_bleTransferChunkSize, data.Length - offset);
            var offsetBytes = ToLittleEndian(BitConverter.GetBytes((ushort)offset));

            var chunkPayload = new byte[2 + length];
            Array.Copy(offsetBytes, 0, chunkPayload, 0, 2);
            Array.Copy(data, offset, chunkPayload, 2, length);

            await SendCommandAndAwaitStatusAsync(chunkOpcode, chunkPayload);
            offset += length;
        }

        await SendCommandAndAwaitStatusAsync(commitOpcode, []);
    }

    /// <summary>
    /// Writes a command to the command characteristic and awaits the matching status
    /// notification (echoed opcode) on the status characteristic.
    /// </summary>
    /// <param name="opcode">The command opcode.</param>
    /// <param name="payload">The opcode-specific payload.</param>
    /// <returns>The result code reported by the device.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the device reports a non-zero result code.</exception>
    /// <exception cref="TimeoutException">Thrown when no status notification is received in time.</exception>
    private async Task<byte> SendCommandAndAwaitStatusAsync(byte opcode, byte[] payload)
    {
        if (MHLCommandChannel is null)
            throw new InvalidOperationException("Command characteristic is not available.");

        var tcs = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);

        void OnStatus(object? sender, (byte Opcode, byte ResultCode) status)
        {
            if (status.Opcode == opcode)
                tcs.TrySetResult(status.ResultCode);
        }

        CommandStatusReceived += OnStatus;
        try
        {
            var command = new byte[1 + payload.Length];
            command[0] = opcode;
            Array.Copy(payload, 0, command, 1, payload.Length);
            await WriteBufferAsync(MHLCommandChannel, command);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(COMMAND_STATUS_TIMEOUT_MS));
            if (completed != tcs.Task)
                throw new TimeoutException($"No status notification received for opcode 0x{opcode:X2}.");

            byte result = await tcs.Task;
            if (result != 0x00)
                throw new InvalidOperationException($"Command 0x{opcode:X2} failed with result 0x{result:X2}.");
            return result;
        }
        finally
        {
            CommandStatusReceived -= OnStatus;
        }
    }

    /// <summary>
    /// Ensures a byte array produced by <see cref="BitConverter"/> is in little-endian order,
    /// as required by the MHL wire protocol.
    /// </summary>
    private static byte[] ToLittleEndian(byte[] bitConverterBytes)
    {
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(bitConverterBytes);
        return bitConverterBytes;
    }

    /// <summary>
    /// Sends the <c>STOP</c> command (opcode <c>0x03</c>) to the device asynchronously.
    /// </summary>
    public async Task StopAsync()
    {
        if (!IsConnected || MHLCommandChannel is null)
            return;

        _logger.LogInformation("Sending STOP command");
        await WriteBufferAsync(MHLCommandChannel, [0x03]);
    }

    /// <summary>
    /// Sends the <c>PAUSE</c> command (opcode <c>0x02</c>) to the device asynchronously.
    /// </summary>
    public async Task PauseAsync()
    {
        if (!IsConnected || MHLCommandChannel is null)
            return;

        _logger.LogInformation("Sending PAUSE command");
        await WriteBufferAsync(MHLCommandChannel, [0x02]);
    }

    /// <summary>
    /// Sends the global brightness value to the device asynchronously.
    /// </summary>
    /// <param name="value">The brightness percentage (0-100).</param>
    public async Task SendBrightnessAsync(int value)
    {
        if (!IsConnected || MHLCommandChannel is null)
            return;

        _logger.LogInformation("Sending brightness {value}", value);
        CurrentBrightness = value;

        var brightnessCommand = new byte[] { 0x05, (byte)value };  // BRIGHTNESS opcode
        try
        {
            await WriteBufferAsync(MHLCommandChannel, brightnessCommand);
            _logger.LogInformation("Brightness command sent successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to send brightness command: {error}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Sends the <c>SET_MODE</c> command (opcode <c>0x06</c>) to switch the device mode.
    /// </summary>
    /// <param name="mode">The desired operating mode.</param>
    /// <remarks>
    /// In Player mode, pausing playback turns the LEDs off.
    /// In Editor mode, pausing playback freezes the current LED state (LEDs remain on).
    /// </remarks>
    public async Task SetModeAsync(BleMode mode)
    {
        if (!IsConnected || MHLCommandChannel is null)
            return;

        _logger.LogInformation("Sending SET_MODE command: {mode}", mode);
        var command = new byte[] { 0x06, (byte)mode };
        await WriteBufferAsync(MHLCommandChannel, command);
    }

    /// <summary>
    /// Writes a data buffer to a BLE characteristic with a small inter-write delay.
    /// </summary>
    /// <param name="characteristic">The target BLE characteristic.</param>
    /// <param name="data">The bytes to write.</param>
    private async Task WriteBufferAsync(ICharacteristic characteristic, byte[] data)
    {
        try
        {
            await _bleWriteSemaphore.WaitAsync();
            await characteristic.WriteAsync(data);
            await Task.Delay(BLE_WRITE_DELAY_MS); // Add delay between writes
        }
        finally
        {
            _bleWriteSemaphore.Release();
        }
    }

    /// <summary>
    /// Writes a raw byte buffer to the command characteristic.
    /// </summary>
    /// <param name="data">The bytes to send.</param>
    public void WriteBuffer(byte[] data)
    {
        MHLCommandChannel?.WriteAsync(data);
    }


    /// <summary>
    /// Checks and requests the Bluetooth permissions required on the current platform.
    /// </summary>
    /// <returns>True if all required permissions are granted, false otherwise.</returns>
    private async Task<bool> CheckAndRequestBluetoothPermissions()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return await Task.FromResult(true);
            }
            else
            {
                var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
                    if (status != PermissionStatus.Granted)
                    {
                        _logger.LogError("Location permission is required for Bluetooth scanning");
                        return false;
                    }
                }

                if (OperatingSystem.IsAndroidVersionAtLeast(31)) // Android 12 or higher
                {
                    var scanStatus = await Permissions.RequestAsync<BluetoothScanPermission>();
                    var connectStatus = await Permissions.RequestAsync<BluetoothConnectPermission>();

                    if (scanStatus != PermissionStatus.Granted || connectStatus != PermissionStatus.Granted)
                    {
                        _logger.LogError("Bluetooth permissions are required");
                        return false;
                    }
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Error checking permissions: {}", ex.Message);
            return false;
        }
    }
}

// Custom permission classes for Android Bluetooth
public class BluetoothScanPermission : Permissions.BasePlatformPermission
{
#if ANDROID
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        ("android.permission.BLUETOOTH_SCAN", true)
    ];
#endif
}

public class BluetoothConnectPermission : Permissions.BasePlatformPermission
{
#if ANDROID
    public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
    [
        ("android.permission.BLUETOOTH_CONNECT", true)
    ];
#endif
}
