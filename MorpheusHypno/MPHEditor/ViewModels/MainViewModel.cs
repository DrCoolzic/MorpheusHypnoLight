using System.Collections.ObjectModel;
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
    //private readonly ISequencePlayerService _sequencePlayerService;
    private readonly IMPHElementService _mes;
    private readonly IEditorModeService _editorModeService;

    //private Sequence? _currentSequence;
    //private MPHSequence? _currentMphSequence;
    private bool _connectCommand = false;

    public MainViewModel(
        ILogger<MainViewModel> logger,
        IBleService bleService,
        ISequencePlayerService sequencePlayerService,
        IMPHElementService mpHElementService,
        IEditorModeService editorModeService)
    {
        _logger = logger;
        _bleService = bleService;
        //_sequencePlayerService = sequencePlayerService;
        _mes = mpHElementService;
        _editorModeService = editorModeService;

        IsEditorMode = _editorModeService.IsEditorMode;
        _editorModeService.PropertyChanged += (_, _) =>
        {
            MainThread.BeginInvokeOnMainThread(() => IsEditorMode = _editorModeService.IsEditorMode);
        };

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



        //// Subscribe to sequence player events
        //_sequencePlayerService.PlayerStateChanged += (sender, state) =>
        //{
        //    MainThread.BeginInvokeOnMainThread(() => PlayerState = state);
        //};

        //_sequencePlayerService.PositionChanged += (sender, position) =>
        //{
        //    MainThread.BeginInvokeOnMainThread(() => CurrentPosition = position);
        //};

        _logger.LogInformation("Initializing MainViewModel");
        _ = InitializeAsync();

    }



    [ObservableProperty]
    public partial bool IsConnecting { get; set; } = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BleIcon))]
    public partial bool IsConnected { get; set; }
    public object BleIcon => IsConnected ? "ble_on.png" : "ble_off.png";

    [ObservableProperty]
    public partial string BleStatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsEditorMode { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<MPHCollection> Collections { get; set; } = [];

    [ObservableProperty]
    public partial MPHCollection? SelectedCollection { get; set; }

    [ObservableProperty]
    public partial ObservableCollection<MPHSequence> Sequences { get; set; } = [];

    [ObservableProperty]
    public partial MPHSequence? SelectedSequence { get; set; }

    private MPHSequence? _expandedSequence;

    partial void OnSelectedCollectionChanged(MPHCollection? value)
    {
        Sequences = value is not null
            ? new ObservableCollection<MPHSequence>(value.SequenceItems)
            : [];
        SelectedSequence = null;
    }

    partial void OnSelectedSequenceChanged(MPHSequence? value)
    {
        if (_expandedSequence is not null)
            _expandedSequence.IsExpanded = false;

        _expandedSequence = value;

        if (value is not null)
            value.IsExpanded = true;
    }

    //[ObservableProperty]
    //public partial string CurrentSequence { get; set; } = "None";

    //[ObservableProperty]
    //public partial PlayerStateEnum PlayerState { get; set; } = PlayerStateEnum.STOPPED;

    //[ObservableProperty]
    //[NotifyPropertyChangedFor(nameof(CurrentPositionText))]
    //public partial double CurrentPosition { get; set; } = 0.0;

    //public string CurrentPositionText => $"{CurrentPosition:F2} s";

    //[ObservableProperty]
    //public partial double SeekPosition { get; set; } = 0.0;

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

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Collections = new ObservableCollection<MPHCollection>(_mes.MPHRoot.Collections);
            SelectedCollection = Collections.FirstOrDefault();
        });


        //_currentMphSequence = _mes.MPHRoot.Collections
        //    .FirstOrDefault()
        //    ?.SequenceItems
        //    .FirstOrDefault();

        //if (_currentMphSequence is null)
        //{
        //    _logger.LogWarning("No sequence found to load");
        //    return;
        //}

        //var sequenceDir = _currentMphSequence.DirPath;
        //_logger.LogInformation("Sequence directory: {SequenceDir}", sequenceDir);
        //_currentSequence = await _mes.LoadSequenceAsync(sequenceDir);
        //_currentMphSequence.Sequence = _currentSequence;
        //CurrentSequence = _currentSequence?.Name ?? "None";
    }

    //[RelayCommand]
    //private async Task Connect()
    //{
    //    _logger.LogInformation("Connecting to device...");
    //    try
    //    {
    //        await _bleService.ConnectAsync();
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex, "Connection failed");
    //    }
    //}

    //[RelayCommand]
    //private async Task Disconnect()
    //{
    //    _logger.LogInformation("Disconnecting from device...");
    //    try
    //    {
    //        await _bleService.ForceDisconnectAsync();
    //    }
    //    catch (Exception ex)
    //    {
    //        _logger.LogError(ex, "Connection failed");
    //    }
    //}



    [RelayCommand]
    private async Task ToggleBle()
    {
        _logger.LogInformation("Toggle BLE");
        if (_connectCommand)
        {
            await _bleService.ConnectAsync();
            _connectCommand = false;
        }
        else
        {
            await _bleService.ForceDisconnectAsync();
            _connectCommand = true;
        }
    }

    [RelayCommand]
    private void AddProgram()
    {
        // TODO: implement adding a new collection.
    }

    [RelayCommand]
    private void DeleteProgram()
    {
        // TODO: implement deleting the selected collection.
    }

    [RelayCommand]
    private void AddSequence()
    {
        // TODO: implement adding a new sequence to the selected collection.
    }

    [RelayCommand]
    private void DeleteSequence()
    {
        // TODO: implement deleting the selected sequence.
    }

    [RelayCommand]
    private void Playlist(MPHSequence sequence)
    {
        // TODO: implement adding the sequence to a playlist.
    }

    [RelayCommand]
    private void GoToEdit(MPHSequence sequence)
    {
        // TODO: implement navigation to the sequence editor.
    }

    [RelayCommand]
    private void GoToPlay(MPHSequence sequence)
    {
        // TODO: implement navigation to the sequence player.
    }
}
