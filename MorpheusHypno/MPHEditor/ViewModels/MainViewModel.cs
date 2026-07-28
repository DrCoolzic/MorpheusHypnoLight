using System.ComponentModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using MPHEditor.Services;

namespace MPHEditor.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ILogger<MainViewModel> _logger;
    private readonly IBleService _bleService;
    private string _status = "Ready";

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
        }
    }

    public ICommand ScanCommand { get; }

    public MainViewModel(ILogger<MainViewModel> logger, IBleService bleService)
    {
        _logger = logger;
        _bleService = bleService;
        ScanCommand = new Command(async () => await ScanAsync());
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;
}
