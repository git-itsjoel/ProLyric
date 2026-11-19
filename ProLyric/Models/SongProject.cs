namespace ProLyric.Models;

public class SongProject
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public LanguageMode LanguageMode { get; set; } = LanguageMode.Single;
    public string TemplatePath { get; set; } = string.Empty;
    public string ExportedFileName { get; set; } = string.Empty;
    public string ExportedFilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public List<LyricSlide> Slides { get; set; } = [];
}
