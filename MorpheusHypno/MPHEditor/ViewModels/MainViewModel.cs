using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MPHCore.Models;
using MPHCore.Services;
using MPHEditor.Services;
using MPHEditor.Utilities;

namespace MPHEditor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly IBleService _bleService;
    private readonly IMPHElementService _mes;

    private Sequence? _currentSequence;

    public MainViewModel(
        ILogger<MainViewModel> logger,
        IBleService bleService,
        IMPHElementService mpHElementService)
    {
        _logger = logger;
        _bleService = bleService;
        _mes = mpHElementService;

        // Subscribe to ble status changes
        _bleService.StatusChanged += (sender, status) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                BleStatusMessage = status;
            });
        };

        // Subscribe to connection changes
        _bleService.ConnectingChanged += (sender, isConnecting) => IsConnecting = isConnecting;
        _bleService.ConnectedChanged += (sender, isConnected) => IsConnected = isConnected;

        _bleService.CommandStatusReceived += (sender, args) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                // Update observable properties based on command status
                CommandOpcode = $"0x{args.Opcode:X2}";
                CommandResultCode = $"0x{args.ResultCode:X2}";
            });
        };

        _logger.LogInformation("Initializing MainViewModel");
        _ = InitializeAsync();

    }
    
    [ObservableProperty]
    public partial string CommandOpcode { get; set; } = "0x??";
    
    [ObservableProperty]
    public partial string CommandResultCode { get; set; } = "0x??";

    [ObservableProperty]
    public partial bool IsConnecting { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BleIcon))]
    public partial bool IsConnected { get; set; }
    public object BleIcon => IsConnected ? "dm_on.png" : "dm_off.png";

    [ObservableProperty]
    public partial string BleStatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CurrentSequence { get; set; } = "None";


    private async Task InitializeAsync()
    {
        _logger.LogInformation("Starting MainViewModel initialization...");

        // Always check and connect BLE (asynchronous, non-blocking)
        bool bluetoothEnabled = await _bleService.CheckBluetoothStatusAsync();
        if (!bluetoothEnabled)
        {
            _logger.LogWarning("Bluetooth is not enabled or available");
            // await ShowBluetoothDisabledPopupAsync();

            // Start auto-connect attempts even when Bluetooth is initially disabled
            _logger.LogInformation("Starting auto-connect timer to retry when Bluetooth is enabled");
            _ = _bleService.ConnectAsync();
        }
        else
        {
            _logger.LogInformation("Starting Bluetooth connection");
            _ = _bleService.ConnectAsync();
        }

        // load database
        _mes.MPHRoot.RootPath = AppDirectories.GetAppDataDirectory();
        await _mes.LoadLocalDb();
        _logger.LogInformation("MPEditor database loaded");


        var sequenceItem = _mes.MPHRoot.Collections
            .FirstOrDefault()
            ?.SequenceItems
            .FirstOrDefault();

        if (sequenceItem is null)
        {
            _logger.LogWarning("No sequence found to load");
            return;
        }

        var sequenceDir = sequenceItem.DirPath;
        _logger.LogInformation("Sequence directory: {SequenceDir}", sequenceDir);
        _currentSequence = await _mes.LoadSequenceAsync(sequenceDir);
        CurrentSequence = _currentSequence?.Name ?? "None";
    }

    [RelayCommand]
    private async Task Connect()
    {
        _logger.LogInformation("Connecting to device...");
        try
        {
            await _bleService.ConnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection failed");
        }
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        _logger.LogInformation("Disconnecting from device...");
        try
        {
            await _bleService.ForceDisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection failed");
        }
    }

    [RelayCommand]
    private async Task PlayAsync()
    {
        _logger.LogInformation("Sending PLAY command");
        await _bleService.PlayAsync();
    }

    [RelayCommand]
    private async Task PauseAsync()
    {
        _logger.LogInformation("Sending PAUSE command");
        await _bleService.PauseAsync();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        _logger.LogInformation("Sending STOP command");
        await _bleService.StopAsync();
    }
    
    [RelayCommand]
    private async Task LoadSequenceAsync()
    {
        _logger.LogInformation("Loading sequence");
        await _bleService.LoadSequenceAsync(_currentSequence!);
    }
}
