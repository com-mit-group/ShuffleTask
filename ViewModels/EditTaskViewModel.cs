using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShuffleTask.Models;
using ShuffleTask.Services;

namespace ShuffleTask.ViewModels;

public partial class EditTaskViewModel : ObservableObject
{
    private readonly StorageService _storage;

    private TaskItem _workingCopy = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Sunday))]
    [NotifyPropertyChangedFor(nameof(Monday))]
    [NotifyPropertyChangedFor(nameof(Tuesday))]
    [NotifyPropertyChangedFor(nameof(Wednesday))]
    [NotifyPropertyChangedFor(nameof(Thursday))]
    [NotifyPropertyChangedFor(nameof(Friday))]
    [NotifyPropertyChangedFor(nameof(Saturday))]
    private Weekdays selectedWeekdays;

    [ObservableProperty]
    private string title = string.Empty;

    [ObservableProperty]
    private string description = string.Empty;

    [ObservableProperty]
    private double importance = 1;

    [ObservableProperty]
    private bool hasDeadline;

    [ObservableProperty]
    private DateTime deadlineDate = DateTime.Today;

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

    public EditTaskViewModel(StorageService storage)
    {
        _storage = storage;
    }

    public RepeatType[] RepeatOptions { get; } = Enum.GetValues<RepeatType>();

    public AllowedPeriod[] AllowedPeriodOptions { get; } = Enum.GetValues<AllowedPeriod>();

    public bool Sunday
    {
        get => SelectedWeekdays.HasFlag(Weekdays.Sun);
        set => UpdateWeekday(Weekdays.Sun, value);
    }

    public bool Monday
    {
        get => SelectedWeekdays.HasFlag(Weekdays.Mon);
        set => UpdateWeekday(Weekdays.Mon, value);
    }

    public bool Tuesday
    {
        get => SelectedWeekdays.HasFlag(Weekdays.Tue);
        set => UpdateWeekday(Weekdays.Tue, value);
    }

    public bool Wednesday
    {
        get => SelectedWeekdays.HasFlag(Weekdays.Wed);
        set => UpdateWeekday(Weekdays.Wed, value);
    }

    public bool Thursday
    {
        get => SelectedWeekdays.HasFlag(Weekdays.Thu);
        set => UpdateWeekday(Weekdays.Thu, value);
    }

    public bool Friday
    {
        get => SelectedWeekdays.HasFlag(Weekdays.Fri);
        set => UpdateWeekday(Weekdays.Fri, value);
    }

    public bool Saturday
    {
        get => SelectedWeekdays.HasFlag(Weekdays.Sat);
        set => UpdateWeekday(Weekdays.Sat, value);
    }

    public event EventHandler? Saved;

    public void Load(TaskItem? task)
    {
        IsBusy = false;
        _workingCopy = task != null ? Clone(task) : new TaskItem();
        IsNew = task == null || string.IsNullOrWhiteSpace(task.Id);

        Title = _workingCopy.Title;
        Description = _workingCopy.Description;
        Importance = Math.Max(1, _workingCopy.Importance);
        Repeat = _workingCopy.Repeat;
        IntervalDays = _workingCopy.IntervalDays > 0 ? _workingCopy.IntervalDays : 1;
        AllowedPeriod = _workingCopy.AllowedPeriod;
        IsPaused = _workingCopy.Paused;
        SelectedWeekdays = _workingCopy.Weekdays;

        if (_workingCopy.Deadline.HasValue)
        {
            HasDeadline = true;
            DeadlineDate = _workingCopy.Deadline.Value.Date;
            DeadlineTime = _workingCopy.Deadline.Value.TimeOfDay;
        }
        else
        {
            HasDeadline = false;
            DeadlineDate = DateTime.Today;
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
            _workingCopy.Repeat = Repeat;
            _workingCopy.Weekdays = Repeat == RepeatType.Weekly ? SelectedWeekdays : Weekdays.None;
            int intervalValue = (int)Math.Max(1, Math.Round(IntervalDays));
            _workingCopy.IntervalDays = Repeat == RepeatType.Interval ? intervalValue : 0;
            _workingCopy.AllowedPeriod = AllowedPeriod;
            _workingCopy.Paused = IsPaused;

            if (HasDeadline)
            {
                _workingCopy.Deadline = DeadlineDate.Date + DeadlineTime;
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

    public void ResetDeadline()
    {
        HasDeadline = false;
    }

    private void UpdateWeekday(Weekdays day, bool enabled)
    {
        Weekdays current = SelectedWeekdays;
        Weekdays updated = enabled ? current | day : current & ~day;
        if (updated != current)
        {
            SelectedWeekdays = updated;
        }
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

    private static TaskItem Clone(TaskItem task)
    {
        return new TaskItem
        {
            Id = task.Id,
            Title = task.Title,
            Description = task.Description,
            Importance = task.Importance,
            Deadline = task.Deadline,
            Repeat = task.Repeat,
            Weekdays = task.Weekdays,
            IntervalDays = task.IntervalDays,
            LastDoneAt = task.LastDoneAt,
            AllowedPeriod = task.AllowedPeriod,
            Paused = task.Paused,
            CreatedAt = task.CreatedAt
        };
    }
}
