using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MPHCore.Services;
using MPHEditor.Services;
using MPHEditor.Utilities;

namespace MPHEditor.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly IBleService _bleService;
    private readonly IMPHElementService _mes;

    [ObservableProperty]
    private string _status = "Ready";

    public MainViewModel(ILogger<MainViewModel> logger, IBleService bleService, IMPHElementService mpHElementService)
    {
        _logger = logger;
        _bleService = bleService;
        _mes = mpHElementService;

        _logger.LogInformation("Initializing MainViewModel");
        _ = InitializeAsync();

    }

    private async Task InitializeAsync()
    {
        _logger.LogInformation("Starting MainViewModel initialization...");
        _mes.MPHRoot.RootPath = AppDirectories.GetAppDataDirectory();
        await _mes.LoadLocalDb();
        _logger.LogInformation("Collections database loaded");  

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
        var sequence = await _mes.LoadSequenceAsync(sequenceDir);
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        _logger.LogInformation("Checking Bluetooth status...");
        try
        {
            var enabled = await _bleService.CheckBluetoothStatusAsync();
            Status = enabled ? "Bluetooth enabled" : "Bluetooth disabled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bluetooth status check failed");
            Status = "Bluetooth status check failed";
        }
    }
}
