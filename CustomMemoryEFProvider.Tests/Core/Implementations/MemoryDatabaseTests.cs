using CustomMemoryEFProvider.Core.Enums;
using CustomMemoryEFProvider.Core.Implementations;
using CustomMemoryEFProvider.Tests.Core;
using Xunit;

public class MemoryDatabaseTests
{
    // 测试1：无事务时基础表CRUD正常
    [Fact]
    public void GetTable_NoTransaction_CrudWorks()
    {
        // Arrange
        var db = new MemoryDatabase();
        var table = db.GetTable<Product>();
        var product = new Product { Id = 1, Name = "Test Product", Price = 99.99m };

        // Act
        table.Add(product);
        table.SaveChanges();
        var foundProduct = table.Find(new object[] { 1 });

        // Assert
        Assert.NotNull(foundProduct);
        Assert.Equal(1, foundProduct.Id);
        Assert.Equal("Test Product", foundProduct.Name);
        Assert.Single(table.Query);
    }

    // 测试2：事务隔离性 - 事务内修改不提交，外部无感知（展示首选）
    [Fact]
    public void BeginTransaction_Isolation_UncommittedChangesNotPersisted()
    {
        // Arrange
        var db = new MemoryDatabase();
        var baseTable = db.GetTable<Product>();
        baseTable.Add(new Product { Id = 1, Price = 100 });
        baseTable.SaveChanges();
        int initialCount = baseTable.Query.Count();

        // Act：事务内修改但不提交
        using (var tran = db.BeginTransaction())
        {
            var tranTable = db.GetTable<Product>();
            tranTable.Add(new Product { Id = 2, Price = 200 });
            tranTable.SaveChanges();
            var product1 = tranTable.Find(new object[] { 1 });
            product1.Price = 150;
            tranTable.Update(product1);
            tranTable.SaveChanges();

            // 验证事务内多次GetTable复用临时表
            var tranTable2 = db.GetTable<Product>();
            Assert.Same(tranTable, tranTable2); // 同一个实例
            Assert.Equal(2, tranTable2.Query.Count());
        } // 事务自动Rollback

        // Assert：事务结束后基础表无变化
        var externalTable = db.GetTable<Product>();
        Assert.Equal(initialCount, externalTable.Query.Count());
        Assert.Equal(100, externalTable.Find(new object[] { 1 }).Price);
        Assert.Null(externalTable.Find(new object[] { 2 }));
    }

    // 测试3：事务原子性 - Commit后变更落地到基础表
    [Fact]
    public void Transaction_Commit_ChangesPersistToBaseTable()
    {
        // Arrange
        var db = new MemoryDatabase();
        var baseTable = db.GetTable<Product>();
        baseTable.Add(new Product { Id = 1, Price = 100 });
        baseTable.SaveChanges();

        // Act
        using (var tran = db.BeginTransaction())
        {
            var tranTable = db.GetTable<Product>();
            tranTable.Add(new Product { Id = 2, Price = 200 });
            var product1 = tranTable.Find(new object[] { 1 });
            product1.Price = 150;
            tranTable.Update(product1);
            tran.Commit();
        }

        // Assert：基础表包含所有变更
        var finalTable = db.GetTable<Product>();
        Assert.Equal(2, finalTable.Query.Count());
        Assert.Equal(150, finalTable.Find(new object[] { 1 }).Price);
        Assert.NotNull(finalTable.Find(new object[] { 2 }));
    }

    // 测试4：事务原子性 - Rollback后变更全部丢弃
    [Fact]
    public void Transaction_Rollback_ChangesDiscarded()
    {
        // Arrange
        var db = new MemoryDatabase();
        var baseTable = db.GetTable<Product>();
        baseTable.Add(new Product { Id = 1, Price = 100 });
        baseTable.SaveChanges();

        // Act
        using (var tran = db.BeginTransaction())
        {
            var tranTable = db.GetTable<Product>();
            tranTable.Add(new Product { Id = 2, Price = 200 });
            var product1 = tranTable.Find(new object[] { 1 });
            product1.Price = 150;
            tranTable.Update(product1);
            tran.Rollback();
        }

        // Assert：基础表恢复原始状态
        var finalTable = db.GetTable<Product>();
        Assert.Single(finalTable.Query);
        Assert.Equal(100, finalTable.Find(new object[] { 1 }).Price);
        Assert.Null(finalTable.Find(new object[] { 2 }));
    }

    // 测试5：事务状态流转 - 提交后状态变为Committed
    [Fact]
    public void Transaction_Commit_StateChangesToCommitted()
    {
        // Arrange
        var db = new MemoryDatabase();
        var tran = db.BeginTransaction();

        // Act
        tran.Commit();

        // Assert
        Assert.Equal(TransactionState.Committed, tran.State);
    }

    // 测试6：事务状态流转 - 回滚后状态变为RolledBack
    [Fact]
    public void Transaction_Rollback_StateChangesToRolledBack()
    {
        // Arrange
        var db = new MemoryDatabase();
        var tran = db.BeginTransaction();

        // Act
        tran.Rollback();

        // Assert
        Assert.Equal(TransactionState.RolledBack, tran.State);
    }

    // 测试7：禁止同时开启多个活跃事务
    [Fact]
    public void BeginTransaction_MultipleActiveTransactions_ThrowsException()
    {
        // Arrange
        var db = new MemoryDatabase();
        db.BeginTransaction();

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => db.BeginTransaction());
    }
}