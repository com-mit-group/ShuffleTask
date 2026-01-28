using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Domain.Entities;

namespace ShuffleTask.ViewModels;

public sealed partial class PeriodDefinitionEditorViewModel : ObservableObject
{
    private readonly IStorageService _storage;
    private Weekdays _selectedWeekdays;
    private string? _definitionId;
    private bool _isNew = true;

    public PeriodDefinitionEditorViewModel(IStorageService storage)
    {
        _storage = storage;
        AlignmentModeOptions = AlignmentModeOption.CreateDefaults();
        SelectedAlignmentMode = AlignmentModeOptions[0];
        SelectedWeekdays = PeriodDefinitionCatalog.AllWeekdays;
    }

    public IReadOnlyList<AlignmentModeOption> AlignmentModeOptions { get; }

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
    private string name = string.Empty;

    [ObservableProperty]
    private bool isAllDay;

    [ObservableProperty]
    private TimeSpan startTime = new(9, 0, 0);

    [ObservableProperty]
    private TimeSpan endTime = new(17, 0, 0);

    [ObservableProperty]
    private AlignmentModeOption selectedAlignmentMode;

    [ObservableProperty]
    private bool isBusy;

    public bool IsNew
    {
        get => _isNew;
        private set => SetProperty(ref _isNew, value);
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

    public event EventHandler<PeriodDefinitionSavedEventArgs>? Saved;

    public void Load(PeriodDefinition? definition)
    {
        if (definition == null)
        {
            _definitionId = null;
            IsNew = true;
            Name = string.Empty;
            SelectedWeekdays = PeriodDefinitionCatalog.AllWeekdays;
            IsAllDay = false;
            StartTime = new TimeSpan(9, 0, 0);
            EndTime = new TimeSpan(17, 0, 0);
            SelectedAlignmentMode = AlignmentModeOptions[0];
            return;
        }

        _definitionId = definition.Id;
        IsNew = string.IsNullOrWhiteSpace(definition.Id);
        Name = definition.Name;
        SelectedWeekdays = definition.Weekdays == Weekdays.None ? PeriodDefinitionCatalog.AllWeekdays : definition.Weekdays;
        IsAllDay = definition.IsAllDay;
        StartTime = definition.StartTime ?? new TimeSpan(9, 0, 0);
        EndTime = definition.EndTime ?? new TimeSpan(17, 0, 0);
        SelectedAlignmentMode = AlignmentModeOptions.FirstOrDefault(option => option.Mode == definition.Mode)
            ?? AlignmentModeOptions[0];
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _storage.InitializeAsync();

            var definition = new PeriodDefinition
            {
                Id = _definitionId ?? string.Empty,
                Name = Name.Trim(),
                Weekdays = SelectedWeekdays == Weekdays.None ? PeriodDefinitionCatalog.AllWeekdays : SelectedWeekdays,
                IsAllDay = IsAllDay,
                StartTime = IsAllDay ? null : StartTime,
                EndTime = IsAllDay ? null : EndTime,
                Mode = SelectedAlignmentMode.Mode
            };

            if (string.IsNullOrWhiteSpace(_definitionId))
            {
                await _storage.AddPeriodDefinitionAsync(definition);
                _definitionId = definition.Id;
                IsNew = false;
            }
            else
            {
                await _storage.UpdatePeriodDefinitionAsync(definition);
            }

            Saved?.Invoke(this, new PeriodDefinitionSavedEventArgs(definition));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static Weekdays ApplyWeekdaySelection(Weekdays current, Weekdays day, bool enabled)
    {
        return enabled ? current | day : current & ~day;
    }

    private bool GetWeekday(Weekdays day) => _selectedWeekdays.HasFlag(day);

    private void SetWeekday(Weekdays day, bool isSelected)
    {
        SelectedWeekdays = ApplyWeekdaySelection(SelectedWeekdays, day, isSelected);
    }
}
