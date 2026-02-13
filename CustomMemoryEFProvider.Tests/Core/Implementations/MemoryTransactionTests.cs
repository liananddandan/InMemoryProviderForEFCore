using CustomMemoryEFProvider.Core.Enums;
using CustomMemoryEFProvider.Core.Exceptions;
using CustomMemoryEFProvider.Core.Implementations;
using Xunit;

namespace CustomMemoryEFProvider.Tests.Core.Implementations;

public class MemoryTransactionTests
{
    [Fact]
    public void BeginTransaction_Should_Start_As_Active()
    {
        var db = new MemoryDatabase();
        var tx = db.BeginTransaction();

        Assert.Equal(TransactionState.Active, tx.State);
    }

    [Fact]
    public void Commit_Should_Change_State_To_Committed()
    {
        var db = new MemoryDatabase();
        var tx = db.BeginTransaction();

        tx.Commit();

        Assert.Equal(TransactionState.Committed, tx.State);
    }

    [Fact]
    public void Rollback_Should_Change_State_To_RolledBack()
    {
        var db = new MemoryDatabase();
        var tx = db.BeginTransaction();

        tx.Rollback();

        Assert.Equal(TransactionState.RolledBack, tx.State);
    }

    [Fact]
    public void Commit_After_Commit_Should_Throw()
    {
        var db = new MemoryDatabase();
        var tx = db.BeginTransaction();

        tx.Commit();

        Assert.Throws<TransactionException>(() => tx.Commit());
    }
}