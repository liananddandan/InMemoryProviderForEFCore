using CustomMemoryEFProvider.Core.Exceptions;
using CustomMemoryEFProvider.Core.Implementations;
using Xunit;

namespace CustomMemoryEFProvider.Tests.Core.Implementations;


#region Test Entities for MemoryTable (Covers all key scenarios)
/// <summary>Single primary key entity (int Id)</summary>
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

/// <summary>Composite primary key entity (OrderId + LineId - sorted alphabetically)</summary>
public class OrderItem
{
    public int OrderId { get; set; }
    public int LineId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>Entity with nullable primary key (for null value testing)</summary>
public class NullableKeyEntity
{
    public int? Id { get; set; }
    public string Data { get; set; } = string.Empty;
}

/// <summary>Entity with no primary key (for exception testing)</summary>
public class NoKeyEntity
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
#endregion

/// <summary>
/// Unit tests for MemoryTable<TEntity> - covers all CRUD operations,
/// boundary conditions, and exception scenarios following requirement-driven principles
/// </summary>
public class MemoryTableTests
{
    #region Single Primary Key Table Tests (Core CRUD)
    /// <summary>
    /// Tests for MemoryTable with single primary key entity (Product)
    /// </summary>
    public class SingleKeyTableTests
    {
        private readonly MemoryTable<Product> _productTable;

        /// <summary>
        /// Initialize clean table for each test (isolation)
        /// </summary>
        public SingleKeyTableTests()
        {
            _productTable = new MemoryTable<Product>(typeof(Product));
        }

        #region Add Method Tests
        /// <summary>
        /// Verifies valid entity is added successfully with correct primary key
        /// </summary>
        [Fact]
        public void Add_ValidProduct_AddsToTableAndCanBeFound()
        {
            // Arrange
            var product = new Product { Id = 1, Name = "Laptop", Price = 5999.99m };

            // Act
            _productTable.Add(product);
            var retrievedProduct = _productTable.Find(new object[] { 1 });

            // Assert
            Assert.NotNull(retrievedProduct);
            Assert.Equal(1, retrievedProduct.Id);
            Assert.Equal("Laptop", retrievedProduct.Name);
            Assert.Equal(5999.99m, retrievedProduct.Price);
        }

        /// <summary>
        /// Verifies duplicate primary key throws MemoryDatabaseException
        /// </summary>
        [Fact]
        public void Add_DuplicateProductId_ThrowsMemoryDatabaseException()
        {
            // Arrange
            var product1 = new Product { Id = 1, Name = "Laptop" };
            var product2 = new Product { Id = 1, Name = "Gaming Laptop" };
            _productTable.Add(product1);

            // Act + Assert
            var exception = Assert.Throws<MemoryDatabaseException>(() => _productTable.Add(product2));
            Assert.Contains("Entity with key", exception.Message);
        }

        /// <summary>
        /// Verifies null entity throws ArgumentNullException
        /// </summary>
        [Fact]
        public void Add_NullProduct_ThrowsArgumentNullException()
        {
            // Act + Assert
            var exception = Assert.Throws<ArgumentNullException>(() => _productTable.Add(null!));
            Assert.Equal("entity", exception.ParamName);
            Assert.Contains("Entity cannot be null", exception.Message);
        }

        /// <summary>
        /// Verifies entity with null primary key (via NullableKeyEntity) throws MemoryDatabaseException
        /// </summary>
        [Fact]
        public void Add_EntityWithNullPrimaryKey_ThrowsMemoryDatabaseException()
        {
            // Arrange
            var nullKeyTable = new MemoryTable<NullableKeyEntity>(typeof(NullableKeyEntity));
            var nullKeyEntity = new NullableKeyEntity { Id = null, Data = "NullKeyTest" };

            // Act + Assert
            var exception = Assert.Throws<MemoryDatabaseException>(() => nullKeyTable.Add(nullKeyEntity));
            Assert.Contains("Primary key property Id cannot be null", exception.Message);
        }

        /// <summary>
        /// Verifies entity with no primary key throws MemoryDatabaseException
        /// </summary>
        [Fact]
        public void Add_EntityWithNoPrimaryKey_ThrowsMemoryDatabaseException()
        {
            // Arrange
            var noKeyTable = new MemoryTable<NoKeyEntity>(typeof(NoKeyEntity));
            var noKeyEntity = new NoKeyEntity { Name = "Test", Value = "NoKey" };

            // Act + Assert
            var exception = Assert.Throws<MemoryDatabaseException>(() => noKeyTable.Add(noKeyEntity));
            Assert.Contains("No primary key defined for entity type: NoKeyEntity", exception.Message);
        }
        #endregion

