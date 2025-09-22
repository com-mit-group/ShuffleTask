using System;
using Microsoft.Maui.Controls;

namespace ShuffleTask.Controls;

public partial class NumericStepper : ContentView
{
    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(
        nameof(Minimum),
        typeof(double),
        typeof(NumericStepper),
        0d,
        propertyChanged: OnRangePropertyChanged);

    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum),
        typeof(double),
        typeof(NumericStepper),
        double.MaxValue,
        propertyChanged: OnRangePropertyChanged);

    public static readonly BindableProperty IncrementProperty = BindableProperty.Create(
        nameof(Increment),
        typeof(double),
        typeof(NumericStepper),
        1d,
        propertyChanged: OnIncrementPropertyChanged);

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value),
        typeof(double),
        typeof(NumericStepper),
        0d,
        BindingMode.TwoWay,
        propertyChanged: OnValuePropertyChanged,
        coerceValue: CoerceValue);

    public static readonly BindableProperty ValueStringFormatProperty = BindableProperty.Create(
        nameof(ValueStringFormat),
        typeof(string),
        typeof(NumericStepper),
        "{0:0}",
        propertyChanged: OnValueStringFormatChanged);

    public NumericStepper()
    {
        InitializeComponent();
        UpdateValueLabel();
        UpdateButtonStates();
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

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string ValueStringFormat
    {
        get => (string)GetValue(ValueStringFormatProperty);
        set => SetValue(ValueStringFormatProperty, value);
    }

    private static void OnRangePropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is NumericStepper stepper)
        {
            stepper.Value = (double)CoerceValue(stepper, stepper.Value);
            stepper.UpdateButtonStates();
        }
    }

    private static void OnIncrementPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is NumericStepper stepper)
        {
            var coerced = (double)CoerceValue(stepper, stepper.Value);
            if (!coerced.Equals(stepper.Value))
            {
                stepper.Value = coerced;
            }
            else
            {
                stepper.UpdateButtonStates();
            }
        }
    }

    private static void OnValuePropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is NumericStepper stepper)
        {
            stepper.UpdateValueLabel();
            stepper.UpdateButtonStates();
        }
    }

    private static void OnValueStringFormatChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is NumericStepper stepper)
        {
            stepper.UpdateValueLabel();
        }
    }

    private static object CoerceValue(BindableObject bindable, object value)
    {
        if (bindable is not NumericStepper stepper)
        {
            return value;
        }

        double numericValue = (double)value;
        double increment = stepper.Increment;
        double minimum = stepper.Minimum;
        double maximum = stepper.Maximum;

        if (increment <= 0)
        {
            increment = 1d;
        }

        numericValue = Math.Max(minimum, Math.Min(maximum, numericValue));

        double steps = Math.Round((numericValue - minimum) / increment, MidpointRounding.AwayFromZero);
        double coercedValue = minimum + steps * increment;

        coercedValue = Math.Max(minimum, Math.Min(maximum, coercedValue));

        return coercedValue;
    }

    private void UpdateValueLabel()
    {
        string format = string.IsNullOrWhiteSpace(ValueStringFormat) ? "{0:0}" : ValueStringFormat;
        try
        {
            ValueLabel.Text = string.Format(format, Value);
        }
        catch (FormatException)
        {
            ValueLabel.Text = Value.ToString();
        }
    }

    private void UpdateButtonStates()
    {
        DecreaseButton.IsEnabled = Value > Minimum + 0.000001;
        IncreaseButton.IsEnabled = Value < Maximum - 0.000001;
    }

    private void OnDecreaseClicked(object sender, EventArgs e)
    {
        double increment = Increment > 0 ? Increment : 1d;
        double newValue = Value - increment;
        Value = (double)CoerceValue(this, newValue);
    }

    private void OnIncreaseClicked(object sender, EventArgs e)
    {
        double increment = Increment > 0 ? Increment : 1d;
        double newValue = Value + increment;
        Value = (double)CoerceValue(this, newValue);
    }
}
