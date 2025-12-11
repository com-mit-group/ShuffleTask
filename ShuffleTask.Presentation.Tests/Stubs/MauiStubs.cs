using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Microsoft.Maui.Storage
{
    public interface IPreferences
    {
        T Get<T>(string key, T defaultValue);
        void Set<T>(string key, T value);
        void Remove(string key);
    }

    public sealed class InMemoryPreferences : IPreferences
    {
        private readonly ConcurrentDictionary<string, object?> _values = new();

        public T Get<T>(string key, T defaultValue)
        {
            if (_values.TryGetValue(key, out var value) && value is T typed)
            {
                return typed;
            }

            return defaultValue;
        }

        public void Set<T>(string key, T value)
        {
            _values[key] = value;
        }

        public void Remove(string key)
        {
            _values.TryRemove(key, out _);
        }

        public void Clear()
        {
            _values.Clear();
        }
    }

    public static class Preferences
    {
        public static IPreferences Default { get; } = new InMemoryPreferences();

        public static void Clear() => (Default as InMemoryPreferences)?.Clear();
    }
}

namespace Microsoft.Maui.ApplicationModel
{
    public static class MainThread
    {
        public static Task InvokeOnMainThreadAsync(Func<Task> function)
        {
            ArgumentNullException.ThrowIfNull(function);
            return function();
        }
    }
}
