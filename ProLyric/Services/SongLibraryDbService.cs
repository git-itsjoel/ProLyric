using System.IO;
using Microsoft.Data.Sqlite;
using ProLyric.Models;

namespace ProLyric.Services;

/// <summary>
/// Local SQLite song library at %AppData%\ProLyric\library.db.
/// Auto-creates schema on first launch.
/// Tables: Songs, SongSlides (with ON DELETE CASCADE).
/// </summary>
public class SongLibraryDbService : IDisposable
{
    private static readonly string DbDir = GetDbDir();

    private static string GetDbDir()
    {
        string newDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProLyric");
        string[] oldDirs =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProLyrics"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ProLyricGen")
        ];
        if (!Directory.Exists(newDir))
        {
            foreach (var oldDir in oldDirs)
            {
                if (Directory.Exists(oldDir))
                {
                    try
                    {
                        Directory.CreateDirectory(newDir);
                        foreach (var file in Directory.GetFiles(oldDir))
                        {
                            string dest = Path.Combine(newDir, Path.GetFileName(file));
                            if (!File.Exists(dest)) File.Copy(file, dest);
                        }
                        break;
                    }
                    catch { }
                }
            }
        }
        return newDir;
    }

    private static readonly string DbPath =
        Path.Combine(DbDir, "library.db");

    private readonly SqliteConnection _conn;

    public SongLibraryDbService() : this(DbPath) { }

    /// <summary>Allows injecting a custom path for testing.</summary>
    public SongLibraryDbService(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        EnableForeignKeys();
        EnsureSchema();
    }

    // -----------------------------------------------------------------------
    //  Schema
    // -----------------------------------------------------------------------

    private void EnableForeignKeys()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = ON;";
        cmd.ExecuteNonQuery();
    }

    private void EnsureSchema()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Songs (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                Title           TEXT NOT NULL,
                LanguageMode    TEXT NOT NULL DEFAULT 'Single',
                TemplatePath    TEXT NOT NULL DEFAULT '',
                ExportedFileName TEXT NOT NULL DEFAULT '',
                ExportedFilePath TEXT NOT NULL DEFAULT '',
                CreatedAt       TEXT NOT NULL,
                UpdatedAt       TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS SongSlides (
                Id          INTEGER PRIMARY KEY AUTOINCREMENT,
                SongId      INTEGER NOT NULL REFERENCES Songs(Id) ON DELETE CASCADE,
                SortOrder   INTEGER NOT NULL DEFAULT 0,
                GroupName   TEXT NOT NULL DEFAULT '',
                Lang1Text   TEXT NOT NULL DEFAULT '',
                Lang2Text   TEXT NOT NULL DEFAULT ''
            );
            """;
        cmd.ExecuteNonQuery();
    }

    // -----------------------------------------------------------------------
    //  CRUD
    // -----------------------------------------------------------------------

    /// <summary>
    /// Inserts or updates a SongProject (and all its slides) in the library.
    /// Sets project.Id on insert.
    /// </summary>
    public void SaveSong(SongProject project)
    {
        using var tx = _conn.BeginTransaction();
        try
        {
            project.UpdatedAt = DateTime.UtcNow;

            if (project.Id == 0)
            {
                // Insert
                project.CreatedAt = DateTime.UtcNow;
                using var insertSong = _conn.CreateCommand();
                insertSong.CommandText = """
                    INSERT INTO Songs (Title, LanguageMode, TemplatePath, ExportedFileName, ExportedFilePath, CreatedAt, UpdatedAt)
                    VALUES ($title, $mode, $tmpl, $fname, $fpath, $created, $updated);
                    SELECT last_insert_rowid();
                    """;
                insertSong.Parameters.AddWithValue("$title",   project.Title);
                insertSong.Parameters.AddWithValue("$mode",    project.LanguageMode.ToString());
                insertSong.Parameters.AddWithValue("$tmpl",    project.TemplatePath);
                insertSong.Parameters.AddWithValue("$fname",   project.ExportedFileName);
                insertSong.Parameters.AddWithValue("$fpath",   project.ExportedFilePath);
                insertSong.Parameters.AddWithValue("$created", project.CreatedAt.ToString("O"));
                insertSong.Parameters.AddWithValue("$updated", project.UpdatedAt.ToString("O"));
                project.Id = (int)(long)insertSong.ExecuteScalar()!;
            }
            else
            {
                // Update
                using var updateSong = _conn.CreateCommand();
                updateSong.CommandText = """
                    UPDATE Songs SET
                        Title            = $title,
                        LanguageMode     = $mode,
                        TemplatePath     = $tmpl,
                        ExportedFileName = $fname,
                        ExportedFilePath = $fpath,
                        UpdatedAt        = $updated
                    WHERE Id = $id;
                    """;
                updateSong.Parameters.AddWithValue("$id",      project.Id);
                updateSong.Parameters.AddWithValue("$title",   project.Title);
                updateSong.Parameters.AddWithValue("$mode",    project.LanguageMode.ToString());
                updateSong.Parameters.AddWithValue("$tmpl",    project.TemplatePath);
                updateSong.Parameters.AddWithValue("$fname",   project.ExportedFileName);
                updateSong.Parameters.AddWithValue("$fpath",   project.ExportedFilePath);
                updateSong.Parameters.AddWithValue("$updated", project.UpdatedAt.ToString("O"));
                updateSong.ExecuteNonQuery();

                // Delete existing slides (cascade not needed here — we replace them)
                using var deleteSlides = _conn.CreateCommand();
                deleteSlides.CommandText = "DELETE FROM SongSlides WHERE SongId = $songId;";
                deleteSlides.Parameters.AddWithValue("$songId", project.Id);
                deleteSlides.ExecuteNonQuery();
            }

            // Insert slides
            foreach (var slide in project.Slides)
            {
                using var insertSlide = _conn.CreateCommand();
                insertSlide.CommandText = """
                    INSERT INTO SongSlides (SongId, SortOrder, GroupName, Lang1Text, Lang2Text)
                    VALUES ($songId, $sort, $group, $l1, $l2);
                    SELECT last_insert_rowid();
                    """;
                insertSlide.Parameters.AddWithValue("$songId", project.Id);
                insertSlide.Parameters.AddWithValue("$sort",   slide.SortOrder);
                insertSlide.Parameters.AddWithValue("$group",  slide.GroupName);
                insertSlide.Parameters.AddWithValue("$l1",     slide.Lang1Text);
                insertSlide.Parameters.AddWithValue("$l2",     slide.Lang2Text);
                slide.Id = (int)(long)insertSlide.ExecuteScalar()!;
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Returns all songs (without slides) for display in the library list.</summary>
    public IReadOnlyList<SongProject> GetAllSongs()
    {
        var list = new List<SongProject>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, LanguageMode, TemplatePath, ExportedFileName, ExportedFilePath, CreatedAt, UpdatedAt FROM Songs ORDER BY UpdatedAt DESC;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadSongRow(reader));
        return list;
    }

    /// <summary>Searches songs by title (case-insensitive LIKE).</summary>
    public IReadOnlyList<SongProject> SearchSongs(string query)
    {
        var list = new List<SongProject>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, LanguageMode, TemplatePath, ExportedFileName, ExportedFilePath, CreatedAt, UpdatedAt FROM Songs WHERE Title LIKE $q ORDER BY UpdatedAt DESC;";
        cmd.Parameters.AddWithValue("$q", $"%{query}%");
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(ReadSongRow(reader));
        return list;
    }

    /// <summary>Loads a song with all its slides.</summary>
    public SongProject? LoadSong(int songId)
    {
        SongProject? project = null;

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Title, LanguageMode, TemplatePath, ExportedFileName, ExportedFilePath, CreatedAt, UpdatedAt FROM Songs WHERE Id = $id;";
            cmd.Parameters.AddWithValue("$id", songId);
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
                project = ReadSongRow(reader);
        }

        if (project is null) return null;

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, SortOrder, GroupName, Lang1Text, Lang2Text FROM SongSlides WHERE SongId = $songId ORDER BY SortOrder;";
            cmd.Parameters.AddWithValue("$songId", songId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                project.Slides.Add(new LyricSlide
                {
                    Id        = reader.GetInt32(0),
                    SortOrder = reader.GetInt32(1),
                    GroupName = reader.GetString(2),
                    Lang1Text = reader.GetString(3),
                    Lang2Text = reader.GetString(4),
                });
            }
        }

        return project;
    }

    /// <summary>Deletes a song and all its slides (via ON DELETE CASCADE).</summary>
    public void DeleteSong(int songId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Songs WHERE Id = $id;";
        cmd.Parameters.AddWithValue("$id", songId);
        cmd.ExecuteNonQuery();
    }

    // -----------------------------------------------------------------------
    //  Private helpers
    // -----------------------------------------------------------------------

    private static SongProject ReadSongRow(SqliteDataReader r)
    {
        return new SongProject
        {
            Id               = r.GetInt32(0),
            Title            = r.GetString(1),
            LanguageMode     = Enum.TryParse<LanguageMode>(r.GetString(2), out var mode) ? mode : LanguageMode.Single,
            TemplatePath     = r.GetString(3),
            ExportedFileName = r.GetString(4),
            ExportedFilePath = r.GetString(5),
            CreatedAt        = DateTime.Parse(r.GetString(6)),
            UpdatedAt        = DateTime.Parse(r.GetString(7)),
        };
    }

    public void Dispose()
    {
        _conn?.Dispose();
    }
}
