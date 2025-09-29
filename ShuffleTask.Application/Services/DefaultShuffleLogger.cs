using Microsoft.Extensions.Logging;
using ShuffleTask.Application.Abstractions;
using System.Diagnostics;

namespace ShuffleTask.Application.Services;

/// <summary>
/// Default implementation of structured logging for ShuffleTask operations.
/// Uses Debug.WriteLine for development and can be extended for production logging.
/// </summary>
public class DefaultShuffleLogger : IShuffleLogger
{
    private readonly TimeProvider _clock;

    public DefaultShuffleLogger(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public void LogTaskSelection(string taskId, string taskTitle, string reason, int candidateCount, TimeSpan nextGap)
    {
        var timestamp = _clock.GetUtcNow().ToString("HH:mm:ss.fff");
        Debug.WriteLine($"[{timestamp}] TASK_SELECTION | TaskId={taskId} | Title=\"{taskTitle}\" | Reason={reason} | Candidates={candidateCount} | NextGap={nextGap:mm\\:ss}");
    }

    public void LogTimerEvent(string eventType, string? taskId = null, TimeSpan? duration = null, string? reason = null)
    {
        var timestamp = _clock.GetUtcNow().ToString("HH:mm:ss.fff");
        var taskInfo = taskId != null ? $" | TaskId={taskId}" : "";
        var durationInfo = duration.HasValue ? $" | Duration={duration.Value:mm\\:ss}" : "";
        var reasonInfo = reason != null ? $" | Reason={reason}" : "";
        Debug.WriteLine($"[{timestamp}] TIMER_EVENT | Event={eventType}{taskInfo}{durationInfo}{reasonInfo}");
    }

    public void LogStateTransition(string taskId, string fromStatus, string toStatus, string? reason = null)
    {
        var timestamp = _clock.GetUtcNow().ToString("HH:mm:ss.fff");
        var reasonInfo = reason != null ? $" | Reason={reason}" : "";
        Debug.WriteLine($"[{timestamp}] STATE_TRANSITION | TaskId={taskId} | From={fromStatus} | To={toStatus}{reasonInfo}");
    }

    public void LogSyncEvent(string eventType, string? details = null, Exception? exception = null)
    {
        var timestamp = _clock.GetUtcNow().ToString("HH:mm:ss.fff");
        var detailsInfo = details != null ? $" | Details={details}" : "";
        var errorInfo = exception != null ? $" | Error={exception.Message}" : "";
        Debug.WriteLine($"[{timestamp}] SYNC_EVENT | Event={eventType}{detailsInfo}{errorInfo}");
    }

    public void LogNotification(string notificationType, string title, string? message = null, bool success = true, Exception? exception = null)
    {
        var timestamp = _clock.GetUtcNow().ToString("HH:mm:ss.fff");
        var status = success ? "SUCCESS" : "FAILED";
        var messageInfo = message != null ? $" | Message=\"{message}\"" : "";
        var errorInfo = exception != null ? $" | Error={exception.Message}" : "";
        Debug.WriteLine($"[{timestamp}] NOTIFICATION | Type={notificationType} | Title=\"{title}\" | Status={status}{messageInfo}{errorInfo}");
    }

    public void LogOperation(LogLevel level, string operation, string? details = null, Exception? exception = null)
    {
        var timestamp = _clock.GetUtcNow().ToString("HH:mm:ss.fff");
        var levelStr = level.ToString().ToUpper();
        var detailsInfo = details != null ? $" | Details={details}" : "";
        var errorInfo = exception != null ? $" | Error={exception.Message}" : "";
        Debug.WriteLine($"[{timestamp}] {levelStr} | Operation={operation}{detailsInfo}{errorInfo}");
    }
}