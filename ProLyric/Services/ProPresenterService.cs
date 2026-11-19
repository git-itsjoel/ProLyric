using System.IO;
using Google.Protobuf;
using Pro.SerializationInterop.RVProtoData;
using ProLyric.Models;
using ProAction = Pro.SerializationInterop.RVProtoData.Action;

namespace ProLyric.Services;

public record TemplateValidationResult(
    bool IsValid,
    string? ErrorMessage,
    bool HasLang1 = false,
    bool HasLang2 = false
);

/// <summary>
/// Validates ProPresenter 7+ (.pro) templates and generates complete presentations
/// by cloning template slide cues, updating RTF text elements, and grouping slides.
/// </summary>
public class ProPresenterService
{
    private const string Lang1Name = "Lang1";
    private const string Lang2Name = "Lang2";

    /// <summary>
    /// Creates a complete internal default ProPresenter 7+ presentation template.
    /// Provides Lang1 for Single mode, and both Lang1 and Lang2 for Dual mode.
    /// Built into the internal structure of ProLyric so external template files are not required.
    /// </summary>
    public static Presentation CreateInternalTemplate(LanguageMode mode)
    {
        bool isDual = mode == LanguageMode.Dual;
        var presentation = new Presentation
        {
            Name = "Default Template",
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

        // Standard ProPresenter 7+ RTF template: centered white text with uniform formatting
        string baseRtf = @"{\rtf1\ansi\ansicpg1252\deff0\nouicompat{\fonttbl{\f0\fnil\fcharset0 Arial;}}{\colortbl ;\red255\green255\blue255;}\viewkind4\uc1\pard\qc\cf1\f0\fs54 Sample\par}";

        // Lang1 element
        var el1 = new Slide.Types.Element
        {
            Element_ = new Graphics.Types.Element
            {
                Uuid = new UUID { String = Guid.NewGuid().ToString() },
                Name = Lang1Name,
                Text = new Graphics.Types.Text
                {
                    RtfData = ByteString.CopyFromUtf8(baseRtf)
                }
            }
        };
        action.Slide.Presentation.BaseSlide.Elements.Add(el1);

        // Lang2 element
        if (isDual)
        {
            var el2 = new Slide.Types.Element
            {
                Element_ = new Graphics.Types.Element
                {
                    Uuid = new UUID { String = Guid.NewGuid().ToString() },
                    Name = Lang2Name,
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
        return presentation;
    }

    /// <summary>
    /// Validates whether the given template file exists and has the required
    /// text elements (Lang1 for Single mode, Lang1 + Lang2 for Dual mode).
    /// If templatePath is empty or null, validates against the internal built-in template.
    /// </summary>
    public TemplateValidationResult ValidateTemplate(string? templatePath, LanguageMode mode)
    {
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            // Internal default template is built-in and always valid
            return new TemplateValidationResult(true, null, HasLang1: true, HasLang2: true);
        }

        if (!File.Exists(templatePath))
            return new TemplateValidationResult(false, $"Template file does not exist: {templatePath}");

        Presentation presentation;
        try
        {
            using var stream = File.OpenRead(templatePath);
            presentation = Presentation.Parser.ParseFrom(stream);
        }
        catch (Exception ex)
        {
            return new TemplateValidationResult(false, $"Failed to parse ProPresenter template: {ex.Message}");
        }

        bool hasLang1 = false;
        bool hasLang2 = false;

        foreach (var cue in presentation.Cues)
        {
            foreach (var action in cue.Actions)
            {
                var baseSlide = action.Slide?.Presentation?.BaseSlide;
                if (baseSlide is null) continue;

                foreach (var el in baseSlide.Elements)
                {
                    string elName = el.Element_?.Name ?? string.Empty;
                    if (string.Equals(elName, Lang1Name, StringComparison.OrdinalIgnoreCase))
                        hasLang1 = true;
                    if (string.Equals(elName, Lang2Name, StringComparison.OrdinalIgnoreCase))
                        hasLang2 = true;
                }
            }
        }

        if (!hasLang1)
        {
            return new TemplateValidationResult(
                false,
                $"Template is missing required text element '{Lang1Name}'.",
                hasLang1,
                hasLang2);
        }

        if (mode == LanguageMode.Dual && !hasLang2)
        {
            return new TemplateValidationResult(
                false,
                $"Dual Language mode requires both '{Lang1Name}' and '{Lang2Name}' text elements, but '{Lang2Name}' was not found.",
                hasLang1,
                hasLang2);
        }

        return new TemplateValidationResult(true, null, hasLang1, hasLang2);
    }

    /// <summary>
    /// Generates a ProPresenter (.pro) file from the template and project data.
    /// If templatePath is null or not found, uses the internal built-in template.
    /// Clones cues, replaces RTF payloads in Lang1/Lang2, organizes cue groups,
    /// and saves to the output path.
    /// </summary>
    public string GeneratePresentation(string? templatePath, SongProject project, string outputPath)
    {
        Presentation presentation;
        if (!string.IsNullOrWhiteSpace(templatePath) && File.Exists(templatePath))
        {
            using var stream = File.OpenRead(templatePath);
            presentation = Presentation.Parser.ParseFrom(stream);
        }
        else
        {
            presentation = CreateInternalTemplate(project.LanguageMode);
        }

        // Find the template cue to clone (the first cue containing a presentation slide)
        Cue? templateCue = null;
        foreach (var cue in presentation.Cues)
        {
            if (cue.Actions.Any(a => a.Slide?.Presentation?.BaseSlide is not null))
            {
                templateCue = cue;
                break;
            }
        }

        if (templateCue is null)
            throw new InvalidOperationException("The template does not contain any slide cues.");

        // Resolve presentation title
        string resolvedTitle = LyricParserService.ResolveSongTitle(project.Title, project.Slides);
        presentation.Name = resolvedTitle;
        if (presentation.Uuid is null || string.IsNullOrEmpty(presentation.Uuid.String))
        {
            presentation.Uuid = new UUID { String = Guid.NewGuid().ToString() };
        }

        // Clear existing presentation cues and groups
        presentation.Cues.Clear();
        presentation.CueGroups.Clear();

        if (project.Slides.Count == 0)
        {
            // Empty slides: write out presentation as-is with title
            WritePresentationToFile(presentation, outputPath);
            return outputPath;
        }

        var generatedCues = new List<(LyricSlide Slide, Cue Cue)>();

        // Clone and populate a cue for each slide
        foreach (var slide in project.Slides)
        {
            var newCue = templateCue.Clone();
            newCue.Uuid = new UUID { String = Guid.NewGuid().ToString() };
            newCue.Name = string.IsNullOrWhiteSpace(slide.GroupName)
                ? $"Slide {slide.SortOrder + 1}"
                : slide.GroupName;

            // Update internal IDs and inject text
            foreach (var action in newCue.Actions)
            {
                action.Uuid = new UUID { String = Guid.NewGuid().ToString() };

                var baseSlide = action.Slide?.Presentation?.BaseSlide;
                if (baseSlide is null) continue;

                baseSlide.Uuid = new UUID { String = Guid.NewGuid().ToString() };

                foreach (var el in baseSlide.Elements)
                {
                    if (el.Element_?.Text is null) continue;

                    string elName = el.Element_.Name ?? string.Empty;

                    if (string.Equals(elName, Lang1Name, StringComparison.OrdinalIgnoreCase))
                    {
                        string curRtf = el.Element_.Text.RtfData.ToStringUtf8();
                        string newRtf = RtfUnicodeEncoder.ReplaceRtfPayload(curRtf, slide.Lang1Text);
                        el.Element_.Text.RtfData = ByteString.CopyFromUtf8(newRtf);

                        // Clear stale character-range custom attributes from the template
                        el.Element_.Text.Attributes?.CustomAttributes.Clear();
                    }
                    else if (string.Equals(elName, Lang2Name, StringComparison.OrdinalIgnoreCase))
                    {
                        string curRtf = el.Element_.Text.RtfData.ToStringUtf8();
                        string newText = project.LanguageMode == LanguageMode.Dual ? slide.Lang2Text : string.Empty;
                        string newRtf = RtfUnicodeEncoder.ReplaceRtfPayload(curRtf, newText);
                        el.Element_.Text.RtfData = ByteString.CopyFromUtf8(newRtf);

                        // Clear stale character-range custom attributes from the template
                        el.Element_.Text.Attributes?.CustomAttributes.Clear();
                    }
                }
            }

            presentation.Cues.Add(newCue);
            generatedCues.Add((slide, newCue));
        }

        // Organize into CueGroups
        // Consecutive slides with the same group name are collected into the same CueGroup
        var groups = new List<(string Name, List<UUID> CueIds)>();
        foreach (var (slide, cue) in generatedCues)
        {
            string groupName = string.IsNullOrWhiteSpace(slide.GroupName) ? "Verse" : slide.GroupName;

            if (groups.Count > 0 && groups[^1].Name.Equals(groupName, StringComparison.OrdinalIgnoreCase))
            {
                groups[^1].CueIds.Add(cue.Uuid);
            }
            else
            {
                groups.Add((groupName, new List<UUID> { cue.Uuid }));
            }
        }

        foreach (var (groupName, cueIds) in groups)
        {
            var cueGroup = new Presentation.Types.CueGroup
            {
                Group = new Group
                {
                    Uuid = new UUID { String = Guid.NewGuid().ToString() },
                    Name = groupName
                }
            };
            cueGroup.CueIdentifiers.AddRange(cueIds);
            presentation.CueGroups.Add(cueGroup);
        }

        WritePresentationToFile(presentation, outputPath);
        return outputPath;
    }

    private static void WritePresentationToFile(Presentation presentation, string outputPath)
    {
        string? dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        using var fileStream = File.Create(outputPath);
        presentation.WriteTo(fileStream);
    }
}
