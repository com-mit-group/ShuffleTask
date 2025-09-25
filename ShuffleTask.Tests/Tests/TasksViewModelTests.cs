using NUnit.Framework;
using ShuffleTask.Models;
using ShuffleTask.Services;
using ShuffleTask.ViewModels;
using System.Threading.Tasks;
using System.Linq;
using ShuffleTask.Tests.TestDoubles;

namespace ShuffleTask.Tests;

[TestFixture]
public class TasksViewModelTests
{
    private StorageServiceStub _storage = null!;
    private TasksViewModel _viewModel = null!;

    [SetUp]
    public async Task SetUp()
    {
        _storage = new StorageServiceStub();
        await _storage.InitializeAsync();
        _viewModel = new TasksViewModel(_storage);
    }

    private static TaskItem CreateTask(string id, DateTime createdAt, bool paused = false)
    {
        return new TaskItem
        {
            Id = id,
            Title = $"Task {id}",
            Description = $"Description {id}",
            Importance = 3,
            Deadline = DateTime.UtcNow.AddDays(1),
            Repeat = RepeatType.Daily,
            Weekdays = Weekdays.Mon | Weekdays.Wed,
            IntervalDays = 2,
            LastDoneAt = DateTime.UtcNow.AddHours(-6),
            AllowedPeriod = AllowedPeriod.Work,
            Paused = paused,
            CreatedAt = createdAt
        };
    }

    [Test]
    public async Task LoadAsync_PopulatesTasksSortedByPriority()
    {
        var older = CreateTask("older", DateTime.UtcNow.AddDays(-2));
        var newer = CreateTask("newer", DateTime.UtcNow.AddDays(-1));

        await _storage.AddTaskAsync(older);
        await _storage.AddTaskAsync(newer);

        await _viewModel.LoadAsync();

        Assert.AreEqual(2, _viewModel.Tasks.Count, "Expected two tasks after load.");
        CollectionAssert.AreEquivalent(
            new[] { older.Id, newer.Id },
            _viewModel.Tasks.Select(t => t.Task.Id).ToArray(),
            "All tasks should be present after load.");
        Assert.That(
            _viewModel.Tasks.Select(t => t.PriorityScore).ToList(),
            Is.Ordered.Descending,
            "Tasks should be sorted by priority score.");
        Assert.IsFalse(_viewModel.IsBusy, "LoadAsync should reset IsBusy.");
        Assert.AreEqual(2, _storage.InitializeCallCount, "LoadAsync should initialize storage each time.");
        Assert.AreEqual(1, _storage.GetTasksCallCount, "LoadAsync should fetch tasks once.");
    }

    [Test]
    public async Task LoadAsync_WhenAlreadyBusy_DoesNotQueryStorage()
    {
        _viewModel.IsBusy = true;

        await _viewModel.LoadAsync();

        Assert.AreEqual(1, _storage.InitializeCallCount, "ViewModel should not reinitialize when busy.");
        Assert.AreEqual(0, _storage.GetTasksCallCount, "LoadAsync should not fetch tasks when IsBusy is true.");
    }

    [Test]
    public async Task TogglePauseAsync_TogglesPausedStateAndRefreshesTasks()
    {
        var task = CreateTask("toggle", DateTime.UtcNow, paused: false);
        await _storage.AddTaskAsync(task);

        await _viewModel.LoadAsync();
        var original = _viewModel.Tasks.Single().Task;

        await _viewModel.TogglePauseAsync(original);

        var afterFirstToggle = _viewModel.Tasks.Single();
        Assert.IsTrue(afterFirstToggle.Task.Paused, "Toggle should mark task as paused.");
        Assert.AreEqual("Paused", afterFirstToggle.StatusText, "Status text should reflect paused state.");
        Assert.AreEqual(1, _storage.UpdateTaskCallCount, "Storage should persist the toggle.");

        await _viewModel.TogglePauseAsync(afterFirstToggle.Task);

        var afterSecondToggle = _viewModel.Tasks.Single();
        Assert.IsFalse(afterSecondToggle.Task.Paused, "Second toggle should resume the task.");
        Assert.AreEqual("Active", afterSecondToggle.StatusText, "Status text should update after resuming.");
        Assert.AreEqual(2, _storage.UpdateTaskCallCount, "Each toggle should persist the change.");
    }

    [Test]
    public async Task DeleteAsync_RemovesTaskAndRefreshesList()
    {
        var keep = CreateTask("keep", DateTime.UtcNow.AddHours(-1));
        var remove = CreateTask("remove", DateTime.UtcNow);

        await _storage.AddTaskAsync(keep);
        await _storage.AddTaskAsync(remove);

        await _viewModel.LoadAsync();
        Assert.AreEqual(2, _viewModel.Tasks.Count, "Precondition: two tasks before deletion.");

        var toDelete = _viewModel.Tasks.Single(item => item.Task.Id == remove.Id).Task;

        await _viewModel.DeleteAsync(toDelete);

        Assert.AreEqual(1, _viewModel.Tasks.Count, "DeleteAsync should refresh the list.");
        Assert.AreEqual(keep.Id, _viewModel.Tasks[0].Task.Id, "Remaining task should be the one not deleted.");
        Assert.AreEqual(1, _storage.DeleteTaskCallCount, "DeleteAsync should call storage delete.");

        var deleted = await _storage.GetTaskAsync(remove.Id);
        Assert.IsNull(deleted, "Deleted task should not remain in storage.");
    }

    [Test]
    public void Clone_CreatesIndependentCopy()
    {
        var source = CreateTask("clone", DateTime.UtcNow, paused: true);

        var clone = TaskItem.Clone(source);

        Assert.AreNotSame(source, clone, "Clone should return a new instance.");
        Assert.AreEqual(source.Id, clone.Id);
        Assert.AreEqual(source.Title, clone.Title);
        Assert.AreEqual(source.Description, clone.Description);
        Assert.AreEqual(source.Importance, clone.Importance);
        Assert.AreEqual(source.Deadline, clone.Deadline);
        Assert.AreEqual(source.Repeat, clone.Repeat);
        Assert.AreEqual(source.Weekdays, clone.Weekdays);
        Assert.AreEqual(source.IntervalDays, clone.IntervalDays);
        Assert.AreEqual(source.LastDoneAt, clone.LastDoneAt);
        Assert.AreEqual(source.AllowedPeriod, clone.AllowedPeriod);
        Assert.AreEqual(source.Paused, clone.Paused);
        Assert.AreEqual(source.CreatedAt, clone.CreatedAt);

        clone.Title = "Updated";
        clone.Paused = false;

        Assert.AreEqual("Task clone", source.Title, "Original title should be unchanged.");
        Assert.IsTrue(source.Paused, "Original paused flag should remain true.");
    }
}
