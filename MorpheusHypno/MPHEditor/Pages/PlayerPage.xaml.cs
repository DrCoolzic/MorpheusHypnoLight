using MPEditor.ViewModel;
using MPMaui.Services; // Added for PlayerStateEnum
using Microsoft.Extensions.Logging;
using static MPEditor.ViewModel.PlayerViewModel;
// Using Plugin.Maui.Audio for audio playback

namespace MPEditor.View
{
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
            _logger.LogInformation("DetailPage created successfully");
        }


        //protected async override void OnAppearing()
        //{
        //    base.OnAppearing();
        //    _logger.LogInformation("{}", _playerViewModel.Sequence?.ToString());
        //    //await _playerViewModel.CheckDmConnectionAsync();
        //}

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
            _logger.LogInformation("DetailPage OnDisappearing: Stopping BLE playback and disposing resources");
            try 
            {
                // Make sure to wait for the StopDm task to complete fully
                await _playerViewModel.StopPlayerAsync();
                _logger.LogInformation("Stopped player before leaving page");
            }
            catch (Exception ex)
            {
                _logger.LogError("Error stopping BLE playback: {ex.Message}", ex.Message);
            }
            
            _sequencePlayerService.Dispose();
            base.OnDisappearing();
        }
    }
}
