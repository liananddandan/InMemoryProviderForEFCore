namespace CustomEFCoreProvider.Samples.Entities;

public class PostComment
{
    public int Id { get; set; }
    public int BlogPostId { get; set; }
    public string Content { get; set; } = "";
    public BlogPost? Post { get; set; }
}