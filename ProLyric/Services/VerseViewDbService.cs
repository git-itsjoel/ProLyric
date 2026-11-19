using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using ProLyric.Models;

namespace ProLyric.Services;

public enum VerseViewLanguageChoice
{
    Both,
    PrimaryOnly,
    SecondaryOnly
}

public record VerseViewSongSummary(
    string Id,
    string Title,
    string? SecondaryTitle = null,
    string? Category = null,
    string? SongNumber = null
)
{
    public string DisplayTitle
    {
        get
        {
            string numPrefix = !string.IsNullOrWhiteSpace(SongNumber) ? $"#{SongNumber}  " : "";
            return !string.IsNullOrWhiteSpace(SecondaryTitle)
                ? $"{numPrefix}{Title} — {SecondaryTitle}"
                : $"{numPrefix}{Title}";
        }
    }
}

public record VerseViewSongContent(
    string SongId,
    string Title,
    string PrimaryLyrics,
    string SecondaryLyrics
);

/// <summary>
/// Loads VerseVIEW song data from either a SQLite .db or .xml file.
/// For SQLite, dynamically inspects the schema to find the songs table and columns.
/// </summary>
public class VerseViewDbService : IDisposable
{
    private readonly string _dbPath;
    private SqliteConnection? _conn;
    private bool _isSqlite;

    // Discovered column names (SQLite mode)
    private string? _tableName;
    private string? _idColumn;
    private string? _titleColumn;
    private string? _title2Column;
    private string? _categoryColumn;
    private string? _songNumberColumn;
    private string? _primaryColumn;
    private string? _secondaryColumn;

    // XML cache (XML mode)
    private XDocument? _xmlDoc;

    public VerseViewDbService(string dbPath)
    {
        _dbPath = dbPath;
    }

    /// <summary>Opens the DB file and inspects the schema.</summary>
    public void Open()
    {
        if (!File.Exists(_dbPath))
            throw new FileNotFoundException($"VerseVIEW DB not found: {_dbPath}");

        string ext = Path.GetExtension(_dbPath).ToLowerInvariant();

        if (ext == ".xml")
        {
            _isSqlite = false;
            _xmlDoc = XDocument.Load(_dbPath);
        }
        else // .db or any SQLite
        {
            _isSqlite = true;
            _conn = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
            _conn.Open();
            InspectSchema();
        }
    }

    /// <summary>Searches songs by title.</summary>
    public IReadOnlyList<VerseViewSongSummary> SearchSongs(string query)
    {
        return _isSqlite ? SearchSqlite(query) : SearchXml(query);
    }

    /// <summary>Loads a song's content by its ID.</summary>
    public VerseViewSongContent? LoadSong(string songId, VerseViewLanguageChoice choice = VerseViewLanguageChoice.Both)
    {
        return _isSqlite ? LoadSqlite(songId, choice) : LoadXml(songId, choice);
    }

    // -----------------------------------------------------------------------
    //  SQLite: schema inspection
    // -----------------------------------------------------------------------

    private void InspectSchema()
    {
        // Find candidate tables via sqlite_master
        var tables = GetTableNames();

        // Heuristic: prefer a table whose name contains "song" or "hymn" (case-insensitive)
        _tableName = tables.FirstOrDefault(t =>
            t.Contains("song", StringComparison.OrdinalIgnoreCase) ||
            t.Contains("hymn", StringComparison.OrdinalIgnoreCase))
            ?? tables.FirstOrDefault()
            ?? throw new InvalidOperationException("No tables found in VerseVIEW DB.");

        var columns = GetColumnNames(_tableName);

        // Find ID column: prefer "id", "songid", "hymn_id", "uid", "pk"
        _idColumn = FindColumn(columns, ["id", "songid", "hymn_id", "uid", "pk"]) ?? columns[0];

        // Find title column
        _titleColumn = FindColumn(columns, ["title", "name", "songtitle", "hymntitle"]) ?? columns[1];

        // Find secondary title column (e.g. Malayalam script title or alternate title)
        _title2Column = FindColumn(columns, ["title2", "name2", "alt_title", "secondary_title"]);

        // Find category/language column
        _categoryColumn = FindColumn(columns, ["cat", "category", "book", "language"]);

        // Find song number column
        _songNumberColumn = FindColumn(columns, [
            "subcat", "songnumber", "song_number", "songno", "song_no",
            "number", "num", "hymnnumber", "hymn_number", "hymnno", "hymn_no", "track"
        ]);

        // Find primary lyrics column
        _primaryColumn = FindColumn(columns, [
            "lyrics", "content", "text", "words", "body",
            "english", "primary", "verse1", "lyric1"
        ]) ?? null;

        // Find secondary/transliteration column
        _secondaryColumn = FindColumn(columns, [
            "transliteration", "manglish", "secondary", "malayalam",
            "lyrics2", "content2", "altlyrics"
        ]) ?? null;
    }

