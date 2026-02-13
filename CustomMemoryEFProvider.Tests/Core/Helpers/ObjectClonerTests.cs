using System.Text.Json;
using CustomMemoryEFProvider.Core.Helpers;
using Xunit;

namespace CustomMemoryEFProvider.Tests.Core.Helpers;

public class ObjectClonerTests
{
    // -------- Test entities --------

    private class Child
    {
        public int Score { get; set; }
    }

    private class Parent
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public Child? Child { get; set; }
        public List<int>? Tags { get; set; }
    }

    private class BaseEntity
    {
        public int Id { get; set; }
    }

    private class DerivedEntity : BaseEntity
    {
        public string? Extra { get; set; }
    }

    private class Node
    {
        public string? Name { get; set; }
        public Node? Next { get; set; }
    }

    // -------- Tests --------

    [Fact]
    public void DeepClone_Generic_Should_Create_New_Instance_With_Same_Values()
    {
        var src = new Parent
        {
            Id = 1,
            Name = "A",
            Child = new Child { Score = 99 },
            Tags = new List<int> { 1, 2, 3 }
        };

        var clone = ObjectCloner.DeepClone(src);

        Assert.NotNull(clone);
        Assert.NotSame(src, clone);

        Assert.Equal(src.Id, clone.Id);
        Assert.Equal(src.Name, clone.Name);

        // deep copy for nested object
        Assert.NotNull(clone.Child);
        Assert.NotSame(src.Child, clone.Child);
        Assert.Equal(src.Child!.Score, clone.Child!.Score);

        // deep copy for list
        Assert.NotNull(clone.Tags);
        Assert.NotSame(src.Tags, clone.Tags);
        Assert.Equal(src.Tags!, clone.Tags!);
    }

    [Fact]
    public void DeepClone_Generic_Should_Not_Affect_Source_When_Modifying_Clone()
    {
        var src = new Parent
        {
            Id = 1,
            Name = "A",
            Child = new Child { Score = 10 },
            Tags = new List<int> { 1 }
        };

        var clone = ObjectCloner.DeepClone(src);

        // modify clone
        clone.Name = "B";
        clone.Child!.Score = 20;
        clone.Tags!.Add(2);

        // source unchanged
        Assert.Equal("A", src.Name);
        Assert.Equal(10, src.Child!.Score);
        Assert.Single(src.Tags!);
        Assert.Equal(new List<int> { 1 }, src.Tags!);
    }

    [Fact]
    public void DeepClone_Object_RuntimeType_Should_Preserve_Derived_Type_Properties()
    {
        BaseEntity srcAsBase = new DerivedEntity
        {
            Id = 7,
            Extra = "hello"
        };

        var cloneObj = ObjectCloner.DeepClone(srcAsBase, srcAsBase.GetType());

        Assert.NotNull(cloneObj);
        Assert.IsType<DerivedEntity>(cloneObj);

        var clone = (DerivedEntity)cloneObj;
        Assert.Equal(7, clone.Id);
        Assert.Equal("hello", clone.Extra);
    }

    [Fact]
    public void DeepClone_Generic_When_Source_Is_Null_Should_Null()
    {
        Parent? src = null;

        var clone = ObjectCloner.DeepClone(src!);

        Assert.Null(clone);
    }


    [Fact]
    public void DeepClone_Object_When_Source_Is_Null_Should_Return_Null()
    {
        object? src = null;

        var clone = ObjectCloner.DeepClone(src!, typeof(object));

        Assert.Null(clone);
    }

    [Fact]
    public void DeepClone_With_Cycle_Should_Throw_By_Default_SystemTextJson_Settings()
    {
        var a = new Node { Name = "A" };
        a.Next = a; // cycle

        // System.Text.Json 默认不支持循环引用，通常抛 JsonException
        Assert.ThrowsAny<JsonException>(() => ObjectCloner.DeepClone(a));
    }
}