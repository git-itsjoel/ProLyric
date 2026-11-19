using System.IO;
using Google.Protobuf;
using Pro.SerializationInterop.RVProtoData;
using ProLyric.Models;
using ProLyric.Services;
using ProAction = Pro.SerializationInterop.RVProtoData.Action;

namespace ProLyric.Tests;

public class Phase5Tests : IDisposable
{
    private readonly string _singleTemplatePath;
    private readonly string _dualTemplatePath;
    private readonly string _outputPath;

    public Phase5Tests()
    {
        string tmp = Path.GetTempPath();
        _singleTemplatePath = Path.Combine(tmp, $"tmpl_single_{Guid.NewGuid():N}.pro");
        _dualTemplatePath = Path.Combine(tmp, $"tmpl_dual_{Guid.NewGuid():N}.pro");
        _outputPath = Path.Combine(tmp, $"out_{Guid.NewGuid():N}.pro");

        CreateMockTemplate(_singleTemplatePath, includeLang2: false);
        CreateMockTemplate(_dualTemplatePath, includeLang2: true);
    }

    public void Dispose()
    {
        try { if (File.Exists(_singleTemplatePath)) File.Delete(_singleTemplatePath); } catch { }
        try { if (File.Exists(_dualTemplatePath)) File.Delete(_dualTemplatePath); } catch { }
        try { if (File.Exists(_outputPath)) File.Delete(_outputPath); } catch { }
    }

    private static void CreateMockTemplate(string path, bool includeLang2)
    {
        var presentation = new Presentation
        {
            Name = "Template",
            Uuid = new UUID { String = Guid.NewGuid().ToString() }
        };

        var cue = new Cue
        {
            Name = "Slide 1",
            Uuid = new UUID { String = Guid.NewGuid().ToString() }
        };

        var action = new ProAction
        {
            Uuid = new UUID { String = Guid.NewGuid().ToString() },
            Type = ProAction.Types.ActionType.PresentationSlide,
            Slide = new ProAction.Types.SlideType
            {
                Presentation = new PresentationSlide
                {
                    BaseSlide = new Slide
                    {
                        Uuid = new UUID { String = Guid.NewGuid().ToString() }
                    }
                }
            }
        };

        string baseRtf = @"{\rtf1\ansi\deff0{\fonttbl{\f0 Arial;}}{\colortbl ;\red255\green255\blue255;}\f0\fs48\cf1 Sample}";

        // Lang1 element
        var el1 = new Slide.Types.Element
        {
            Element_ = new Graphics.Types.Element
            {
                Uuid = new UUID { String = Guid.NewGuid().ToString() },
                Name = "Lang1",
                Text = new Graphics.Types.Text
                {
                    RtfData = ByteString.CopyFromUtf8(baseRtf)
                }
            }
        };
        action.Slide.Presentation.BaseSlide.Elements.Add(el1);

        // Lang2 element
        if (includeLang2)
        {
            var el2 = new Slide.Types.Element
            {
                Element_ = new Graphics.Types.Element
                {
                    Uuid = new UUID { String = Guid.NewGuid().ToString() },
                    Name = "Lang2",
                    Text = new Graphics.Types.Text
                    {
                        RtfData = ByteString.CopyFromUtf8(baseRtf)
                    }
                }
            };
            action.Slide.Presentation.BaseSlide.Elements.Add(el2);
        }

        cue.Actions.Add(action);
        presentation.Cues.Add(cue);

        using var fs = File.Create(path);
        presentation.WriteTo(fs);
    }

