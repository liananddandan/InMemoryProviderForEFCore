using CustomMemoryEFProvider.Core.Implementations;
using Xunit;

namespace CustomMemoryEFProvider.Tests.Core.Implementations;

public class MemoryDatabaseRootTests
{
    [Fact]
    public void GetOrAdd_SameName_ReturnsSameInstance()
    {
        var root = new MemoryDatabaseRoot();

        var db1 = root.GetOrAdd("TestDb");
        var db2 = root.GetOrAdd("TestDb");

        Assert.Same(db1, db2);
    }

    [Fact]
    public void GetOrAdd_DifferentNames_ReturnDifferentInstances()
    {
        var root = new MemoryDatabaseRoot();

        var db1 = root.GetOrAdd("Db1");
        var db2 = root.GetOrAdd("Db2");

        Assert.NotSame(db1, db2);
    }
}