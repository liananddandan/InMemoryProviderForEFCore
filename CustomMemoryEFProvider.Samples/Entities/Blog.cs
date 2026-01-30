namespace CustomEFCoreProvider.Samples.Entities;

public class Blog
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public BlogDetail? Detail { get; set; }
    
    public ICollection<BlogPost>? Posts { get; set; }
}