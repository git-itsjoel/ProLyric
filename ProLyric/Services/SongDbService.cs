using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using ProLyric.Models;

namespace ProLyric.Services;

public enum SongDbLanguageChoice
{
    Both,
    PrimaryOnly,
    SecondaryOnly
}

public record SongDbSongSummary(
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

public record SongDbSongContent(
    string SongId,
    string Title,
    string PrimaryLyrics,
    string SecondaryLyrics
);

/// <summary>
/// Loads song database data from either a SQLite (.db, .sqlite) or .xml file.
/// For SQLite, dynamically inspects the schema to discover tables and columns.
/// </summary>
public class SongDbService : IDisposable
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

    public SongDbService(string dbPath)
    {
        _dbPath = dbPath;
    }

    /// <summary>Opens the DB file and inspects the schema.</summary>
    public void Open()
    {
        if (!File.Exists(_dbPath))
            throw new FileNotFoundException($"SongDB database not found: {_dbPath}");

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

    /// <summary>Searches songs by title, lyrics, or song number.</summary>
    public IReadOnlyList<SongDbSongSummary> SearchSongs(string query)
    {
        return _isSqlite ? SearchSqlite(query) : SearchXml(query);
    }

    /// <summary>Loads a song's content by its ID.</summary>
    public SongDbSongContent? LoadSong(string songId, SongDbLanguageChoice choice = SongDbLanguageChoice.Both)
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
            ?? throw new InvalidOperationException("No tables found in song database.");

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
        var list = new List<string>();
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(r.GetString(0));
        return list;
    }

    private List<string> GetColumnNames(string table)
    {
        var list = new List<string>();
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(r.GetString(1)); // column 1 is "name"
        return list;
    }

    private static string? FindColumn(List<string> columns, string[] candidates)
    {
        foreach (var c in candidates)
        {
            var match = columns.FirstOrDefault(col =>
                col.Equals(c, StringComparison.OrdinalIgnoreCase) ||
                col.Replace("_", "").Equals(c.Replace("_", ""), StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match;
        }
        return null;
    }

    // -----------------------------------------------------------------------
    //  SQLite: searching and loading
    // -----------------------------------------------------------------------

    private IReadOnlyList<SongDbSongSummary> SearchSqlite(string query)
    {
        if (_conn is null || _tableName is null || _idColumn is null || _titleColumn is null)
            return [];

        var results = new List<SongDbSongSummary>();
        using var cmd = _conn.CreateCommand();

        string selectCols = $"\"{_idColumn}\", \"{_titleColumn}\"";
        if (_title2Column is not null) selectCols += $", \"{_title2Column}\"";
        if (_categoryColumn is not null) selectCols += $", \"{_categoryColumn}\"";
        if (_songNumberColumn is not null) selectCols += $", \"{_songNumberColumn}\"";

        string cleanQuery = query.Trim();
        string? exactNum = null;
        var numMatch = Regex.Match(cleanQuery, @"^(?:#\s*|song\s*#?\s*|no\.?\s*#?\s*|hymn\s*#?\s*)?(\d+)$", RegexOptions.IgnoreCase);
        if (numMatch.Success)
        {
            exactNum = numMatch.Groups[1].Value;
        }

        if (string.IsNullOrWhiteSpace(cleanQuery))
        {
            cmd.CommandText = $"SELECT {selectCols} FROM \"{_tableName}\" ORDER BY \"{_titleColumn}\" LIMIT 5000";
        }
        else
        {
            var whereClauses = new List<string>();

            if (exactNum is not null)
            {
                if (_songNumberColumn is not null)
                {
                    whereClauses.Add($"CAST(\"{_songNumberColumn}\" AS TEXT) = @exactNum");
                    whereClauses.Add($"CAST(\"{_songNumberColumn}\" AS TEXT) LIKE @numPrefix");
                    whereClauses.Add($"CAST(\"{_idColumn}\" AS TEXT) = @exactNum");
                }
                else
                {
                    whereClauses.Add($"CAST(\"{_idColumn}\" AS TEXT) = @exactNum");
                    whereClauses.Add($"CAST(\"{_idColumn}\" AS TEXT) LIKE @numPrefix");
                }
                whereClauses.Add($"\"{_titleColumn}\" LIKE @q");
                if (_title2Column is not null)
                    whereClauses.Add($"\"{_title2Column}\" LIKE @q");
            }
            else
            {
                whereClauses.Add($"\"{_titleColumn}\" LIKE @q");

                if (_title2Column is not null)
                    whereClauses.Add($"\"{_title2Column}\" LIKE @q");

                if (_primaryColumn is not null)
                    whereClauses.Add($"\"{_primaryColumn}\" LIKE @q");

                if (_secondaryColumn is not null)
                    whereClauses.Add($"\"{_secondaryColumn}\" LIKE @q");

                if (_categoryColumn is not null)
                    whereClauses.Add($"\"{_categoryColumn}\" LIKE @q");

                if (_songNumberColumn is not null)
                    whereClauses.Add($"\"{_songNumberColumn}\" LIKE @q");
            }

            cmd.CommandText = $"SELECT {selectCols} FROM \"{_tableName}\" WHERE {string.Join(" OR ", whereClauses)}";

            if (exactNum is not null)
            {
                string orderIdExpr = $"CAST(\"{_idColumn}\" AS TEXT)";
                string orderNumExpr = _songNumberColumn is not null ? $"CAST(\"{_songNumberColumn}\" AS TEXT)" : orderIdExpr;

                cmd.CommandText += $@"
                    ORDER BY 
                        CASE 
                            WHEN {orderNumExpr} = @exactNum THEN 0
                            WHEN {orderIdExpr} = @exactNum THEN 1
                            WHEN {orderNumExpr} LIKE @numPrefix THEN 2
                            ELSE 3
                        END,
                        LENGTH({orderNumExpr}),
                        ""{_titleColumn}""
                    LIMIT 200";

                cmd.Parameters.AddWithValue("@exactNum", exactNum);
                cmd.Parameters.AddWithValue("@numPrefix", exactNum + "%");
            }
            else
            {
                cmd.CommandText += $" ORDER BY \"{_titleColumn}\" LIMIT 200";
            }

            cmd.Parameters.AddWithValue("@q", $"%{cleanQuery}%");
        }

        using var r = cmd.ExecuteReader();
        int ordId = r.GetOrdinal(_idColumn);
        int ordTitle = r.GetOrdinal(_titleColumn);
        int ordTitle2 = _title2Column is not null ? r.GetOrdinal(_title2Column) : -1;
        int ordCat = _categoryColumn is not null ? r.GetOrdinal(_categoryColumn) : -1;
        int ordNum = _songNumberColumn is not null ? r.GetOrdinal(_songNumberColumn) : -1;

        while (r.Read())
        {
            string id = r.GetValue(ordId)?.ToString() ?? "";
            string title = r.GetValue(ordTitle)?.ToString() ?? "";
            string? title2 = ordTitle2 >= 0 && !r.IsDBNull(ordTitle2) ? r.GetValue(ordTitle2)?.ToString() : null;
            string? cat = ordCat >= 0 && !r.IsDBNull(ordCat) ? r.GetValue(ordCat)?.ToString() : null;
            string? num = ordNum >= 0 && !r.IsDBNull(ordNum) ? r.GetValue(ordNum)?.ToString() : null;

            if (string.IsNullOrWhiteSpace(num) && int.TryParse(id, out _))
            {
                num = id;
            }

            results.Add(new SongDbSongSummary(id, title, title2, cat, num));
        }

        return results;
    }

    private SongDbSongContent? LoadSqlite(string songId, SongDbLanguageChoice choice)
    {
        if (_conn is null || _tableName is null || _idColumn is null || _titleColumn is null)
            return null;

        using var cmd = _conn.CreateCommand();
        string cols = BuildSelectColumns(choice);
        cmd.CommandText = $"SELECT {cols} FROM \"{_tableName}\" WHERE \"{_idColumn}\" = @id LIMIT 1";
        cmd.Parameters.AddWithValue("@id", songId);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        return BuildContentFromReader(r, songId, choice);
    }

    private string BuildSelectColumns(SongDbLanguageChoice choice)
    {
        var cols = new List<string> { $"\"{_titleColumn}\"" };
        if (choice != SongDbLanguageChoice.SecondaryOnly && _primaryColumn is not null)
            cols.Add($"\"{_primaryColumn}\"");
        if (choice != SongDbLanguageChoice.PrimaryOnly && _secondaryColumn is not null)
            cols.Add($"\"{_secondaryColumn}\"");
        return string.Join(", ", cols);
    }

    private SongDbSongContent BuildContentFromReader(SqliteDataReader r, string songId, SongDbLanguageChoice choice)
    {
        string title = r.GetString(0);
        string primary = "";
        string secondary = "";
        int colIdx = 1;

        if (choice != SongDbLanguageChoice.SecondaryOnly && _primaryColumn is not null)
            primary = NormalizeLyrics(!r.IsDBNull(colIdx) ? r.GetString(colIdx) : ""); colIdx++;
        if (choice != SongDbLanguageChoice.PrimaryOnly && _secondaryColumn is not null)
            secondary = NormalizeLyrics(!r.IsDBNull(colIdx) ? r.GetString(colIdx) : "");

        return new SongDbSongContent(songId, title, primary, secondary);
    }

    private static string NormalizeLyrics(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Replace <slide> or <SLIDE> tags with paragraph breaks (\n\n)
        text = Regex.Replace(text, @"<\s*slide\s*>", "\n\n", RegexOptions.IgnoreCase);

        // Replace <br>, <br/>, <BR> with line breaks (\n)
        text = Regex.Replace(text, @"<\s*br\s*/?\s*>", "\n", RegexOptions.IgnoreCase);

        return text.Replace("\\r\\n", "\n")
                   .Replace("\\n", "\n")
                   .Replace("\r\n", "\n")
                   .Replace('\r', '\n');
    }

    // -----------------------------------------------------------------------
    //  XML fallback
    // -----------------------------------------------------------------------

    private IReadOnlyList<SongDbSongSummary> SearchXml(string query)
    {
        if (_xmlDoc is null) return [];

        var results = new List<SongDbSongSummary>();
        string cleanQuery = query.Trim();
        string? exactNum = null;
        var numMatch = Regex.Match(cleanQuery, @"^(?:#\s*|song\s*#?\s*|no\.?\s*#?\s*|hymn\s*#?\s*)?(\d+)$", RegexOptions.IgnoreCase);
        if (numMatch.Success)
        {
            exactNum = numMatch.Groups[1].Value;
        }

        foreach (var el in _xmlDoc.Descendants())
        {
            string? id = el.Attribute("id")?.Value ?? el.Element("id")?.Value;
            if (string.IsNullOrEmpty(id)) continue;

            string title = el.Attribute("title")?.Value ?? el.Element("title")?.Value
                        ?? el.Attribute("name")?.Value ?? el.Element("name")?.Value ?? "";
            if (string.IsNullOrEmpty(title)) continue;

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
                results.Add(new SongDbSongSummary(id, title, title2, cat, num));
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

    private SongDbSongContent? LoadXml(string songId, SongDbLanguageChoice choice)
    {
        if (_xmlDoc is null) return null;

        foreach (var el in _xmlDoc.Descendants())
        {
            string? id = el.Attribute("id")?.Value ?? el.Element("id")?.Value;
            if (id != songId) continue;

            string title   = el.Attribute("title")?.Value ?? el.Element("title")?.Value ?? "";
            string primary = NormalizeLyrics(el.Element("lyrics")?.Value ?? el.Element("content")?.Value ?? "");
            string secondary = NormalizeLyrics(el.Element("transliteration")?.Value ?? el.Element("secondary")?.Value ?? "");

            return new SongDbSongContent(songId, title, primary, secondary);
        }
        return null;
    }

    public void Dispose()
    {
        _conn?.Dispose();
    }
}
