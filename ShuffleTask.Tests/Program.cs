using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace ShuffleTask.Tests;

internal static class Program
{
    private static int Main()
    {
        bool success = MiniNUnitRunner.RunAll(Assembly.GetExecutingAssembly());
        return success ? 0 : 1;
    }
}

internal static class MiniNUnitRunner
{
    public static bool RunAll(Assembly assembly)
    {
        var fixtures = assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type => type.GetMethods().Any(m => m.GetCustomAttribute<TestAttribute>() != null));

        bool allPassed = true;

        foreach (var fixture in fixtures)
        {
            object? instance = null;
            try
            {
                instance = Activator.CreateInstance(fixture);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] Unable to create fixture {fixture.FullName}: {ex.Message}");
                allPassed = false;
                continue;
            }

            var setup = fixture.GetMethods().FirstOrDefault(m => m.GetCustomAttribute<SetUpAttribute>() != null);

            foreach (var test in fixture.GetMethods().Where(m => m.GetCustomAttribute<TestAttribute>() != null))
            {
                try
                {
                    InvokeIfAsync(setup, instance);
                    InvokeIfAsync(test, instance);
                    Console.WriteLine($"[PASS] {fixture.Name}.{test.Name}");
                }
                catch (Exception ex)
                {
                    allPassed = false;
                    var actual = ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;
                    Console.WriteLine($"[FAIL] {fixture.Name}.{test.Name}: {actual.GetType().Name} - {actual.Message}");
                }
            }
        }

        return allPassed;
    }

    private static void InvokeIfAsync(MethodInfo? method, object? instance)
    {
        if (method == null)
        {
            return;
        }

        object? result = method.Invoke(instance, Array.Empty<object?>());
        if (result is Task task)
        {
            task.GetAwaiter().GetResult();
        }
    }
}
