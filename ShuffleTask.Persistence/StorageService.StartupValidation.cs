using System.Diagnostics;
using System.Globalization;
using Newtonsoft.Json;
using SQLite;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Persistence.Models;

namespace ShuffleTask.Persistence;

public partial class StorageService
{
    private async Task ValidateAndRecoverStartupStateAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        _logger?.LogSyncEvent("PersistenceValidationStarted", "domain=startup");

        if (!_taskSchemaIsFuture)
        {
            await ValidateAndRecoverTaskTableAsync().ConfigureAwait(false);
        }
        else
        {
            _logger?.LogSyncEvent("PersistenceRecovery", "Skipped task validation because stored task schema is newer than supported.");
        }

        if (!_periodSchemaIsFuture)
        {
            await ValidateAndRecoverPeriodTableAsync().ConfigureAwait(false);
        }
        else
        {
            _logger?.LogSyncEvent("PersistenceRecovery", "Skipped period validation because stored period schema is newer than supported.");
        }

        _logger?.LogSyncEvent("PersistenceValidationCompleted", $"domain=startup; durationMs={stopwatch.ElapsedMilliseconds}");
    }

    private async Task ValidateAndRecoverTaskTableAsync()
    {
        if (_taskSchemaIsFuture)
        {
            return;
        }

        await Db.RunInTransactionAsync(conn =>
        {
            var rows = conn.Query<TaskValidationRow>(TaskValidationSql);
            QuarantineRowsWithMissingTaskIds(conn, rows);

            var validRows = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Id))
                .ToList();

            QuarantineDuplicateTaskIds(conn, validRows);

            var exposedRows = validRows
                .GroupBy(row => row.Id!.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => ChooseTaskRecordToKeep(group))
                .ToList();

            foreach (var row in exposedRows)
            {
                RepairTaskRow(conn, row);
            }
        }).ConfigureAwait(false);
    }

    private async Task ValidateAndRecoverPeriodTableAsync()
    {
        if (_periodSchemaIsFuture)
        {
            return;
        }

        await Db.RunInTransactionAsync(conn =>
        {
            var rows = conn.Query<PeriodValidationRow>(PeriodValidationSql);
            foreach (var row in rows.Where(row => string.IsNullOrWhiteSpace(row.Id)))
            {
                QuarantinePeriodRow(conn, row, "missing-id");
                conn.Execute("DELETE FROM PeriodDefinition WHERE rowid = ?", row.RowId);
            }

            var validRows = rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Id))
                .ToList();

            foreach (var group in validRows.GroupBy(row => row.Id!.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var keep = ChoosePeriodRecordToKeep(group);
                foreach (var duplicate in group.Where(row => row.RowId != keep.RowId))
                {
                    QuarantinePeriodRow(conn, duplicate, "duplicate-id");
                    conn.Execute("DELETE FROM PeriodDefinition WHERE rowid = ?", duplicate.RowId);
                }

                RepairPeriodRow(conn, keep);
            }
        }).ConfigureAwait(false);
    }

    private void QuarantineRowsWithMissingTaskIds(SQLiteConnection conn, IReadOnlyList<TaskValidationRow> rows)
    {
        foreach (var row in rows.Where(row => string.IsNullOrWhiteSpace(row.Id)))
        {
            QuarantineTaskRow(conn, row, "missing-id");
            conn.Execute("DELETE FROM TaskItem WHERE rowid = ?", row.RowId);
        }
    }

    private void QuarantineDuplicateTaskIds(SQLiteConnection conn, IReadOnlyList<TaskValidationRow> rows)
    {
        foreach (var group in rows.GroupBy(row => row.Id!.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var keep = ChooseTaskRecordToKeep(group);
            foreach (var duplicate in group.Where(row => row.RowId != keep.RowId))
            {
                QuarantineTaskRow(conn, duplicate, "duplicate-id");
                conn.Execute("DELETE FROM TaskItem WHERE rowid = ?", duplicate.RowId);
            }
        }
    }

    private static TaskValidationRow ChooseTaskRecordToKeep(IEnumerable<TaskValidationRow> rows)
    {
        return rows
            .OrderByDescending(row => row.EventVersion ?? 0)
            .ThenByDescending(row => ParseOptionalDate(row.UpdatedAt) ?? DateTime.MinValue)
            .ThenByDescending(row => ParseOptionalDate(row.CreatedAt) ?? DateTime.MinValue)
            .ThenBy(row => row.RowId)
            .First();
    }

    private static PeriodValidationRow ChoosePeriodRecordToKeep(IEnumerable<PeriodValidationRow> rows)
    {
        return rows
            .OrderBy(row => PeriodDefinitionCatalog.TryGet(row.Id, out _) ? 0 : 1)
            .ThenBy(row => row.RowId)
            .First();
    }

    private void RepairTaskRow(SQLiteConnection conn, TaskValidationRow row)
    {
        bool repaired = false;
        DateTime nowUtc = _clock.GetUtcNow().UtcDateTime;
        DateTime createdAt = ParseOptionalDate(row.CreatedAt) ?? nowUtc;
        DateTime updatedAt = ParseOptionalDate(row.UpdatedAt) ?? createdAt;
        if (updatedAt < createdAt)
        {
            updatedAt = createdAt;
        }

        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "Id", row.Id?.Trim());
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "Title", string.IsNullOrWhiteSpace(row.Title) ? "Untitled" : row.Title);
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "Description", row.Description ?? string.Empty);
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "CreatedAt", EnsureUtc(createdAt));
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "UpdatedAt", EnsureUtc(updatedAt));

        int status = row.Status ?? (int)TaskLifecycleStatus.Active;
        if (!IsValidTaskStatus(status))
        {
            _logger?.LogSyncEvent("PersistenceValidationFailure", $"domain=tasks; rowid={row.RowId}; field=status; action=repair");
            status = (int)TaskLifecycleStatus.Active;
            repaired = true;
        }

        int repeat = row.Repeat ?? (int)RepeatType.None;
        if (!IsValidRepeat(repeat))
        {
            _logger?.LogSyncEvent("PersistenceValidationFailure", $"domain=tasks; rowid={row.RowId}; field=repeat; action=repair");
            repeat = (int)RepeatType.None;
            repaired = true;
        }

        int weekdays = (row.Weekdays ?? 0) & ValidWeekdayMask;
        int intervalDays = Math.Max(0, row.IntervalDays ?? 0);
        if (repeat == (int)RepeatType.Interval && intervalDays < 1)
        {
            intervalDays = 1;
            repaired = true;
        }
        else if (repeat != (int)RepeatType.Interval && (row.IntervalDays ?? 0) < 0)
        {
            intervalDays = 0;
            repaired = true;
        }

        if (repeat == (int)RepeatType.None)
        {
            weekdays = 0;
            intervalDays = 0;
        }

        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "Repeat", repeat);
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "Weekdays", weekdays);
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "IntervalDays", intervalDays);

        int allowedPeriod = row.AllowedPeriod ?? (int)AllowedPeriod.Any;
        if (!IsValidAllowedPeriod(allowedPeriod))
        {
            allowedPeriod = (int)AllowedPeriod.Any;
            repaired = true;
        }

        int cutInLine = row.CutInLineMode ?? (int)CutInLineMode.None;
        if (!IsValidCutInLineMode(cutInLine))
        {
            cutInLine = (int)CutInLineMode.None;
            repaired = true;
        }

        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "AllowedPeriod", allowedPeriod);
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "CutInLineMode", cutInLine);
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "EventVersion", Math.Max(1, row.EventVersion ?? 0));

        RepairDateColumn(conn, row, "Deadline", row.Deadline, nullable: true, ref repaired);
        RepairDateColumn(conn, row, "LastDoneAt", row.LastDoneAt, nullable: true, ref repaired);

        DateTime? completedAt = ParseOptionalDate(row.CompletedAt);
        DateTime? snoozedUntil = ParseOptionalDate(row.SnoozedUntil);
        DateTime? nextEligibleAt = ParseOptionalDate(row.NextEligibleAt);

        if (status == (int)TaskLifecycleStatus.Completed)
        {
            completedAt ??= updatedAt;
            snoozedUntil = null;
            if (repeat == (int)RepeatType.None)
            {
                nextEligibleAt = null;
            }
        }
        else if (status == (int)TaskLifecycleStatus.Snoozed)
        {
            if (!snoozedUntil.HasValue || snoozedUntil.Value <= nowUtc)
            {
                status = (int)TaskLifecycleStatus.Active;
                snoozedUntil = null;
                nextEligibleAt = null;
            }
            else
            {
                nextEligibleAt = snoozedUntil;
            }

            completedAt = null;
        }
        else
        {
            completedAt = null;
            snoozedUntil = null;
            nextEligibleAt = null;
        }

        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "Status", status);
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "CompletedAt", completedAt);
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "SnoozedUntil", snoozedUntil);
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "NextEligibleAt", nextEligibleAt);

        repaired |= RepairTaskOwnership(conn, row);
        repaired |= RepairTimerOverrides(conn, row);

        if (repaired)
        {
            _logger?.LogSyncEvent("PersistenceRecovery", $"domain=tasks; rowid={row.RowId}; action=repaired");
        }
    }

    private void RepairPeriodRow(SQLiteConnection conn, PeriodValidationRow row)
    {
        bool repaired = false;
        string id = row.Id?.Trim() ?? string.Empty;
        int weekdays = (row.Weekdays ?? 0) & ValidWeekdayMask;
        if (weekdays == 0)
        {
            weekdays = ValidWeekdayMask;
        }

        int mode = row.Mode ?? (int)PeriodDefinitionMode.None;
        if ((mode & ~ValidPeriodModeMask) != 0)
        {
            mode = (int)PeriodDefinitionMode.None;
        }

        bool isAllDay = (row.IsAllDay ?? 0) != 0;
        object? startTime = NormalizeTimeSpanValue(row.StartTime);
        object? endTime = NormalizeTimeSpanValue(row.EndTime);
        if (isAllDay)
        {
            startTime = null;
            endTime = null;
        }

        repaired |= SetIfChanged(conn, "PeriodDefinition", row.RowId, "Id", id);
        repaired |= SetIfChanged(conn, "PeriodDefinition", row.RowId, "Name", string.IsNullOrWhiteSpace(row.Name) ? "Untitled period" : row.Name);
        repaired |= SetIfChanged(conn, "PeriodDefinition", row.RowId, "Weekdays", weekdays);
        repaired |= SetIfChanged(conn, "PeriodDefinition", row.RowId, "Mode", mode);
        repaired |= SetIfChanged(conn, "PeriodDefinition", row.RowId, "StartTime", startTime);
        repaired |= SetIfChanged(conn, "PeriodDefinition", row.RowId, "EndTime", endTime);

        if (repaired)
        {
            _logger?.LogSyncEvent("PersistenceRecovery", $"domain=periods; rowid={row.RowId}; action=repaired");
        }
    }

    private bool RepairTaskOwnership(SQLiteConnection conn, TaskValidationRow row)
    {
        string? userId = string.IsNullOrWhiteSpace(row.UserId) ? null : row.UserId.Trim();
        string? deviceId = string.IsNullOrWhiteSpace(row.DeviceId) ? null : row.DeviceId.Trim();

        if (!string.IsNullOrWhiteSpace(userId))
        {
            bool changedUser = SetIfChanged(conn, "TaskItem", row.RowId, "UserId", userId);
            bool changedDevice = SetIfChanged(conn, "TaskItem", row.RowId, "DeviceId", null);
            return changedUser || changedDevice;
        }

        bool userChanged = SetIfChanged(conn, "TaskItem", row.RowId, "UserId", null);
        bool deviceChanged = SetIfChanged(conn, "TaskItem", row.RowId, "DeviceId", deviceId ?? Environment.MachineName);
        return userChanged || deviceChanged;
    }

    private static bool RepairTimerOverrides(SQLiteConnection conn, TaskValidationRow row)
    {
        bool repaired = false;
        int? timerMode = row.CustomTimerMode;
        if (timerMode.HasValue && timerMode.Value is not 0 and not 1)
        {
            timerMode = null;
        }

        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "CustomTimerMode", timerMode);
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "CustomReminderMinutes", NormalizePositiveNullable(row.CustomReminderMinutes));
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "CustomFocusMinutes", NormalizeRangeNullable(row.CustomFocusMinutes, 5, 120));
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "CustomBreakMinutes", NormalizeRangeNullable(row.CustomBreakMinutes, 1, 60));
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, "CustomPomodoroCycles", NormalizeRangeNullable(row.CustomPomodoroCycles, 1, 8));
        return repaired;
    }

    private static int? NormalizePositiveNullable(int? value)
        => value.HasValue && value.Value > 0 ? value.Value : null;

    private static int? NormalizeRangeNullable(int? value, int min, int max)
        => value.HasValue && value.Value >= min && value.Value <= max ? value.Value : null;

    private void RepairDateColumn(
        SQLiteConnection conn,
        TaskValidationRow row,
        string column,
        string? rawValue,
        bool nullable,
        ref bool repaired)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return;
        }

        if (ParseOptionalDate(rawValue).HasValue)
        {
            return;
        }

        _logger?.LogSyncEvent("PersistenceValidationFailure", $"domain=tasks; rowid={row.RowId}; field={column}; action=repair");
        repaired |= SetIfChanged(conn, "TaskItem", row.RowId, column, nullable ? null : _clock.GetUtcNow().UtcDateTime);
    }

    private void QuarantineTaskRow(SQLiteConnection conn, TaskValidationRow row, string reason)
    {
        string key = CreateQuarantineKey("task", row.RowId, reason);
        conn.InsertOrReplace(new KeyValueEntity { Key = key, Value = JsonConvert.SerializeObject(row) });
        _logger?.LogSyncEvent("PersistenceQuarantine", $"domain=tasks; rowid={row.RowId}; reason={reason}; artifact={key}");
    }

    private void QuarantinePeriodRow(SQLiteConnection conn, PeriodValidationRow row, string reason)
    {
        string key = CreateQuarantineKey("period", row.RowId, reason);
        conn.InsertOrReplace(new KeyValueEntity { Key = key, Value = JsonConvert.SerializeObject(row) });
        _logger?.LogSyncEvent("PersistenceQuarantine", $"domain=periods; rowid={row.RowId}; reason={reason}; artifact={key}");
    }

    private string CreateQuarantineKey(string domain, long rowId, string reason)
    {
        string suffix = _clock.GetUtcNow().UtcDateTime.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture);
        return $"{domain}_quarantine_{reason}_{rowId}_{suffix}";
    }

    private static bool SetIfChanged(SQLiteConnection conn, string tableName, long rowId, string column, object? value)
    {
        object? current = conn.ExecuteScalar<string?>($"SELECT CAST({column} AS TEXT) FROM {tableName} WHERE rowid = ?", rowId);
        if (ValuesEquivalent(current, value))
        {
            return false;
        }

        conn.Execute($"UPDATE {tableName} SET {column} = ? WHERE rowid = ?", value, rowId);
        return true;
    }

    private static bool ValuesEquivalent(object? current, object? value)
    {
        if (current is null or DBNull)
        {
            return value is null;
        }

        if (value is null)
        {
            return false;
        }

        string currentText = Convert.ToString(current, CultureInfo.InvariantCulture) ?? string.Empty;
        if (value is DateTime dateTime)
        {
            return ParseOptionalDate(currentText) == EnsureUtc(dateTime);
        }

        if (value is TimeSpan timeSpan)
        {
            return NormalizeTimeSpanValue(currentText) is TimeSpan currentTimeSpan
                && currentTimeSpan == timeSpan;
        }

        string valueText = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return string.Equals(currentText, valueText, StringComparison.Ordinal);
    }

    private static DateTime? ParseOptionalDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks)
            && ticks > 0
            && ticks <= DateTime.MaxValue.Ticks)
        {
            return EnsureUtc(new DateTime(ticks, DateTimeKind.Utc));
        }

        if (DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTime parsed))
        {
            return EnsureUtc(parsed);
        }

        return null;
    }

    private static object? NormalizeTimeSpanValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks)
            && ticks >= 0
            && ticks < TimeSpan.FromDays(1).Ticks)
        {
            return new TimeSpan(ticks);
        }

        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan parsed)
            && parsed >= TimeSpan.Zero
            && parsed < TimeSpan.FromDays(1))
        {
            return parsed;
        }

        return null;
    }

    private static bool IsValidTaskStatus(int status)
        => status == (int)TaskLifecycleStatus.Active
            || status == (int)TaskLifecycleStatus.Snoozed
            || status == (int)TaskLifecycleStatus.Completed;

    private static bool IsValidRepeat(int repeat)
        => repeat == (int)RepeatType.None
            || repeat == (int)RepeatType.Daily
            || repeat == (int)RepeatType.Weekly
            || repeat == (int)RepeatType.Interval;

    private static bool IsValidAllowedPeriod(int allowedPeriod)
        => allowedPeriod == (int)AllowedPeriod.Any
            || allowedPeriod == (int)AllowedPeriod.Work
            || allowedPeriod == (int)AllowedPeriod.OffWork
            || allowedPeriod == (int)AllowedPeriod.Custom;

    private static bool IsValidCutInLineMode(int cutInLineMode)
        => cutInLineMode == (int)CutInLineMode.None
            || cutInLineMode == (int)CutInLineMode.Once
            || cutInLineMode == (int)CutInLineMode.UntilCompletion;

    private const string TaskValidationSql = """
        SELECT
            rowid AS RowId,
            Id,
            DeviceId,
            UserId,
            Title,
            Description,
            CAST(Deadline AS TEXT) AS Deadline,
            CAST(LastDoneAt AS TEXT) AS LastDoneAt,
            CAST(CreatedAt AS TEXT) AS CreatedAt,
            CAST(UpdatedAt AS TEXT) AS UpdatedAt,
            CAST(SnoozedUntil AS TEXT) AS SnoozedUntil,
            CAST(CompletedAt AS TEXT) AS CompletedAt,
            CAST(NextEligibleAt AS TEXT) AS NextEligibleAt,
            Status,
            Repeat,
            Weekdays,
            IntervalDays,
            AllowedPeriod,
            CutInLineMode,
            EventVersion,
            CustomTimerMode,
            CustomReminderMinutes,
            CustomFocusMinutes,
            CustomBreakMinutes,
            CustomPomodoroCycles
        FROM TaskItem
        """;

    private const string PeriodValidationSql = """
        SELECT
            rowid AS RowId,
            Id,
            Name,
            CAST(StartTime AS TEXT) AS StartTime,
            CAST(EndTime AS TEXT) AS EndTime,
            Weekdays,
            IsAllDay,
            Mode
        FROM PeriodDefinition
        """;

}
