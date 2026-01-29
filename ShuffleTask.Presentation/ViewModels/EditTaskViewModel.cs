using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Utilities;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Presentation.Utilities;
using System.Collections.ObjectModel;

namespace ShuffleTask.ViewModels;

public partial class EditTaskViewModel : ViewModelWithWeekdaySelection
{
    private readonly IStorageService _storage;
    private readonly TimeProvider _clock;

    private TaskItem _workingCopy = new();

    private Weekdays _selectedAdHocWeekdays;

    private static readonly Weekdays AllWeekdays = Weekdays.Sun | Weekdays.Mon | Weekdays.Tue | Weekdays.Wed | Weekdays.Thu | Weekdays.Fri | Weekdays.Sat;

    private const double MinSizePoints = 0.5;
    private const double MaxSizePoints = 13.0;
    private const double DefaultSizePoints = 3.0;

    public Weekdays SelectedAdHocWeekdays
    {
        get => _selectedAdHocWeekdays;
        set
        {
            if (SetProperty(ref _selectedAdHocWeekdays, value))
            {
                OnPropertyChanged(nameof(AdHocSunday));
                OnPropertyChanged(nameof(AdHocMonday));
                OnPropertyChanged(nameof(AdHocTuesday));
                OnPropertyChanged(nameof(AdHocWednesday));
                OnPropertyChanged(nameof(AdHocThursday));
                OnPropertyChanged(nameof(AdHocFriday));
                OnPropertyChanged(nameof(AdHocSaturday));
                OnPropertyChanged(nameof(SelectedPeriodDefinitionDescription));
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
    private bool autoShuffleAllowed = true;

    [ObservableProperty]
    private TimeSpan adHocStartTime = new(9, 0, 0);

    [ObservableProperty]
    private TimeSpan adHocEndTime = new(17, 0, 0);

    [ObservableProperty]
    private bool adHocIsAllDay;

    [ObservableProperty]
    private bool isPaused;

    [ObservableProperty]
    private CutInLineMode cutInLineMode;

    [ObservableProperty]
    private bool isBusy;

    // Per-task timer override _settings
    [ObservableProperty]
    private bool useCustomTimer;

    [ObservableProperty]
    private int customTimerMode; // 0=LongInterval, 1=Pomodoro

    [ObservableProperty]
    private double customReminderMinutes = 60;

    [ObservableProperty]
    private double customFocusMinutes = 15;

    [ObservableProperty]
    private double customBreakMinutes = 5;

    [ObservableProperty]
    private double customPomodoroCycles = 3;

    [ObservableProperty]
    private AlignmentModeOption selectedAlignmentMode;

    [ObservableProperty]
    private PeriodDefinitionOption? selectedPeriodDefinition;

    private bool _isNew = true;
    public bool IsNew
    {
        get => _isNew;
        private set => SetProperty(ref _isNew, value);
    }

    public EditTaskViewModel(IStorageService storage, TimeProvider clock, AppSettings appSettings)
    {
        _storage = storage;
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        AppSettings = appSettings;
        DeadlineDate = GetTodayUtcDate();
        AlignmentModeOptions = AlignmentModeCatalog.Defaults;
        SelectedAlignmentMode = AlignmentModeOptions[0];
    }

    public RepeatType[] RepeatOptions { get; } = Enum.GetValues<RepeatType>();

    public CutInLineMode[] CutInLineModeOptions { get; } = Enum.GetValues<CutInLineMode>();

    public ObservableCollection<PeriodDefinitionOption> PeriodDefinitionOptions { get; } = new();

    public IReadOnlyList<AlignmentModeOption> AlignmentModeOptions { get; }

    public string[] TimerModeOptions { get; } = new[] { "Long Interval", "Pomodoro" };

    public bool IsAdHocSelection => SelectedPeriodDefinition?.IsAdHoc ?? false;

    public bool CanEditSelectedDefinition => SelectedPeriodDefinition?.IsEditable ?? false;

    public string SelectedPeriodDefinitionDescription
    {
        get
        {
            if (SelectedPeriodDefinition == null)
            {
                return string.Empty;
            }

            if (SelectedPeriodDefinition.IsAdHoc)
            {
                return PeriodDefinitionFormatter.DescribeDefinition(BuildAdHocDefinition());
            }

            return SelectedPeriodDefinition.Description;
        }
    }

    private bool GetAdHocWeekday(Weekdays day) => _selectedAdHocWeekdays.HasFlag(day);

    private void SetAdHocWeekday(Weekdays day, bool isSelected)
    {
        SelectedAdHocWeekdays = ApplyWeekdaySelection(SelectedAdHocWeekdays, day, isSelected);
    }

    public bool AdHocSunday
    {
        get => GetAdHocWeekday(Weekdays.Sun);
        set => SetAdHocWeekday(Weekdays.Sun, value);
    }

    public bool AdHocMonday
    {
        get => GetAdHocWeekday(Weekdays.Mon);
        set => SetAdHocWeekday(Weekdays.Mon, value);
    }

    public bool AdHocTuesday
    {
        get => GetAdHocWeekday(Weekdays.Tue);
        set => SetAdHocWeekday(Weekdays.Tue, value);
    }

    public bool AdHocWednesday
    {
        get => GetAdHocWeekday(Weekdays.Wed);
        set => SetAdHocWeekday(Weekdays.Wed, value);
    }

    public bool AdHocThursday
    {
        get => GetAdHocWeekday(Weekdays.Thu);
        set => SetAdHocWeekday(Weekdays.Thu, value);
    }

    public bool AdHocFriday
    {
        get => GetAdHocWeekday(Weekdays.Fri);
        set => SetAdHocWeekday(Weekdays.Fri, value);
    }

    public bool AdHocSaturday
    {
        get => GetAdHocWeekday(Weekdays.Sat);
        set => SetAdHocWeekday(Weekdays.Sat, value);
    }
    public AppSettings AppSettings { get; }

    public event EventHandler? Saved;

    public async Task LoadAsync(TaskItem? task)
    {
        IsBusy = false;
        await _storage.InitializeAsync();
        await LoadPeriodDefinitionOptionsAsync();
        _workingCopy = task != null ? TaskItem.Clone(task) : new TaskItem();
        IsNew = task == null || string.IsNullOrWhiteSpace(task.Id);

        Title = _workingCopy.Title;
        Description = _workingCopy.Description;
        Importance = Math.Max(1, _workingCopy.Importance);
        SizePoints = SanitizeSizePoints(_workingCopy.SizePoints);
        Repeat = _workingCopy.Repeat;
        IntervalDays = _workingCopy.IntervalDays > 0 ? _workingCopy.IntervalDays : 1;
        AllowedPeriod = _workingCopy.AllowedPeriod;
        AutoShuffleAllowed = _workingCopy.AutoShuffleAllowed;
        AdHocStartTime = _workingCopy.AdHocStartTime ?? _workingCopy.CustomStartTime ?? new TimeSpan(9, 0, 0);
        AdHocEndTime = _workingCopy.AdHocEndTime ?? _workingCopy.CustomEndTime ?? new TimeSpan(17, 0, 0);
        bool isLegacyCustomAllDay = _workingCopy.AllowedPeriod == AllowedPeriod.Custom
            && string.IsNullOrWhiteSpace(_workingCopy.PeriodDefinitionId)
            && _workingCopy.AdHocMode == PeriodDefinitionMode.None
            && !_workingCopy.AdHocStartTime.HasValue
            && !_workingCopy.AdHocEndTime.HasValue;
        AdHocIsAllDay = _workingCopy.AdHocIsAllDay || isLegacyCustomAllDay;
        IsPaused = _workingCopy.Paused;
        CutInLineMode = _workingCopy.CutInLineMode;
        SelectedWeekdays = _workingCopy.Weekdays;
        SelectedAdHocWeekdays = _workingCopy.AdHocWeekdays ?? _workingCopy.CustomWeekdays ?? AllWeekdays;
        SelectedAlignmentMode = AlignmentModeOptions.FirstOrDefault(option => option.Mode == _workingCopy.AdHocMode)
            ?? AlignmentModeOptions[0];

        // Load custom timer _settings
        UseCustomTimer = _workingCopy.CustomTimerMode.HasValue;
        if (UseCustomTimer)
        {
            CustomTimerMode = _workingCopy.CustomTimerMode ?? 0;
            CustomReminderMinutes = _workingCopy.CustomReminderMinutes ?? 60;
            CustomFocusMinutes = _workingCopy.CustomFocusMinutes ?? 15;
            CustomBreakMinutes = _workingCopy.CustomBreakMinutes ?? 5;
            CustomPomodoroCycles = _workingCopy.CustomPomodoroCycles ?? 3;
        }
        else
        {
            CustomTimerMode = 0;
            CustomReminderMinutes = 60;
            CustomFocusMinutes = 15;
            CustomBreakMinutes = 5;
            CustomPomodoroCycles = 3;
        }

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

        SelectPeriodDefinitionOption(_workingCopy);
        OnPropertyChanged(nameof(SelectedPeriodDefinitionDescription));
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
            _workingCopy.AutoShuffleAllowed = AutoShuffleAllowed;
            _workingCopy.CustomStartTime = null;
            _workingCopy.CustomEndTime = null;
            _workingCopy.CustomWeekdays = null;
            ApplyPeriodDefinitionSelection(_workingCopy);
            _workingCopy.Paused = IsPaused;
            _workingCopy.CutInLineMode = CutInLineMode;

            // Save custom timer _settings
            if (UseCustomTimer)
            {
                _workingCopy.CustomTimerMode = CustomTimerMode;
                _workingCopy.CustomReminderMinutes = (int)Math.Max(1, Math.Round(CustomReminderMinutes));
                _workingCopy.CustomFocusMinutes = (int)Math.Max(1, Math.Round(CustomFocusMinutes));
                _workingCopy.CustomBreakMinutes = (int)Math.Max(1, Math.Round(CustomBreakMinutes));
                _workingCopy.CustomPomodoroCycles = (int)Math.Max(1, Math.Round(CustomPomodoroCycles));
            }
            else
            {
                _workingCopy.CustomTimerMode = null;
                _workingCopy.CustomReminderMinutes = null;
                _workingCopy.CustomFocusMinutes = null;
                _workingCopy.CustomBreakMinutes = null;
                _workingCopy.CustomPomodoroCycles = null;
            }

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

            if (!string.IsNullOrWhiteSpace(AppSettings.Network.UserId))
            {
                _workingCopy.UserId = AppSettings.Network.UserId;
                _workingCopy.DeviceId = string.Empty;
            }
            else
            {
                
                _workingCopy.UserId =  string.Empty;
                _workingCopy.DeviceId = AppSettings.Network.DeviceId;
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

    private async Task LoadPeriodDefinitionOptionsAsync()
    {
        var definitions = await _storage.GetPeriodDefinitionsAsync();
        var options = new List<PeriodDefinitionOption>
        {
            CreateOption(PeriodDefinitionCatalog.Any, isCoreBuiltIn: true),
            CreateOption(PeriodDefinitionCatalog.Work, isCoreBuiltIn: true),
            CreateOption(PeriodDefinitionCatalog.OffWork, isCoreBuiltIn: true)
        };

        foreach (PeriodDefinition definition in definitions)
        {
            if (string.Equals(definition.Id, PeriodDefinitionCatalog.AnyId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(definition.Id, PeriodDefinitionCatalog.WorkId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(definition.Id, PeriodDefinitionCatalog.OffWorkId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            options.Add(CreateOption(definition, isCoreBuiltIn: false));
        }

        options.Add(new PeriodDefinitionOption(
            "ad-hoc",
            "Ad-hoc custom",
            "Define a one-off window just for this task.",
            definition: null,
            isAdHoc: true,
            isCoreBuiltIn: false));

        PeriodDefinitionOptions.Clear();
        foreach (PeriodDefinitionOption option in options)
        {
            PeriodDefinitionOptions.Add(option);
        }
    }

    public async Task RefreshPeriodDefinitionsAsync(string? selectDefinitionId = null)
    {
        await _storage.InitializeAsync();
        string? desiredId = selectDefinitionId ?? SelectedPeriodDefinition?.Id;
        await LoadPeriodDefinitionOptionsAsync();

        if (!string.IsNullOrWhiteSpace(desiredId))
        {
            PeriodDefinitionOption? match = PeriodDefinitionOptions
                .FirstOrDefault(option => string.Equals(option.Id, desiredId, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                SelectedPeriodDefinition = match;
            }
        }

        OnPropertyChanged(nameof(SelectedPeriodDefinitionDescription));
    }

    private void SelectPeriodDefinitionOption(TaskItem task)
    {
        PeriodDefinitionOption? selected = null;

        if (!string.IsNullOrWhiteSpace(task.PeriodDefinitionId))
        {
            selected = PeriodDefinitionOptions
                .FirstOrDefault(option => string.Equals(option.Id, task.PeriodDefinitionId, StringComparison.OrdinalIgnoreCase));
        }

        if (selected == null && HasAdHocDefinition(task))
        {
            selected = PeriodDefinitionOptions.FirstOrDefault(option => option.IsAdHoc);
        }

        if (selected == null)
        {
            selected = task.AllowedPeriod switch
            {
                AllowedPeriod.Work => PeriodDefinitionOptions.FirstOrDefault(option =>
                    string.Equals(option.Id, PeriodDefinitionCatalog.WorkId, StringComparison.OrdinalIgnoreCase)),
                AllowedPeriod.OffWork => PeriodDefinitionOptions.FirstOrDefault(option =>
                    string.Equals(option.Id, PeriodDefinitionCatalog.OffWorkId, StringComparison.OrdinalIgnoreCase)),
                _ => PeriodDefinitionOptions.FirstOrDefault(option =>
                    string.Equals(option.Id, PeriodDefinitionCatalog.AnyId, StringComparison.OrdinalIgnoreCase))
            };
        }

        SelectedPeriodDefinition = selected ?? PeriodDefinitionOptions.FirstOrDefault();
    }

    private void ApplyPeriodDefinitionSelection(TaskItem task)
    {
        if (SelectedPeriodDefinition?.IsAdHoc == true)
        {
            task.PeriodDefinitionId = null;
            task.AdHocStartTime = AdHocIsAllDay ? null : AdHocStartTime;
            task.AdHocEndTime = AdHocIsAllDay ? null : AdHocEndTime;
            task.AdHocWeekdays = SelectedAdHocWeekdays == AllWeekdays ? null : SelectedAdHocWeekdays;
            task.AdHocIsAllDay = AdHocIsAllDay;
            task.AdHocMode = SelectedAlignmentMode.Mode;
            task.AllowedPeriod = AllowedPeriod.Custom;
            return;
        }

        task.PeriodDefinitionId = SelectedPeriodDefinition?.Id;
        task.AdHocStartTime = null;
        task.AdHocEndTime = null;
        task.AdHocWeekdays = null;
        task.AdHocIsAllDay = false;
        task.AdHocMode = PeriodDefinitionMode.None;

        task.AllowedPeriod = SelectedPeriodDefinition?.Id switch
        {
            PeriodDefinitionCatalog.WorkId => AllowedPeriod.Work,
            PeriodDefinitionCatalog.OffWorkId => AllowedPeriod.OffWork,
            PeriodDefinitionCatalog.AnyId => AllowedPeriod.Any,
            _ => AllowedPeriod.Custom
        };
    }

    private static bool HasAdHocDefinition(TaskItem task)
    {
        return task.AdHocStartTime.HasValue
            || task.AdHocEndTime.HasValue
            || task.AdHocWeekdays.HasValue
            || task.AdHocIsAllDay
            || task.AdHocMode != PeriodDefinitionMode.None
            || task.AllowedPeriod == AllowedPeriod.Custom;
    }

    private static PeriodDefinitionOption CreateOption(PeriodDefinition definition, bool isCoreBuiltIn)
    {
        return new PeriodDefinitionOption(
            definition.Id,
            definition.Name,
            PeriodDefinitionFormatter.DescribeDefinition(definition),
            definition,
            isAdHoc: false,
            isCoreBuiltIn: isCoreBuiltIn);
    }

    private PeriodDefinition BuildAdHocDefinition()
    {
        return new PeriodDefinition
        {
            Id = string.Empty,
            Name = "Ad-hoc custom",
            StartTime = AdHocIsAllDay ? null : AdHocStartTime,
            EndTime = AdHocIsAllDay ? null : AdHocEndTime,
            Weekdays = SelectedAdHocWeekdays == Weekdays.None ? AllWeekdays : SelectedAdHocWeekdays,
            IsAllDay = AdHocIsAllDay,
            Mode = SelectedAlignmentMode.Mode
        };
    }

    partial void OnSelectedPeriodDefinitionChanged(PeriodDefinitionOption? value)
    {
        OnPropertyChanged(nameof(IsAdHocSelection));
        OnPropertyChanged(nameof(CanEditSelectedDefinition));
        OnPropertyChanged(nameof(SelectedPeriodDefinitionDescription));
    }

    partial void OnAdHocStartTimeChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(SelectedPeriodDefinitionDescription));
    }

    partial void OnAdHocEndTimeChanged(TimeSpan value)
    {
        OnPropertyChanged(nameof(SelectedPeriodDefinitionDescription));
    }

    partial void OnAdHocIsAllDayChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectedPeriodDefinitionDescription));
    }

    partial void OnSelectedAlignmentModeChanged(AlignmentModeOption value)
    {
        OnPropertyChanged(nameof(SelectedPeriodDefinitionDescription));
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
            _ => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime()
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
