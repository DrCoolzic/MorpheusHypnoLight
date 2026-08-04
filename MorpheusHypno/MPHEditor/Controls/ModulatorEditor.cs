using MPHCore.Models;

namespace MPHEditor.Controls;

/**
 * @brief Editor for a single MHL modulator (static, linear, or LFO).
 *
 * The editor exposes a mode selector and a dynamic set of rotary buttons
 * matching the selected mode. Numeric ranges are configurable because the
 * same control is reused for frequency, brightness, and duty cycle.
 */
public class ModulatorEditor : ContentView
{
    public static readonly BindableProperty ModulatorProperty = BindableProperty.Create(
        nameof(Modulator),
        typeof(Modulator),
        typeof(ModulatorEditor),
        null,
        BindingMode.TwoWay,
        propertyChanged: (bindable, _, __) => ((ModulatorEditor)bindable).RebuildEditor());

    public static readonly BindableProperty ValueMinimumProperty = BindableProperty.Create(
        nameof(ValueMinimum), typeof(double), typeof(ModulatorEditor), 0.0,
        propertyChanged: (bindable, _, __) => ((ModulatorEditor)bindable).RebuildEditor());

    public static readonly BindableProperty ValueMaximumProperty = BindableProperty.Create(
        nameof(ValueMaximum), typeof(double), typeof(ModulatorEditor), 100.0,
        propertyChanged: (bindable, _, __) => ((ModulatorEditor)bindable).RebuildEditor());

    public static readonly BindableProperty ValueIncrementProperty = BindableProperty.Create(
        nameof(ValueIncrement), typeof(double), typeof(ModulatorEditor), 1.0,
        propertyChanged: (bindable, _, __) => ((ModulatorEditor)bindable).RebuildEditor());

