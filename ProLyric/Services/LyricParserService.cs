using System.IO;
using System.Text.RegularExpressions;
using ProLyric.Models;

namespace ProLyric.Services;

public enum SplitMode
{
    ByParagraph,
    ByLineCount
}

/// <summary>
/// Parses raw lyric text into LyricSlide objects.
/// Supports By Paragraph (blank lines) and By Line Count modes.
/// Detects section headers like [Verse 1], [Chorus], [Bridge].
/// </summary>
public class LyricParserService
{
    private static readonly Regex SectionHeaderRegex =
        new(@"^\s*(?:\[([^\]]+)\]|(?:(Verse\s*\d+|Chorus|Bridge|Ending|Intro|Tag)\s*:?))\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LeadingVerseNumberRegex =
        new(@"^\s*(\d+)[\.\s\-:]+(.*)$", RegexOptions.Compiled);

    /// <summary>
    /// Splits text into slides by blank-line-separated paragraphs.
    /// Section headers ([Verse 1] etc.) become the GroupName for the following slides.
    /// </summary>
    public IReadOnlyList<LyricSlide> ParseByParagraph(string text)
    {
        var slides = new List<LyricSlide>();
        if (string.IsNullOrWhiteSpace(text))
            return slides;

        string[] lines = SplitLines(text);
        var currentGroup = string.Empty;
        var currentBlock = new List<string>();
        int sortOrder = 0;

        void FlushBlock()
        {
            // Remove leading/trailing empty lines from block
            while (currentBlock.Count > 0 && string.IsNullOrWhiteSpace(currentBlock[0]))
                currentBlock.RemoveAt(0);
            while (currentBlock.Count > 0 && string.IsNullOrWhiteSpace(currentBlock[^1]))
                currentBlock.RemoveAt(currentBlock.Count - 1);

            if (currentBlock.Count == 0) return;

            string group = currentGroup;

            // If no explicit section header was provided, check if the block starts with a verse number
            if (string.IsNullOrWhiteSpace(group))
            {
                var numMatch = LeadingVerseNumberRegex.Match(currentBlock[0]);
                if (numMatch.Success)
                {
                    group = $"Verse {numMatch.Groups[1].Value}";
                }
                else if (sortOrder == 0)
                {
                    // In Indian/Malayalam and hymn tradition, unnumbered opening stanzas are typically the Chorus (Pallavi)
                    group = "Chorus";
                }
                else
                {
                    group = $"Verse {sortOrder + 1}";
                }
            }

            slides.Add(new LyricSlide
            {
                SortOrder = sortOrder++,
                GroupName = group,
                Lang1Text = string.Join("\n", currentBlock)
            });
            currentBlock.Clear();
        }

        foreach (string line in lines)
        {
            // Check for section header
            var match = SectionHeaderRegex.Match(line);
            if (match.Success)
            {
                FlushBlock();
                currentGroup = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Trim();
                continue;
            }

            // Blank line = paragraph separator
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushBlock();
            }
            else
            {
                currentBlock.Add(line);
            }
        }
        FlushBlock();

        return slides;
    }

    /// <summary>
    /// Splits text into slides, grouping every <paramref name="linesPerSlide"/> non-empty lines.
    /// Section headers are detected and set the GroupName for subsequent slides.
    /// </summary>
    public IReadOnlyList<LyricSlide> ParseByLineCount(string text, int linesPerSlide = 4)
    {
        if (linesPerSlide < 1) linesPerSlide = 1;

        var slides = new List<LyricSlide>();
        if (string.IsNullOrWhiteSpace(text))
            return slides;

        string[] lines = SplitLines(text);
        var currentGroup = string.Empty;
        var bucket = new List<string>();
        int sortOrder = 0;

        void FlushBucket()
        {
            if (bucket.Count == 0) return;
            slides.Add(new LyricSlide
            {
                SortOrder = sortOrder++,
                GroupName = currentGroup,
                Lang1Text = string.Join("\n", bucket)
            });
            bucket.Clear();
        }

        foreach (string line in lines)
        {
            // Check for section header
            var match = SectionHeaderRegex.Match(line);
            if (match.Success)
            {
                FlushBucket();
                currentGroup = match.Groups[1].Value.Trim();
                continue;
            }

            // Skip blank lines in line-count mode
            if (string.IsNullOrWhiteSpace(line))
                continue;

            bucket.Add(line);
            if (bucket.Count >= linesPerSlide)
                FlushBucket();
        }
        FlushBucket();

        return slides;
    }

    /// <summary>
    /// Resolves the song title: uses userTitle if non-empty, otherwise falls back
    /// to the first non-empty line of the first slide's Lang1Text, sanitized for filenames.
    /// </summary>
    public static string ResolveSongTitle(string userTitle, IEnumerable<LyricSlide> slides)
    {
        if (!string.IsNullOrWhiteSpace(userTitle))
            return userTitle.Trim();

        foreach (var slide in slides)
        {
            if (string.IsNullOrWhiteSpace(slide.Lang1Text)) continue;
            string firstLine = slide.Lang1Text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))
                ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(firstLine))
                return SanitizeFileName(firstLine.Trim());
        }

        return "Untitled Song";
    }

    // -----------------------------------------------------------------------
    //  Private helpers
    // -----------------------------------------------------------------------

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static string SanitizeFileName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (char c in name)
        {
            if (Array.IndexOf(invalid, c) >= 0)
                sb.Append('_');
            else
                sb.Append(c);
        }
        return sb.ToString().Trim('_').Trim();
    }
}
