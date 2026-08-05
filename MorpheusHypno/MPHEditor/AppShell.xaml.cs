using MPHEditor.Services;

namespace MPHEditor;

public partial class AppShell : Shell
{
    public AppShell(IEditorModeService editorModeService)
    {
        InitializeComponent();

        RealtimeEditorFlyoutItem.IsVisible = editorModeService.IsEditorMode;
        editorModeService.PropertyChanged += (_, _) =>
        {
            RealtimeEditorFlyoutItem.IsVisible = editorModeService.IsEditorMode;
        };
    }
}
