namespace ShuffleTask.Persistence;

internal sealed class UnsupportedSettingsSchemaException : InvalidOperationException
{
    public UnsupportedSettingsSchemaException(int schemaVersion)
        : base($"Unsupported future settings schema version: {schemaVersion}")
    {
        SchemaVersion = schemaVersion;
    }

    public int SchemaVersion { get; }
}