    private List<string> GetTableNames()
    {
        var tables = new List<string>();
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
        using var r = cmd.ExecuteReader();
        while (r.Read()) tables.Add(r.GetString(0));
        return tables;
    }

    private List<string> GetColumnNames(string table)
    {
        var cols = new List<string>();
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\");";
        using var r = cmd.ExecuteReader();
        while (r.Read()) cols.Add(r.GetString(1)); // column "name" is at index 1
        return cols;
    }

    private static string? FindColumn(List<string> columns, string[] candidates)
    {
        foreach (var candidate in candidates)
            foreach (var col in columns)
                if (col.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                    return col;
        return null;
    }

    // -----------------------------------------------------------------------
    //  SQLite: search and load
    // -----------------------------------------------------------------------

    private IReadOnlyList<VerseViewSongSummary> SearchSqlite(string query)
    {
        var results = new List<VerseViewSongSummary>();
        using var cmd = _conn!.CreateCommand();

        string selectCols = $"\"{_idColumn}\", \"{_titleColumn}\"";
        if (_title2Column is not null)
            selectCols += $", \"{_title2Column}\"";
        if (_categoryColumn is not null)
            selectCols += $", \"{_categoryColumn}\"";
        if (_songNumberColumn is not null && !_songNumberColumn.Equals(_idColumn, StringComparison.OrdinalIgnoreCase))
            selectCols += $", \"{_songNumberColumn}\"";

        string whereClause;
        string orderClause;

        string cleanQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            whereClause = "1=1";
            orderClause = $"\"{_titleColumn}\"";
        }
        else
        {
            var match = Regex.Match(cleanQuery, @"^(?:song|hymn|no\.?|number)?\s*#?\s*(\d+)$", RegexOptions.IgnoreCase);
            string? exactNum = match.Success ? match.Groups[1].Value : null;

            var conditions = new List<string> { $"\"{_titleColumn}\" LIKE $q" };
            cmd.Parameters.AddWithValue("$q", $"%{cleanQuery}%");

            if (_title2Column is not null)
                conditions.Add($"\"{_title2Column}\" LIKE $q");
            if (_categoryColumn is not null)
                conditions.Add($"\"{_categoryColumn}\" LIKE $q");

            if (exactNum is not null)
            {
                cmd.Parameters.AddWithValue("$numExact", exactNum);
                cmd.Parameters.AddWithValue("$numLike", $"{exactNum}%");
                cmd.Parameters.AddWithValue("$prefixQuery", $"{cleanQuery}%");

                var numConditions = new List<string>();
                if (_songNumberColumn is not null)
                {
                    numConditions.Add($"\"{_songNumberColumn}\" = $numExact");
                    numConditions.Add($"\"{_songNumberColumn}\" LIKE $numLike");
                }
                numConditions.Add($"\"{_idColumn}\" = $numExact");
                conditions.Add($"({string.Join(" OR ", numConditions)})");

                if (_songNumberColumn is not null)
                {
                    orderClause = $"""
                        CASE 
                            WHEN "{_songNumberColumn}" = $numExact THEN 0
                            WHEN "{_idColumn}" = $numExact THEN 1
                            WHEN "{_songNumberColumn}" LIKE $numLike THEN 2
                            WHEN "{_titleColumn}" LIKE $prefixQuery THEN 3
                            ELSE 4
                        END,
                        LENGTH("{_songNumberColumn}") ASC,
                        "{_titleColumn}"
                        """;
                }
                else
                {
                    orderClause = $"""
                        CASE 
                            WHEN "{_idColumn}" = $numExact THEN 0
                            WHEN "{_titleColumn}" LIKE $prefixQuery THEN 1
                            ELSE 2
                        END,
                        "{_titleColumn}"
                        """;
                }
            }
            else
            {
                cmd.Parameters.AddWithValue("$prefixQuery", $"{cleanQuery}%");
                if (_songNumberColumn is not null)
                {
                    conditions.Add($"\"{_songNumberColumn}\" LIKE $q");
                }

                orderClause = $"""
                    CASE 
                        WHEN "{_titleColumn}" LIKE $prefixQuery THEN 0
                        {(_title2Column is not null ? $"WHEN \"{_title2Column}\" LIKE $prefixQuery THEN 1" : "")}
                        ELSE 2
                    END,
                    "{_titleColumn}"
                    """;
            }

            whereClause = string.Join(" OR ", conditions);
        }

        cmd.CommandText = $"""
            SELECT {selectCols}
            FROM "{_tableName}"
            WHERE {whereClause}
            ORDER BY {orderClause}
            LIMIT 500;
            """;

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string id = r.GetValue(0).ToString()!;
            string title = r.IsDBNull(1) ? "" : r.GetString(1);
            int nextCol = 2;
            string? title2 = _title2Column is not null && !r.IsDBNull(nextCol) ? r.GetString(nextCol++) : null;
            if (title2 == "null") title2 = null;

            string? cat = _categoryColumn is not null && !r.IsDBNull(nextCol) ? r.GetString(nextCol++) : null;
            if (cat == "null") cat = null;

            string? songNum = null;
            if (_songNumberColumn is not null)
            {
                if (_songNumberColumn.Equals(_idColumn, StringComparison.OrdinalIgnoreCase))
                {
                    songNum = id;
                }
                else if (!r.IsDBNull(nextCol))
                {
                    songNum = r.GetValue(nextCol++).ToString();
                }
            }
            if (songNum == "null" || string.IsNullOrWhiteSpace(songNum)) songNum = null;

            results.Add(new VerseViewSongSummary(id, title, title2, cat, songNum));
        }
        return results;
    }

    private VerseViewSongContent? LoadSqlite(string songId, VerseViewLanguageChoice choice)
    {
        var selectCols = BuildSelectColumns(choice);
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = $"""
            SELECT {selectCols}
            FROM "{_tableName}"
            WHERE "{_idColumn}" = $id;
            """;
        cmd.Parameters.AddWithValue("$id", songId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        return BuildContent(songId, r, choice);
    }

    private string BuildSelectColumns(VerseViewLanguageChoice choice)
    {
        var cols = new List<string> { $"\"{_titleColumn}\"" };
        if (choice != VerseViewLanguageChoice.SecondaryOnly && _primaryColumn is not null)
            cols.Add($"\"{_primaryColumn}\"");
        if (choice != VerseViewLanguageChoice.PrimaryOnly && _secondaryColumn is not null)
            cols.Add($"\"{_secondaryColumn}\"");
        return string.Join(", ", cols);
    }

    private VerseViewSongContent BuildContent(string songId, SqliteDataReader r, VerseViewLanguageChoice choice)
    {
        int col = 0;
        string title    = r.IsDBNull(col) ? "" : r.GetString(col++);
        string primary  = "";
        string secondary = "";

        if (choice != VerseViewLanguageChoice.SecondaryOnly && _primaryColumn is not null)
            primary = r.IsDBNull(col) ? "" : NormalizeLyrics(r.GetString(col++));
        if (choice != VerseViewLanguageChoice.PrimaryOnly && _secondaryColumn is not null)
            secondary = r.IsDBNull(col) ? "" : NormalizeLyrics(r.GetString(col++));

        return new VerseViewSongContent(songId, title, primary, secondary);
    }

    private static string NormalizeLyrics(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Replace VerseVIEW <slide> or <SLIDE> tags with paragraph breaks (\n\n)
        text = Regex.Replace(text, @"<\s*slide\s*>", "\n\n", RegexOptions.IgnoreCase);

        // Replace <br>, <br/>, <BR> with line breaks (\n)
        text = Regex.Replace(text, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);

        return text.Replace("\\r\\n", "\n")
                   .Replace("\\n", "\n")
                   .Replace("\r\n", "\n")
                   .Replace('\r', '\n');
    }

    // -----------------------------------------------------------------------
    //  XML mode
    // -----------------------------------------------------------------------

    private IReadOnlyList<VerseViewSongSummary> SearchXml(string query)
    {
        var results = new List<VerseViewSongSummary>();
        if (_xmlDoc is null) return results;

        string cleanQuery = query.Trim();
        var match = Regex.Match(cleanQuery, @"^(?:song|hymn|no\.?|number)?\s*#?\s*(\d+)$", RegexOptions.IgnoreCase);
        string? exactNum = match.Success ? match.Groups[1].Value : null;

        foreach (var el in _xmlDoc.Descendants())
        {
            string? id = el.Attribute("id")?.Value ?? el.Element("id")?.Value;
            string? title = el.Attribute("title")?.Value ?? el.Element("title")?.Value
                         ?? el.Attribute("name")?.Value ?? el.Element("name")?.Value;
            if (id is null || title is null) continue;

            string? title2 = el.Attribute("title2")?.Value ?? el.Element("title2")?.Value;
            if (title2 == "null") title2 = null;

            string? cat = el.Attribute("category")?.Value ?? el.Element("category")?.Value
                       ?? el.Attribute("cat")?.Value ?? el.Element("cat")?.Value;
            if (cat == "null") cat = null;

            string? num = el.Attribute("number")?.Value ?? el.Element("number")?.Value
                       ?? el.Attribute("songnumber")?.Value ?? el.Element("songnumber")?.Value
                       ?? el.Attribute("subcat")?.Value ?? el.Element("subcat")?.Value
                       ?? el.Attribute("no")?.Value ?? el.Element("no")?.Value;
            if (num == "null" || string.IsNullOrWhiteSpace(num)) num = null;

            if (num is null && int.TryParse(id, out _))
            {
                num = id;
            }

            bool matches = false;
            if (string.IsNullOrWhiteSpace(cleanQuery))
            {
                matches = true;
            }
            else
            {
                if (title.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase)) matches = true;
                else if (title2 is not null && title2.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase)) matches = true;
                else if (cat is not null && cat.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase)) matches = true;
                else if (exactNum is not null)
                {
                    if (num == exactNum || id == exactNum || (num is not null && num.StartsWith(exactNum)))
                        matches = true;
                }
                else if (num is not null && num.Contains(cleanQuery, StringComparison.OrdinalIgnoreCase))
                {
                    matches = true;
                }
            }

            if (matches)
            {
                results.Add(new VerseViewSongSummary(id, title, title2, cat, num));
            }
        }

        if (!string.IsNullOrWhiteSpace(exactNum))
        {
            return results.OrderBy(s => s.SongNumber == exactNum ? 0 : (s.Id == exactNum ? 1 : (s.SongNumber?.StartsWith(exactNum) == true ? 2 : 3)))
                          .ThenBy(s => s.SongNumber?.Length ?? 999)
                          .ThenBy(s => s.Title)
                          .ToList();
        }

        return results.OrderBy(s => s.Title).ToList();
    }

    private VerseViewSongContent? LoadXml(string songId, VerseViewLanguageChoice choice)
    {
        if (_xmlDoc is null) return null;

        foreach (var el in _xmlDoc.Descendants())
        {
            string? id = el.Attribute("id")?.Value ?? el.Element("id")?.Value;
            if (id != songId) continue;

            string title   = el.Attribute("title")?.Value ?? el.Element("title")?.Value ?? "";
            string primary = NormalizeLyrics(el.Element("lyrics")?.Value ?? el.Element("content")?.Value ?? "");
            string secondary = NormalizeLyrics(el.Element("transliteration")?.Value ?? el.Element("secondary")?.Value ?? "");

            return new VerseViewSongContent(songId, title, primary, secondary);
        }
        return null;
    }

    public void Dispose()
    {
        _conn?.Dispose();
    }
}
