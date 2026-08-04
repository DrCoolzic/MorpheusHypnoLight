using MPHEditor.ViewModels;

namespace MPHEditor.Pages;

/// <summary>
/// Page for the realtime step editor.
/// </summary>
public partial class RealtimeEditorPage : ContentPage
{
    public RealtimeEditorPage(RealtimeEditorViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