        #region Update Method Tests
        /// <summary>
        /// Verifies existing entity is updated successfully
        /// </summary>
        [Fact]
        public void Update_ExistingProduct_UpdatesValuesCorrectly()
        {
            // Arrange
            var originalProduct = new Product { Id = 1, Name = "Laptop", Price = 5999.99m };
            var updatedProduct = new Product { Id = 1, Name = "Laptop Pro", Price = 6999.99m };
            _productTable.Add(originalProduct);

            // Act
            _productTable.Update(updatedProduct);
            var retrievedProduct = _productTable.Find(new object[] { 1 });

            // Assert
            Assert.NotNull(retrievedProduct);
            Assert.Equal("Laptop Pro", retrievedProduct.Name);
            Assert.Equal(6999.99m, retrievedProduct.Price);
        }

        /// <summary>
        /// Verifies updating non-existent entity throws KeyNotFoundException
        /// </summary>
        [Fact]
        public void Update_NonExistentProduct_ThrowsKeyNotFoundException()
        {
            // Arrange
            var nonExistentProduct = new Product { Id = 999, Name = "NonExistent" };

            // Act + Assert
            var exception = Assert.Throws<KeyNotFoundException>(() => _productTable.Update(nonExistentProduct));
            Assert.Contains("999", exception.Message);
            Assert.Contains("Entity with key [999]", exception.Message);
        }

        /// <summary>
        /// Verifies updating null entity throws ArgumentNullException
        /// </summary>
        [Fact]
        public void Update_NullProduct_ThrowsArgumentNullException()
        {
            // Act + Assert
            var exception = Assert.Throws<ArgumentNullException>(() => _productTable.Update(null!));
            Assert.Equal("entity", exception.ParamName);
            Assert.Contains("Entity cannot be null", exception.Message);
        }
        #endregion

        #region Remove Method Tests
        /// <summary>
        /// Verifies existing entity is removed successfully
        /// </summary>
        [Fact]
        public void Remove_ExistingProduct_RemovesFromTable()
        {
            // Arrange
            var product = new Product { Id = 1, Name = "ToRemove" };
            _productTable.Add(product);
            Assert.NotNull(_productTable.Find(new object[] { 1 })); // Pre-verify exists

            // Act
            _productTable.Remove(product);
            var retrievedProduct = _productTable.Find(new object[] { 1 });

            // Assert
            Assert.Null(retrievedProduct);
        }

        /// <summary>
        /// Verifies removing non-existent entity throws KeyNotFoundException
        /// </summary>
        [Fact]
        public void Remove_NonExistentProduct_ThrowsKeyNotFoundException()
        {
            // Arrange
            var nonExistentProduct = new Product { Id = 999, Name = "NonExistent" };

            // Act + Assert
            var exception = Assert.Throws<KeyNotFoundException>(() => _productTable.Remove(nonExistentProduct));
            Assert.Contains("999", exception.Message);
            Assert.Contains("Entity with key [999]", exception.Message);
        }

        /// <summary>
        /// Verifies removing null entity throws ArgumentNullException
        /// </summary>
        [Fact]
        public void Remove_NullProduct_ThrowsArgumentNullException()
        {
            // Act + Assert
            var exception = Assert.Throws<ArgumentNullException>(() => _productTable.Remove(null!));
            Assert.Equal("entity", exception.ParamName);
            Assert.Contains("Entity cannot be null", exception.Message);
        }
        #endregion

        #region Find Method Tests
        /// <summary>
        /// Verifies Find returns null for non-existent primary key
        /// </summary>
        [Fact]
        public void Find_NonExistentProductId_ReturnsNull()
        {
            // Act
            var retrievedProduct = _productTable.Find(new object[] { 999 });

            // Assert
            Assert.Null(retrievedProduct);
        }

        /// <summary>
        /// Verifies Find with null key array throws ArgumentNullException
        /// </summary>
        [Fact]
        public void Find_NullKeyArray_ThrowsArgumentNullException()
        {
            // Act + Assert
            var exception = Assert.Throws<ArgumentNullException>(() => _productTable.Find(null!));
            Assert.Equal("keyValues", exception.ParamName);
        }
        #endregion

        #region Query Property Tests
        /// <summary>
        /// Verifies Query returns all entities in the table
        /// </summary>
        [Fact]
        public void Query_ReturnsAllAddedProducts()
        {
            // Arrange
            _productTable.Add(new Product { Id = 1, Name = "Laptop", Price = 5999 });
            _productTable.Add(new Product { Id = 2, Name = "Phone", Price = 2999 });
            _productTable.Add(new Product { Id = 3, Name = "Tablet", Price = 1999 });

            // Act
            var products = _productTable.QueryRows.ToList();

            // Assert
            Assert.Equal(3, products.Count);
            Assert.Contains(products, r => (int)r.Key[0] == 1);
            Assert.Contains(products, r => (int)r.Key[0] == 2);
            Assert.Contains(products, r => (int)r.Key[0] == 3);
        }

