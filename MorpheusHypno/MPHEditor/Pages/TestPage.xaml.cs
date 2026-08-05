using MPHEditor.ViewModels;

namespace MPHEditor.Pages;

public partial class TestPage : ContentPage
{
    public TestPage(TestViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
