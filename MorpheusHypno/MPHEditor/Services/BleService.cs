// Ignore Spelling: Ble Osc

using Plugin.BLE;
using Plugin.BLE.Abstractions.Contracts;
using Plugin.BLE.Abstractions.EventArgs;
using MPHCore.Models;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace MPHEditor.Services;

/// <summary>
/// Service for managing Bluetooth Low Energy (BLE) communications with Dream Machine devices.
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
    private IDevice? DmIDevice = null;
    private IService? DmIService { get; set; } = null;
    private ICharacteristic? DmBrightChannel { get; set; } = null;
    private ICharacteristic? DmCommandChannel { get; set; } = null;
    private readonly SemaphoreSlim _playSequenceSemaphore = new(1, 1);
    private readonly SemaphoreSlim _bleWriteSemaphore = new(1, 1);
    private const int BLE_WRITE_DELAY_MS = 10; // Delay between BLE writes 50?
    private volatile Sequence? _pendingSequence = null;
    private volatile Step? _pendingStep = null;

    #region properties
    private bool _isConnected = false;
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


    private string _status = "DM not connected";
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
    /// Checks if Bluetooth is enabled and available on the device.
    /// Also checks for necessary permissions on Android.
    /// </summary>
    /// <returns>True if Bluetooth is enabled and permissions are granted, false otherwise</returns>
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
            if (!_bluetoothManager.IsAvailable)
            {
                _logger.LogWarning("Bluetooth is not available on this device");
                return false;
            }

            if (!_bluetoothManager.IsOn)
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
        _logger.LogInformation("Auto-connect stopped. Scanning for DM");

        await ScanForDM();

        if (DmIDevice != null)
            await ConnectToDevice();
        IsConnecting = false;
    }

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
            if (DmIDevice is not null)
                await _adapter.DisconnectDeviceAsync(DmIDevice);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error during disconnect cleanup: {}", ex.Message);
        }
        _dmConnected = false;
        IsConnected = false;
        Status = $"Disconnected";
        _logger.LogInformation("connection to the DM closed - restarting auto-connect");
        _autoConnectTimer?.Start();
    }

    /// <summary>
    /// Mainly for testing does not restart auto-connect timer
    /// </summary>
    /// <returns></returns>
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
        if (DmIDevice != null && _dmConnected)
        {
            try
            {
                // Check if device is still reachable
                var services = await DmIDevice.GetServicesAsync();
                if (services == null || !services.Any())
                {
                    _logger.LogInformation("ToggleConnectionToDm lost detected by status check");
                    await DisconnectAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error checking connection: {}", ex.Message);
                if (DmIDevice != null)
                {
                    await DisconnectAsync();
                }
            }
        }
    }

    private async void AutoConnectTimer_Tick(object? sender, EventArgs e)
    {
        if (!_dmConnected)
        {
            _logger.LogInformation("Auto-connect: attempting to find and connect to DM");
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
        _logger.LogInformation("Could not find a Dream Machine - restarting auto-connection timer");
        Status = "DM not found";
        _autoConnectTimer?.Start();
    }

    /// <summary>
    /// Handler for device discovery events during BLE scanning.
    /// </summary>
    /// <param name="sender">Event sender</param>
    /// <param name="args">Device event arguments containing discovered device information</param>
    /// <remarks>
    /// Only processes devices with names starting with "DM_".
    /// When a Dream Machine device is found (devices with names starting with "DM_"), 
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
            _logger.LogInformation("Found DM device: {} (ID: {}, RSSI: {})", args.Device.Name, args.Device.Id, args.Device.Rssi);
            _ = new BleDevice(args.Device.Name, args.Device.Id, args.Device.Rssi);
            DmIDevice = args.Device;
            await _adapter.StopScanningForDevicesAsync();
            Status = $"Found {args.Device.Name}";
        }
        else
        {
            _logger.LogInformation("Skipping device: {}", args.Device.Name);
        }
    }

    /// <summary>
    /// Initiates a scan for Dream Machine devices.
    /// </summary>
    /// <remarks>
    /// Checks for necessary Bluetooth permissions before starting the scan.
    /// Updates status messages to inform the user of the scanning process.
    /// </remarks>
    private async Task ScanForDM()
    {
        try
        {
            _logger.LogInformation("Scanning for a Dream Machine...");
            Status = "Searching DM ...";

            if (!await CheckAndRequestBluetoothPermissions())
            {
                _logger.LogError("Bluetooth permission problems");
                Status = "Bluetooth permission problems";
                // Restart auto-connect timer to retry when permissions are granted
                _autoConnectTimer?.Start();
                return;
            }
            if (!(_bluetoothManager.IsAvailable && _bluetoothManager.IsOn))
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
    /// Attempts to connect to the discovered Dream Machine device.
    /// </summary>
    /// <remarks>
    /// This method is called after a device is discovered during scanning.
    /// It initiates the connection process, including discovering services and characteristics.
    /// </remarks>
    private async Task ConnectToDevice()
    {
        if (DmIDevice == null)
        {
            _logger.LogError("DM not found");
            Status = "DM not found";
            return;
        }

        try
        {
            Status = $"Connecting to {DmIDevice.Name}...";
            await _adapter.ConnectToDeviceAsync(DmIDevice);
            _logger.LogInformation("Connected to device {}", DmIDevice.Name);

            Status = $"Connected to {DmIDevice.Name} looking for DM Service and Characteristics ...";
            await GetDmServiceAndCharacteristics();

            if (DmIService == null || DmBrightChannel == null || DmCommandChannel == null)
            {
                _logger.LogError("Problem getting DM characteristics");
                Status = "Problem getting characteristics";
                await DisconnectAsync();
                return;
            }

            Status = $"{DmIDevice.Name} ready";
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
    /// Discovers the necessary services and characteristics for communication with the Dream Machine device.
    /// </summary>
    /// <remarks>
    /// This method is called after a device is connected.
    /// It discovers the services and characteristics needed for sending commands and setting brightness.
    /// </remarks>
    private async Task GetDmServiceAndCharacteristics()
    {
        Guid ServiceUuid = Guid.Parse("36794f20-3a88-418c-8df8-7394c5c80200");
        Guid CommandUuid = Guid.Parse("36794f20-3a88-418c-8df8-7394c5c80201");
        Guid VolumeUuid_ = Guid.Parse("36794f20-3a88-418c-8df8-7394c5c80202");

        if (DmIDevice == null)
        {
            _logger.LogError("No Dream Machine found");
            return;
        }

        try
        {
            _logger.LogInformation("Getting service: {}", ServiceUuid);
            DmIService = await DmIDevice.GetServiceAsync(ServiceUuid);

            if (DmIService == null)
            {
                _logger.LogError("Failed to get DM service");
                return;
            }

            _logger.LogInformation("Getting volume ch: {}", VolumeUuid_);
            DmBrightChannel = await DmIService.GetCharacteristicAsync(VolumeUuid_);
            if (DmBrightChannel == null)
            {
                _logger.LogError("Failed to get brightness characteristic");
                return;
            }

            // Check if characteristic has write permission
            if (!DmBrightChannel.CanWrite)
            {
                _logger.LogError("Brightness characteristic does not have write permission");
                return;
            }

            _logger.LogInformation("Getting command ch: {}", CommandUuid);
            DmCommandChannel = await DmIService.GetCharacteristicAsync(CommandUuid);
        }
        catch (Exception ex)
        {
            _logger.LogError("Error getting service: {}", ex.Message);
            DmIService = null;
            DmBrightChannel = null;
            DmCommandChannel = null;
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

    public async Task PlayStepAsync(Step step)
    {
        if (step is null || !IsConnected || DmCommandChannel is null)
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
                await WriteBufferAsync(DmCommandChannel, [0x01]);  // Start command

                var command = new List<byte[]>();
                for (int i = 0; i < step.Oscillators.Count; i++)
                {
                    var oscillator = step.Oscillators[i];
                    if (oscillator.LEDs.Count == 0)
                        continue;   // skip empty
                    byte[] buffer = OscToCommand(0, oscillator, i, step.Duration);
                    _logger.LogInformation("async buffer {}", Convert.ToHexString(buffer));
                    command.Add(buffer);
                }

                foreach (var item in command)
                {
                    await WriteBufferAsync(DmCommandChannel, item);
                }

                await WriteBufferAsync(DmCommandChannel, [0x02]);  // End command
            }
        }
        finally
        {
            _playSequenceSemaphore.Release();
        }
    }

    public void PlayStep(Step step)
    {
        if (step is null || !IsConnected || DmCommandChannel is null)
            return;

        // Play the step
        DmCommandChannel.WriteAsync([0x01]);  // Start command

        var command = new List<byte[]>();
        for (int i = 0; i < step.Oscillators.Count; i++)
        {
            var oscillator = step.Oscillators[i];
            if (oscillator.LEDs.Count == 0)
                continue;   // skip empty
            byte[] buffer = OscToCommand(0, oscillator, i, step.Duration);
            _logger.LogInformation("buffer {}", Convert.ToHexString(buffer));
            command.Add(buffer);
        }

        foreach (var item in command)
        {
            DmCommandChannel.WriteAsync(item);
        }

        DmCommandChannel.WriteAsync([0x02]);  // End command

    }

    public async Task PlaySequenceAsync(Sequence sequence)
    {
        if (sequence is null || !IsConnected || DmCommandChannel is null)
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
                await WriteBufferAsync(DmCommandChannel, [0x01]);  // Start command

                var command = new List<byte[]>();
                for (int i = 0; i < sequenceToPlay.Steps.Count; i++)
                {
                    var step = sequenceToPlay.Steps[i];
                    for (int j = 0; j < step.Oscillators.Count; j++)
                    {
                        var oscillator = step.Oscillators[j];
                        if (oscillator.LEDs.Count == 0)
                            continue;   // skip empty
                        byte[] buffer = OscToCommand(i, oscillator, j, step.Duration);
                        _logger.LogInformation("{}", Convert.ToHexString(buffer));
                        command.Add(buffer);
                    }
                }

                foreach (var item in command)
                {
                    await WriteBufferAsync(DmCommandChannel, item);
                }

                await WriteBufferAsync(DmCommandChannel, [0x02]);  // End command

                // If a new sequence was queued while we were playing, loop and play it
            }
        }
        finally
        {
            _playSequenceSemaphore.Release();
        }
    }



    public void PlaySequence(Sequence sequence)
    {
        if (sequence is null || !IsConnected || DmCommandChannel is null)
            return;
        _logger.LogInformation("sequence name {} duration {} steps {}",
            sequence.Name, sequence.Duration, sequence.Steps.Count);

        // Play the sequence
        SendBrightness(CurrentBrightness);
        DmCommandChannel.WriteAsync([0x01]);  // Start of frame

        var command = new List<byte[]>();
        for (int i = 0; i < sequence.Steps.Count; i++)
        {
            var step = sequence.Steps[i];
            for (int j = 0; j < step.Oscillators.Count; j++)
            {
                var oscillator = step.Oscillators[j];
                if (oscillator.LEDs.Count == 0)
                    continue;   // skip empty
                byte[] buffer = OscToCommand(i, oscillator, j, step.Duration);
                _logger.LogInformation("{}", Convert.ToHexString(buffer));
                command.Add(buffer);
            }
        }

        foreach (var item in command)
        {
            DmCommandChannel.WriteAsync(item);
        }

        DmCommandChannel.WriteAsync([0x02]);  // End of frame command
    }

    public async Task StopAsync()
    {
        if (!IsConnected || DmCommandChannel is null)
            return;

        _logger.LogInformation("Stopping sequence");

        await SendBrightnessAsync(CurrentBrightness);
        await WriteBufferAsync(DmCommandChannel, [0xFF]);  // Stop command
    }

    public void Stop()
    {
        if (!IsConnected || DmCommandChannel is null)
            return;

        _logger.LogInformation("Stopping sequence");

        SendBrightness(CurrentBrightness);
        DmCommandChannel.WriteAsync([0xFF]);  // Stop command
    }

    public async Task PauseSequenceAsync()
    {
        if (!IsConnected || DmCommandChannel is null)
            return;

        _logger.LogInformation("Pausing sequence");

        //await SendBrightnessAsync(CurrentBrightness);
        await WriteBufferAsync(DmCommandChannel, [0x01]);
        await WriteBufferAsync(DmCommandChannel, [0x02]);
    }

    public void PauseSequence()
    {
        if (!IsConnected || DmCommandChannel is null)
            return;

        _logger.LogInformation("Pausing sequence");

        //SendBrightness(CurrentBrightness);
        DmCommandChannel.WriteAsync([0x01]);  // start frame
        DmCommandChannel.WriteAsync([0x02]);  // end frame
    }

    /// <summary>
    /// Send brightness value to Dream Machine
    /// </summary>
    /// <param name="value">Brightness value (0-100)</param>
    public async Task SendBrightnessAsync(int value)
    {
        if (!IsConnected || DmBrightChannel is null)
            return;

        _logger.LogInformation("Sending brightness {value}", value);
        CurrentBrightness = value;

        var brightnessCommand = new byte[] { (byte)value };
        try
        {
            await WriteBufferAsync(DmBrightChannel, brightnessCommand);
            _logger.LogInformation("Brightness command sent successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to send brightness command: {error}", ex.Message);
            throw;
        }
    }

    public void SendBrightness(int value)
    {
        if (!IsConnected || DmBrightChannel is null)
            return;

        _logger.LogInformation("Sending brightness {value}", value);
        CurrentBrightness = value;

        var brightnessCommand = new byte[] { (byte)value };
        try
        {
            DmBrightChannel.WriteAsync(brightnessCommand);
            _logger.LogInformation("Brightness command sent successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to send brightness command: {error}", ex.Message);
            throw;
        }
    }

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

    public void WriteBuffer(byte[] data)
    {
        DmCommandChannel?.WriteAsync(data);
    }


    /// <summary>
    /// Converts the values of an Oscillator into a command to be sent to the DM
    /// <param name="stepIndex">Index of the step</param>
    /// <param name="osc">Oscillator to convert</param>
    /// <param name="oscIndex">Index of the oscillator in the step</param>
    /// <param name="duration">Duration of the step in 1/10 seconds</param>
    /// <returns>The command to be sent to the DM (16 bytes array)</returns>
    /// </summary>
    public static byte[] OscToCommand(int stepIndex, Oscillator osc, int oscIndex, int duration)
    {
        var command = new List<byte>
        {
            (byte)stepIndex,
            (byte)oscIndex
        };

        var durationBytes = BitConverter.GetBytes((ushort)duration);
        Array.Reverse(durationBytes);
        command.AddRange(durationBytes);

        var ledsValue = (ushort)(
              (ushort)(osc.LEDs.Contains("A1") ? 1 : 0)
            + (ushort)(osc.LEDs.Contains("A2") ? 2 : 0)
            + (ushort)(osc.LEDs.Contains("A3") ? 4 : 0) // TODO test
            + (ushort)(osc.LEDs.Contains("A4") ? 8 : 0)
            + (ushort)(osc.LEDs.Contains("A5") ? 16 : 0)
            + (ushort)(osc.LEDs.Contains("B1") ? 32 : 0)
            + (ushort)(osc.LEDs.Contains("B2") ? 64 : 0)
            + (ushort)(osc.LEDs.Contains("B3") ? 128 : 0)
            + (ushort)(osc.LEDs.Contains("B4") ? 256 : 0)
            + (ushort)(osc.LEDs.Contains("B5") ? 512 : 0)
        );
        var ledsBytes = BitConverter.GetBytes(ledsValue);
        Array.Reverse(ledsBytes);
        command.AddRange(ledsBytes);

        var freqStartBytes = BitConverter.GetBytes((ushort)(osc.FrequencyStart * 10d));
        Array.Reverse(freqStartBytes);
        command.AddRange(freqStartBytes);

        var freqEndBytes = BitConverter.GetBytes((ushort)(osc.FrequencyEnd * 10d));
        Array.Reverse(freqEndBytes);
        command.AddRange(freqEndBytes);

        command.Add((byte)(osc.DutyStart));
        command.Add((byte)(osc.DutyEnd));

        var brightStartBytes = BitConverter.GetBytes((ushort)(osc.BrightnessStart * 10d));
        Array.Reverse(brightStartBytes);
        command.AddRange(brightStartBytes);

        var brightEndBytes = BitConverter.GetBytes((ushort)(osc.BrightnessEnd * 10d));
        Array.Reverse(brightEndBytes);
        command.AddRange(brightEndBytes);

        return [.. command];
    }


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
