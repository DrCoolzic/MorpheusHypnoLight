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
            FontSize = 16,
            HorizontalOptions = LayoutOptions.Start,
            TextColor = Colors.White,
        };

        _waveformPicker = new Picker
        {
            Title = "Wave",
            TextColor = Colors.White,
            TitleColor = Colors.Gray,
            WidthRequest = 90,
        };
        _waveformPicker.Items.Add("Sine");
        _waveformPicker.Items.Add("Square");
        _waveformPicker.Items.Add("Triangle");
        _waveformPicker.Items.Add("Custom");
        _waveformPicker.SelectedIndexChanged += OnWaveformChanged;

        _phaseButton = new RotaryButton
        {
            Title = "Phase",
            Minimum = 0.0,
            Maximum = 360.0,
            Increment = 1.0,
            FineIncrement = 0.1,
            CoarseIncrement = 10.0,
            DisplayFormat = "F0",
            WidthRequest = 80,
            HeightRequest = 120,
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
            Title = "Bright",
        };

        _dutyEditor = new ModulatorEditor
        {
            ValueMinimum = 0.0,
            ValueMaximum = 100.0,
            ValueIncrement = 1.0,
            ValueDisplayFormat = "F1",
            Title = "Duty",
        };

        HorizontalStackLayout modulatorRow = new HorizontalStackLayout
        {
            Spacing = 8,
            Children =
            {
                _waveformPicker,
                _phaseButton,
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

        VerticalStackLayout layout = new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                _titleLabel,
                _contentLayout,
            },
        };

        Content = new Border
        {
            Stroke = Colors.Gray,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Padding = 8,
            BackgroundColor = Colors.Transparent,
            Content = layout,
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
    }

    private void OnPhaseChanged(object? sender, ValueChangedEventArgs e)
    {
        if (Oscillator is not null)
        {
            Oscillator.PhaseDegrees = e.NewValue;
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
