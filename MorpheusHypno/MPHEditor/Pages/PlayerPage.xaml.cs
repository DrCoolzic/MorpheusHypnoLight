using Microsoft.Extensions.Logging;
using MPHEditor.Services;
using MPHEditor.ViewModels;

namespace MPHEditor.Pages;

public partial class PlayerPage : ContentPage
{
    private readonly ILogger<PlayerPage> _logger;
    private readonly PlayerViewModel _playerViewModel;
    private readonly ISequencePlayerService _sequencePlayerService;

    public PlayerPage(PlayerViewModel viewModel, ILogger<PlayerPage> logger, ISequencePlayerService sequencePlayerService)
    {
        InitializeComponent();
        _logger = logger;
        _playerViewModel = viewModel;
        _sequencePlayerService = sequencePlayerService;
        BindingContext = viewModel;
        _logger.LogInformation("PlayerPage created successfully");
    }

    private void Slider_DragStarted(object sender, EventArgs e)
    {
        if (_playerViewModel.PlayerState == PlayerStateEnum.PLAYING)
        {
            _playerViewModel.DraggingInPlayMode = true;
            _playerViewModel.PausePlayerCommand.Execute(null); // Pause during slider interaction
        }
    }

    private async void Slider_DragCompleted(object sender, EventArgs e)
    {
        var slider = (Slider)sender;

        // Update PlayerCurrentPosition based on slider position
        await _sequencePlayerService.SeekToPositionAsync(slider.Value);

        // If dragging was done in Play mode, resume playback
        if (_playerViewModel.DraggingInPlayMode)
        {
            _playerViewModel.StartPlayerCommand.Execute(null);
        }
        _playerViewModel.DraggingInPlayMode = false; // Reset dragging state
    }

    private void OnAudioToggled(object sender, ToggledEventArgs e)
    {
        if (e.Value)
        {
            _playerViewModel.UnmuteAudio();
        }
        else
        {
            _playerViewModel.MuteAudio();
        }
    }

    protected override async void OnDisappearing()
    {
        _logger.LogInformation("PlayerPage OnDisappearing: Stopping playback");
        try
        {
            // Make sure to wait for the stop task to complete fully
            await _playerViewModel.StopPlayerAsync();
            _logger.LogInformation("Stopped player before leaving page");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping playback");
        }

        // Note: ISequencePlayerService is a shared singleton (registered in MauiProgram.cs),
        // so it must NOT be disposed here - only the current page's session is torn down.
        base.OnDisappearing();
    }
}
