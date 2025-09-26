using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace ShuffleTask.Converters;

public class ComplementPercentageConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double numeric = ToDouble(value, culture);
        if (double.IsNaN(numeric))
        {
            return 0.0;
        }

        double complement = 100.0 - Math.Clamp(numeric, 0.0, 100.0);
        return complement;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static double ToDouble(object? value, CultureInfo culture)
    {
        switch (value)
        {
            case null:
                return double.NaN;
            case double d when !double.IsNaN(d) && !double.IsInfinity(d):
                return d;
            case float f when !float.IsNaN(f) && !float.IsInfinity(f):
                return f;
            case IConvertible convertible:
                try
                {
                    return convertible.ToDouble(culture);
                }
                catch
                {
                    return double.NaN;
                }
            default:
                return double.NaN;
        }
    }
}
