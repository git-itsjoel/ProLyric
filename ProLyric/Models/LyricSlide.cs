namespace ProLyric.Models;

public class LyricSlide
{
    public int Id { get; set; }
    public int SortOrder { get; set; }
    public string GroupName { get; set; } = string.Empty;
    public string Lang1Text { get; set; } = string.Empty;
    public string Lang2Text { get; set; } = string.Empty;
}
