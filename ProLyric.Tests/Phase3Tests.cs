using System.IO;
using ProLyric.Models;
using ProLyric.Services;

namespace ProLyric.Tests;

public class Phase3Tests
{
    // -----------------------------------------------------------------------
    //  SettingsService tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Settings_SaveAndLoad_PersistsValues()
    {
        // Use a temp file by temporarily overriding via reflection is complex;
        // instead, just test round-trip via save/load with the actual service.
        var svc = new SettingsService();
        string originalDb = svc.Current.VerseViewDbPath;

        svc.Update(s => s.VerseViewDbPath = "C:\\test\\verses.db");

        // Create a new instance to reload from disk
        var svc2 = new SettingsService();
        Assert.Equal("C:\\test\\verses.db", svc2.Current.VerseViewDbPath);

        // Restore
        svc.Update(s => s.VerseViewDbPath = originalDb);
    }

    [Fact]
    public void Settings_DefaultTemplate_Persists()
    {
        var svc = new SettingsService();
        string orig = svc.Current.DefaultTemplatePath;

        svc.Update(s => s.DefaultTemplatePath = "C:\\Templates\\dual.pro");
        var svc2 = new SettingsService();
        Assert.Equal("C:\\Templates\\dual.pro", svc2.Current.DefaultTemplatePath);

        svc.Update(s => s.DefaultTemplatePath = orig);
    }

    // -----------------------------------------------------------------------
    //  LyricParserService — By Paragraph tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Parser_ByParagraph_SplitsOnBlankLines()
    {
        var svc = new LyricParserService();
        string text = "Line 1\nLine 2\n\nLine 3\nLine 4";
        var slides = svc.ParseByParagraph(text);

        Assert.Equal(2, slides.Count);
        Assert.Equal("Line 1\nLine 2", slides[0].Lang1Text);
        Assert.Equal("Line 3\nLine 4", slides[1].Lang1Text);
    }

    [Fact]
    public void Parser_ByParagraph_DetectsSectionHeaders()
    {
        var svc = new LyricParserService();
        string text = "[Verse 1]\nLine A\nLine B\n\n[Chorus]\nLine C";
        var slides = svc.ParseByParagraph(text);

        Assert.Equal(2, slides.Count);
        Assert.Equal("Verse 1", slides[0].GroupName);
        Assert.Equal("Chorus", slides[1].GroupName);
    }

    [Fact]
    public void Parser_ByParagraph_MultipleBlankLines_SingleSplit()
    {
        var svc = new LyricParserService();
        string text = "A\nB\n\n\n\nC\nD";
        var slides = svc.ParseByParagraph(text);
        Assert.Equal(2, slides.Count);
    }

    [Fact]
    public void Parser_ByParagraph_EmptyInput_ReturnsEmpty()
    {
        var svc = new LyricParserService();
        var slides = svc.ParseByParagraph("");
        Assert.Empty(slides);
    }

    [Fact]
    public void Parser_ByParagraph_SortOrderIncrementing()
    {
        var svc = new LyricParserService();
        string text = "A\n\nB\n\nC";
        var slides = svc.ParseByParagraph(text);
        Assert.Equal(0, slides[0].SortOrder);
        Assert.Equal(1, slides[1].SortOrder);
        Assert.Equal(2, slides[2].SortOrder);
    }

    // -----------------------------------------------------------------------
    //  LyricParserService — By Line Count tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Parser_ByLineCount_GroupsEveryNLines()
    {
        var svc = new LyricParserService();
        string text = "1\n2\n3\n4\n5\n6\n7\n8";
        var slides = svc.ParseByLineCount(text, 4);

        Assert.Equal(2, slides.Count);
        Assert.Equal("1\n2\n3\n4", slides[0].Lang1Text);
        Assert.Equal("5\n6\n7\n8", slides[1].Lang1Text);
    }

    [Fact]
    public void Parser_ByLineCount_SkipsBlankLines()
    {
        var svc = new LyricParserService();
        string text = "1\n\n2\n3\n4\n5";
        var slides = svc.ParseByLineCount(text, 4);

        // 5 non-blank lines → first slide has 4, second has 1
        Assert.Equal(2, slides.Count);
        Assert.Equal(4, slides[0].Lang1Text.Split('\n').Length);
    }

    [Fact]
    public void Parser_ByLineCount_DetectsSectionHeaders()
    {
        var svc = new LyricParserService();
        string text = "[Verse 1]\nA\nB\n[Chorus]\nC\nD";
        var slides = svc.ParseByLineCount(text, 4);

        Assert.Equal(2, slides.Count);
        Assert.Equal("Verse 1", slides[0].GroupName);
        Assert.Equal("Chorus", slides[1].GroupName);
    }

