using MPHEditor.Services;

namespace MPHEditor;

public partial class App : Application
{
    private readonly IEditorModeService _editorModeService;

    public App(IEditorModeService editorModeService)
    {
        InitializeComponent();
        _editorModeService = editorModeService;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var appShell = new AppShell(_editorModeService);
        var window = new Window(appShell);

        window.SizeChanged += (_, _) => _editorModeService.UpdateFromWindowWidth(window.Width);
        _editorModeService.UpdateFromWindowWidth(window.Width);

        return window;
    }
}
