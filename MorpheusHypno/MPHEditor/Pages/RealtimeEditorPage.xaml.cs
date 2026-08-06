using Microsoft.Extensions.Logging;
using MPHEditor.Services;
using MPHEditor.ViewModels;

namespace MPHEditor.Pages;

/// <summary>
/// Page for the realtime step editor.
/// </summary>
public partial class RealtimeEditorPage : ContentPage
{
    private readonly IBleService _bleService;
    private readonly ILogger<RealtimeEditorPage> _logger;

    public RealtimeEditorPage(RealtimeEditorViewModel viewModel, IBleService bleService, ILogger<RealtimeEditorPage> logger)
    {
        InitializeComponent();
        _bleService = bleService;
        _logger = logger;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _logger.LogInformation("RealtimeEditorPage OnAppearing: Setting editor mode");
        try
        {
            await _bleService.SetModeAsync(BleMode.Editor);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set editor mode");
        }
    }
}
