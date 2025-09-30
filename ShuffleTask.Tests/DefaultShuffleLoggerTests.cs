using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using ShuffleTask.Application.Services;

namespace ShuffleTask.Tests;

[TestFixture]
public class DefaultShuffleLoggerTests
{
    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }

    [Test]
    public void LogMethods_WriteStructuredMessagesWithConsistentTimestamp()
    {
        var now = new DateTimeOffset(2024, 4, 1, 14, 30, 15, 123, TimeSpan.Zero);
        var clock = new FixedTimeProvider(now);
        var logger = new DefaultShuffleLogger(clock);

        using var writer = new StringWriter();
        using var listener = new TextWriterTraceListener(writer);

        Debug.Listeners.Add(listener);

        try
        {
            logger.LogTaskSelection("123", "Deep Work", "top priority", 4, TimeSpan.FromMinutes(12) + TimeSpan.FromSeconds(45));
            logger.LogTimerEvent("started");
            logger.LogTimerEvent("stopped", "123", TimeSpan.FromMinutes(2), "focus interval complete");
            logger.LogStateTransition("123", "Active", "Snoozed", "user requested break");
            logger.LogSyncEvent("push", "synced 10 tasks", new InvalidOperationException("intermittent"));
            logger.LogSyncEvent("heartbeat");
            logger.LogNotification("Reminder", "Daily planning");
            logger.LogNotification("Reminder", "Weekly review", "time to reflect", success: false, exception: new Exception("toast"));
            logger.LogOperation(LogLevel.Warning, "Cleanup", "pruned backlog", new Exception("io"));
            logger.LogOperation(LogLevel.Information, "Heartbeat");

            Debug.Flush();
        }
        finally
        {
            Debug.Listeners.Remove(listener);
        }

        var output = writer.ToString();

        StringAssert.Contains("[14:30:15.123] TASK_SELECTION | TaskId=123 | Title=\"Deep Work\" | Reason=top priority | Candidates=4 | NextGap=12:45", output);
        StringAssert.Contains("[14:30:15.123] TIMER_EVENT | Event=started", output);
        StringAssert.Contains("| TaskId=123 | Duration=02:00 | Reason=focus interval complete", output);
        StringAssert.Contains("STATE_TRANSITION | TaskId=123 | From=Active | To=Snoozed | Reason=user requested break", output);
        StringAssert.Contains("SYNC_EVENT | Event=push | Details=synced 10 tasks | Error=intermittent", output);
        StringAssert.Contains("SYNC_EVENT | Event=heartbeat", output);
        StringAssert.Contains("NOTIFICATION | Type=Reminder | Title=\"Daily planning\" | Status=SUCCESS", output);
        StringAssert.Contains("NOTIFICATION | Type=Reminder | Title=\"Weekly review\" | Status=FAILED | Message=\"time to reflect\" | Error=toast", output);
        StringAssert.Contains("WARNING | Operation=Cleanup | Details=pruned backlog | Error=io", output);
        StringAssert.Contains("INFORMATION | Operation=Heartbeat", output);
    }
}
