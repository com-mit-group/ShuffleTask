using System;

namespace SQLite;

[AttributeUsage(AttributeTargets.Property)]
public sealed class PrimaryKeyAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class IndexedAttribute : Attribute
{
    public bool Unique { get; set; }
}