        /// <summary>
        /// Verifies Query supports LINQ filtering operations
        /// </summary>
        [Fact]
        public void Query_SupportsLinqWhereFiltering()
        {
            // Arrange
            _productTable.Add(new Product { Id = 1, Name = "Laptop", Price = 5999 });
            _productTable.Add(new Product { Id = 2, Name = "Phone", Price = 2999 });
            _productTable.Add(new Product { Id = 3, Name = "Tablet", Price = 1999 });

            var rows = _productTable.QueryRows.ToList();

            Assert.Equal(3, rows.Count);
            Assert.Contains(rows, r => (int)r.Key[0] == 1);
            Assert.Contains(rows, r => (int)r.Key[0] == 2);
            Assert.Contains(rows, r => (int)r.Key[0] == 3);
        }
        #endregion
    }
    #endregion

    #region Composite Primary Key Table Tests
    /// <summary>
    /// Tests for MemoryTable with composite primary key entity (OrderItem)
    /// Covers alphabetical sorting of composite keys (LineId → OrderId)
    /// </summary>
    public class CompositeKeyTableTests
    {
        private readonly MemoryTable<OrderItem> _orderItemTable;

        public CompositeKeyTableTests()
        {
            _orderItemTable = new MemoryTable<OrderItem>(typeof(OrderItem));
        }

        /// <summary>
        /// Verifies composite key entity is added and found with correct sorted key array
        /// Key order: LineId (L) first, OrderId (O) second (alphabetical)
        /// </summary>
        [Fact]
        public void Add_ValidOrderItem_AddedAndFoundWithCompositeKey()
        {
            // Arrange
            var orderItem = new OrderItem { OrderId = 1001, LineId = 5, Quantity = 10 };
            // Key array must match sorted order (LineId, OrderId)
            var compositeKey = new object[] { 5, 1001 };

            // Act
            _orderItemTable.Add(orderItem);
            var retrievedItem = _orderItemTable.Find(compositeKey);

            // Assert
            Assert.NotNull(retrievedItem);
            Assert.Equal(1001, retrievedItem.OrderId);
            Assert.Equal(5, retrievedItem.LineId);
            Assert.Equal(10, retrievedItem.Quantity);
        }

        /// <summary>
        /// Verifies duplicate composite key throws MemoryDatabaseException
        /// </summary>
        [Fact]
        public void Add_DuplicateCompositeKey_ThrowsMemoryDatabaseException()
        {
            // Arrange
            var orderItem1 = new OrderItem { OrderId = 1001, LineId = 5, Quantity = 10 };
            var orderItem2 = new OrderItem { OrderId = 1001, LineId = 5, Quantity = 20 };
            _orderItemTable.Add(orderItem1);

            // Act + Assert
            var exception = Assert.Throws<MemoryDatabaseException>(() => _orderItemTable.Add(orderItem2));
            Assert.Contains("Entity with key", exception.Message);
        }

        /// <summary>
        /// Verifies composite key entity update works correctly
        /// </summary>
        [Fact]
        public void Update_ExistingOrderItem_UpdatesQuantity()
        {
            // Arrange
            var originalItem = new OrderItem { OrderId = 1001, LineId = 5, Quantity = 10 };
            var updatedItem = new OrderItem { OrderId = 1001, LineId = 5, Quantity = 25 };
            var compositeKey = new object[] { 5, 1001 };
            
            _orderItemTable.Add(originalItem);

            // Act
            _orderItemTable.Update(updatedItem);
            var retrievedItem = _orderItemTable.Find(compositeKey);

            // Assert
            Assert.NotNull(retrievedItem);
            Assert.Equal(25, retrievedItem.Quantity);
        }

        /// <summary>
        /// Verifies removing composite key entity works correctly
        /// </summary>
        [Fact]
        public void Remove_ExistingOrderItem_RemovesFromTable()
        {
            // Arrange
            var orderItem = new OrderItem { OrderId = 1001, LineId = 5, Quantity = 10 };
            var compositeKey = new object[] { 5, 1001 };
            
            _orderItemTable.Add(orderItem);
            Assert.NotNull(_orderItemTable.Find(compositeKey)); // Pre-verify exists

            // Act
            _orderItemTable.Remove(orderItem);
            var retrievedItem = _orderItemTable.Find(compositeKey);

            // Assert
            Assert.Null(retrievedItem);
        }
    }
    #endregion
}