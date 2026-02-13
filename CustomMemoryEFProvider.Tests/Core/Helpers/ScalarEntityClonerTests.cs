using CustomMemoryEFProvider.Core.Diagnostics;
using CustomMemoryEFProvider.Core.Helpers;
using CustomMemoryEFProvider.Core.Implementations;
using Xunit;

namespace CustomMemoryEFProvider.Tests.Core.Helpers;

public class ScalarEntityClonerTests
{
    // -------- Test entities --------

    private class Nav
    {
        public string? Name { get; set; }
    }

    private class ScalarEntity
    {
        // scalar
        public int Id { get; set; }
        public string? Name { get; set; }
        public decimal Price { get; set; }
        public Guid RowId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTimeOffset CreatedAtOffset { get; set; }
        public TimeSpan Duration { get; set; }

        // nullable scalar
        public int? OptionalInt { get; set; }

        // non-scalar: reference navigation
        public Nav? Navigation { get; set; }

        // non-scalar: collection
        public List<int> Tags { get; set; } = new();
    }

    private class NoParameterlessCtor
    {
        public NoParameterlessCtor(int id) { Id = id; }
        public int Id { get; set; }
        public string? Name { get; set; }
    }

    // -------- Tests --------

    [Fact]
    public void GetScalarProps_Should_Include_Only_Scalar_Writable_Public_Instance_Props()
    {
        var props = ScalarEntityCloner.GetScalarProps(typeof(ScalarEntity));
        var names = props.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        // scalar should exist
        Assert.Contains(nameof(ScalarEntity.Id), names);
        Assert.Contains(nameof(ScalarEntity.Name), names);
        Assert.Contains(nameof(ScalarEntity.Price), names);
        Assert.Contains(nameof(ScalarEntity.RowId), names);
        Assert.Contains(nameof(ScalarEntity.CreatedAt), names);
        Assert.Contains(nameof(ScalarEntity.CreatedAtOffset), names);
        Assert.Contains(nameof(ScalarEntity.Duration), names);
        Assert.Contains(nameof(ScalarEntity.OptionalInt), names);

        // non-scalar should NOT exist
        Assert.DoesNotContain(nameof(ScalarEntity.Navigation), names);
        Assert.DoesNotContain(nameof(ScalarEntity.Tags), names);
    }

    [Fact]
    public void CloneScalar_Should_Copy_All_Scalar_Props_And_Skip_NonScalar()
    {
        var src = new ScalarEntity
        {
            Id = 7,
            Name = "Laptop",
            Price = 99.5m,
            RowId = Guid.NewGuid(),
            CreatedAt = new DateTime(2026, 2, 13, 12, 0, 0, DateTimeKind.Utc),
            CreatedAtOffset = new DateTimeOffset(2026, 2, 13, 12, 0, 0, TimeSpan.Zero),
            Duration = TimeSpan.FromMinutes(5),
            OptionalInt = 123,
            Navigation = new Nav { Name = "NAV" },
            Tags = new List<int> { 1, 2, 3 }
        };

        var clone = ScalarEntityCloner.CloneScalar(src);

        // new instance
        Assert.NotSame(src, clone);

        // scalar copied
        Assert.Equal(src.Id, clone.Id);
        Assert.Equal(src.Name, clone.Name);
        Assert.Equal(src.Price, clone.Price);
        Assert.Equal(src.RowId, clone.RowId);
        Assert.Equal(src.CreatedAt, clone.CreatedAt);
        Assert.Equal(src.CreatedAtOffset, clone.CreatedAtOffset);
        Assert.Equal(src.Duration, clone.Duration);
        Assert.Equal(src.OptionalInt, clone.OptionalInt);

        // non-scalar should NOT be copied (keeps default/null)
        Assert.Null(clone.Navigation);
        Assert.Empty(clone.Tags);
    }

