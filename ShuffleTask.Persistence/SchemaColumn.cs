namespace ShuffleTask.Persistence;

internal readonly record struct SchemaColumn(string Name, string SqlType, string DefaultSql);