    // -----------------------------------------------------------------------
    //  ValidateTemplate Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void ValidateTemplate_NonExistentFile_ReturnsInvalid()
    {
        var svc = new ProPresenterService();
        var result = svc.ValidateTemplate(@"C:\NonExistent_ProLyric_File.pro", LanguageMode.Single);
        Assert.False(result.IsValid);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void ValidateTemplate_SingleMode_ValidWithLang1()
    {
        var svc = new ProPresenterService();
        var result = svc.ValidateTemplate(_singleTemplatePath, LanguageMode.Single);
        Assert.True(result.IsValid);
        Assert.True(result.HasLang1);
        Assert.False(result.HasLang2);
    }

    [Fact]
    public void ValidateTemplate_DualMode_InvalidWhenMissingLang2()
    {
        var svc = new ProPresenterService();
        var result = svc.ValidateTemplate(_singleTemplatePath, LanguageMode.Dual);
        Assert.False(result.IsValid);
        Assert.True(result.HasLang1);
        Assert.False(result.HasLang2);
        Assert.Contains("Lang2", result.ErrorMessage);
    }

    [Fact]
    public void ValidateTemplate_DualMode_ValidWhenBothPresent()
    {
        var svc = new ProPresenterService();
        var result = svc.ValidateTemplate(_dualTemplatePath, LanguageMode.Dual);
        Assert.True(result.IsValid);
        Assert.True(result.HasLang1);
        Assert.True(result.HasLang2);
    }

    // -----------------------------------------------------------------------
    //  GeneratePresentation Tests
    // -----------------------------------------------------------------------

    [Fact]
    public void GeneratePresentation_SingleMode_GeneratesValidProFile()
    {
        var svc = new ProPresenterService();
        var project = new SongProject
        {
            Title = "Amazing Grace",
            LanguageMode = LanguageMode.Single,
            Slides =
            {
                new LyricSlide { SortOrder = 0, GroupName = "Verse 1", Lang1Text = "Amazing grace how sweet the sound" },
                new LyricSlide { SortOrder = 1, GroupName = "Verse 2", Lang1Text = "'Twas grace that taught my heart to fear" }
            }
        };

        string outPath = svc.GeneratePresentation(_singleTemplatePath, project, _outputPath);
        Assert.True(File.Exists(outPath));

        // Read back generated file and verify
        using var fs = File.OpenRead(outPath);
        var generated = Presentation.Parser.ParseFrom(fs);

        Assert.Equal("Amazing Grace", generated.Name);
        Assert.Equal(2, generated.Cues.Count);
        Assert.Equal("Verse 1", generated.Cues[0].Name);
        Assert.Equal("Verse 2", generated.Cues[1].Name);

        // Verify UUIDs are distinct
        Assert.NotEqual(generated.Cues[0].Uuid.String, generated.Cues[1].Uuid.String);

        // Verify Lang1 text in first cue
        var baseSlide = generated.Cues[0].Actions[0].Slide.Presentation.BaseSlide;
        var lang1El = baseSlide.Elements.First(e => e.Element_.Name == "Lang1");
        string rtf1 = lang1El.Element_.Text.RtfData.ToStringUtf8();
        Assert.Contains("Amazing grace", rtf1);
    }

    [Fact]
    public void GeneratePresentation_DualMode_InjectsBothAndOrganizesCueGroups()
    {
        var svc = new ProPresenterService();
        var project = new SongProject
        {
            Title = "Daivam Nallavan",
            LanguageMode = LanguageMode.Dual,
            Slides =
            {
                new LyricSlide
                {
                    SortOrder = 0,
                    GroupName = "Chorus",
                    Lang1Text = "ദൈവം നല്ലവൻ",
                    Lang2Text = "Daivam Nallavan"
                },
                new LyricSlide
                {
                    SortOrder = 1,
                    GroupName = "Chorus",
                    Lang1Text = "അവൻ എന്നേക്കും നല്ലവൻ",
                    Lang2Text = "Avan Ennekkum Nallavan"
                },
                new LyricSlide
                {
                    SortOrder = 2,
                    GroupName = "Verse 1",
                    Lang1Text = "ഭാരങ്ങൾ ഏറിടുമ്പോൾ",
                    Lang2Text = "Bhaarangal Eridumbol"
                }
            }
        };

        string outPath = svc.GeneratePresentation(_dualTemplatePath, project, _outputPath);
        Assert.True(File.Exists(outPath));

        using var fs = File.OpenRead(outPath);
        var generated = Presentation.Parser.ParseFrom(fs);

        Assert.Equal("Daivam Nallavan", generated.Name);
        Assert.Equal(3, generated.Cues.Count);

        // Check CueGroups: 2 groups (Chorus with 2 cues, Verse 1 with 1 cue)
        Assert.Equal(2, generated.CueGroups.Count);
        Assert.Equal("Chorus", generated.CueGroups[0].Group.Name);
        Assert.Equal(2, generated.CueGroups[0].CueIdentifiers.Count);
        Assert.Equal("Verse 1", generated.CueGroups[1].Group.Name);
        Assert.Single(generated.CueGroups[1].CueIdentifiers);

        // Verify Malayalam RTF encoding in Lang1
        var baseSlide0 = generated.Cues[0].Actions[0].Slide.Presentation.BaseSlide;
        var lang1El = baseSlide0.Elements.First(e => e.Element_.Name == "Lang1");
        var lang2El = baseSlide0.Elements.First(e => e.Element_.Name == "Lang2");

        string rtf1 = lang1El.Element_.Text.RtfData.ToStringUtf8();
        string rtf2 = lang2El.Element_.Text.RtfData.ToStringUtf8();

        // Malayalam should be encoded with \uN? escapes
        Assert.Contains(@"\u", rtf1);
        Assert.Contains("Daivam Nallavan", rtf2);
    }

    [Fact]
    public void GeneratePresentation_TitleFallback_UsesFirstLineWhenTitleBlank()
    {
        var svc = new ProPresenterService();
        var project = new SongProject
        {
            Title = "", // Blank
            LanguageMode = LanguageMode.Single,
            Slides =
            {
                new LyricSlide { SortOrder = 0, Lang1Text = "First Line of Song\nSecond Line" }
            }
        };

        string outPath = svc.GeneratePresentation(_singleTemplatePath, project, _outputPath);
        using var fs = File.OpenRead(outPath);
        var generated = Presentation.Parser.ParseFrom(fs);

        Assert.Equal("First Line of Song", generated.Name);
    }

    [Fact]
    public void GeneratePresentation_MultiLineSlide_PropagatesFormattingAndClearsCustomAttributes()
    {
        string customTemplatePath = Path.Combine(Path.GetTempPath(), $"tmpl_custom_{Guid.NewGuid():N}.pro");
        try
        {
            Presentation p;
            using (var fs = File.OpenRead(_singleTemplatePath))
            {
                p = Presentation.Parser.ParseFrom(fs);
            }

            var el = p.Cues[0].Actions[0].Slide.Presentation.BaseSlide.Elements[0];
            el.Element_.Text.Attributes = new Graphics.Types.Text.Types.Attributes();
            el.Element_.Text.Attributes.CustomAttributes.Add(new Graphics.Types.Text.Types.Attributes.Types.CustomAttribute
            {
                Range = new IntRange { Start = 0, End = 10 },
                OriginalFontSize = 48
            });

            using (var outStream = File.Create(customTemplatePath))
            {
                p.WriteTo(outStream);
            }

            var svc = new ProPresenterService();
            var project = new SongProject
            {
                Title = "MultiLine Song",
                LanguageMode = LanguageMode.Single,
                Slides =
                {
                    new LyricSlide
                    {
                        SortOrder = 0,
                        Lang1Text = "Line 1\nLine 2\nLine 3"
                    }
                }
            };

            string outPath = svc.GeneratePresentation(customTemplatePath, project, _outputPath);
            using var readStream = File.OpenRead(outPath);
            var generated = Presentation.Parser.ParseFrom(readStream);

            var baseSlide = generated.Cues[0].Actions[0].Slide.Presentation.BaseSlide;
            var lang1El = baseSlide.Elements.First(e => e.Element_.Name == "Lang1");

            // Assert stale CustomAttributes are cleared
            Assert.Empty(lang1El.Element_.Text.Attributes.CustomAttributes);

            // Assert RTF has \fs48 on every line
            string rtf = lang1El.Element_.Text.RtfData.ToStringUtf8();
            int fsMatches = System.Text.RegularExpressions.Regex.Matches(rtf, @"\\fs48\b").Count;
            Assert.Equal(3, fsMatches);
        }
        finally
        {
            if (File.Exists(customTemplatePath)) File.Delete(customTemplatePath);
        }
    }
}