    [Fact]
    public void ExtractSnapshot_Should_Contain_Only_Scalar_Values()
    {
        var src = new ScalarEntity
        {
            Id = 1,
            Name = "Phone",
            Price = 1999m,
            OptionalInt = null,
            Navigation = new Nav { Name = "nav" },
            Tags = new List<int> { 9 }
        };

        var snap = ScalarEntityCloner.ExtractSnapshot(src);

        Assert.NotNull(snap.ValuesByName);

        // scalar keys present
        Assert.True(snap.ValuesByName.ContainsKey(nameof(ScalarEntity.Id)));
        Assert.True(snap.ValuesByName.ContainsKey(nameof(ScalarEntity.Name)));
        Assert.True(snap.ValuesByName.ContainsKey(nameof(ScalarEntity.Price)));
        Assert.True(snap.ValuesByName.ContainsKey(nameof(ScalarEntity.OptionalInt)));

        // values correct
        Assert.Equal(1, snap.ValuesByName[nameof(ScalarEntity.Id)]);
        Assert.Equal("Phone", snap.ValuesByName[nameof(ScalarEntity.Name)]);
        Assert.Equal(1999m, snap.ValuesByName[nameof(ScalarEntity.Price)]);
        Assert.Null(snap.ValuesByName[nameof(ScalarEntity.OptionalInt)]);

        // non-scalar keys not present
        Assert.False(snap.ValuesByName.ContainsKey(nameof(ScalarEntity.Navigation)));
        Assert.False(snap.ValuesByName.ContainsKey(nameof(ScalarEntity.Tags)));
    }

    [Fact]
    public void MaterializeFromSnapshot_Should_Create_Object_And_Set_Scalars()
    {
        ProviderDiagnostics.MaterializeCalled = 0;

        var snap = new ScalarSnapshot
        {
            ValuesByName = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [nameof(ScalarEntity.Id)] = 42,
                [nameof(ScalarEntity.Name)] = "Tablet",
                [nameof(ScalarEntity.Price)] = 2999m,
                [nameof(ScalarEntity.OptionalInt)] = 5
            }
        };

        var obj = ScalarEntityCloner.MaterializeFromSnapshot<ScalarEntity>(snap);

        Assert.Equal(1, ProviderDiagnostics.MaterializeCalled);

        Assert.Equal(42, obj.Id);
        Assert.Equal("Tablet", obj.Name);
        Assert.Equal(2999m, obj.Price);
        Assert.Equal(5, obj.OptionalInt);

        // non-scalar remain default
        Assert.Null(obj.Navigation);
        Assert.Empty(obj.Tags);
    }

    [Fact]
    public void MaterializeFromSnapshot_Should_Keep_Default_When_Field_Missing()
    {
        var snap = new ScalarSnapshot
        {
            ValuesByName = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [nameof(ScalarEntity.Id)] = 9
                // Name/Price not provided
            }
        };

        var obj = ScalarEntityCloner.MaterializeFromSnapshot<ScalarEntity>(snap);

        Assert.Equal(9, obj.Id);
        Assert.Null(obj.Name);
        Assert.Equal(0m, obj.Price); // default decimal
    }

    [Fact]
    public void MaterializeFromSnapshot_Should_Convert_Common_Types_For_Nullable()
    {
        // OptionalInt is int? but snapshot gives boxed int (OK) or string convertible.
        var snap = new ScalarSnapshot
        {
            ValuesByName = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [nameof(ScalarEntity.OptionalInt)] = "123"
            }
        };

        var obj = ScalarEntityCloner.MaterializeFromSnapshot<ScalarEntity>(snap);

        Assert.Equal(123, obj.OptionalInt);
    }

    [Fact]
    public void CloneScalar_When_Source_Has_No_ParameterlessCtor_Should_Throw()
    {
        var src = new NoParameterlessCtor(1) { Name = "X" };

        // Activator.CreateInstance(type)! will fail
        Assert.ThrowsAny<Exception>(() => ScalarEntityCloner.CloneScalar(src));
    }
}