    [Fact]
    public void Parser_ByLineCount_RemainingLines_CreateLastSlide()
    {
        var svc = new LyricParserService();
        string text = "1\n2\n3\n4\n5";
        var slides = svc.ParseByLineCount(text, 4);
        Assert.Equal(2, slides.Count);
        Assert.Equal("5", slides[1].Lang1Text);
    }

    // -----------------------------------------------------------------------
    //  ResolveSongTitle tests
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveSongTitle_UserTitle_ReturnsUserTitle()
    {
        var slides = new[] { new LyricSlide { Lang1Text = "Some Line" } };
        string title = LyricParserService.ResolveSongTitle("My Song", slides);
        Assert.Equal("My Song", title);
    }

    [Fact]
    public void ResolveSongTitle_EmptyUserTitle_ReturnsFirstLine()
    {
        var slides = new[] { new LyricSlide { Lang1Text = "Jesus is Lord\nSecond line" } };
        string title = LyricParserService.ResolveSongTitle("", slides);
        Assert.Equal("Jesus is Lord", title);
    }

    [Fact]
    public void ResolveSongTitle_WhitespaceUserTitle_ReturnsFirstLine()
    {
        var slides = new[] { new LyricSlide { Lang1Text = "Amazing Grace" } };
        string title = LyricParserService.ResolveSongTitle("   ", slides);
        Assert.Equal("Amazing Grace", title);
    }

    [Fact]
    public void ResolveSongTitle_NoSlides_ReturnsUntitled()
    {
        string title = LyricParserService.ResolveSongTitle("", Array.Empty<LyricSlide>());
        Assert.Equal("Untitled Song", title);
    }

    [Fact]
    public void ResolveSongTitle_SanitizesInvalidChars()
    {
        var slides = new[] { new LyricSlide { Lang1Text = "Song: Title/Name" } };
        string title = LyricParserService.ResolveSongTitle("", slides);
        // ':' and '/' are invalid filename chars → replaced with '_'
        Assert.DoesNotContain(":", title);
        Assert.DoesNotContain("/", title);
    }

    // -----------------------------------------------------------------------
    //  MalayalamTransliteratorService tests
    // -----------------------------------------------------------------------

    [Fact]
    public void Transliterator_ContainsMalayalam_DetectsCorrectly()
    {
        Assert.True(MalayalamTransliteratorService.ContainsMalayalam("ദൈവം"));
        Assert.False(MalayalamTransliteratorService.ContainsMalayalam("Hello"));
        Assert.False(MalayalamTransliteratorService.ContainsMalayalam(""));
    }

