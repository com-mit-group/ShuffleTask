using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShuffleTask.Models;
using ShuffleTask.Services;

namespace ShuffleTask.ViewModels;

public partial class EditTaskViewModel : ObservableObject
{
    private readonly StorageService _storage;

    public EditTaskViewModel(StorageService storage)
    {
        _storage = storage;
        Task = new TaskItem();
        if (Task.Importance < 1) Task.Importance = 1;

        // Initialize split date/time properties from Task.Deadline or defaults
        var d = Task.Deadline ?? DateTime.Now;
        _deadlineDate = d.Date;
        _deadlineTime = d.TimeOfDay;
        _hasDeadline = Task.Deadline.HasValue;
    }

    [ObservableProperty]
    private TaskItem task;

    partial void OnTaskChanged(TaskItem value)
    {
        // Sync split fields when Task instance is replaced (new or editing existing)
        var d = value?.Deadline ?? DateTime.Now;
        _deadlineDate = d.Date;
        _deadlineTime = d.TimeOfDay;
        OnPropertyChanged(nameof(DeadlineDate));
        OnPropertyChanged(nameof(DeadlineTime));
        HasDeadline = value?.Deadline != null;
        if (Task.Importance < 1) Task.Importance = 1;
        // Refresh weekday flags bindings
        OnPropertyChanged(nameof(Sun));
        OnPropertyChanged(nameof(Mon));
        OnPropertyChanged(nameof(Tue));
        OnPropertyChanged(nameof(Wed));
        OnPropertyChanged(nameof(Thu));
        OnPropertyChanged(nameof(Fri));
        OnPropertyChanged(nameof(Sat));
    }

    // Toggle to control whether a deadline is set
    private bool _hasDeadline;
    public bool HasDeadline
    {
        get => _hasDeadline;
        set
        {
            if (_hasDeadline != value)
            {
                _hasDeadline = value;
                if (!_hasDeadline)
                {
                    Task.Deadline = null;
                }
                else
                {
                    Task.Deadline = DeadlineDate.Date + DeadlineTime;
                }
                OnPropertyChanged();
                OnPropertyChanged(nameof(Task));
            }
        }
    }

    // Backing fields for Deadline split bindings
    private DateTime _deadlineDate;
    public DateTime DeadlineDate
    {
        get => _deadlineDate;
        set
        {
            if (SetProperty(ref _deadlineDate, value))
            {
                UpdateTaskDeadline();
            }
        }
    }

    private TimeSpan _deadlineTime;
    public TimeSpan DeadlineTime
    {
        get => _deadlineTime;
        set
        {
            if (SetProperty(ref _deadlineTime, value))
            {
                UpdateTaskDeadline();
            }
        }
    }

    private void UpdateTaskDeadline()
    {
        // Combine date and time into a single DateTime when enabled; otherwise keep null
        if (HasDeadline)
            Task.Deadline = DeadlineDate.Date + DeadlineTime;
        else
            Task.Deadline = null;
        OnPropertyChanged(nameof(Task));
    }

    public RepeatType[] RepeatTypes { get; } = Enum.GetValues<RepeatType>();
    public AllowedPeriod[] AllowedPeriods { get; } = Enum.GetValues<AllowedPeriod>();

    // Weekday flag helpers for checkbox bindings
    private bool HasFlag(Weekdays f) => (Task.Weekdays & f) == f;
    private void SetFlag(Weekdays f, bool on)
    {
        if (on)
            Task.Weekdays |= f;
        else
            Task.Weekdays &= ~f;
        OnPropertyChanged(nameof(Task));
    }

    public bool Sun { get => HasFlag(Weekdays.Sun); set { if (value != Sun) { SetFlag(Weekdays.Sun, value); OnPropertyChanged(); } } }
    public bool Mon { get => HasFlag(Weekdays.Mon); set { if (value != Mon) { SetFlag(Weekdays.Mon, value); OnPropertyChanged(); } } }
    public bool Tue { get => HasFlag(Weekdays.Tue); set { if (value != Tue) { SetFlag(Weekdays.Tue, value); OnPropertyChanged(); } } }
    public bool Wed { get => HasFlag(Weekdays.Wed); set { if (value != Wed) { SetFlag(Weekdays.Wed, value); OnPropertyChanged(); } } }
    public bool Thu { get => HasFlag(Weekdays.Thu); set { if (value != Thu) { SetFlag(Weekdays.Thu, value); OnPropertyChanged(); } } }
    public bool Fri { get => HasFlag(Weekdays.Fri); set { if (value != Fri) { SetFlag(Weekdays.Fri, value); OnPropertyChanged(); } } }
    public bool Sat { get => HasFlag(Weekdays.Sat); set { if (value != Sat) { SetFlag(Weekdays.Sat, value); OnPropertyChanged(); } } }

    public event EventHandler? Saved;

    [RelayCommand]
    public async Task SaveAsync()
    {
        await _storage.InitializeAsync();
        // Basic validation
        if (string.IsNullOrWhiteSpace(Task.Title))
            return;
        if (Task.Importance < 1) Task.Importance = 1;

        // Ensure Task.Deadline reflects latest split fields and toggle
        UpdateTaskDeadline();

        var existing = await _storage.GetTaskAsync(Task.Id);
        if (existing == null)
            await _storage.AddTaskAsync(Task);
        else
            await _storage.UpdateTaskAsync(Task);

        Saved?.Invoke(this, EventArgs.Empty);
    }
}
