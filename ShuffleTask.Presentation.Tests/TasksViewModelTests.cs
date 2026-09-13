using NSubstitute;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using ShuffleTask.Application.Abstractions;
using ShuffleTask.Application.Models;
using ShuffleTask.Application.Services;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Presentation.Models;
using ShuffleTask.Tests.TestDoubles;
using ShuffleTask.ViewModels;

namespace ShuffleTask.Presentation.Tests;

[TestFixture]
public class TasksViewModelTests
{
    private StorageServiceStub _storage = null!;
    private TasksViewModel _viewModel = null!;
    private TimeProvider _clock = null!;
    private AppSettings _settings = null!;

    [SetUp]
    public async Task SetUp()
    {
        _clock = TimeProvider.System;
        _settings = new AppSettings();
        _storage = new StorageServiceStub(_clock);
        await _storage.InitializeAsync();
        var ns = Substitute.For<INetworkSyncService>();
        _viewModel = new TasksViewModel(_storage, _clock, ns, _settings);
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
    public async Task QuickAddAsync_WhileLoadIsRunning_IsDisabledAndRetainsInput()
    {
        IStorageService storage = Substitute.For<IStorageService>();
        storage.InitializeAsync().Returns(Task.CompletedTask);
        var loadStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseLoad = new TaskCompletionSource<List<TaskItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.GetTasksAsync(Arg.Any<string?>(), Arg.Any<string>()).Returns(_ =>
        {
            loadStarted.TrySetResult();
            return releaseLoad.Task;
        });
        var viewModel = new TasksViewModel(storage, _clock, Substitute.For<INetworkSyncService>(), _settings)
        {
            QuickAddTitle = "Keep this thought"
        };

        Task load = viewModel.LoadAsync();
        await loadStarted.Task;

        Assert.That(viewModel.CanQuickAdd, Is.False);
        await viewModel.QuickAddAsync();
        Assert.That(viewModel.QuickAddTitle, Is.EqualTo("Keep this thought"));
        await storage.DidNotReceive().AddTaskAsync(Arg.Any<TaskItem>());

        releaseLoad.SetResult([]);
        await load;

        Assert.That(viewModel.CanQuickAdd, Is.True);
        await viewModel.QuickAddAsync();
        await storage.Received(1).AddTaskAsync(Arg.Is<TaskItem>(task => task.Title == "Keep this thought"));
    }

    [Test]
    public async Task LoadAsync_WhenRefreshFails_PreservesTasksAndRetryRecovers()
    {
        IStorageService storage = Substitute.For<IStorageService>();
        storage.InitializeAsync().Returns(Task.CompletedTask);
        var task = CreateTask("retained", TimeSpan.Zero);
        storage.GetTasksAsync(Arg.Any<string?>(), Arg.Any<string>()).Returns(
            Task.FromResult(new List<TaskItem> { task }),
            Task.FromException<List<TaskItem>>(new IOException("Injected storage failure.")),
            Task.FromResult(new List<TaskItem> { task }));
        var networkSync = Substitute.For<INetworkSyncService>();
        var viewModel = new TasksViewModel(storage, _clock, networkSync, _settings);

        await viewModel.LoadAsync();
        await viewModel.LoadAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Tasks.Select(item => item.Task.Id), Is.EqualTo(new[] { task.Id }));
            Assert.That(viewModel.OperationState.Kind, Is.EqualTo(OperationStateKind.TransientFailure));
            Assert.That(viewModel.OperationState.CanRetry, Is.True);
            Assert.That(viewModel.OperationState.IsBlocking, Is.False);
        });

        await viewModel.OperationState.RetryCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Tasks.Select(item => item.Task.Id), Is.EqualTo(new[] { task.Id }));
            Assert.That(viewModel.OperationState.Kind, Is.EqualTo(OperationStateKind.Success));
        });
    }

    [Test]
    public async Task QuickAddAsync_CreatesOneDefaultActiveTaskAndClearsMatchingInput()
    {
        var networkSync = Substitute.For<INetworkSyncService>();
        var viewModel = new TasksViewModel(_storage, _clock, networkSync, _settings)
        {
            QuickAddTitle = "  Send stand-up notes  "
        };

        await viewModel.QuickAddAsync();

        TaskItem saved = (await _storage.GetTasksAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(saved.Title, Is.EqualTo("Send stand-up notes"));
            Assert.That(saved.Status, Is.EqualTo(TaskLifecycleStatus.Active));
            Assert.That(saved.Importance, Is.EqualTo(1));
            Assert.That(saved.SizePoints, Is.EqualTo(3.0));
            Assert.That(saved.AutoShuffleAllowed, Is.True);
            Assert.That(viewModel.Tasks.Select(item => item.Task.Id), Is.EqualTo(new[] { saved.Id }));
            Assert.That(viewModel.QuickAddTitle, Is.Empty);
            Assert.That(viewModel.QuickAddOperationState.LocalDataSaved, Is.True);
        });
        await networkSync.Received(1).PublishTaskUpsertAsync(Arg.Is<TaskItem>(task => task.Id == saved.Id), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task QuickAddAsync_ConcurrentSubmissionCreatesExactlyOneTask()
    {
        IStorageService storage = Substitute.For<IStorageService>();
        storage.InitializeAsync().Returns(Task.CompletedTask);
        var addStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAdd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var captured = new List<TaskItem>();
        storage.AddTaskAsync(Arg.Any<TaskItem>()).Returns(call =>
        {
            captured.Add(TaskItem.Clone(call.Arg<TaskItem>()));
            addStarted.TrySetResult();
            return releaseAdd.Task;
        });
        var networkSync = Substitute.For<INetworkSyncService>();
        var viewModel = new TasksViewModel(storage, _clock, networkSync, _settings)
        {
            QuickAddTitle = "Only once"
        };

        Task first = viewModel.QuickAddAsync();
        await addStarted.Task;
        await viewModel.QuickAddAsync();
        releaseAdd.SetResult();
        await first;

        Assert.That(captured, Has.Count.EqualTo(1));
        await storage.Received(1).AddTaskAsync(Arg.Any<TaskItem>());
    }

    [Test]
    public async Task QuickAddAsync_LocalFailureRetainsInputAndRetryDoesNotDuplicate()
    {
        IStorageService storage = Substitute.For<IStorageService>();
        storage.InitializeAsync().Returns(Task.CompletedTask);
        storage.AddTaskAsync(Arg.Any<TaskItem>()).Returns(
            Task.FromException(new IOException("Injected local failure.")),
            Task.CompletedTask);
        var networkSync = Substitute.For<INetworkSyncService>();
        var viewModel = new TasksViewModel(storage, _clock, networkSync, _settings)
        {
            QuickAddTitle = "Retry me"
        };

        await viewModel.QuickAddAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.QuickAddTitle, Is.EqualTo("Retry me"));
            Assert.That(viewModel.QuickAddOperationState.CanRetry, Is.True);
            Assert.That(viewModel.QuickAddOperationState.LocalDataSaved, Is.False);
        });

        await viewModel.QuickAddOperationState.RetryCommand.ExecuteAsync(null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Tasks, Has.Count.EqualTo(1));
            Assert.That(viewModel.QuickAddTitle, Is.Empty);
            Assert.That(viewModel.QuickAddOperationState.LocalDataSaved, Is.True);
        });
        await storage.Received(2).AddTaskAsync(Arg.Any<TaskItem>());
        await networkSync.Received(1).PublishTaskUpsertAsync(Arg.Any<TaskItem>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task QuickAddAsync_DownstreamFailureReportsLocalSaveWithoutRetry()
    {
        var networkSync = Substitute.For<INetworkSyncService>();
        networkSync.PublishTaskUpsertAsync(Arg.Any<TaskItem>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new IOException("Injected publish failure.")));
        var viewModel = new TasksViewModel(_storage, _clock, networkSync, _settings)
        {
            QuickAddTitle = "Saved locally"
        };

        await viewModel.QuickAddAsync();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Tasks, Has.Count.EqualTo(1));
            Assert.That(viewModel.QuickAddOperationState.Message, Does.Contain("sync pending"));
            Assert.That(viewModel.QuickAddOperationState.LocalDataSaved, Is.True);
            Assert.That(viewModel.QuickAddOperationState.CanRetry, Is.False);
        });
    }

    [Test]
    public async Task QuickAddAsync_ActiveFilterExplainsWhenSavedTaskIsHidden()
    {
        _viewModel.SelectedRepeatFilter = "Repeating";
        _viewModel.QuickAddTitle = "One-off hidden task";

        await _viewModel.QuickAddAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.Tasks, Is.Empty);
            Assert.That(_viewModel.QuickAddOperationState.Message, Does.Contain("hidden by the current filters"));
            Assert.That(_viewModel.QuickAddOperationState.LocalDataSaved, Is.True);
        });
        Assert.That(await _storage.GetTasksAsync(), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task QuickAddAsync_StaleCompletionDoesNotClearNewerText()
    {
        IStorageService storage = Substitute.For<IStorageService>();
        storage.InitializeAsync().Returns(Task.CompletedTask);
        var addStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAdd = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.AddTaskAsync(Arg.Any<TaskItem>()).Returns(_ =>
        {
            addStarted.TrySetResult();
            return releaseAdd.Task;
        });
        var viewModel = new TasksViewModel(storage, _clock, Substitute.For<INetworkSyncService>(), _settings)
        {
            QuickAddTitle = "First thought"
        };

        Task submission = viewModel.QuickAddAsync();
        await addStarted.Task;
        viewModel.QuickAddTitle = "Newer thought";
        releaseAdd.SetResult();
        await submission;

        Assert.That(viewModel.QuickAddTitle, Is.EqualTo("Newer thought"));
        await storage.Received(1).AddTaskAsync(Arg.Is<TaskItem>(task => task.Title == "First thought"));
    }

    [Test]
    public async Task QuickAddAsync_ValidationRetainsFocusableInputStateWithoutSaving()
    {
        _viewModel.QuickAddTitle = "   ";

        await _viewModel.QuickAddAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_viewModel.QuickAddTitle, Is.EqualTo("   "));
            Assert.That(_viewModel.QuickAddOperationState.Kind, Is.EqualTo(OperationStateKind.Validation));
            Assert.That(_viewModel.QuickAddOperationState.Message, Does.Contain("title"));
            Assert.That(_viewModel.Tasks, Is.Empty);
        });
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
