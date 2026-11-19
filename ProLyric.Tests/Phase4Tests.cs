using System.IO;
using Microsoft.Data.Sqlite;
using ProLyric.Models;
using ProLyric.Services;
using Xunit.Abstractions;

namespace ProLyric.Tests;

public class Phase4Tests : IDisposable
{
    private readonly ITestOutputHelper? _output;
    private readonly string _testDbPath;
    private readonly string _songDbPath;
    private readonly string _songDbXmlPath;

    public Phase4Tests(ITestOutputHelper output)
    {
        _output = output;
        _testDbPath = Path.Combine(Path.GetTempPath(), $"prolyric_test_{Guid.NewGuid():N}.db");
        _songDbPath = Path.Combine(Path.GetTempPath(), $"songdb_test_{Guid.NewGuid():N}.db");
        _songDbXmlPath = Path.Combine(Path.GetTempPath(), $"songdb_test_{Guid.NewGuid():N}.xml");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_testDbPath)) File.Delete(_testDbPath); } catch { }
        try { if (File.Exists(_songDbPath)) File.Delete(_songDbPath); } catch { }
        try { if (File.Exists(_songDbXmlPath)) File.Delete(_songDbXmlPath); } catch { }
    }

    // -----------------------------------------------------------------------
    //  SongLibraryDbService Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void SongLibrary_SaveAndLoad_PreservesAllFieldsAndSlides()
    {
        using (var lib = new SongLibraryDbService(_testDbPath))
        {
            var project = new SongProject
            {
                Title = "Test Song",
                LanguageMode = LanguageMode.Dual,
                TemplatePath = @"C:\Templates\template.pro",
                ExportedFileName = "Test Song.pro",
                ExportedFilePath = @"C:\Exports\Test Song.pro",
                Slides =
                {
                    new LyricSlide { SortOrder = 0, GroupName = "Verse 1", Lang1Text = "L1 V1", Lang2Text = "L2 V1" },
                    new LyricSlide { SortOrder = 1, GroupName = "Chorus", Lang1Text = "L1 C", Lang2Text = "L2 C" }
                }
            };

            lib.SaveSong(project);
            Assert.True(project.Id > 0);

            var loaded = lib.LoadSong(project.Id);
            Assert.NotNull(loaded);
            Assert.Equal("Test Song", loaded.Title);
            Assert.Equal(LanguageMode.Dual, loaded.LanguageMode);
            Assert.Equal(@"C:\Templates\template.pro", loaded.TemplatePath);
            Assert.Equal("Test Song.pro", loaded.ExportedFileName);
            Assert.Equal(@"C:\Exports\Test Song.pro", loaded.ExportedFilePath);
            Assert.Equal(2, loaded.Slides.Count);
            Assert.Equal("Verse 1", loaded.Slides[0].GroupName);
            Assert.Equal("L1 V1", loaded.Slides[0].Lang1Text);
            Assert.Equal("L2 V1", loaded.Slides[0].Lang2Text);
            Assert.Equal("Chorus", loaded.Slides[1].GroupName);
        }
    }

    [Fact]
    public void SongLibrary_UpdateSong_ReplacesSlidesCorrectly()
    {
        using var lib = new SongLibraryDbService(_testDbPath);
        var project = new SongProject
        {
            Title = "Original Title",
            Slides =
            {
                new LyricSlide { SortOrder = 0, GroupName = "Verse 1", Lang1Text = "Original Slide" }
            }
        };

        lib.SaveSong(project);
        int id = project.Id;

        // Update
        project.Title = "Updated Title";
        project.Slides.Clear();
        project.Slides.Add(new LyricSlide { SortOrder = 0, GroupName = "New Verse", Lang1Text = "Updated Slide 1" });
        project.Slides.Add(new LyricSlide { SortOrder = 1, GroupName = "New Chorus", Lang1Text = "Updated Slide 2" });

        lib.SaveSong(project);
        Assert.Equal(id, project.Id);

        var loaded = lib.LoadSong(id);
        Assert.NotNull(loaded);
        Assert.Equal("Updated Title", loaded.Title);
        Assert.Equal(2, loaded.Slides.Count);
        Assert.Equal("New Verse", loaded.Slides[0].GroupName);
        Assert.Equal("Updated Slide 1", loaded.Slides[0].Lang1Text);
    }

    [Fact]
    public void SongLibrary_SearchAndGetAll_ReturnsMatchingProjects()
    {
        using var lib = new SongLibraryDbService(_testDbPath);
        lib.SaveSong(new SongProject { Title = "Amazing Grace" });
        lib.SaveSong(new SongProject { Title = "How Great Thou Art" });
        lib.SaveSong(new SongProject { Title = "Grace Greater Than Our Sin" });

        var all = lib.GetAllSongs();
        Assert.Equal(3, all.Count);

        var searchGrace = lib.SearchSongs("Grace");
        Assert.Equal(2, searchGrace.Count);

        var searchArt = lib.SearchSongs("Art");
        Assert.Single(searchArt);
    }

    [Fact]
    public void SongLibrary_DeleteSong_CascadesToSlides()
    {
        using var lib = new SongLibraryDbService(_testDbPath);
        var project = new SongProject
        {
            Title = "To Delete",
            Slides = { new LyricSlide { Lang1Text = "Slide" } }
        };
        lib.SaveSong(project);

        lib.DeleteSong(project.Id);

        var loaded = lib.LoadSong(project.Id);
        Assert.Null(loaded);
        Assert.Empty(lib.GetAllSongs());
    }

    // -----------------------------------------------------------------------
    //  SongDbService Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void SongDb_Sqlite_InspectsAndLoadsCorrectly()
    {
        // Setup mock SQLite song database
        using (var conn = new SqliteConnection($"Data Source={_songDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE tbl_songs (
                    songid INTEGER PRIMARY KEY,
                    songtitle TEXT,
                    lyrics TEXT,
                    transliteration TEXT
                );
                INSERT INTO tbl_songs (songid, songtitle, lyrics, transliteration)
                VALUES (1, 'Nalla Daivam', 'നല്ല ദൈവം\nവലിയവൻ', 'Nalla Daivam\nValiyavan');
                """;
            cmd.ExecuteNonQuery();
        }

        using var sdb = new SongDbService(_songDbPath);
        sdb.Open();

        var search = sdb.SearchSongs("Daivam");
        Assert.Single(search);
        Assert.Equal("1", search[0].Id);
        Assert.Equal("Nalla Daivam", search[0].Title);

        var song = sdb.LoadSong("1", SongDbLanguageChoice.Both);
        Assert.NotNull(song);
        Assert.Equal("Nalla Daivam", song.Title);
        Assert.Equal("നല്ല ദൈവം\nവലിയവൻ", song.PrimaryLyrics);
        Assert.Equal("Nalla Daivam\nValiyavan", song.SecondaryLyrics);
    }

    [Fact]
    public void SongDb_Sqlite_SongNumberSearch_ExactAndPrefix()
    {
        using (var conn = new SqliteConnection($"Data Source={_songDbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE sm (
                    id INTEGER PRIMARY KEY,
                    name TEXT,
                    title2 TEXT,
                    cat TEXT,
                    subcat TEXT,
                    lyrics TEXT,
                    lyrics2 TEXT
                );
                INSERT INTO sm (id, name, title2, cat, subcat, lyrics, lyrics2)
                VALUES 
                    (101, 'Song A', 'ഗാനം എ', 'Malayalam', '1', 'L1', 'L2'),
                    (102, 'Song B', 'ഗാനം ബി', 'Malayalam', '10', 'L1', 'L2'),
                    (103, 'Song C', 'ഗാനം സി', 'Malayalam', '100', 'L1', 'L2'),
                    (104, 'Song D', 'ഗാനം ഡി', 'Malayalam', '25', 'L1', 'L2');
                """;
            cmd.ExecuteNonQuery();
        }

        using var sdb = new SongDbService(_songDbPath);
        sdb.Open();

        // 1. Search exact number "25"
        var s25 = sdb.SearchSongs("25");
        Assert.Single(s25);
        Assert.Equal("104", s25[0].Id);
        Assert.Equal("25", s25[0].SongNumber);
        Assert.Equal("#25  Song D — ഗാനം ഡി", s25[0].DisplayTitle);

        // 2. Search "#25", "Song 25", "No. 25", "Hymn 25"
        Assert.Equal("25", sdb.SearchSongs("#25")[0].SongNumber);
        Assert.Equal("25", sdb.SearchSongs("Song 25")[0].SongNumber);
        Assert.Equal("25", sdb.SearchSongs("No. 25")[0].SongNumber);
        Assert.Equal("25", sdb.SearchSongs("Hymn 25")[0].SongNumber);

        // 3. Search "1" -> exact match "1" is first, followed by "10", then "100"
        var s1 = sdb.SearchSongs("1");
        Assert.Equal(3, s1.Count);
        Assert.Equal("1", s1[0].SongNumber);
        Assert.Equal("10", s1[1].SongNumber);
        Assert.Equal("100", s1[2].SongNumber);

        // 4. Search "10" -> exact match "10" is first, followed by "100"
        var s10 = sdb.SearchSongs("10");
        Assert.Equal(2, s10.Count);
        Assert.Equal("10", s10[0].SongNumber);
        Assert.Equal("100", s10[1].SongNumber);
    }

    [Fact]
    public void SongDb_Xml_SearchesAndLoads()
    {
        string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <songbook>
                <song id="101" title="Yeshuve Pole">
                    <lyrics>യേശുവേ പോലെ</lyrics>
                    <transliteration>Yeshuve Pole</transliteration>
                </song>
            </songbook>
            """;
        File.WriteAllText(_songDbXmlPath, xml);

        using var sdb = new SongDbService(_songDbXmlPath);
        sdb.Open();

        var search = sdb.SearchSongs("Yeshuve");
        Assert.Single(search);
        Assert.Equal("101", search[0].Id);

        var song = sdb.LoadSong("101", SongDbLanguageChoice.Both);
        Assert.NotNull(song);
        Assert.Equal("Yeshuve Pole", song.Title);
        Assert.Equal("യേശുവേ പോലെ", song.PrimaryLyrics);
        Assert.Equal("Yeshuve Pole", song.SecondaryLyrics);
    }

    [Fact]
    public void SongDb_Xml_SongNumberSearch()
    {
        string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <songbook>
                <song id="1" number="25" title="Amazing Grace">
                    <lyrics>Amazing Grace</lyrics>
                </song>
                <song id="2" number="250" title="Grace Greater">
                    <lyrics>Grace Greater</lyrics>
                </song>
            </songbook>
            """;
        File.WriteAllText(_songDbXmlPath, xml);

        using var sdb = new SongDbService(_songDbXmlPath);
        sdb.Open();

        var search25 = sdb.SearchSongs("25");
        Assert.NotEmpty(search25);
        Assert.Equal("25", search25[0].SongNumber);
        Assert.Equal("#25  Amazing Grace", search25[0].DisplayTitle);

        var searchHash = sdb.SearchSongs("#25");
        Assert.Equal("25", searchHash[0].SongNumber);

        var searchSong = sdb.SearchSongs("Song 25");
        Assert.Equal("25", searchSong[0].SongNumber);
    }

    [Fact]
    public void SongDb_ExternalFile_InspectIfExists()
    {
        string? realDb = Environment.GetEnvironmentVariable("TEST_SONG_DB");
        if (string.IsNullOrEmpty(realDb) || !File.Exists(realDb)) return;

        using var sdb = new SongDbService(realDb);
        sdb.Open();

        var search = sdb.SearchSongs("1");
        Assert.NotNull(search);
    }
}
