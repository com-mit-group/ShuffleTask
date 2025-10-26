using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Microsoft.Maui.Storage;

public interface IPreferences
{
    string Get(string key, string defaultValue);
    int Get(string key, int defaultValue);
    void Set(string key, string value);
    void Set(string key, int value);
    void Remove(string key);
}

internal sealed class InMemoryPreferences : IPreferences
{
    private readonly ConcurrentDictionary<string, object> _values = new();

    public string Get(string key, string defaultValue)
        => _values.TryGetValue(key, out object? value) && value is string text
            ? text
            : defaultValue;

    public int Get(string key, int defaultValue)
        => _values.TryGetValue(key, out object? value) && value is int number
            ? number
            : defaultValue;

    public void Set(string key, string value)
        => _values[key] = value;

    public void Set(string key, int value)
        => _values[key] = value;

    public void Remove(string key)
    {
        _values.TryRemove(key, out _);
    }

    public void Clear() => _values.Clear();
}

public static class Preferences
{
    private static readonly InMemoryPreferences DefaultInstance = new();

    public static IPreferences Default => DefaultInstance;

    public static void Reset() => DefaultInstance.Clear();
}

namespace Microsoft.Maui.ApplicationModel;

public static class MainThread
{
    public static bool IsMainThread => true;

    public static Task InvokeOnMainThreadAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    public static Task InvokeOnMainThreadAsync(Func<Task> function)
        => function();

    public static Task<T> InvokeOnMainThreadAsync<T>(Func<Task<T>> function)
        => function();
}

public static class FileSystem
{
    private static string? _appDataDirectory;

    public static string AppDataDirectory
    {
        get
        {
            if (string.IsNullOrEmpty(_appDataDirectory))
            {
                _appDataDirectory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ShuffleTaskTests");
            }

            return _appDataDirectory;
        }
    }

    public static void SetAppDataDirectory(string path)
    {
        _appDataDirectory = path;
        if (!System.IO.Directory.Exists(_appDataDirectory))
        {
            System.IO.Directory.CreateDirectory(_appDataDirectory);
        }
    }
}
