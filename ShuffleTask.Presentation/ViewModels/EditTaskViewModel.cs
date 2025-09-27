using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShuffleTask.Models;
using ShuffleTask.Services;

namespace ShuffleTask.ViewModels;

public partial class EditTaskViewModel : ObservableObject
{
    private readonly IStorageService _storage;
    private readonly TimeProvider _clock;

    private TaskItem _workingCopy = new();

    private Weekdays _selectedWeekdays;

    private const double MinSizePoints = 0.5;
    private const double MaxSizePoints = 13.0;
    private const double DefaultSizePoints = 3.0;

    public Weekdays SelectedWeekdays
    {
        get => _selectedWeekdays;
        set
        {
            if (SetProperty(ref _selectedWeekdays, value))
            {
                OnPropertyChanged(nameof(Sunday));
                OnPropertyChanged(nameof(Monday));
                OnPropertyChanged(nameof(Tuesday));
                OnPropertyChanged(nameof(Wednesday));
                OnPropertyChanged(nameof(Thursday));
                OnPropertyChanged(nameof(Friday));
                OnPropertyChanged(nameof(Saturday));
            }
        }
    }

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private double importance = 1;

    [ObservableProperty]
    private double sizePoints = DefaultSizePoints;

    [ObservableProperty]
    private bool hasDeadline;

    [ObservableProperty]
    private DateTime deadlineDate = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    [ObservableProperty]
    private TimeSpan deadlineTime = new(9, 0, 0);

    [ObservableProperty]
    private RepeatType repeat;

    [ObservableProperty]
    private double intervalDays = 1;

    [ObservableProperty]
    private AllowedPeriod allowedPeriod;

    [ObservableProperty]
    private bool isPaused;

    [ObservableProperty]
    private bool isBusy;

    private bool _isNew = true;
    public bool IsNew
    {
        get => _isNew;
        private set => SetProperty(ref _isNew, value);
    }

    public EditTaskViewModel(IStorageService storage, TimeProvider clock)
    {
        _storage = storage;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        DeadlineDate = GetTodayUtcDate();
    }

    public RepeatType[] RepeatOptions { get; } = Enum.GetValues<RepeatType>();

    public AllowedPeriod[] AllowedPeriodOptions { get; } = new[]
    {
        AllowedPeriod.Any,
        AllowedPeriod.Work,
        AllowedPeriod.OffWork,
        AllowedPeriod.Off
    };

    private static Weekdays ApplyWeekdaySelection(Weekdays current, Weekdays day, bool enabled)
    {
        return enabled ? current | day : current & ~day;
    }

    private bool GetWeekday(Weekdays day) => _selectedWeekdays.HasFlag(day);

    private void SetWeekday(Weekdays day, bool isSelected)
    {
        SelectedWeekdays = ApplyWeekdaySelection(SelectedWeekdays, day, isSelected);
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

    public event EventHandler? Saved;

    public void Load(TaskItem? task)
    {
        IsBusy = false;
        _workingCopy = task != null ? TaskItem.Clone(task) : new TaskItem();
        IsNew = task == null || string.IsNullOrWhiteSpace(task.Id);

        Title = _workingCopy.Title;
        Description = _workingCopy.Description;
        Importance = Math.Max(1, _workingCopy.Importance);
        SizePoints = SanitizeSizePoints(_workingCopy.SizePoints);
        Repeat = _workingCopy.Repeat;
        IntervalDays = _workingCopy.IntervalDays > 0 ? _workingCopy.IntervalDays : 1;
        AllowedPeriod = _workingCopy.AllowedPeriod;
        IsPaused = _workingCopy.Paused;
        SelectedWeekdays = _workingCopy.Weekdays;

        if (_workingCopy.Deadline.HasValue)
        {
            HasDeadline = true;
            DateTime deadlineUtc = EnsureUtc(_workingCopy.Deadline.Value);
            DeadlineDate = deadlineUtc.Date;
            DeadlineTime = deadlineUtc.TimeOfDay;
        }
        else
        {
            HasDeadline = false;
            DeadlineDate = GetTodayUtcDate();
            DeadlineTime = new TimeSpan(9, 0, 0);
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Title))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _storage.InitializeAsync();

            _workingCopy.Title = Title.Trim();
            _workingCopy.Description = Description?.Trim() ?? string.Empty;
            _workingCopy.Importance = (int)Math.Max(1, Math.Round(Importance));
            _workingCopy.SizePoints = SanitizeSizePoints(SizePoints);
            _workingCopy.Repeat = Repeat;
            _workingCopy.Weekdays = Repeat == RepeatType.Weekly ? SelectedWeekdays : Weekdays.None;
            int intervalValue = (int)Math.Max(1, Math.Round(IntervalDays));
            _workingCopy.IntervalDays = Repeat == RepeatType.Interval ? intervalValue : 0;
            _workingCopy.AllowedPeriod = AllowedPeriod;
            _workingCopy.Paused = IsPaused;

            if (HasDeadline)
            {
                DateTime combined = DeadlineDate.Date + DeadlineTime;
                _workingCopy.Deadline = EnsureUtc(combined);
            }
            else
            {
                _workingCopy.Deadline = null;
            }

            if (string.IsNullOrWhiteSpace(_workingCopy.Id))
            {
                _workingCopy.Id = Guid.NewGuid().ToString("n");
            }

            if (IsNew)
            {
                await _storage.AddTaskAsync(_workingCopy);
            }
            else
            {
                await _storage.UpdateTaskAsync(_workingCopy);
            }

            Saved?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static double SanitizeSizePoints(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
        {
            return DefaultSizePoints;
        }

        if (value < MinSizePoints)
        {
            return MinSizePoints;
        }

        if (value > MaxSizePoints)
        {
            return MaxSizePoints;
        }

        // Stepper may return values like 2.4999999997, round to 0.5 increments
        double rounded = Math.Round(value * 2.0, MidpointRounding.AwayFromZero) / 2.0;
        return Math.Max(MinSizePoints, Math.Min(MaxSizePoints, rounded));
    }

    private DateTime GetTodayUtcDate()
    {
        DateTime utcNow = _clock.GetUtcNow().UtcDateTime;
        return utcNow.Date;
    }

    private static DateTime EnsureUtc(DateTime value)
    {
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    partial void OnRepeatChanged(RepeatType value)
    {
        if (value != RepeatType.Weekly && SelectedWeekdays != Weekdays.None)
        {
            SelectedWeekdays = Weekdays.None;
        }

        if (value != RepeatType.Interval)
        {
            IntervalDays = 1;
        }
    }

}
