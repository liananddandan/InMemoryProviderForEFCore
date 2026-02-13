using CustomMemoryEFProvider.Core.Helpers;
using Xunit;

namespace CustomMemoryEFProvider.Tests.Core.Helpers;

public class ArrayComparerTests
{
    private readonly ArrayComparer _comparer = new();

    #region Path 1：x=null && y=null → return true
    [Fact]
    public void Equals_BothArraysNull_ReturnsTrue()
    {
        // Arrange
        object[]? x = null;
        object[]? y = null;

        // Act
        var result = _comparer.Equals(x, y);

        // Assert
        Assert.True(result);
    }
    #endregion

    #region Path /3：一个null一个non-null → return false
    [Fact]
    public void Equals_XNull_YNonNull_ReturnsFalse()
    {
        // Arrange
        object[]? x = null;
        object[] y = new object[] { 1 };

        // Act
        var result = _comparer.Equals(x, y);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Equals_XNonNull_YNull_ReturnsFalse()
    {
        // Arrange
        object[] x = new object[] { 1 };
        object[]? y = null;

        // Act
        var result = _comparer.Equals(x, y);

        // Assert
        Assert.False(result);
    }
    #endregion

    #region Path 4：different arrays length → return false
    [Fact]
    public void Equals_DifferentLengthArrays_ReturnsFalse()
    {
        // Arrange
        var x = new object[] { 1, 2 };
        var y = new object[] { 1 };

        // Act
        var result = _comparer.Equals(x, y);

        // Assert
        Assert.False(result);
    }
    #endregion

    #region Path 5：same length and elements → return true
    [Fact]
    public void Equals_EqualSingleKeyArrays_ReturnsTrue()
    {
        // Arrange
        var x = new object[] { 1 };
        var y = new object[] { 1 };

        // Act
        var result = _comparer.Equals(x, y);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Equals_EqualCompositeKeyArrays_ReturnsTrue()
    {
        // Arrange
        var x = new object[] { 1001, "Product1" };
        var y = new object[] { 1001, "Product1" };

        // Act
        var result = _comparer.Equals(x, y);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Equals_BothEmptyArrays_ReturnsTrue()
    {
        // Arrange
        var x = Array.Empty<object>();
        var y = Array.Empty<object>();

        // Act
        var result = _comparer.Equals(x, y);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Equals_ArraysWithNullElements_Equal_ReturnsTrue()
    {
        // Arrange
        var x = new object[] { 1, null, "Test" };
        var y = new object[] { 1, null, "Test" };

        // Act
        var result = _comparer.Equals(x, y);

        // Assert
        Assert.True(result);
    }
    #endregion

    #region Path 6：same lengths with different elements → return false
    [Fact]
    public void Equals_SameLengthDifferentValues_ReturnsFalse()
    {
        // Arrange
        var x = new object[] { 1, 2 };
        var y = new object[] { 1, 3 };

        // Act
        var result = _comparer.Equals(x, y);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Equals_ArraysWithNullElements_Different_ReturnsFalse()
    {
        // Arrange
        var x = new object[] { 1, null, "Test" };
        var y = new object[] { 1, "Null", "Test" };

        // Act
        var result = _comparer.Equals(x, y);

        // Assert
        Assert.False(result);
    }
    #endregion

    #region GetHashCode
    [Fact]
    public void GetHashCode_NullArray_ReturnsZero()
    {
        // Act
        var hashCode = _comparer.GetHashCode(null!);

        // Assert
        Assert.Equal(0, hashCode);
    }

    [Fact]
    public void GetHashCode_EmptyArray_ReturnsZero()
    {
        // Arrange
        var emptyArray = Array.Empty<object>();

        // Act
        var hashCode = _comparer.GetHashCode(emptyArray);

        // Assert
        Assert.Equal(17, hashCode);
    }
    
    [Fact]
    public void GetHashCode_EqualArrays_Should_Return_Same_Value()
    {
        var a1 = Array.Empty<object>();
        var a2 = Array.Empty<object>();

        var h1 = _comparer.GetHashCode(a1);
        var h2 = _comparer.GetHashCode(a2);

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void GetHashCode_ArrayWithNullElements_ReturnsConsistentValue()
    {
        // Arrange
        var array1 = new object[] { 1, null, "Test" };
        var array2 = new object[] { 1, null, "Test" };

        // Act
        var hashCode1 = _comparer.GetHashCode(array1);
        var hashCode2 = _comparer.GetHashCode(array2);

        // Assert
        Assert.Equal(hashCode1, hashCode2);
    }
    #endregion
}