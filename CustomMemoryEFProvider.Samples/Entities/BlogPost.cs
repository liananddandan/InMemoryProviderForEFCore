namespace CustomEFCoreProvider.Samples.Entities;

public class BlogPost
{
    public int Id { get; set; }
    public string Title { get; set; } = "";

    public int BlogId { get; set; }
    public Blog? Blog { get; set; }
    
    public List<PostComment>? Comments { get; set; }
}