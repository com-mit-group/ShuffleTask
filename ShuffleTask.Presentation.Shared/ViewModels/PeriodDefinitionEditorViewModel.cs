using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Presentation.Utilities;

namespace ShuffleTask.ViewModels;

public sealed partial class PeriodDefinitionEditorViewModel : ViewModelWithWeekdaySelection
{
    private readonly IStorageService _storage;
    private AppSettings _settings = new();
    private string? _definitionId;
    private bool _isNew = true;

    public PeriodDefinitionEditorViewModel(IStorageService storage)
    {
        _storage = storage;
        AlignmentModeOptions = AlignmentModeCatalog.Defaults;
        SelectedAlignmentMode = AlignmentModeOptions[0];
        SelectedWeekdays = PeriodDefinitionCatalog.AllWeekdays;
    }

    public IReadOnlyList<AlignmentModeOption> AlignmentModeOptions { get; }

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

    public bool IsAlignmentFromSettings => IsSettingsAlignedMode(SelectedAlignmentMode?.Mode ?? PeriodDefinitionMode.None);

    public string SettingsResolvedTimeRange => GetSettingsRangeText(SelectedAlignmentMode?.Mode ?? PeriodDefinitionMode.None);

    public bool IsNew
    {
        get => _isNew;
        private set => SetProperty(ref _isNew, value);
    }

    public event EventHandler<PeriodDefinitionSavedEventArgs>? Saved;

    public async Task LoadAsync(PeriodDefinition? definition)
    {
        await _storage.InitializeAsync();
        _settings = await _storage.GetSettingsAsync();
        ApplyDefinition(definition);
        OnPropertyChanged(nameof(SettingsResolvedTimeRange));
    }

    private void ApplyDefinition(PeriodDefinition? definition)
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

    partial void OnSelectedAlignmentModeChanged(AlignmentModeOption? value)
    {
        OnPropertyChanged(nameof(IsAlignmentFromSettings));
        OnPropertyChanged(nameof(SettingsResolvedTimeRange));
    }

    private static bool IsSettingsAlignedMode(PeriodDefinitionMode mode)
    {
        return mode.HasFlag(PeriodDefinitionMode.AlignWithWorkHours)
            || mode.HasFlag(PeriodDefinitionMode.OffWorkRelativeToWorkHours)
            || mode.HasFlag(PeriodDefinitionMode.Morning)
            || mode.HasFlag(PeriodDefinitionMode.Lunch)
            || mode.HasFlag(PeriodDefinitionMode.Evening);
    }

    private string GetSettingsRangeText(PeriodDefinitionMode mode)
    {
        (string label, TimeSpan start, TimeSpan end) = mode switch
        {
            var m when m.HasFlag(PeriodDefinitionMode.Morning) => ("Morning hours", _settings.MorningStart, _settings.MorningEnd),
            var m when m.HasFlag(PeriodDefinitionMode.Lunch) => ("Lunch hours", _settings.LunchStart, _settings.LunchEnd),
            var m when m.HasFlag(PeriodDefinitionMode.Evening) => ("Evening hours", _settings.EveningStart, _settings.EveningEnd),
            var m when m.HasFlag(PeriodDefinitionMode.OffWorkRelativeToWorkHours) => ("Work hours (off-work)", _settings.WorkStart, _settings.WorkEnd),
            var m when m.HasFlag(PeriodDefinitionMode.AlignWithWorkHours) => ("Work hours", _settings.WorkStart, _settings.WorkEnd),
            _ => (string.Empty, TimeSpan.Zero, TimeSpan.Zero)
        };

        if (string.IsNullOrWhiteSpace(label))
        {
            return string.Empty;
        }

        return $"Settings time range: {label} {start:hh\\:mm}–{end:hh\\:mm}";
    }
}
