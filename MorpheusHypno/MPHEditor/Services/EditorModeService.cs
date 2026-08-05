using System.ComponentModel;

namespace MPHEditor.Services;

/// <summary>
/// Tracks whether the app currently has enough screen width to show editor-only
/// features (navigation to editor pages, collection add/delete buttons, etc.).
/// The value is updated dynamically as the window is resized.
/// </summary>
public interface IEditorModeService : INotifyPropertyChanged
{
    /// <summary>
    /// Minimum window width, in device-independent units, required to enable editor features.
    /// </summary>
    double MinEditorWidth { get; }

    /// <summary>
    /// Whether editor-only features should currently be shown.
    /// </summary>
    bool IsEditorMode { get; }

    /// <summary>
    /// Recomputes <see cref="IsEditorMode"/> from the given window width and raises
    /// <see cref="INotifyPropertyChanged.PropertyChanged"/> if the value changed.
    /// </summary>
    /// <param name="windowWidth">Current window width, in device-independent units.</param>
    void UpdateFromWindowWidth(double windowWidth);
}

/// <inheritdoc cref="IEditorModeService"/>
public class EditorModeService : IEditorModeService
{
    public double MinEditorWidth => 600.0;

    private bool _isEditorMode;
    public bool IsEditorMode
    {
        get => _isEditorMode;
        private set
        {
            if (_isEditorMode != value)
            {
                _isEditorMode = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsEditorMode)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void UpdateFromWindowWidth(double windowWidth)
    {
        IsEditorMode = windowWidth >= MinEditorWidth;
    }
}
