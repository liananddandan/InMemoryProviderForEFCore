using CustomMemoryEFProvider.Core.Exceptions;
using CustomMemoryEFProvider.Core.Helpers;
using Xunit;

namespace CustomMemoryEFProvider.Tests.Core.Helpers;

#region Extended Test Entities (Covers all primary key scenarios)
/// <summary>Single primary key (int Id)</summary>
public class SingleIntKeyEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Single primary key (string Id)</summary>
public class SingleStringKeyEntity
{
    public string Id { get; set; } = string.Empty;
    public int Value { get; set; }
}

/// <summary>Single nullable primary key with non-null value</summary>
public class NullableIdWithValueEntity
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>Composite primary key (OrderId + ProductId)</summary>
public class CompositeKeyEntity
{
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Composite primary key (includes string type)</summary>
public class CompositeStringKeyEntity
{
    public string UserId { get; set; } = string.Empty;
    public Guid RoleId { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>No primary key (no Id/XXXId properties)</summary>
public class NoPrimaryKeyEntity
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>Case-insensitive primary key name (iD)</summary>
public class CaseInsensitiveKeyEntity
{
    public int iD { get; set; } // Lowercase i, uppercase D
    public string Name { get; set; } = string.Empty;
}

/// <summary>Composite key with non-Id suffix properties (boundary test)</summary>
public class MixedCompositeKeyEntity
{
    public int OrderId { get; set; }
    public string ProductCode { get; set; } = string.Empty; // Non-Id suffix
    public int LineId { get; set; }
}

/// <summary>Composite key for sorting test (ZId → AId → MId)</summary>
public class CompositeKeyForSortingTest
{
    public int ZId { get; set; }   // Z开头（最后）
    public int AId { get; set; }   // A开头（最先）
    public int MId { get; set; }   // M开头（中间）
    public string NonIdField { get; set; } = string.Empty;
}
#endregion

/// <summary>
/// Unit tests for PrimaryKeyHelper - covers all primary key extraction scenarios
/// following requirement-driven testing principles (not code-driven)
/// </summary>
public class PrimaryKeyHelperTests
{
    #region Core Requirement: Extract Single Primary Key (Id)
    /// <summary>
    /// Verifies that single integer primary key (Id) is extracted correctly
    /// </summary>
    [Fact]
    public void ExtractPrimaryKeyValues_SingleIntKeyEntity_ReturnsCorrectIntKey()
    {
        // Arrange
        var entity = new SingleIntKeyEntity { Id = 123, Name = "Test" };
        var entityType = typeof(SingleIntKeyEntity);

        // Act
        var keyValues = PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, entityType);

        // Assert
        Assert.Single(keyValues);
        Assert.Equal(123, keyValues[0]);
        Assert.IsType<int>(keyValues[0]);
    }

    /// <summary>
    /// Verifies that single string primary key (Id) is extracted correctly
    /// </summary>
    [Fact]
    public void ExtractPrimaryKeyValues_SingleStringKeyEntity_ReturnsCorrectStringKey()
    {
        // Arrange
        var entity = new SingleStringKeyEntity { Id = "ABC123", Value = 456 };
        var entityType = typeof(SingleStringKeyEntity);

        // Act
        var keyValues = PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, entityType);

        // Assert
        Assert.Single(keyValues);
        Assert.Equal("ABC123", keyValues[0]);
        Assert.IsType<string>(keyValues[0]);
    }

    /// <summary>
    /// Verifies that nullable primary key with non-null value is extracted correctly
    /// </summary>
    [Fact]
    public void ExtractPrimaryKeyValues_NullableIdWithValue_ReturnsCorrectKey()
    {
        // Arrange
        var entity = new NullableIdWithValueEntity { Id = 456, Name = "NullableId" };
        var entityType = typeof(NullableIdWithValueEntity);

        // Act
        var keyValues = PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, entityType);

