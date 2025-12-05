using System.Globalization;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Presentation.Converters;

public class WeekdaysConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Weekdays wd)
        {
            if (wd == Weekdays.None) return "None";
            IEnumerable<string> names = Enum.GetValues<Weekdays>()
                .Where(f => f != Weekdays.None && wd.HasFlag(f))
                .Select(f => f.ToString());
            return string.Join("|", names);
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? text = value?.ToString()?.Trim();
        if (string.IsNullOrEmpty(text) || string.Equals(text, "None", StringComparison.OrdinalIgnoreCase))
            return Weekdays.None;

        Weekdays result = Weekdays.None;
        foreach (string part in text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Enum.TryParse<Weekdays>(part, ignoreCase: true, out var flag))
            {
                result |= flag;
            }
        }
        return result;
    }
}
