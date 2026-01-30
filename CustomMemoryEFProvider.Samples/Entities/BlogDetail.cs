namespace CustomEFCoreProvider.Samples.Entities;

public class BlogDetail
{
    public int Id { get; set; }
    public string Description { get; set; } = "";
    public int BlogId { get; set; }
    public Blog? Blog { get; set; }
}