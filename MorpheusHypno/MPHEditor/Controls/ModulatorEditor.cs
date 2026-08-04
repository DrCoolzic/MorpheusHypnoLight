using System.Globalization;
using Microsoft.Maui.Controls.Shapes;
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

    /// <summary>
    /// Raised when any editable modulator property changes.
    /// </summary>
    public event EventHandler? ModulatorChanged;

    private void OnModulatorChanged()
    {
        ModulatorChanged?.Invoke(this, EventArgs.Empty);
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

    private const float LabelRowHeight = 18.0f;
    private const float RowSpacing = 0.0f;
    private const float ColumnSpacing = 6.0f;

    private readonly Picker _modePicker;
    private readonly Label _titleLabel;
    private readonly Grid _contentGrid;

    private bool _isRebuilding;

    public ModulatorEditor()
    {
        _modePicker = new Picker
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            TextColor = Colors.White,
            TitleColor = Colors.Gray,
            WidthRequest = 90,
        };
        _modePicker.Items.Add("Static");
        _modePicker.Items.Add("Linear");
        _modePicker.Items.Add("Lfo");
        _modePicker.SelectedIndexChanged += OnModeChanged;

        _titleLabel = new Label
        {
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Start,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
        };

        _contentGrid = new Grid
        {
            RowSpacing = RowSpacing,
            ColumnSpacing = ColumnSpacing,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto },
            },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
            },
        };

        // Fixed first column: modulator title, mode picker, (empty bottom row).
        _contentGrid.Add(_titleLabel, 0, 0);
        _contentGrid.Add(_modePicker, 0, 1);

        Content = new Border
        {
            Stroke = Colors.Gray,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Padding = 6,
            BackgroundColor = Colors.Transparent,
            Content = _contentGrid,
        };
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
        OnModulatorChanged();
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

        // Clear all columns except the fixed mode/title column.
        for (int i = _contentGrid.Children.Count - 1; i >= 0; i--)
        {
            IView child = _contentGrid.Children[i];
            if (_contentGrid.GetColumn(child) > 0)
            {
                _contentGrid.Children.RemoveAt(i);
            }
        }

        while (_contentGrid.ColumnDefinitions.Count > 1)
        {
            _contentGrid.ColumnDefinitions.RemoveAt(_contentGrid.ColumnDefinitions.Count - 1);
        }

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
            Modulator.Value.Value,
            (sender, e) =>
            {
                if (Modulator is not null)
                {
                    Modulator.Value = e.NewValue;
                    OnModulatorChanged();
                }
            });

        AddRotaryColumn("Value", valueButton);
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
            Modulator.Start.Value,
            (sender, e) =>
            {
                if (Modulator is not null)
                {
                    Modulator.Start = e.NewValue;
                    OnModulatorChanged();
                }
            });

        RotaryButton endButton = CreateValueButton(
            Modulator.End.Value,
            (sender, e) =>
            {
                if (Modulator is not null)
                {
                    Modulator.End = e.NewValue;
                    OnModulatorChanged();
                }
            });

        AddRotaryColumn("Start", startButton);
        AddRotaryColumn("End", endButton);
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

        bool isSquare = Modulator.LfoWaveform.Value == MPHCore.Models.LfoWaveform.Square;
        Switch waveformSwitch = new Switch
        {
            IsToggled = isSquare,
            OnColor = Colors.Blue,
            ThumbColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
        };
        Label waveformLabel = new Label
        {
            Text = isSquare ? "Square" : "Sine",
            TextColor = Colors.White,
            FontSize = 12,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        waveformSwitch.Toggled += (sender, e) =>
        {
            waveformLabel.Text = e.Value ? "Square" : "Sine";
            if (Modulator is not null)
            {
                Modulator.LfoWaveform = e.Value
                    ? MPHCore.Models.LfoWaveform.Square
                    : MPHCore.Models.LfoWaveform.Sine;
                OnModulatorChanged();
            }
        };

        AddSwitchColumn(waveformSwitch, waveformLabel);

        RotaryButton frequencyButton = CreateValueButton(
            Modulator.LfoFrequency.Value,
            (sender, e) =>
            {
                if (Modulator is not null)
                {
                    Modulator.LfoFrequency = e.NewValue;
                    OnModulatorChanged();
                }
            });
        frequencyButton.Minimum = 0.1;
        frequencyButton.Maximum = 10.0;
        frequencyButton.Increment = 0.1;
        frequencyButton.FineIncrement = 0.01;
        frequencyButton.CoarseIncrement = 1.0;

        RotaryButton lowButton = CreateValueButton(
            Modulator.Low.Value,
            (sender, e) =>
            {
                if (Modulator is not null)
                {
                    Modulator.Low = e.NewValue;
                    OnModulatorChanged();
                }
            });

        RotaryButton highButton = CreateValueButton(
            Modulator.High.Value,
            (sender, e) =>
            {
                if (Modulator is not null)
                {
                    Modulator.High = e.NewValue;
                    OnModulatorChanged();
                }
            });

        AddRotaryColumn("Freq", frequencyButton);
        AddRotaryColumn("Low", lowButton);
        AddRotaryColumn("High", highButton);
    }

    private void AddRotaryColumn(string title, RotaryButton button)
    {
        int column = _contentGrid.ColumnDefinitions.Count;
        _contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Label titleLabel = new Label
        {
            Text = title,
            FontSize = 12,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
        };

        Label valueLabel = new Label
        {
            Text = button.Value.ToString(ValueDisplayFormat, CultureInfo.InvariantCulture),
            FontSize = 12,
            HorizontalOptions = LayoutOptions.Center,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
        };

        button.ValueChanged += (sender, e) =>
        {
            valueLabel.Text = e.NewValue.ToString(ValueDisplayFormat, CultureInfo.InvariantCulture);
        };

        // Hide internal title/value; labels are managed externally.
        button.Title = string.Empty;
        button.DisplayFormat = string.Empty;

        _contentGrid.Add(titleLabel, column, 0);
        _contentGrid.Add(button, column, 1);
        _contentGrid.Add(valueLabel, column, 2);
    }

    private void AddSwitchColumn(Switch switchControl, Label switchLabel)
    {
        int column = _contentGrid.ColumnDefinitions.Count;
        _contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _contentGrid.Add(switchControl, column, 1);
        _contentGrid.Add(switchLabel, column, 2);
    }

    private RotaryButton CreateValueButton(double initialValue, EventHandler<ValueChangedEventArgs> handler)
    {
        RotaryButton button = new RotaryButton
        {
            Minimum = ValueMinimum,
            Maximum = ValueMaximum,
            Increment = ValueIncrement,
            FineIncrement = ValueIncrement / 10.0,
            CoarseIncrement = ValueIncrement * 10.0,
            Value = initialValue,
            WidthRequest = 50,
            HeightRequest = 50,
            Title = string.Empty,
            DisplayFormat = string.Empty,
        };
        button.ValueChanged += handler;
        return button;
    }
}
