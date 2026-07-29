// Ignore Spelling: Ble Osc

using System.Linq;
using Microsoft.Extensions.Logging;
using MPHCore.Models;
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
    private bool _dmConnected = false;
    private IDevice? MHLIDevice = null;
    private IService? MHLIService { get; set; } = null;
    private ICharacteristic? MHLBrightChannel { get; set; } = null;
    private ICharacteristic? MHLCommandChannel { get; set; } = null;
    private readonly SemaphoreSlim _playSequenceSemaphore = new(1, 1);
    private readonly SemaphoreSlim _bleWriteSemaphore = new(1, 1);
    private const int BLE_WRITE_DELAY_MS = 10; // Delay between BLE writes 50?
    private volatile Sequence? _pendingSequence = null;
    private volatile Step? _pendingStep = null;

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
        _dmConnected = false;
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
        _dmConnected = false;
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
        if (MHLIDevice != null && _dmConnected)
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
        if (!_dmConnected)
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
    /// Only processes devices with names starting with "DM_".
    /// When a Morpheus HypnoLight device is found (devices with names starting with "DM_"), 
    /// scanning is stopped and connection can proceed.
    /// </remarks>
    private async void OnDeviceDiscovered(object? sender, DeviceEventArgs args)
    {
        if (args.Device?.Name == null)
        {
            _logger.LogInformation("Skipping device with null name");
            return;
        }

        if (args.Device.Name.StartsWith("DM_"))
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
            await GetMHLServiceAndCharacteristics();

            if (MHLIService == null || MHLBrightChannel == null || MHLCommandChannel == null)
            {
                _logger.LogError("Problem getting MHL characteristics");
                Status = "Problem getting characteristics";
                await DisconnectAsync();
                return;
            }

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
    /// Discovers the necessary services and characteristics for communication with the Morpheus HypnoLight device.
    /// </summary>
    /// <remarks>
    /// This method is called after a device is connected.
    /// It discovers the services and characteristics needed for sending commands and setting brightness.
    /// </remarks>
    private async Task GetMHLServiceAndCharacteristics()
    {
        Guid ServiceUuid = Guid.Parse("36794f20-3a88-418c-8df8-7394c5c80200");
        Guid CommandUuid = Guid.Parse("36794f20-3a88-418c-8df8-7394c5c80201");
        Guid VolumeUuid_ = Guid.Parse("36794f20-3a88-418c-8df8-7394c5c80202");

        if (MHLIDevice == null)
        {
            _logger.LogError("No Morpheus HypnoLight found");
            return;
        }

        try
        {
            _logger.LogInformation("Getting service: {}", ServiceUuid);
            MHLIService = await MHLIDevice.GetServiceAsync(ServiceUuid);

            if (MHLIService == null)
            {
                _logger.LogError("Failed to get MHL service");
                return;
            }

            _logger.LogInformation("Getting volume ch: {}", VolumeUuid_);
            MHLBrightChannel = await MHLIService.GetCharacteristicAsync(VolumeUuid_);
            if (MHLBrightChannel == null)
            {
                _logger.LogError("Failed to get brightness characteristic");
                return;
            }

            // Check if characteristic has write permission
            if (!MHLBrightChannel.CanWrite)
            {
                _logger.LogError("Brightness characteristic does not have write permission");
                return;
            }

            _logger.LogInformation("Getting command ch: {}", CommandUuid);
            MHLCommandChannel = await MHLIService.GetCharacteristicAsync(CommandUuid);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting service: {}", ex.Message);
            MHLIService = null;
            MHLBrightChannel = null;
            MHLCommandChannel = null;
        }
    }

    /// <summary>
    /// Handler for device connection events.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="args">Device event arguments containing connected device information</param>
    private void OnDeviceConnected(object? sender, DeviceEventArgs args)
    {
        _dmConnected = true;
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
        _dmConnected = false;
        _logger.LogInformation("Event: Disconnected from Device: {}", args.Device.Name);
        Status = $"Disconnected from {args.Device.Name}";
        StopConnectionCheck();
    }

    #endregion

    /// <summary>
    /// Sends a single step to the connected device asynchronously.
    /// </summary>
    /// <param name="step">The step to play.</param>
    public async Task PlayStepAsync(Step step)
    {
        if (step is null || !IsConnected || MHLCommandChannel is null)
            return;

        // Store this as the pending step
        _pendingStep = step;

        // If we can't get the semaphore immediately, return - another operation is in progress
        // and will pick up our pending step when it's done
        if (!_playSequenceSemaphore.Wait(0))
        {
            _logger.LogInformation("Step queued, waiting for current operation to complete");
            return;
        }

        try
        {
            while (true)
            {
                // Get the next step to play
                var stepToPlay = _pendingStep;
                if (stepToPlay == null)
                    break;

                // Clear the pending step before playing
                _pendingStep = null;

                // Play the step
                await WriteBufferAsync(MHLCommandChannel, [0x01]);  // Start command

                var command = new List<byte[]>();
                for (int i = 0; i < step.Oscillators.Count; i++)
                {
                    var oscillator = step.Oscillators[i];
                    byte[] buffer = OscToCommand(0, oscillator, i, (int)(step.DurationSeconds * 10));
                    _logger.LogInformation("async buffer {}", Convert.ToHexString(buffer));
                    command.Add(buffer);
                }

                foreach (var item in command)
                {
                    await WriteBufferAsync(MHLCommandChannel, item);
                }

                await WriteBufferAsync(MHLCommandChannel, [0x02]);  // End command
            }
        }
        finally
        {
            _playSequenceSemaphore.Release();
        }
    }

    /// <summary>
    /// Sends a single step to the connected device synchronously.
    /// </summary>
    /// <param name="step">The step to play.</param>
    public void PlayStep(Step step)
    {
        if (step is null || !IsConnected || MHLCommandChannel is null)
            return;

        // Play the step
        MHLCommandChannel.WriteAsync([0x01]);  // Start command

        var command = new List<byte[]>();
        for (int i = 0; i < step.Oscillators.Count; i++)
        {
            var oscillator = step.Oscillators[i];
            byte[] buffer = OscToCommand(0, oscillator, i, (int)(step.DurationSeconds * 10));
            _logger.LogInformation("buffer {}", Convert.ToHexString(buffer));
            command.Add(buffer);
        }

        foreach (var item in command)
        {
            MHLCommandChannel.WriteAsync(item);
        }

        MHLCommandChannel.WriteAsync([0x02]);  // End command

    }

    /// <summary>
    /// Sends the full sequence to the connected device asynchronously.
    /// </summary>
    /// <param name="sequence">The sequence to play.</param>
    public async Task PlaySequenceAsync(Sequence sequence)
    {
        if (sequence is null || !IsConnected || MHLCommandChannel is null)
            return;

        // Store this as the pending sequence
        _pendingSequence = sequence;

        // If we can't get the semaphore immediately, return - another operation is in progress
        // and will pick up our pending sequence when it's done
        if (!_playSequenceSemaphore.Wait(0))
        {
            _logger.LogInformation("Sequence queued, waiting for current operation to complete");
            return;
        }

        try
        {
            while (true)
            {
                // Get the next sequence to play
                var sequenceToPlay = _pendingSequence;
                if (sequenceToPlay == null)
                    break;

                // Clear the pending sequence before playing
                _pendingSequence = null;

                // Play the sequence
                await SendBrightnessAsync(CurrentBrightness);
                await WriteBufferAsync(MHLCommandChannel, [0x01]);  // Start command

                var command = new List<byte[]>();
                for (int i = 0; i < sequenceToPlay.Steps.Count; i++)
                {
                    var step = sequenceToPlay.Steps[i];
                    for (int j = 0; j < step.Oscillators.Count; j++)
                    {
                        var oscillator = step.Oscillators[j];
                        byte[] buffer = OscToCommand(i, oscillator, j, (int)(step.DurationSeconds * 10));
                        _logger.LogInformation("{}", Convert.ToHexString(buffer));
                        command.Add(buffer);
                    }
                }

                foreach (var item in command)
                {
                    await WriteBufferAsync(MHLCommandChannel, item);
                }

                await WriteBufferAsync(MHLCommandChannel, [0x02]);  // End command

                // If a new sequence was queued while we were playing, loop and play it
            }
        }
        finally
        {
            _playSequenceSemaphore.Release();
        }
    }



    /// <summary>
    /// Sends the full sequence to the connected device synchronously.
    /// </summary>
    /// <param name="sequence">The sequence to play.</param>
    public void PlaySequence(Sequence sequence)
    {
        if (sequence is null || !IsConnected || MHLCommandChannel is null)
            return;
        _logger.LogInformation("sequence name {} duration {} steps {}",
            sequence.Name, sequence.DurationSeconds, sequence.Steps.Count);

        // Play the sequence
        SendBrightness(CurrentBrightness);
        MHLCommandChannel.WriteAsync([0x01]);  // Start of frame

        var command = new List<byte[]>();
        for (int i = 0; i < sequence.Steps.Count; i++)
        {
            var step = sequence.Steps[i];
            for (int j = 0; j < step.Oscillators.Count; j++)
            {
                var oscillator = step.Oscillators[j];
                byte[] buffer = OscToCommand(i, oscillator, j, (int)(step.DurationSeconds * 10));
                _logger.LogInformation("{}", Convert.ToHexString(buffer));
                command.Add(buffer);
            }
        }

        foreach (var item in command)
        {
            MHLCommandChannel.WriteAsync(item);
        }

        MHLCommandChannel.WriteAsync([0x02]);  // End of frame command
    }

    /// <summary>
    /// Sends the <c>PLAY</c> command (opcode <c>0x01</c>) to start or resume playback.
    /// </summary>
    /// <remarks>
    /// Stub implementation: the MHL compact wire protocol is not yet wired up.
    /// See <c>doc/ble_protocol.md</c> for the target protocol.
    /// </remarks>
    public Task PlayAsync()
    {
        // TODO: send opcode 0x01 (PLAY) once the MHL wire protocol is implemented.
        _logger.LogWarning("PlayAsync is not implemented yet");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends the <c>SEEK</c> command (opcode <c>0x04</c>) to jump to an absolute position.
    /// </summary>
    /// <param name="positionMs">The target position, in milliseconds.</param>
    /// <remarks>
    /// Stub implementation: the MHL compact wire protocol is not yet wired up.
    /// See <c>doc/ble_protocol.md</c> for the target protocol.
    /// </remarks>
    public Task SeekAsync(int positionMs)
    {
        // TODO: send opcode 0x04 (SEEK) with a 4-byte little-endian position_ms payload.
        _ = positionMs;
        _logger.LogWarning("SeekAsync is not implemented yet");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Transfers a full sequence to the device.
    /// </summary>
    /// <param name="seq">The sequence to encode and transfer.</param>
    /// <remarks>
    /// Stub implementation: the MHL compact wire protocol is not yet wired up.
    /// Once implemented, this method will encode <paramref name="seq"/> into the compact wire
    /// format and internally drive <c>LOAD_START</c> (<c>0x10</c>), one or more <c>LOAD_CHUNK</c>
    /// (<c>0x11</c>) messages fragmented to the negotiated ATT MTU, and <c>LOAD_COMMIT</c> (<c>0x12</c>).
    /// See <c>doc/ble_protocol.md</c>.
    /// </remarks>
    public Task LoadSequenceAsync(Sequence seq)
    {
        // TODO: encode seq to the compact wire format, then implement
        // LOAD_START/LOAD_CHUNK*/LOAD_COMMIT fragmentation and transfer.
        _ = seq;
        _logger.LogWarning("LoadSequenceAsync is not implemented yet");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Transfers an updated single step to the device.
    /// </summary>
    /// <param name="stepIndex">The index of the step to update.</param>
    /// <param name="step">The step to encode and transfer.</param>
    /// <remarks>
    /// Stub implementation: the MHL compact wire protocol is not yet wired up.
    /// Once implemented, this method will encode <paramref name="step"/> into the compact wire
    /// format and internally drive <c>UPDATE_STEP_START</c> (<c>0x20</c>), one or more
    /// <c>UPDATE_STEP_CHUNK</c> (<c>0x21</c>) messages fragmented to the negotiated ATT MTU,
    /// and <c>UPDATE_STEP_COMMIT</c> (<c>0x22</c>). See <c>doc/ble_protocol.md</c>.
    /// </remarks>
    public Task UpdateStepAsync(int stepIndex, Step step)
    {
        // TODO: encode step to the compact wire format, then implement
        // UPDATE_STEP_START/UPDATE_STEP_CHUNK*/UPDATE_STEP_COMMIT fragmentation and transfer.
        _ = stepIndex;
        _ = step;
        _logger.LogWarning("UpdateStepAsync is not implemented yet");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sends the stop command to the device asynchronously.
    /// </summary>
    public async Task StopAsync()
    {
        if (!IsConnected || MHLCommandChannel is null)
            return;

        _logger.LogInformation("Stopping sequence");

        await SendBrightnessAsync(CurrentBrightness);
        await WriteBufferAsync(MHLCommandChannel, [0xFF]);  // Stop command
    }

    /// <summary>
    /// Sends the stop command to the device synchronously.
    /// </summary>
    public void Stop()
    {
        if (!IsConnected || MHLCommandChannel is null)
            return;

        _logger.LogInformation("Stopping sequence");

        SendBrightness(CurrentBrightness);
        MHLCommandChannel.WriteAsync([0xFF]);  // Stop command
    }

    /// <summary>
    /// Sends the pause command to the device asynchronously.
    /// </summary>
    public async Task PauseAsync()
    {
        if (!IsConnected || MHLCommandChannel is null)
            return;

        _logger.LogInformation("Pausing sequence");

        //await SendBrightnessAsync(CurrentBrightness);
        await WriteBufferAsync(MHLCommandChannel, [0x01]);
        await WriteBufferAsync(MHLCommandChannel, [0x02]);
    }

    /// <summary>
    /// Sends the pause command to the device synchronously.
    /// </summary>
    public void PauseSequence()
    {
        if (!IsConnected || MHLCommandChannel is null)
            return;

        _logger.LogInformation("Pausing sequence");

        //SendBrightness(CurrentBrightness);
        MHLCommandChannel.WriteAsync([0x01]);  // start frame
        MHLCommandChannel.WriteAsync([0x02]);  // end frame
    }

    /// <summary>
    /// Sends the global brightness value to the device asynchronously.
    /// </summary>
    /// <param name="value">The brightness percentage (0-100).</param>
    public async Task SendBrightnessAsync(int value)
    {
        if (!IsConnected || MHLBrightChannel is null)
            return;

        _logger.LogInformation("Sending brightness {value}", value);
        CurrentBrightness = value;

        var brightnessCommand = new byte[] { (byte)value };
        try
        {
            await WriteBufferAsync(MHLBrightChannel, brightnessCommand);
            _logger.LogInformation("Brightness command sent successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to send brightness command: {error}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Sends the global brightness value to the device synchronously.
    /// </summary>
    /// <param name="value">The brightness percentage (0-100).</param>
    public void SendBrightness(int value)
    {
        if (!IsConnected || MHLBrightChannel is null)
            return;

        _logger.LogInformation("Sending brightness {value}", value);
        CurrentBrightness = value;

        var brightnessCommand = new byte[] { (byte)value };
        try
        {
            MHLBrightChannel.WriteAsync(brightnessCommand);
            _logger.LogInformation("Brightness command sent successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to send brightness command: {error}", ex.Message);
            throw;
        }
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
    /// Converts an oscillator step into a compact BLE command.
    /// </summary>
    /// <param name="stepIndex">Index of the step containing the oscillator.</param>
    /// <param name="osc">The oscillator to encode.</param>
    /// <param name="oscIndex">Index of the oscillator within the step.</param>
    /// <param name="duration">Duration of the step in 1/10 seconds.</param>
    /// <returns>
    /// The encoded command bytes. Currently returns an empty array as the MHL
    /// compact wire encoder is not yet implemented.
    /// </returns>
    /// <remarks>
    /// This method is a placeholder. The MHL compact BLE wire encoder still
    /// needs to be implemented once the protocol is finalized.
    /// </remarks>
    public static byte[] OscToCommand(int stepIndex, Oscillator osc, int oscIndex, int duration)
    {
        // TODO: implement MHL compact wire encoder once the BLE protocol is finalized.
        _ = stepIndex;
        _ = osc;
        _ = oscIndex;
        _ = duration;
        return [];
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
