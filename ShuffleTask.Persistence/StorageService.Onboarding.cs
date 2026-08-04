using System.Globalization;
using ShuffleTask.Domain.Entities;
using ShuffleTask.Persistence.Models;

namespace ShuffleTask.Persistence;

public partial class StorageService
{
    private async Task EnsureOnboardingStateAsync()
    {
        KeyValueEntity? existing = await Db.FindAsync<KeyValueEntity>(OnboardingVersionKey).ConfigureAwait(false);
        if (existing != null)
        {
            return;
        }

        bool hasPriorUse = false;
        if (_databaseExistedBeforeOpen)
        {
            int taskCount = await Db.Table<TaskItemRecord>().CountAsync().ConfigureAwait(false);
            KeyValueEntity? settings = await Db.FindAsync<KeyValueEntity>(SettingsKey).ConfigureAwait(false);
            hasPriorUse = taskCount > 0 || settings != null;
        }

        await Db.RunInTransactionAsync(conn =>
        {
            if (conn.Find<KeyValueEntity>(OnboardingVersionKey) == null)
            {
                conn.Insert(new KeyValueEntity
                {
                    Key = OnboardingVersionKey,
                    Value = hasPriorUse ? "1" : "0"
                });
            }
        }).ConfigureAwait(false);
    }

    public async Task<int> GetCompletedVersionAsync()
    {
        KeyValueEntity? entry = await Db.FindAsync<KeyValueEntity>(OnboardingVersionKey).ConfigureAwait(false);
        return entry != null
            && int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version)
            ? Math.Max(0, version)
            : 0;
    }

    public async Task CompleteAsync(int version)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        await _onboardingLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await Db.RunInTransactionAsync(conn =>
            {
                SetCompletedVersion(conn, version);
                _faultInjector?.BeforeCommit("onboarding.complete");
            }).ConfigureAwait(false);
        }
        finally
        {
            _onboardingLock.Release();
        }
    }

    public async Task CompleteWithSamplesAsync(IReadOnlyCollection<TaskItem> samples, int version)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        await _onboardingLock.WaitAsync().ConfigureAwait(false);
        await _taskLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_taskSchemaIsFuture)
            {
                throw new InvalidOperationException("Sample tasks cannot be added because the stored task schema is newer than this app.");
            }

            if (await GetCompletedVersionAsync().ConfigureAwait(false) >= version)
            {
                return;
            }

            var records = new List<TaskItemRecord>(samples.Count);
            foreach (TaskItem sample in samples)
            {
                if (string.IsNullOrWhiteSpace(sample.Id))
                {
                    throw new InvalidOperationException("Onboarding sample tasks require stable identifiers.");
                }

                EnsureMetadata(sample, null, bumpVersion: true);
                records.Add(BuildTaskRecord(sample));
            }

            await Db.RunInTransactionAsync(conn =>
            {
                foreach (TaskItemRecord record in records)
                {
                    if (conn.Find<TaskItemRecord>(record.Id) == null)
                    {
                        conn.Insert(record);
                    }
                }

                SetCompletedVersion(conn, version);
                _faultInjector?.BeforeCommit("onboarding.samples");
            }).ConfigureAwait(false);
        }
        finally
        {
            _taskLock.Release();
            _onboardingLock.Release();
        }
    }

    private static void SetCompletedVersion(SQLite.SQLiteConnection connection, int version)
    {
        KeyValueEntity? entry = connection.Find<KeyValueEntity>(OnboardingVersionKey);
        if (entry == null)
        {
            connection.Insert(new KeyValueEntity
            {
                Key = OnboardingVersionKey,
                Value = version.ToString(CultureInfo.InvariantCulture)
            });
            return;
        }

        if (!int.TryParse(entry.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int existingVersion)
            || existingVersion < version)
        {
            entry.Value = version.ToString(CultureInfo.InvariantCulture);
            connection.Update(entry);
        }
    }
}