    [Fact]
    public void Transliterator_PlainEnglish_PassesThrough()
    {
        string result = MalayalamTransliteratorService.Transliterate("Hello World");
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Transliterator_Anusvara_ProducesM()
    {
        // ം (U+0D02) should produce "M" (or "m" when lowercase)
        string result = MalayalamTransliteratorService.Transliterate("ം");
        Assert.Equal("M", result);
        Assert.Equal("m", MalayalamTransliteratorService.Transliterate("ം", titleCase: false));
    }

    [Fact]
    public void Transliterator_IndependentVowelA_ProducesA()
    {
        // അ (U+0D05)
        string result = MalayalamTransliteratorService.Transliterate("അ");
        Assert.Equal("A", result);
        Assert.Equal("a", MalayalamTransliteratorService.Transliterate("അ", titleCase: false));
    }

    [Fact]
    public void Transliterator_ConsonantKa_ProducesKa()
    {
        // ക (U+0D15) + inherent 'a' = "Ka"
        string result = MalayalamTransliteratorService.Transliterate("ക");
        Assert.Equal("Ka", result);
        Assert.Equal("ka", MalayalamTransliteratorService.Transliterate("ക", titleCase: false));
    }

    [Fact]
    public void Transliterator_ConsonantWithMatra_CorrectOutput()
    {
        // കി = ക (ka) + ി (i matra) = "Ki"
        string result = MalayalamTransliteratorService.Transliterate("കി");
        Assert.Equal("Ki", result);
        Assert.Equal("ki", MalayalamTransliteratorService.Transliterate("കി", titleCase: false));
    }

    [Fact]
    public void Transliterator_ViramaSupressesInherentVowel()
    {
        // ക് at end of word produces "Ku" via samvruthokaram
        string result = MalayalamTransliteratorService.Transliterate("ക്");
        Assert.Equal("Ku", result);
        Assert.Equal("ku", MalayalamTransliteratorService.Transliterate("ക്", titleCase: false));
    }

    [Fact]
    public void Transliterator_DaivamWord_TransliteratesCorrectly()
    {
        // ദൈവം = ദ+ൈ(ai matra)+വ+ം = "Daivam"
        string result = MalayalamTransliteratorService.Transliterate("ദൈവം");
        Assert.Equal("Daivam", result);
        Assert.Equal("daivam", MalayalamTransliteratorService.Transliterate("ദൈവം", titleCase: false));
    }

    [Fact]
    public void Transliterator_YeshuWord_TransliteratesCorrectly()
    {
        // യേശു = യ(ya) + േ(e matra) + ശ(sha) + ു(u matra) = "Yeshu"
        string result = MalayalamTransliteratorService.Transliterate("യേശു");
        Assert.Equal("Yeshu", result);
        Assert.Equal("yeshu", MalayalamTransliteratorService.Transliterate("യേശു", titleCase: false));
    }

    [Fact]
    public void Transliterator_AksharamFix_Kai_MapsToAiNotY()
    {
        // Aksharam fix: matra ൈ is "ai", not "y" (കൈ -> Kai, not Ky)
        string result = MalayalamTransliteratorService.Transliterate("കൈ");
        Assert.Equal("Kai", result);
    }

    [Fact]
    public void Transliterator_AksharamFix_Ganga_AssimilatesAnusvara()
    {
        // Aksharam fix: anusvaram assimilates to 'ng' before velars (ഗംഗ -> Ganga, not Gamga)
        string result = MalayalamTransliteratorService.Transliterate("ഗംഗ");
        Assert.Equal("Ganga", result);
    }

    [Fact]
    public void Transliterator_AksharamFix_Oduka_RetroflexVoicing()
    {
        // Aksharam fix: inter-vocalic/non-initial retroflex ട voiced to 'd' (ഓടുക -> Oduka)
        string result = MalayalamTransliteratorService.Transliterate("ഓടുക");
        Assert.Equal("Oduka", result);
    }

    [Fact]
    public void Transliterator_AksharamFix_Jnaanam_CompoundHandled()
    {
        // Aksharam fix: compound ജ്ഞ correctly maps to 'jn' (ജ്ഞാനം -> Jnaanam)
        string result = MalayalamTransliteratorService.Transliterate("ജ്ഞാനം");
        Assert.Equal("Jnaanam", result);
    }

    [Fact]
    public void Transliterator_AksharamFix_WordFinalVirama_TrailingU()
    {
        // Aksharam fix: word-final non-chillu virama produces colloquial trailing 'u'
        Assert.Equal("Aanu", MalayalamTransliteratorService.Transliterate("ആണ്"));
        Assert.Equal("Undu", MalayalamTransliteratorService.Transliterate("ഉണ്ട്"));
        Assert.Equal("Nallathu", MalayalamTransliteratorService.Transliterate("നല്ലത്"));
        Assert.Equal("Paattu", MalayalamTransliteratorService.Transliterate("പാട്ട്"));
        Assert.Equal("Koodu", MalayalamTransliteratorService.Transliterate("കൂട്"));
    }

    [Fact]
    public void Transliterator_AksharamFix_ChilluLetters_MappedCorrectly()
    {
        // Chillu letters do not get trailing 'u' or doubling:
        // ർ -> r (Karthavu, NOT karrththaav)
        Assert.Equal("Karthavu", MalayalamTransliteratorService.Transliterate("കർത്താവ്"));
        Assert.Equal("Yeshu Karthavu", MalayalamTransliteratorService.Transliterate("യേശു കർത്താവ്"));
        Assert.Equal("Avan", MalayalamTransliteratorService.Transliterate("അവൻ"));
        Assert.Equal("Aval", MalayalamTransliteratorService.Transliterate("അവൾ"));
        Assert.Equal("Sooryan", MalayalamTransliteratorService.Transliterate("സൂര്യൻ"));
    }

    [Fact]
    public void Transliterator_AksharamFix_VisargaAndCompounds()
    {
        // Visarga assimilation and common compounds
        Assert.Equal("Dukham", MalayalamTransliteratorService.Transliterate("ദുഃഖം"));
        Assert.Equal("Santhosham", MalayalamTransliteratorService.Transliterate("സന്തോഷം"));
        Assert.Equal("Sangeetham", MalayalamTransliteratorService.Transliterate("സംഗീതം"));
        Assert.Equal("Achhan", MalayalamTransliteratorService.Transliterate("അച്ഛൻ"));
        Assert.Equal("Sneham", MalayalamTransliteratorService.Transliterate("സ്നേഹം"));
    }

    [Fact]
    public void Transliterator_PreservesLineBreaks()
    {
        string result = MalayalamTransliteratorService.Transliterate("ദൈവം\nയേശു");
        Assert.Contains("\n", result);
        string[] lines = result.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("Daivam", lines[0]);
        Assert.Equal("Yeshu", lines[1]);
    }

    [Fact]
    public void Transliterator_MixedText_OnlyMalayalamTransliterated()
    {
        string result = MalayalamTransliteratorService.Transliterate("Hello ദൈവം World");
        Assert.StartsWith("Hello ", result);
        Assert.EndsWith(" World", result);
        Assert.Contains("Daivam", result);
    }
}
