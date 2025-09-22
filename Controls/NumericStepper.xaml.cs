using System;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;

namespace ShuffleTask.Controls;

public partial class NumericStepper : ContentView
{
    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(
        nameof(Minimum),
        typeof(double?),
        typeof(NumericStepper),
        null,
        propertyChanged: OnRangePropertyChanged);

    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum),
        typeof(double?),
        typeof(NumericStepper),
        null,
        propertyChanged: OnRangePropertyChanged);

    public static readonly BindableProperty IncrementProperty = BindableProperty.Create(
        nameof(Increment),
        typeof(double),
        typeof(NumericStepper),
        1d,
        propertyChanged: OnIncrementChanged);

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value),
        typeof(double),
        typeof(NumericStepper),
        0d,
        BindingMode.TwoWay,
        propertyChanged: OnValuePropertyChanged,
        coerceValue: (bindable, value) => ((NumericStepper)bindable).CoerceValue((double)value));

    public static readonly BindableProperty ValueStringFormatProperty = BindableProperty.Create(
        nameof(ValueStringFormat),
        typeof(string),
        typeof(NumericStepper),
        "{0:0}",
        propertyChanged: OnValueStringFormatChanged);

    private string displayValue = "0";

    public NumericStepper()
    {
        DecreaseCommand = new RelayCommand(OnDecrease, () => CanDecrease);
        IncreaseCommand = new RelayCommand(OnIncrease, () => CanIncrease);

        InitializeComponent();

        RefreshState();
    }

    public double? Minimum
    {
        get => (double?)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double? Maximum
    {
        get => (double?)GetValue(MaximumProperty);
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

    public IRelayCommand DecreaseCommand { get; }

    public IRelayCommand IncreaseCommand { get; }

    public string DisplayValue
    {
        get => displayValue;
        private set
        {
            if (displayValue == value)
            {
                return;
            }

            displayValue = value;
            OnPropertyChanged(nameof(DisplayValue));
        }
    }

    public bool CanDecrease
    {
        get
        {
            var (minimum, _) = GetOrderedRange();
            return !minimum.HasValue || Value > minimum.Value;
        }
    }

    public bool CanIncrease
    {
        get
        {
            var (_, maximum) = GetOrderedRange();
            return !maximum.HasValue || Value < maximum.Value;
        }
    }

    private static void OnRangePropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is NumericStepper stepper)
        {
            stepper.Value = stepper.CoerceValue(stepper.Value);
            stepper.RefreshState();
        }
    }

    private static void OnIncrementChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is NumericStepper stepper)
        {
            stepper.Value = stepper.CoerceValue(stepper.Value);
            stepper.RefreshState();
        }
    }

    private static void OnValuePropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is NumericStepper stepper)
        {
            stepper.RefreshState();
        }
    }

    private static void OnValueStringFormatChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is NumericStepper stepper)
        {
            stepper.UpdateDisplayValue();
        }
    }

    private void RefreshState()
    {
        UpdateDisplayValue();
        OnPropertyChanged(nameof(CanDecrease));
        OnPropertyChanged(nameof(CanIncrease));

        DecreaseCommand.NotifyCanExecuteChanged();
        IncreaseCommand.NotifyCanExecuteChanged();
    }

    private void UpdateDisplayValue()
    {
        var format = string.IsNullOrWhiteSpace(ValueStringFormat) ? "{0:0}" : ValueStringFormat;

        try
        {
            DisplayValue = string.Format(format, Value);
        }
        catch (FormatException)
        {
            DisplayValue = Value.ToString();
        }
    }

    private void OnDecrease()
    {
        Value = CoerceValue(Value - GetIncrement());
    }

    private void OnIncrease()
    {
        Value = CoerceValue(Value + GetIncrement());
    }

    private double CoerceValue(double value)
    {
        var (minimum, maximum) = GetOrderedRange();
        var increment = GetIncrement();

        if (minimum.HasValue)
        {
            value = minimum.Value + Math.Round((value - minimum.Value) / increment, MidpointRounding.AwayFromZero) * increment;
        }
        else
        {
            value = Math.Round(value / increment, MidpointRounding.AwayFromZero) * increment;
        }

        if (minimum.HasValue && value < minimum.Value)
        {
            value = minimum.Value;
        }

        if (maximum.HasValue && value > maximum.Value)
        {
            value = maximum.Value;
        }

        return value;
    }

    private (double? Minimum, double? Maximum) GetOrderedRange()
    {
        var minimum = Minimum;
        var maximum = Maximum;

        if (minimum.HasValue && maximum.HasValue && minimum.Value > maximum.Value)
        {
            return (maximum, minimum);
        }

        return (minimum, maximum);
    }

    private double GetIncrement()
    {
        var increment = Increment;
        return increment > 0 ? increment : 1d;
    }
}
