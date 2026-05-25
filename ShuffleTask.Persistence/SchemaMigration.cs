using SQLite;

namespace ShuffleTask.Persistence;

internal sealed record SchemaMigration(int FromVersion, int ToVersion, Action<SQLiteConnection> Apply);
