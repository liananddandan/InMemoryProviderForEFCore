using CustomMemoryEFProvider.Core.Implementations;
using Xunit;

namespace CustomMemoryEFProvider.Tests.Core.Implementations;

public class MemoryDatabaseTests
{
    private class BaseEntity
    {
        public int Id { get; set; }
    }

    private class DerivedEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    private class AnotherEntity
    {
        public int Id { get; set; }
    }

    [Fact]
    public void GetTable_Should_Return_Same_Instance_For_Same_Type()
    {
        var db = new MemoryDatabase();

        var table1 = db.GetTable<BaseEntity>();
        var table2 = db.GetTable<BaseEntity>();

        Assert.Same(table1, table2);
    }

    [Fact]
    public void GetTable_Should_Isolate_Different_Entity_Types()
    {
        var db = new MemoryDatabase();

        var table1 = db.GetTable<BaseEntity>();
        var table2 = db.GetTable<AnotherEntity>();

        Assert.NotSame(table1, table2);
    }

    [Fact]
    public void GetTable_Should_Allow_Derived_Type_With_Base_Generic()
    {
        var db = new MemoryDatabase();

        var table = db.GetTable<DerivedEntity>(typeof(DerivedEntity));

        Assert.NotNull(table);
    }

    [Fact]
    public void GetTable_Should_Throw_When_Type_Not_Assignable()
    {
        var db = new MemoryDatabase();

        Assert.Throws<ArgumentException>(() =>
            db.GetTable<BaseEntity>(typeof(AnotherEntity)));
    }

    [Fact]
    public void Transaction_Should_Rollback_Correctly()
    {
        var db = new MemoryDatabase();

        var table = db.GetTable<AnotherEntity>();
        table.Add(new AnotherEntity { Id = 1 });
        table.SaveChanges();

        using var tx = db.BeginTransaction();

        var txTable = db.GetTable<AnotherEntity>();
        txTable.Add(new AnotherEntity { Id = 2 });
        txTable.SaveChanges();

        tx.Rollback();

        // After rollback: base table unchanged
        var afterRollback = db.GetTable<AnotherEntity>();
        Assert.Single(afterRollback.GetAllEntities());
    }

    [Fact]
    public void SaveChanges_Should_Return_Total_Affected_Count()
    {
        var db = new MemoryDatabase();

        var table1 = db.GetTable<BaseEntity>();
        var table2 = db.GetTable<AnotherEntity>();

        table1.Add(new BaseEntity { Id = 1 });
        table2.Add(new AnotherEntity { Id = 1 });

        var affected = db.SaveChanges();

        Assert.True(affected >= 2);
    }

    [Fact]
    public void BeginTransaction_Should_Throw_If_Already_Active()
    {
        var db = new MemoryDatabase();

        var tx = db.BeginTransaction();

        Assert.Throws<InvalidOperationException>(() =>
            db.BeginTransaction());
    }
    
    [Fact]
    public void Transaction_Should_ReadYourWrites_WithinActiveTransaction()
    {
        using var db = new MemoryDatabase();

        // base: commit 1 row
        var baseTable = db.GetTable<AnotherEntity>();
        baseTable.Add(new AnotherEntity { Id = 1 });
        baseTable.SaveChanges();

        Assert.Single(baseTable.QueryRows);

        // begin tx: from now on, db.GetTable returns tx table
        using var tx = db.BeginTransaction();

        var txTable = db.GetTable<AnotherEntity>();
        txTable.Add(new AnotherEntity { Id = 2 });

        // IMPORTANT: txTable.QueryRows merges committed + pending, so within tx we should see 2 rows.
        Assert.Equal(2, txTable.QueryRows.Count());

        // Also, since db is in tx mode, fetching again should still show tx view
        var again = db.GetTable<AnotherEntity>();
        Assert.Equal(2, again.QueryRows.Count());
    }

    [Fact]
    public void Rollback_Should_Discard_TxChanges_And_ReturnToBaseView()
    {
        using var db = new MemoryDatabase();

        var table = db.GetTable<AnotherEntity>();
        table.Add(new AnotherEntity { Id = 1 });
        table.SaveChanges();

        Assert.Single(table.QueryRows);

        using (var tx = db.BeginTransaction())
        {
            var txTable = db.GetTable<AnotherEntity>();
            txTable.Add(new AnotherEntity { Id = 2 });

            // within tx, we see base + tx pending
            Assert.Equal(2, txTable.QueryRows.Count());

            // rollback
            tx.Rollback();
        }

        // after rollback, db.GetTable returns base table again
        var after = db.GetTable<AnotherEntity>();
        Assert.Single(after.QueryRows);
    }

    [Fact]
    public void Commit_Should_Persist_TxChanges_ToBase()
    {
        using var db = new MemoryDatabase();

        var table = db.GetTable<AnotherEntity>();
        table.Add(new AnotherEntity { Id = 1});
        table.SaveChanges();

        Assert.Single(table.QueryRows);

        using (var tx = db.BeginTransaction())
        {
            var txTable = db.GetTable<AnotherEntity>();
            txTable.Add(new AnotherEntity { Id = 2 });

            Assert.Equal(2, txTable.QueryRows.Count());

            tx.Commit();
        }

        // after commit, base should now have 2 rows
        var after = db.GetTable<AnotherEntity>();
        Assert.Equal(2, after.QueryRows.Count());
    }
}