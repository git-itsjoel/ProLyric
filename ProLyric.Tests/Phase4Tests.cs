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
    private readonly string _verseViewDbPath;
    private readonly string _verseViewXmlPath;

    public Phase4Tests(ITestOutputHelper output)
    {
        _output = output;
        _testDbPath = Path.Combine(Path.GetTempPath(), $"prolyric_test_{Guid.NewGuid():N}.db");
        _verseViewDbPath = Path.Combine(Path.GetTempPath(), $"verseview_test_{Guid.NewGuid():N}.db");
        _verseViewXmlPath = Path.Combine(Path.GetTempPath(), $"verseview_test_{Guid.NewGuid():N}.xml");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_testDbPath)) File.Delete(_testDbPath); } catch { }
        try { if (File.Exists(_verseViewDbPath)) File.Delete(_verseViewDbPath); } catch { }
        try { if (File.Exists(_verseViewXmlPath)) File.Delete(_verseViewXmlPath); } catch { }
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
    //  VerseViewDbService Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void VerseViewDb_Sqlite_InspectsAndLoadsCorrectly()
    {
        // Setup mock SQLite VerseVIEW database
        using (var conn = new SqliteConnection($"Data Source={_verseViewDbPath}"))
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

        using var vv = new VerseViewDbService(_verseViewDbPath);
        vv.Open();

        var search = vv.SearchSongs("Daivam");
        Assert.Single(search);
        Assert.Equal("1", search[0].Id);
        Assert.Equal("Nalla Daivam", search[0].Title);

        var song = vv.LoadSong("1", VerseViewLanguageChoice.Both);
        Assert.NotNull(song);
        Assert.Equal("Nalla Daivam", song.Title);
        Assert.Equal("നല്ല ദൈവം\nവലിയവൻ", song.PrimaryLyrics);
        Assert.Equal("Nalla Daivam\nValiyavan", song.SecondaryLyrics);
    }

    [Fact]
    public void VerseViewDb_Sqlite_SongNumberSearch_ExactAndPrefix()
    {
        using (var conn = new SqliteConnection($"Data Source={_verseViewDbPath}"))
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

        using var vv = new VerseViewDbService(_verseViewDbPath);
        vv.Open();

        // 1. Search exact number "25"
        var s25 = vv.SearchSongs("25");
        Assert.Single(s25);
        Assert.Equal("104", s25[0].Id);
        Assert.Equal("25", s25[0].SongNumber);
        Assert.Equal("#25  Song D — ഗാനം ഡി", s25[0].DisplayTitle);

        // 2. Search "#25", "Song 25", "No. 25", "Hymn 25"
        Assert.Equal("25", vv.SearchSongs("#25")[0].SongNumber);
        Assert.Equal("25", vv.SearchSongs("Song 25")[0].SongNumber);
        Assert.Equal("25", vv.SearchSongs("No. 25")[0].SongNumber);
        Assert.Equal("25", vv.SearchSongs("Hymn 25")[0].SongNumber);

        // 3. Search "1" -> exact match "1" is first, followed by "10", then "100"
        var s1 = vv.SearchSongs("1");
        Assert.Equal(3, s1.Count);
        Assert.Equal("1", s1[0].SongNumber);
        Assert.Equal("10", s1[1].SongNumber);
        Assert.Equal("100", s1[2].SongNumber);

        // 4. Search "10" -> exact match "10" is first, followed by "100"
        var s10 = vv.SearchSongs("10");
        Assert.Equal(2, s10.Count);
        Assert.Equal("10", s10[0].SongNumber);
        Assert.Equal("100", s10[1].SongNumber);
    }

    [Fact]
    public void VerseViewDb_Xml_SearchesAndLoads()
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
        File.WriteAllText(_verseViewXmlPath, xml);

        using var vv = new VerseViewDbService(_verseViewXmlPath);
        vv.Open();

        var search = vv.SearchSongs("Yeshuve");
        Assert.Single(search);
        Assert.Equal("101", search[0].Id);

        var song = vv.LoadSong("101", VerseViewLanguageChoice.Both);
        Assert.NotNull(song);
        Assert.Equal("Yeshuve Pole", song.Title);
        Assert.Equal("യേശുവേ പോലെ", song.PrimaryLyrics);
        Assert.Equal("Yeshuve Pole", song.SecondaryLyrics);
    }

    [Fact]
    public void VerseViewDb_Xml_SongNumberSearch()
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
        File.WriteAllText(_verseViewXmlPath, xml);

        using var vv = new VerseViewDbService(_verseViewXmlPath);
        vv.Open();

        var search25 = vv.SearchSongs("25");
        Assert.NotEmpty(search25);
        Assert.Equal("25", search25[0].SongNumber);
        Assert.Equal("#25  Amazing Grace", search25[0].DisplayTitle);

        var searchHash = vv.SearchSongs("#25");
        Assert.Equal("25", searchHash[0].SongNumber);

        var searchSong = vv.SearchSongs("Song 25");
        Assert.Equal("25", searchSong[0].SongNumber);
    }

    [Fact]
    public void VerseViewDb_RealFile_Inspect()
    {
        string realDb = @"c:\Program Files\VerseVIEW10\resources\app.asar.unpacked\songs_database\songs.db";
        if (!File.Exists(realDb)) return;

        using var conn = new SqliteConnection($"Data Source={realDb};Mode=ReadOnly");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name, sql FROM sqlite_master WHERE type='table';";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            _output?.WriteLine($"TABLE: {r.GetString(0)}");
            _output?.WriteLine($"SQL: {r.GetString(1)}\n");
        }

        // Test with VerseViewDbService
        using var vv = new VerseViewDbService(realDb);
        vv.Open();
        
        using var cmdMalayalam = conn.CreateCommand();
        cmdMalayalam.CommandText = "SELECT id, name, title2, cat, lyrics, lyrics2 FROM sm WHERE cat LIKE '%Malayalam%' OR lyrics LIKE '%ദൈവം%' LIMIT 3;";
        using var rMal = cmdMalayalam.ExecuteReader();
        while (rMal.Read())
        {
            _output?.WriteLine($"\n================== MALAYALAM SONG #{rMal.GetInt64(0)}: {rMal.GetString(1)} (title2: {rMal.GetString(2)}) ==================");
            _output?.WriteLine($"Category: {rMal.GetString(3)}");
            _output?.WriteLine($"Lyrics 1:\n{rMal.GetString(4)}");
            _output?.WriteLine($"Lyrics 2:\n{rMal.GetString(5)}");
        }

        // Test loading song 226016 via VerseViewDbService
        var song = vv.LoadSong("226016", VerseViewLanguageChoice.Both);
        Assert.NotNull(song);
        Assert.Contains("എന്നു കാണും", song.PrimaryLyrics);
        Assert.Contains("ennu kanum", song.SecondaryLyrics);
        // Verify <slide> tags were converted to \n\n and <BR> tags converted to \n
        Assert.DoesNotContain("<slide>", song.PrimaryLyrics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<br>", song.PrimaryLyrics, StringComparison.OrdinalIgnoreCase);

        // Test search by song number on real VerseVIEW database
        var search25 = vv.SearchSongs("25");
        Assert.NotEmpty(search25);
        Assert.Equal("25", search25[0].SongNumber);
        Assert.Equal("Aakaashatherathil kristheshu", search25[0].Title);
        Assert.StartsWith("#25  ", search25[0].DisplayTitle);

        var searchHash1 = vv.SearchSongs("#1");
        Assert.NotEmpty(searchHash1);
        Assert.Equal("1", searchHash1[0].SongNumber);
        Assert.Equal("Aa aa aa aa ennu kanum yeshu", searchHash1[0].Title);

        var search100 = vv.SearchSongs("100");
        Assert.NotEmpty(search100);
        Assert.Equal("100", search100[0].SongNumber);
        Assert.Equal("Aaradhyane aaradhyane", search100[0].Title);

        // Test parser on the normalized lyrics
        var parser = new LyricParserService();
        var slides1 = parser.ParseByParagraph(song.PrimaryLyrics);
        var slides2 = parser.ParseByParagraph(song.SecondaryLyrics);
        Assert.Equal(slides1.Count, slides2.Count);
        _output?.WriteLine($"\nSuccessfully parsed into {slides1.Count} synchronized slides!");
        for (int i = 0; i < slides1.Count; i++)
        {
            _output?.WriteLine($"\nSlide #{i + 1} [{slides1[i].GroupName}]:");
            _output?.WriteLine($"  Lang1 (Malayalam): {slides1[i].Lang1Text.Replace("\n", " / ")}");
            _output?.WriteLine($"  Lang2 (Manglish):  {slides2[i].Lang1Text.Replace("\n", " / ")}");
        }
    }
}
