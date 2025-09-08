using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShuffleTask.Models;
using ShuffleTask.Services;

namespace ShuffleTask.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly StorageService _storage;

    public MainViewModel(StorageService storage)
    {
        _storage = storage;
    }

    public ObservableCollection<TaskRow> TaskRows { get; } = new();

    [ObservableProperty]
    private bool isBusy;

    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await _storage.InitializeAsync();
            List<TaskItem> list = await _storage.GetTasksAsync();
            TaskRows.Clear();
            foreach (TaskItem t in list)
                TaskRows.Add(TaskRow.From(t));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task PauseResumeAsync(TaskItem t)
    {
        t.Paused = !t.Paused;
        await _storage.UpdateTaskAsync(t);
        await LoadAsync();
    }

    [RelayCommand]
    public async Task DeleteAsync(TaskItem t)
    {
        await _storage.DeleteTaskAsync(t.Id);
        await LoadAsync();
    }
}

public class TaskRow
{
    public TaskItem Item { get; init; } = default!;
    public string RepeatChip { get; init; } = "";
    public string AllowedChip { get; init; } = "";
    public string NextDue { get; init; } = "";

    public static TaskRow From(TaskItem t)
    {
        string repeat = t.Repeat switch
        {
            RepeatType.None => "One-off",
            RepeatType.Daily => "Daily",
            RepeatType.Weekly => $"Weekly({t.Weekdays})",
            RepeatType.Interval => $"Every {Math.Max(1, t.IntervalDays)}d",
            _ => t.Repeat.ToString()
        };
        string allowed = t.AllowedPeriod switch
        {
            AllowedPeriod.Any => "Any",
            AllowedPeriod.Work => "Work",
            AllowedPeriod.Off => "Off",
            _ => t.AllowedPeriod.ToString()
        };
        string nextDue = t.Deadline.HasValue ? $"Due {t.Deadline:yyyy-MM-dd HH:mm}" : string.Empty;
        return new TaskRow
        {
            Item = t,
            RepeatChip = repeat,
            AllowedChip = allowed,
            NextDue = nextDue
        };
    }
}