        // Assert
        Assert.Single(keyValues);
        Assert.Equal(456, keyValues[0]);
        Assert.IsType<int>(keyValues[0]); // Extracts actual value when not null
    }

    /// <summary>
    /// Verifies case-insensitive Id property name is recognized correctly
    /// </summary>
    [Fact]
    public void ExtractPrimaryKeyValues_CaseInsensitiveId_ReturnsCorrectKey()
    {
        // Arrange
        var entity = new CaseInsensitiveKeyEntity { iD = 789, Name = "CaseTest" };
        var entityType = typeof(CaseInsensitiveKeyEntity);

        // Act
        var keyValues = PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, entityType);

        // Assert
        Assert.Single(keyValues);
        Assert.Equal(789, keyValues[0]);
    }
    #endregion

    #region Core Requirement: Extract Composite Primary Key (XXXId)
    /// <summary>
    /// Verifies composite integer primary keys (OrderId + ProductId) are extracted correctly
    /// (sorted alphabetically: OrderId → ProductId)
    /// </summary>
    [Fact]
    public void ExtractPrimaryKeyValues_CompositeIntKeyEntity_ReturnsCorrectCompositeKeys()
    {
        // Arrange
        var entity = new CompositeKeyEntity { OrderId = 1001, ProductId = 2002, Quantity = 5 };
        var entityType = typeof(CompositeKeyEntity);

        // Act
        var keyValues = PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, entityType);

        // Assert
        Assert.Equal(2, keyValues.Length);
        Assert.Equal(1001, keyValues[0]); // OrderId (O) comes before ProductId (P)
        Assert.Equal(2002, keyValues[1]); // ProductId (P) comes after OrderId (O)
    }

    /// <summary>
    /// Verifies composite mixed-type primary keys (UserId + RoleId) are extracted correctly
    /// (sorted alphabetically: RoleId → UserId)
    /// </summary>
    [Fact]
    public void ExtractPrimaryKeyValues_CompositeStringKeyEntity_ReturnsCorrectCompositeKeys()
    {
        // Arrange
        var guid = Guid.NewGuid();
        var entity = new CompositeStringKeyEntity { UserId = "U1001", RoleId = guid, IsActive = true };
        var entityType = typeof(CompositeStringKeyEntity);

        // Act
        var keyValues = PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, entityType);

        // Assert
        Assert.Equal(2, keyValues.Length);
        Assert.Equal(guid, keyValues[0]);    // RoleId (R) comes before UserId (U)
        Assert.Equal("U1001", keyValues[1]); // UserId (U) comes after RoleId (R)
    }

    /// <summary>
    /// Verifies only XXXId suffix properties are extracted for composite keys
    /// (ignores non-Id suffix properties like ProductCode)
    /// NOTE: Sorted alphabetically → LineId (L) first, OrderId (O) second
    /// </summary>
    [Fact]
    public void ExtractPrimaryKeyValues_MixedCompositeKeyEntity_OnlyExtractsXXXId()
    {
        // Arrange
        var entity = new MixedCompositeKeyEntity { OrderId = 1001, ProductCode = "P123", LineId = 5 };
        var entityType = typeof(MixedCompositeKeyEntity);

        // Act
        var keyValues = PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, entityType);

        // Assert
        Assert.Equal(2, keyValues.Length); // Only LineId and OrderId are extracted (ignore ProductCode)
        Assert.Equal(5, keyValues[0]);     // LineId (L) - sorted first (alphabetical)
        Assert.Equal(1001, keyValues[1]);  // OrderId (O) - sorted second (alphabetical)
    }

    /// <summary>
    ///专项测试：复合主键按属性名字母序排序（核心逻辑验证）
    /// Special test: Composite keys are sorted alphabetically by property name
    /// </summary>
    [Fact]
    public void ExtractPrimaryKeyValues_CompositeKeyForSortingTest_SortedAlphabetically()
    {
        // Arrange
        var entity = new CompositeKeyForSortingTest 
        { 
            ZId = 999,  // Z开头（最后）
            AId = 111,  // A开头（最先）
            MId = 555,  // M开头（中间）
            NonIdField = "Test"
        };
        var entityType = typeof(CompositeKeyForSortingTest);

        // Act
        var keyValues = PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, entityType);

        // Assert
        Assert.Equal(3, keyValues.Length);
        Assert.Equal(111, keyValues[0]); // AId (A) - first
        Assert.Equal(555, keyValues[1]); // MId (M) - middle
        Assert.Equal(999, keyValues[2]); // ZId (Z) - last
    }
    #endregion

    #region Exception Scenario: Invalid Input
    /// <summary>
    /// Verifies ArgumentNullException is thrown when entity is null
    /// </summary>
    [Fact]
    public void ExtractPrimaryKeyValues_NullEntity_ThrowsArgumentNullException()
    {
        // Arrange
        SingleIntKeyEntity? nullEntity = null;
        var entityType = typeof(SingleIntKeyEntity);

        // Act + Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            PrimaryKeyHelper.ExtractPrimaryKeyValues(nullEntity!, entityType));
        Assert.Equal("entity", exception.ParamName);
        Assert.Contains("Entity cannot be null", exception.Message);
    }

    /// <summary>
    /// Verifies ArgumentNullException is thrown when entityType is null
    /// </summary>
    [Fact]
    public void ExtractPrimaryKeyValues_NullEntityType_ThrowsArgumentNullException()
    {
        // Arrange
        var entity = new SingleIntKeyEntity { Id = 123 };
        Type? nullType = null;

        // Act + Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, nullType!));
        Assert.Equal("entityType", exception.ParamName);
        Assert.Contains("Entity type cannot be null", exception.Message);
    }
    #endregion

    #region Exception Scenario: Missing/Invalid Primary Key
    /// <summary>
    /// Verifies MemoryDatabaseException is thrown when entity has no primary key
    /// </summary>
    [Fact]
    public void ExtractPrimaryKeyValues_NoPrimaryKeyEntity_ThrowsMemoryDatabaseException()
    {
        // Arrange
        var entity = new NoPrimaryKeyEntity { Name = "NoKey", Description = "Test" };
        var entityType = typeof(NoPrimaryKeyEntity);

        // Act + Assert
        var exception = Assert.Throws<MemoryDatabaseException>(() =>
            PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, entityType));
        Assert.Contains("No primary key defined for entity type: NoPrimaryKeyEntity", exception.Message);
    }

    /// <summary>
    /// Verifies MemoryDatabaseException is thrown when nullable primary key has null value
    /// </summary>
    [Fact]
    public void ExtractPrimaryKeyValues_NullableIdWithNullValue_ThrowsMemoryDatabaseException()
    {
        // Arrange
        var entity = new NullableIdWithValueEntity { Id = null, Name = "NullId" };
        var entityType = typeof(NullableIdWithValueEntity);

        // Act + Assert
        var exception = Assert.Throws<MemoryDatabaseException>(() =>
            PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, entityType));
        Assert.Contains("Primary key property Id cannot be null", exception.Message);
    }

    /// <summary>
    /// Verifies MemoryDatabaseException is thrown when composite key has null value
    /// </summary>
    [Fact]
    public void ExtractPrimaryKeyValues_CompositeKeyWithNullValue_ThrowsMemoryDatabaseException()
    {
        // Arrange
        var entity = new CompositeStringKeyEntity
        {
            UserId = null!, // Intentionally set to null
            RoleId = Guid.NewGuid(),
            IsActive = true
        };
        var entityType = typeof(CompositeStringKeyEntity);

        // Act + Assert
        var exception = Assert.Throws<MemoryDatabaseException>(() =>
            PrimaryKeyHelper.ExtractPrimaryKeyValues(entity, entityType));
        Assert.Contains("Primary key property UserId cannot be null", exception.Message);
    }
    #endregion
}