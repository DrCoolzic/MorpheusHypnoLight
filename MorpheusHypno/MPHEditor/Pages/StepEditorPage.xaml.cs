using MPHEditor.ViewModels;

namespace MPHEditor.Pages;

/**
 * @brief Test page that hosts the StepEditor control.
 */
public partial class StepEditorPage : ContentPage
{
    public StepEditorPage()
    {
        InitializeComponent();
        BindingContext = new StepEditorViewModel();
    }
}
