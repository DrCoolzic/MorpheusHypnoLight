using MPHEditor.Pages;
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

        // PlayerPage is navigated to programmatically with a sequence parameter,
        // it is not part of the flyout menu.
        Routing.RegisterRoute(nameof(PlayerPage), typeof(PlayerPage));
    }
}
