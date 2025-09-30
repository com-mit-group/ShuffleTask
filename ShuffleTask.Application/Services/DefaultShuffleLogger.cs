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
    private const string ClockFormat = "HH:mm:ss.fff";
    private readonly TimeProvider _clock;

    public DefaultShuffleLogger(TimeProvider clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public void LogTaskSelection(string taskId, string taskTitle, string reason, int candidateCount, TimeSpan nextGap)
    {
        var timestamp = _clock.GetUtcNow().ToString(ClockFormat);
        Trace.TraceInformation($"[{timestamp}] TASK_SELECTION | TaskId={taskId} | Title=\"{taskTitle}\" | Reason={reason} | Candidates={candidateCount} | NextGap={nextGap:mm\\:ss}");
    }

    public void LogTimerEvent(string eventType, string? taskId = null, TimeSpan? duration = null, string? reason = null)
    {
        var timestamp = _clock.GetUtcNow().ToString(ClockFormat);
        var taskInfo = taskId != null ? $" | TaskId={taskId}" : "";
        var durationInfo = duration.HasValue ? $" | Duration={duration.Value:mm\\:ss}" : "";
        var reasonInfo = reason != null ? $" | Reason={reason}" : "";
        Trace.TraceInformation($"[{timestamp}] TIMER_EVENT | Event={eventType}{taskInfo}{durationInfo}{reasonInfo}");
    }

    public void LogStateTransition(string taskId, string fromStatus, string toStatus, string? reason = null)
    {
        var timestamp = _clock.GetUtcNow().ToString(ClockFormat);
        var reasonInfo = reason != null ? $" | Reason={reason}" : "";
        Trace.TraceInformation($"[{timestamp}] STATE_TRANSITION | TaskId={taskId} | From={fromStatus} | To={toStatus}{reasonInfo}");
    }

    public void LogSyncEvent(string eventType, string? details = null, Exception? exception = null)
    {
        var timestamp = _clock.GetUtcNow().ToString(ClockFormat);
        var detailsInfo = details != null ? $" | Details={details}" : "";
        var errorInfo = exception != null ? $" | Error={exception.Message}" : "";
        var traceMessage = $"[{timestamp}] SYNC_EVENT | Event={eventType}{detailsInfo}{errorInfo}";

        if (exception is not null)
        {
            Trace.TraceError(traceMessage);
        }
        else
        {
            Trace.TraceInformation(traceMessage);
        }
    }

    public void LogNotification(string notificationType, string title, string? message = null, bool success = true, Exception? exception = null)
    {
        var timestamp = _clock.GetUtcNow().ToString(ClockFormat);
        var status = success ? "SUCCESS" : "FAILED";
        var messageInfo = message != null ? $" | Message=\"{message}\"" : "";
        var errorInfo = exception != null ? $" | Error={exception.Message}" : "";
        var traceMessage = $"[{timestamp}] NOTIFICATION | Type={notificationType} | Title=\"{title}\" | Status={status}{messageInfo}{errorInfo}";

        if (!success || exception is not null)
        {
            Trace.TraceWarning(traceMessage);
        }
        else
        {
            Trace.TraceInformation(traceMessage);
        }
    }

    public void LogOperation(LogLevel level, string operation, string? details = null, Exception? exception = null)
    {
        var timestamp = _clock.GetUtcNow().ToString(ClockFormat);
        var levelStr = level.ToString().ToUpper();
        var detailsInfo = details != null ? $" | Details={details}" : "";
        var errorInfo = exception != null ? $" | Error={exception.Message}" : "";
        var traceMessage = $"[{timestamp}] {levelStr} | Operation={operation}{detailsInfo}{errorInfo}";

        switch (level)
        {
            case LogLevel.Critical:
            case LogLevel.Error:
                Trace.TraceError(traceMessage);
                break;
            case LogLevel.Warning:
                Trace.TraceWarning(traceMessage);
                break;
            default:
                Trace.TraceInformation(traceMessage);
                break;
        }
    }
}
