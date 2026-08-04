using System.Windows.Input;
using MPHCore.Models;

namespace MPHEditor.Controls;

/**
 * @brief Editor for one MHL step.
 *
 * Displays the five oscillator editors stacked vertically. The step duration is
 * not edited here; it is managed by the sequence timeline view.
 */
public class StepEditor : ContentView
{
    public static readonly BindableProperty StepProperty = BindableProperty.Create(
        nameof(Step),
        typeof(Step),
        typeof(StepEditor),
        null,
        BindingMode.TwoWay,
        coerceValue: (bindable, value) => value ?? new Step(),
        propertyChanged: (bindable, _, __) => ((StepEditor)bindable).RebuildEditor());

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(StepEditor), string.Empty,
        propertyChanged: (bindable, _, __) => ((StepEditor)bindable).UpdateTitle());

    public static readonly BindableProperty StepChangedCommandProperty = BindableProperty.Create(
        nameof(StepChangedCommand), typeof(ICommand), typeof(StepEditor), null);

    public Step? Step
    {
        get => (Step?)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public ICommand? StepChangedCommand
    {
        get => (ICommand?)GetValue(StepChangedCommandProperty);
        set => SetValue(StepChangedCommandProperty, value);
    }

    /// <summary>
    /// Raises the step changed command when any oscillator changes.
    /// </summary>
    private void OnStepChanged()
    {
        if (StepChangedCommand is not null && StepChangedCommand.CanExecute(Step))
        {
            StepChangedCommand.Execute(Step);
        }
    }

    private readonly Label _titleLabel;
    private readonly VerticalStackLayout _oscillatorsLayout;
    private bool _isRebuilding;

    public StepEditor()
    {
        _titleLabel = new Label
        {
            FontSize = 18,
            HorizontalOptions = LayoutOptions.Start,
            TextColor = Colors.White,
        };

        _oscillatorsLayout = new VerticalStackLayout
        {
            Spacing = 12,
        };

        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                Children =
                {
                    _titleLabel,
                    _oscillatorsLayout,
                },
            },
        };

        BackgroundColor = Colors.Transparent;
        RebuildEditor();
    }

    private void UpdateTitle()
    {
        _titleLabel.Text = Title;
    }

    private void RebuildEditor()
    {
        if (_isRebuilding)
        {
            return;
        }

        _isRebuilding = true;
        try
        {
            UpdateTitle();

            Step ??= new Step();

            while (Step.Oscillators.Count < 5)
            {
                Step.Oscillators.Add(new Oscillator());
            }

            _oscillatorsLayout.Children.Clear();

            for (int i = 0; i < 5; i++)
            {
                OscillatorEditor editor = new OscillatorEditor
                {
                    Oscillator = Step.Oscillators[i],
                    Title = $"OSCILLATOR {i + 1}",
                };
                editor.OscillatorChanged += (_, _) => OnStepChanged();
                _oscillatorsLayout.Children.Add(editor);
            }
        }
        finally
        {
            _isRebuilding = false;
        }
    }
}
