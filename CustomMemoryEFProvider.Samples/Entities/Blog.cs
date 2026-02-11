namespace CustomEFCoreProvider.Samples.Entities;

public class Blog
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public BlogDetail? Detail { get; set; }
    
    public ICollection<BlogPost>? Posts { get; set; } = new List<BlogPost>();
    
    public ICollection<BlogNote> Notes { get; set; } = new List<BlogNote>();

}