using NUnit.Framework;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Tests.TestDoubles;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Presentation.Tests;

[TestFixture]
public class TasksViewModelTests
{
    private StorageServiceStub _storage = null!;
    private TasksViewModel _viewModel = null!;
    private TimeProvider _clock = null!;

    [SetUp]
    public async Task SetUp()
    {
        _clock = TimeProvider.System;
        _storage = new StorageServiceStub(_clock);
        await _storage.InitializeAsync();
        _viewModel = new TasksViewModel(_storage, _clock);
    }

    private TaskItem CreateTask(string id, TimeSpan createdOffset, bool paused = false)
    {
        DateTime baseUtc = _clock.GetUtcNow().UtcDateTime;
        return new TaskItem
        {
            Id = id,
            Title = $"Task {id}",
            Description = $"Description {id}",
            Importance = 3,
            Deadline = baseUtc.AddDays(1),
            Repeat = RepeatType.Daily,
            Weekdays = Weekdays.Mon | Weekdays.Wed,
            IntervalDays = 2,
            LastDoneAt = baseUtc.AddHours(-6),
            AllowedPeriod = AllowedPeriod.Work,
            Paused = paused,
            CreatedAt = baseUtc.Add(createdOffset)
        };
    }

    [Test]
    public async Task LoadAsync_PopulatesTasksSortedByPriority()
    {
        var older = CreateTask("older", TimeSpan.FromDays(-2));
        var newer = CreateTask("newer", TimeSpan.FromDays(-1));

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
        var task = CreateTask("toggle", TimeSpan.Zero, paused: false);
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
        var keep = CreateTask("keep", TimeSpan.FromHours(-1));
        var remove = CreateTask("remove", TimeSpan.Zero);

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
    public async Task MarkDoneAsync_MarksTaskAsCompletedAndRefreshesList()
    {
        var task = CreateTask("task1", TimeSpan.Zero);
        task.Status = TaskLifecycleStatus.Active;

        await _storage.AddTaskAsync(task);

        await _viewModel.LoadAsync();
        Assert.AreEqual(1, _viewModel.Tasks.Count, "Precondition: one task before marking done.");

        var toMarkDone = _viewModel.Tasks.Single().Task;
        Assert.AreEqual(TaskLifecycleStatus.Active, toMarkDone.Status, "Task should start as Active.");

        await _viewModel.MarkDoneAsync(toMarkDone);

        Assert.AreEqual(1, _viewModel.Tasks.Count, "MarkDoneAsync should refresh the list.");
        Assert.AreEqual(1, _storage.MarkDoneCallCount, "MarkDoneAsync should call storage mark done.");

        var updated = await _storage.GetTaskAsync(task.Id);
        Assert.IsNotNull(updated, "Task should still exist after marking done.");
        Assert.AreEqual(TaskLifecycleStatus.Completed, updated!.Status, "Task status should be Completed.");
        Assert.IsNotNull(updated.CompletedAt, "CompletedAt timestamp should be set.");
    }

    [Test]
    public async Task MarkDoneAsync_WithNullTask_DoesNothing()
    {
        await _viewModel.MarkDoneAsync(null!);

        Assert.AreEqual(0, _storage.MarkDoneCallCount, "MarkDoneAsync with null should not call storage.");
    }

    [Test]
    public async Task MarkDoneAsync_UpdatesTaskStatusInUI()
    {
        var task = CreateTask("task1", TimeSpan.Zero);
        task.Status = TaskLifecycleStatus.Active;

        await _storage.AddTaskAsync(task);
        await _viewModel.LoadAsync();

        var beforeMarkDone = _viewModel.Tasks.Single();
        Assert.AreEqual("Active", beforeMarkDone.StatusText, "Task should initially show as Active.");

        await _viewModel.MarkDoneAsync(beforeMarkDone.Task);

        var afterMarkDone = _viewModel.Tasks.Single();
        Assert.IsTrue(afterMarkDone.StatusText.Contains("Completed"), "Task should show as Completed after marking done.");
        Assert.IsTrue(afterMarkDone.HasStatusBadge, "Completed task should have a status badge.");
    }

    [Test]
    public void Clone_CreatesIndependentCopy()
    {
        var source = CreateTask("clone", TimeSpan.Zero, paused: true);

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
