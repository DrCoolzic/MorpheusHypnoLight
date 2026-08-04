using MPHEditor.ViewModels;

namespace MPHEditor.Pages;

/**
 * @brief Test page that hosts the StepEditor control.
 */
public partial class RealtimeEditorPage : ContentPage
{
    public RealtimeEditorPage()
    {
        InitializeComponent();
        BindingContext = new RealtimeEditorViewModel();
    }
}
