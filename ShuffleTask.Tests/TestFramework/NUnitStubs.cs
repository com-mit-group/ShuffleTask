using System.Collections;
using System.Collections.Generic;

namespace NUnit.Framework;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class TestFixtureAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class TestAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SetUpAttribute : Attribute
{
}

public sealed class AssertionException : Exception
{
    public AssertionException(string message) : base(message)
    {
    }
}

public static class Assert
{
    public static void AreEqual<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AssertionException(message ?? $"Expected: {expected}. Actual: {actual}.");
        }
    }

    public static void AreNotEqual<T>(T notExpected, T actual, string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
        {
            throw new AssertionException(message ?? $"Did not expect: {notExpected}.");
        }
    }

    public static void IsTrue(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new AssertionException(message ?? "Condition is false but expected true.");
        }
    }

    public static void IsFalse(bool condition, string? message = null)
    {
        if (condition)
        {
            throw new AssertionException(message ?? "Condition is true but expected false.");
        }
    }

    public static void IsNull(object? value, string? message = null)
    {
        if (value != null)
        {
            throw new AssertionException(message ?? "Expected null value.");
        }
    }

    public static void IsNotNull(object? value, string? message = null)
    {
        if (value == null)
        {
            throw new AssertionException(message ?? "Expected non-null value.");
        }
    }

    public static void AreSame(object? expected, object? actual, string? message = null)
    {
        if (!ReferenceEquals(expected, actual))
        {
            throw new AssertionException(message ?? "Expected references to be the same instance.");
        }
    }

    public static void AreNotSame(object? notExpected, object? actual, string? message = null)
    {
        if (ReferenceEquals(notExpected, actual))
        {
            throw new AssertionException(message ?? "Expected references to be different instances.");
        }
    }
}

public static class CollectionAssert
{
    public static void AreEqual(IEnumerable expected, IEnumerable actual, string? message = null)
    {
        var expectedList = expected.Cast<object?>().ToList();
        var actualList = actual.Cast<object?>().ToList();

        if (expectedList.Count != actualList.Count)
        {
            throw new AssertionException(message ?? $"Collection counts differ. Expected {expectedList.Count} but was {actualList.Count}.");
        }

        for (int i = 0; i < expectedList.Count; i++)
        {
            if (!Equals(expectedList[i], actualList[i]))
            {
                throw new AssertionException(message ?? $"Collections differ at index {i}. Expected {expectedList[i]} but was {actualList[i]}.");
            }
        }
    }
}
