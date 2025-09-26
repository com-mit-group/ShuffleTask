using System;
using System.Buffers;
using System.Globalization;
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

        if (ContainsFormatPlaceholder(format))
        {
            DisplayValue = FormatWithPlaceholder(format);
        }
        else if (!ContainsUnescapedBraces(format) && TryFormatValue(format, out var formattedValue))
        {
            DisplayValue = formattedValue;
        }
        else
        {
            DisplayValue = Value.ToString(CultureInfo.CurrentCulture);
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
        var incrementDecimal = ToDecimal(increment);
        if (incrementDecimal <= 0m)
        {
            incrementDecimal = 0.0000000000000000000000000001m;
        }
        var valueDecimal = ToDecimal(value);

        decimal coerced;

        if (minimum.HasValue)
        {
            var minimumDecimal = ToDecimal(minimum.Value);
            var steps = decimal.Round((valueDecimal - minimumDecimal) / incrementDecimal, 0, MidpointRounding.AwayFromZero);
            coerced = minimumDecimal + steps * incrementDecimal;
        }
        else
        {
            var steps = decimal.Round(valueDecimal / incrementDecimal, 0, MidpointRounding.AwayFromZero);
            coerced = steps * incrementDecimal;
        }

        var coercedValue = (double)coerced;

        if (minimum.HasValue && coercedValue < minimum.Value)
        {
            coercedValue = minimum.Value;
        }

        if (maximum.HasValue && coercedValue > maximum.Value)
        {
            coercedValue = maximum.Value;
        }

        return coercedValue;
    }

    private static bool ContainsFormatPlaceholder(string format)
    {
        var skipNext = false;

        for (var index = 0; index < format.Length; index++)
        {
            if (skipNext)
            {
                skipNext = false;
                continue;
            }

            if (format[index] != '{')
            {
                continue;
            }

            if (index + 1 < format.Length && format[index + 1] == '{')
            {
                skipNext = true;
                continue;
            }

            var scanIndex = index + 1;

            while (scanIndex < format.Length && char.IsWhiteSpace(format[scanIndex]))
            {
                scanIndex++;
            }

            if (scanIndex < format.Length && format[scanIndex] == '0')
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsUnescapedBraces(string format)
    {
        var skipNext = false;

        for (var index = 0; index < format.Length; index++)
        {
            if (skipNext)
            {
                skipNext = false;
                continue;
            }

            var current = format[index];

            if (current != '{' && current != '}')
            {
                continue;
            }

            if (index + 1 < format.Length && format[index + 1] == current)
            {
                skipNext = true;
                continue;
            }

            return true;
        }

        return false;
    }

    private string FormatWithPlaceholder(string format)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, format, Value);
        }
        catch (FormatException)
        {
            return Value.ToString(CultureInfo.CurrentCulture);
        }
    }

    private bool TryFormatValue(string format, out string formatted)
    {
        Span<char> stackBuffer = stackalloc char[64];

        if (TryFormatCore(stackBuffer, format, out formatted))
        {
            return true;
        }

        var poolBuffer = ArrayPool<char>.Shared.Rent(Math.Max(128, format.Length * 4));
        try
        {
            return TryFormatCore(poolBuffer.AsSpan(), format, out formatted);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(poolBuffer);
        }
    }

    private bool TryFormatCore(Span<char> destination, string format, out string formatted)
    {
        try
        {
            if (Value.TryFormat(destination, out var charsWritten, format.AsSpan(), CultureInfo.CurrentCulture))
            {
                formatted = new string(destination[..charsWritten]);
                return true;
            }
        }
        catch (FormatException)
        {
            // Ignore invalid format strings and fall back to culture-based default formatting.
        }

        formatted = string.Empty;
        return false;
    }

    private static decimal ToDecimal(double value)
    {
        if (double.IsNaN(value))
        {
            return 0m;
        }

        if (value >= (double)decimal.MaxValue)
        {
            return decimal.MaxValue;
        }

        if (value <= (double)decimal.MinValue)
        {
            return decimal.MinValue;
        }

        return (decimal)value;
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
