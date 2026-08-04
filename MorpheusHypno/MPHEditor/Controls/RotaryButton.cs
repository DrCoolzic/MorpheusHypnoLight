using System.Globalization;
using System.Linq;

namespace MPHEditor.Controls;

/**
 * @brief A circular rotary knob control for numeric values.
 *
 * Drag up/right to increase, down/left to decrease. Tap the knob to enter a
 * value directly. A title and the current value are displayed outside the knob.
 */
public class RotaryButton : GraphicsView
{
    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value),
        typeof(double),
        typeof(RotaryButton),
        0.0,
        BindingMode.TwoWay,
        propertyChanged: (bindable, oldValue, newValue) =>
        {
            ((RotaryButton)bindable).OnValueChanged((double)oldValue, (double)newValue);
        });

    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(
        nameof(Minimum), typeof(double), typeof(RotaryButton), 0.0);

    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum), typeof(double), typeof(RotaryButton), 100.0);

    public static readonly BindableProperty IncrementProperty = BindableProperty.Create(
        nameof(Increment), typeof(double), typeof(RotaryButton), 1.0);

    public static readonly BindableProperty CoarseIncrementProperty = BindableProperty.Create(
        nameof(CoarseIncrement), typeof(double), typeof(RotaryButton), 10.0);

    public static readonly BindableProperty FineIncrementProperty = BindableProperty.Create(
        nameof(FineIncrement), typeof(double), typeof(RotaryButton), 0.1);

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(RotaryButton), string.Empty,
        propertyChanged: (bindable, _, __) => ((RotaryButton)bindable).Invalidate());

    public static readonly BindableProperty DisplayFormatProperty = BindableProperty.Create(
        nameof(DisplayFormat), typeof(string), typeof(RotaryButton), "F1",
        propertyChanged: (bindable, _, __) => ((RotaryButton)bindable).Invalidate());

    public static readonly BindableProperty TrackColorProperty = BindableProperty.Create(
        nameof(TrackColor), typeof(Color), typeof(RotaryButton), Colors.Gray,
        propertyChanged: (bindable, _, __) => ((RotaryButton)bindable).Invalidate());

    public static readonly BindableProperty ProgressColorProperty = BindableProperty.Create(
        nameof(ProgressColor), typeof(Color), typeof(RotaryButton), Colors.DodgerBlue,
        propertyChanged: (bindable, _, __) => ((RotaryButton)bindable).Invalidate());

    public static readonly BindableProperty IndicatorColorProperty = BindableProperty.Create(
        nameof(IndicatorColor), typeof(Color), typeof(RotaryButton), Colors.White,
        propertyChanged: (bindable, _, __) => ((RotaryButton)bindable).Invalidate());

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(RotaryButton), Colors.White,
        propertyChanged: (bindable, _, __) => ((RotaryButton)bindable).Invalidate());

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, Clamp(value, Minimum, Maximum));
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Increment
    {
        get => (double)GetValue(IncrementProperty);
        set => SetValue(IncrementProperty, value);
    }

    public double CoarseIncrement
    {
        get => (double)GetValue(CoarseIncrementProperty);
        set => SetValue(CoarseIncrementProperty, value);
    }

    public double FineIncrement
    {
        get => (double)GetValue(FineIncrementProperty);
        set => SetValue(FineIncrementProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string DisplayFormat
    {
        get => (string)GetValue(DisplayFormatProperty);
        set => SetValue(DisplayFormatProperty, value);
    }

    public Color TrackColor
    {
        get => (Color)GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    public Color ProgressColor
    {
        get => (Color)GetValue(ProgressColorProperty);
        set => SetValue(ProgressColorProperty, value);
    }

    public Color IndicatorColor
    {
        get => (Color)GetValue(IndicatorColorProperty);
        set => SetValue(IndicatorColorProperty, value);
    }

    public Color TextColor
    {
        get => (Color)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public event EventHandler<ValueChangedEventArgs>? ValueChanged;

    private double _dragStartValue;
    private bool _isDragging;
    private const float DragSensitivity = 0.25f;

    public RotaryButton()
    {
        Drawable = new RotaryButtonDrawable(this);
        BackgroundColor = Colors.Transparent;

        PanGestureRecognizer pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        GestureRecognizers.Add(pan);

        TapGestureRecognizer tap = new TapGestureRecognizer();
        tap.Tapped += OnTappedAsync;
        GestureRecognizers.Add(tap);
    }

    private void OnValueChanged(double oldValue, double newValue)
    {
        ValueChanged?.Invoke(this, new ValueChangedEventArgs(oldValue, newValue));
        Invalidate();
    }

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _isDragging = true;
                _dragStartValue = Value;
                break;

            case GestureStatus.Running:
                if (!_isDragging)
                {
                    return;
                }

                // Up/right increases the value, down/left decreases it.
                float delta = (float)(e.TotalX - e.TotalY);
                double change = delta * DragSensitivity * Increment;
                double newValue = _dragStartValue + change;
                Value = Clamp(newValue, Minimum, Maximum);
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _isDragging = false;
                break;
        }
    }

    private async void OnTappedAsync(object? sender, TappedEventArgs e)
    {
        Page? page = Application.Current?.Windows[0].Page;
        if (page is null)
        {
            return;
        }

        string? result = await page.DisplayPromptAsync(
            Title,
            "Enter value",
            initialValue: string.Empty,
            maxLength: 10,
            keyboard: Keyboard.Numeric);

        if (result is not null && double.TryParse(result, NumberStyles.Float, CultureInfo.InvariantCulture, out double newValue))
        {
            Value = Clamp(newValue, Minimum, Maximum);
        }
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }
        if (value > max)
        {
            return max;
        }
        return value;
    }

    private class RotaryButtonDrawable : IDrawable
    {
        private readonly RotaryButton _button;

        public RotaryButtonDrawable(RotaryButton button)
        {
            _button = button;
        }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            float width = dirtyRect.Width;
            float height = dirtyRect.Height;
            float labelHeight = 18.0f;
            float knobSize = Math.Min(width, height - (labelHeight * 2.0f));
            float centerX = width / 2.0f;
            float centerY = labelHeight + (knobSize / 2.0f);
            float radius = (knobSize / 2.0f) - 8.0f;
            float strokeWidth = Math.Max(4.0f, radius * 0.12f);

            canvas.FontColor = _button.TextColor;

            // Title at the top.
            if (!string.IsNullOrEmpty(_button.Title))
            {
                canvas.FontSize = 12.0f;
                canvas.DrawString(
                    _button.Title,
                    0.0f,
                    0.0f,
                    width,
                    labelHeight,
                    HorizontalAlignment.Center,
                    VerticalAlignment.Center);
            }

            // Background track (full 360-degree circle, drawn in two 180-degree
            // arcs because DrawArc treats identical start/end angles as zero-length).
            canvas.StrokeColor = _button.TrackColor;
            canvas.StrokeSize = strokeWidth;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.DrawArc(
                centerX - radius,
                centerY - radius,
                radius * 2.0f,
                radius * 2.0f,
                -90.0f,
                90.0f,
                true,
                false);
            canvas.DrawArc(
                centerX - radius,
                centerY - radius,
                radius * 2.0f,
                radius * 2.0f,
                90.0f,
                270.0f,
                true,
                false);

            // Progress arc.
            double t = (_button.Value - _button.Minimum) / (_button.Maximum - _button.Minimum);
            t = Clamp(t, 0.0, 1.0);

            if (t > 0.0)
            {
                canvas.StrokeColor = _button.ProgressColor;

                // Draw the clockwise arc from the bottom (90 deg) to the
                // current value angle. Using startAngle > endAngle makes
                // MAUI/Win2D take the short arc in the desired direction.
                float startAngle = 90.0f;
                float endAngle = startAngle - (float)(t * 360.0);
                canvas.DrawArc(
                    centerX - radius,
                    centerY - radius,
                    radius * 2.0f,
                    radius * 2.0f,
                    startAngle,
                    endAngle,
                    true,
                    false);
            }

            // Indicator line from the center to the current value angle.
            float indicatorAngle = -90.0f + (float)(t * 360.0);
            double angleRadians = DegreesToRadians(indicatorAngle);
            float indicatorRadius = radius * 0.65f;
            float endX = centerX + (indicatorRadius * (float)Math.Cos(angleRadians));
            float endY = centerY + (indicatorRadius * (float)Math.Sin(angleRadians));

            canvas.StrokeColor = _button.IndicatorColor;
            canvas.StrokeSize = strokeWidth * 0.8f;
            canvas.DrawLine(centerX, centerY, endX, endY);

            // Value label at the bottom.
            string valueText = _button.Value.ToString(_button.DisplayFormat, CultureInfo.InvariantCulture);
            canvas.FontSize = 12.0f;
            canvas.DrawString(
                valueText,
                0.0f,
                centerY + radius + 4.0f,
                width,
                labelHeight,
                HorizontalAlignment.Center,
                VerticalAlignment.Center);
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }
    }
}
