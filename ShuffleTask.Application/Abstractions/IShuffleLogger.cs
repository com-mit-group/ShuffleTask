using Microsoft.Extensions.Logging;

namespace ShuffleTask.Application.Abstractions;

/// <summary>
/// Structured logging interface for ShuffleTask operations
/// </summary>
public interface IShuffleLogger
{
    /// <summary>
    /// Log task selection and shuffle decision events
    /// </summary>
    void LogTaskSelection(string taskId, string taskTitle, string reason, int candidateCount, TimeSpan nextGap);
    
    /// <summary>
    /// Log timer lifecycle events
    /// </summary>
    void LogTimerEvent(string eventType, string? taskId = null, TimeSpan? duration = null, string? reason = null);
    
    /// <summary>
    /// Log task state transitions
    /// </summary>
    void LogStateTransition(string taskId, string fromStatus, string toStatus, string? reason = null);
    
    /// <summary>
    /// Log sync-related events
    /// </summary>
    void LogSyncEvent(string eventType, string? details = null, Exception? exception = null);
    
    /// <summary>
    /// Log notification pipeline events
    /// </summary>
    void LogNotification(string notificationType, string title, string? message = null, bool success = true, Exception? exception = null);
    
    /// <summary>
    /// Log general operations with structured context
    /// </summary>
    void LogOperation(LogLevel level, string operation, string? details = null, Exception? exception = null);
}