    public static readonly BindableProperty ValueDisplayFormatProperty = BindableProperty.Create(
        nameof(ValueDisplayFormat), typeof(string), typeof(ModulatorEditor), "F1",
        propertyChanged: (bindable, _, __) => ((ModulatorEditor)bindable).RebuildEditor());

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(ModulatorEditor), string.Empty,
        propertyChanged: (bindable, _, __) => ((ModulatorEditor)bindable).RebuildEditor());

    public Modulator? Modulator
    {
        get => (Modulator?)GetValue(ModulatorProperty);
        set => SetValue(ModulatorProperty, value);
    }

    public double ValueMinimum
    {
        get => (double)GetValue(ValueMinimumProperty);
        set => SetValue(ValueMinimumProperty, value);
    }

    public double ValueMaximum
    {
        get => (double)GetValue(ValueMaximumProperty);
        set => SetValue(ValueMaximumProperty, value);
    }

    public double ValueIncrement
    {
        get => (double)GetValue(ValueIncrementProperty);
        set => SetValue(ValueIncrementProperty, value);
    }

    public string ValueDisplayFormat
    {
        get => (string)GetValue(ValueDisplayFormatProperty);
        set => SetValue(ValueDisplayFormatProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    private readonly Label _titleLabel;
    private readonly Picker _modePicker;
    private readonly HorizontalStackLayout _contentLayout;

    private bool _isRebuilding;

    public ModulatorEditor()
    {
        _titleLabel = new Label
        {
            FontSize = 14,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Colors.White,
        };

        _modePicker = new Picker
        {
            Title = "Mode",
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Start,
            TextColor = Colors.White,
            TitleColor = Colors.Gray,
            WidthRequest = 80,
        };
        _modePicker.Items.Add("FIX");
        _modePicker.Items.Add("LIN");
        _modePicker.Items.Add("LFO");
        _modePicker.SelectedIndexChanged += OnModeChanged;

        _contentLayout = new HorizontalStackLayout
        {
            Spacing = 4,
        };

        Grid grid = new Grid
        {
            RowSpacing = 4,
            ColumnSpacing = 6,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
            },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
            },
        };

        grid.Add(_titleLabel, 0, 0);
        Grid.SetColumnSpan(_titleLabel, 2);

        grid.Add(_modePicker, 0, 1);
        grid.Add(_contentLayout, 1, 1);

        Content = grid;
        BackgroundColor = Colors.Transparent;
        RebuildEditor();
    }

    private void OnModeChanged(object? sender, EventArgs e)
    {
        if (_isRebuilding)
        {
            return;
        }

        ModulatorMode newMode = _modePicker.SelectedIndex switch
        {
            0 => ModulatorMode.Static,
            1 => ModulatorMode.Linear,
            2 => ModulatorMode.Lfo,
            _ => ModulatorMode.Static,
        };

        Modulator ??= new Modulator();

        if (Modulator.Mode == newMode)
        {
            return;
        }

        double currentValue = GetCurrentValue();
        Modulator.Mode = newMode;

        switch (newMode)
        {
            case ModulatorMode.Static:
                Modulator.Value = currentValue;
                Modulator.Start = null;
                Modulator.End = null;
                Modulator.LfoWaveform = null;
                Modulator.LfoFrequency = null;
                Modulator.Low = null;
                Modulator.High = null;
                break;

            case ModulatorMode.Linear:
                Modulator.Value = null;
                Modulator.Start = currentValue;
                Modulator.End = currentValue;
                Modulator.LfoWaveform = null;
                Modulator.LfoFrequency = null;
                Modulator.Low = null;
                Modulator.High = null;
                break;

            case ModulatorMode.Lfo:
                Modulator.Value = null;
                Modulator.Start = null;
                Modulator.End = null;
                Modulator.LfoWaveform = MPHCore.Models.LfoWaveform.Sine;
                Modulator.LfoFrequency = 1.0;
                Modulator.Low = ValueMinimum;
                Modulator.High = ValueMaximum;
                break;
        }

        RebuildEditor();
    }

    private double GetCurrentValue()
    {
        if (Modulator is null)
        {
            return ValueMinimum;
        }

        return Modulator.Mode switch
        {
            ModulatorMode.Static => Modulator.Value ?? ValueMinimum,
            ModulatorMode.Linear => Modulator.Start ?? ValueMinimum,
            ModulatorMode.Lfo => Modulator.Low ?? ValueMinimum,
            _ => ValueMinimum,
        };
    }

    private void RebuildEditor()
    {
        _isRebuilding = true;

        _titleLabel.Text = Title;

        Modulator ??= new Modulator { Mode = ModulatorMode.Static, Value = ValueMinimum };

        _modePicker.SelectedIndex = Modulator.Mode switch
        {
            ModulatorMode.Static => 0,
            ModulatorMode.Linear => 1,
            ModulatorMode.Lfo => 2,
            _ => 0,
        };

        _contentLayout.Children.Clear();

        switch (Modulator.Mode)
        {
            case ModulatorMode.Static:
                BuildStaticEditor();
                break;

            case ModulatorMode.Linear:
                BuildLinearEditor();
                break;

            case ModulatorMode.Lfo:
                BuildLfoEditor();
                break;
        }

        _isRebuilding = false;
    }

    private void BuildStaticEditor()
    {
        if (Modulator is null)
        {
            return;
        }

        Modulator.Value ??= ValueMinimum;

        RotaryButton valueButton = CreateValueButton(
            "Value",
            Modulator.Value.Value,
            (sender, e) =>
            {
                if (Modulator is not null)
                {
                    Modulator.Value = e.NewValue;
                }
            });

        _contentLayout.Children.Add(valueButton);
    }

    private void BuildLinearEditor()
    {
        if (Modulator is null)
        {
            return;
        }

        Modulator.Start ??= ValueMinimum;
        Modulator.End ??= ValueMaximum;

        RotaryButton startButton = CreateValueButton(
            "Start",
            Modulator.Start.Value,
            (sender, e) =>
            {
                if (Modulator is not null)
                {
                    Modulator.Start = e.NewValue;
                }
            });

        RotaryButton endButton = CreateValueButton(
            "End",
            Modulator.End.Value,
            (sender, e) =>
            {
                if (Modulator is not null)
                {
                    Modulator.End = e.NewValue;
                }
            });

        _contentLayout.Children.Add(startButton);
        _contentLayout.Children.Add(endButton);
    }

    private void BuildLfoEditor()
    {
        if (Modulator is null)
        {
            return;
        }

        Modulator.LfoWaveform ??= MPHCore.Models.LfoWaveform.Sine;
        Modulator.LfoFrequency ??= 1.0;
        Modulator.Low ??= ValueMinimum;
        Modulator.High ??= ValueMaximum;

        Picker waveformPicker = new Picker
        {
            Title = "Waveform",
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Colors.White,
            TitleColor = Colors.Gray,
        };
        waveformPicker.Items.Add("Sine");
        waveformPicker.Items.Add("Square");
        waveformPicker.SelectedIndex = Modulator.LfoWaveform.Value == MPHCore.Models.LfoWaveform.Sine ? 0 : 1;
        waveformPicker.SelectedIndexChanged += (sender, e) =>
        {
            if (Modulator is not null)
            {
                Modulator.LfoWaveform = waveformPicker.SelectedIndex == 0
                    ? MPHCore.Models.LfoWaveform.Sine
                    : MPHCore.Models.LfoWaveform.Square;
            }
        };

        RotaryButton frequencyButton = new RotaryButton
        {
            Title = "Freq",
            Minimum = 0.1,
            Maximum = 10.0,
            Increment = 0.1,
            FineIncrement = 0.01,
            CoarseIncrement = 1.0,
            DisplayFormat = "F1",
            Value = Modulator.LfoFrequency.Value,
            WidthRequest = 70,
            HeightRequest = 100,
        };
        frequencyButton.ValueChanged += (sender, e) =>
        {
            if (Modulator is not null)
            {
                Modulator.LfoFrequency = e.NewValue;
            }
        };

        RotaryButton lowButton = CreateValueButton(
            "Low",
            Modulator.Low.Value,
            (sender, e) =>
            {
                if (Modulator is not null)
                {
                    Modulator.Low = e.NewValue;
                }
            });

        RotaryButton highButton = CreateValueButton(
            "High",
            Modulator.High.Value,
            (sender, e) =>
            {
                if (Modulator is not null)
                {
                    Modulator.High = e.NewValue;
                }
            });

        _contentLayout.Children.Add(waveformPicker);
        _contentLayout.Children.Add(frequencyButton);
        _contentLayout.Children.Add(lowButton);
        _contentLayout.Children.Add(highButton);
    }

    private RotaryButton CreateValueButton(string title, double initialValue, EventHandler<ValueChangedEventArgs> handler)
    {
        RotaryButton button = new RotaryButton
        {
            Title = title,
            Minimum = ValueMinimum,
            Maximum = ValueMaximum,
            Increment = ValueIncrement,
            FineIncrement = ValueIncrement / 10.0,
            CoarseIncrement = ValueIncrement * 10.0,
            DisplayFormat = ValueDisplayFormat,
            Value = initialValue,
            WidthRequest = 70,
            HeightRequest = 100,
        };
        button.ValueChanged += handler;
        return button;
    }
}
