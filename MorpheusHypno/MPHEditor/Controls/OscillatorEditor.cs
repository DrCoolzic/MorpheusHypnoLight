using Microsoft.Maui.Controls.Shapes;
using MPHCore.Models;

namespace MPHEditor.Controls;

/**
 * @brief Editor for one MHL oscillator.
 *
 * Displays the waveform selector, phase knob, and three modulator editors
 * for frequency, brightness, and duty cycle.
 */
public class OscillatorEditor : ContentView
{
    public static readonly BindableProperty OscillatorProperty = BindableProperty.Create(
        nameof(Oscillator),
        typeof(Oscillator),
        typeof(OscillatorEditor),
        null,
        BindingMode.TwoWay,
        propertyChanged: (bindable, _, __) => ((OscillatorEditor)bindable).RebuildEditor());

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(OscillatorEditor), string.Empty,
        propertyChanged: (bindable, _, __) => ((OscillatorEditor)bindable).RebuildEditor());

    public Oscillator? Oscillator
    {
        get => (Oscillator?)GetValue(OscillatorProperty);
        set => SetValue(OscillatorProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// Raised when any editable oscillator property changes.
    /// </summary>
    public event EventHandler? OscillatorChanged;

    private void OnOscillatorChanged()
    {
        OscillatorChanged?.Invoke(this, EventArgs.Empty);
    }

    private readonly Label _titleLabel;
    private readonly ScrollView _contentLayout;
    private readonly Picker _waveformPicker;
    private readonly RotaryButton _phaseButton;
    private readonly ModulatorEditor _frequencyEditor;
    private readonly ModulatorEditor _brightnessEditor;
    private readonly ModulatorEditor _dutyEditor;

    private bool _isRebuilding;

    public OscillatorEditor()
    {
        _titleLabel = new Label
        {
            FontSize = 12,
            HorizontalOptions = LayoutOptions.Start,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
        };

        _waveformPicker = new Picker
        {
            Title = "Wave",
            TextColor = Colors.White,
            TitleColor = Colors.Gray,
            WidthRequest = 110,
            HeightRequest = 60,
        };
        _waveformPicker.Items.Add("Sine");
        _waveformPicker.Items.Add("Square");
        _waveformPicker.Items.Add("Triangle");
        _waveformPicker.Items.Add("Custom");
        _waveformPicker.SelectedIndexChanged += OnWaveformChanged;

        _phaseButton = new RotaryButton
        {
            Minimum = 0.0,
            Maximum = 360.0,
            Increment = 1.0,
            FineIncrement = 0.1,
            CoarseIncrement = 10.0,
            Value = 0.0,
            WidthRequest = 50,
            HeightRequest = 50,
            DisplayFormat = string.Empty,
        };
        _phaseButton.ValueChanged += OnPhaseChanged;

        _frequencyEditor = new ModulatorEditor
        {
            ValueMinimum = 0.0,
            ValueMaximum = 100.0,
            ValueIncrement = 1.0,
            ValueDisplayFormat = "F1",
            Title = "Freq",
        };

        _brightnessEditor = new ModulatorEditor
        {
            ValueMinimum = 0.0,
            ValueMaximum = 100.0,
            ValueIncrement = 1.0,
            ValueDisplayFormat = "F1",
            ValueScale = 100.0,
            Title = "Bright",
        };

        _dutyEditor = new ModulatorEditor
        {
            ValueMinimum = 0.0,
            ValueMaximum = 100.0,
            ValueIncrement = 1.0,
            ValueDisplayFormat = "F1",
            ValueScale = 100.0,
            Title = "Duty",
        };

        _frequencyEditor.ModulatorChanged += (_, _) => OnOscillatorChanged();
        _brightnessEditor.ModulatorChanged += (_, _) => OnOscillatorChanged();
        _dutyEditor.ModulatorChanged += (_, _) => OnOscillatorChanged();

        HorizontalStackLayout modulatorRow = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                _frequencyEditor,
                _brightnessEditor,
                _dutyEditor,
            },
        };

        _contentLayout = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            Content = modulatorRow,
        };

        // Left column with title, wave picker, phase knob and phase value.
        Grid leftColumn = new Grid
        {
            RowSpacing = 1,
            ColumnSpacing = 8,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
            },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        Label phaseHeader = new Label
        {
            Text = "Phase",
            FontSize = 12,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
        };

        Label phaseValue = new Label
        {
            Text = _phaseButton.Value.ToString("F0", System.Globalization.CultureInfo.InvariantCulture),
            FontSize = 12,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
        };
        _phaseButton.ValueChanged += (sender, e) =>
        {
            phaseValue.Text = e.NewValue.ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        };

        leftColumn.Add(_titleLabel, 0, 0);
        leftColumn.Add(_waveformPicker, 0, 1);
        leftColumn.Add(phaseHeader, 1, 0);
        leftColumn.Add(_phaseButton, 1, 1);
        leftColumn.Add(phaseValue, 1, 2);

        Grid grid = new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
            },
        };
        grid.Add(leftColumn, 0, 0);
        grid.Add(_contentLayout, 1, 0);

        Content = new Border
        {
            Stroke = Colors.Gray,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = 8,
            BackgroundColor = Colors.Transparent,
            Content = grid,
        };

        RebuildEditor();
    }

    private void OnWaveformChanged(object? sender, EventArgs e)
    {
        if (_isRebuilding || Oscillator is null)
        {
            return;
        }

        Oscillator.Waveform = _waveformPicker.SelectedIndex switch
        {
            0 => OscillatorWaveform.Sine,
            1 => OscillatorWaveform.Square,
            2 => OscillatorWaveform.Triangle,
            3 => OscillatorWaveform.Custom,
            _ => OscillatorWaveform.Square,
        };

        OnOscillatorChanged();
    }

    private void OnPhaseChanged(object? sender, ValueChangedEventArgs e)
    {
        if (Oscillator is not null)
        {
            Oscillator.PhaseDegrees = e.NewValue;
            OnOscillatorChanged();
        }
    }

    private void RebuildEditor()
    {
        _isRebuilding = true;

        _titleLabel.Text = Title;

        Oscillator ??= new Oscillator();

        _waveformPicker.SelectedIndex = Oscillator.Waveform switch
        {
            OscillatorWaveform.Sine => 0,
            OscillatorWaveform.Square => 1,
            OscillatorWaveform.Triangle => 2,
            OscillatorWaveform.Custom => 3,
            _ => 1,
        };

        _phaseButton.Value = Oscillator.PhaseDegrees;

        _frequencyEditor.Modulator = Oscillator.Frequency;
        _brightnessEditor.Modulator = Oscillator.Brightness;
        _dutyEditor.Modulator = Oscillator.Duty;

        _isRebuilding = false;
    }
}
