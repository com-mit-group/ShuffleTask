using CommunityToolkit.Mvvm.ComponentModel;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.Presentation.Utilities;

public abstract class WeekdaySelectionHelper : ObservableObject
{
    private Weekdays _selectedWeekdays;

    public Weekdays SelectedWeekdays
    {
        get => _selectedWeekdays;
        set
        {
            if (SetProperty(ref _selectedWeekdays, value))
            {
                RaiseWeekdayPropertiesChanged();
            }
        }
    }

    protected static Weekdays ApplyWeekdaySelection(Weekdays current, Weekdays day, bool enabled)
    {
        return enabled ? current | day : current & ~day;
    }

    protected bool GetWeekday(Weekdays day) => _selectedWeekdays.HasFlag(day);

    protected void SetWeekday(Weekdays day, bool isSelected)
    {
        SelectedWeekdays = ApplyWeekdaySelection(SelectedWeekdays, day, isSelected);
    }

    protected void RaiseWeekdayPropertiesChanged()
    {
        OnPropertyChanged(nameof(Sunday));
        OnPropertyChanged(nameof(Monday));
        OnPropertyChanged(nameof(Tuesday));
        OnPropertyChanged(nameof(Wednesday));
        OnPropertyChanged(nameof(Thursday));
        OnPropertyChanged(nameof(Friday));
        OnPropertyChanged(nameof(Saturday));
    }

    public bool Sunday
    {
        get => GetWeekday(Weekdays.Sun);
        set => SetWeekday(Weekdays.Sun, value);
    }

    public bool Monday
    {
        get => GetWeekday(Weekdays.Mon);
        set => SetWeekday(Weekdays.Mon, value);
    }

    public bool Tuesday
    {
        get => GetWeekday(Weekdays.Tue);
        set => SetWeekday(Weekdays.Tue, value);
    }

    public bool Wednesday
    {
        get => GetWeekday(Weekdays.Wed);
        set => SetWeekday(Weekdays.Wed, value);
    }

    public bool Thursday
    {
        get => GetWeekday(Weekdays.Thu);
        set => SetWeekday(Weekdays.Thu, value);
    }

    public bool Friday
    {
        get => GetWeekday(Weekdays.Fri);
        set => SetWeekday(Weekdays.Fri, value);
    }

    public bool Saturday
    {
        get => GetWeekday(Weekdays.Sat);
        set => SetWeekday(Weekdays.Sat, value);
    }
}
