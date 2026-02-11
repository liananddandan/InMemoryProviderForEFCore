namespace CustomEFCoreProvider.Samples.Entities;

public class BlogNote
{
    public int Id { get; set; }
    public int BlogId { get; set; }
    public string Text { get; set; } = "";
    public Blog? Blog { get; set; }
}