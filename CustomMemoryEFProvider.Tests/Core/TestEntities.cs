namespace CustomMemoryEFProvider.Tests.Core;

#region single primary key entity（Id）
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
}
#endregion

#region composite primary key entity（OrderId + ProductId）
public class OrderItem
{
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}
#endregion

#region non-key primary entity（for exception scene）
public class InvalidEntity
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
#endregion

#region nullable primary key（for exception scene）
public class NullKeyEntity
{
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
#endregion