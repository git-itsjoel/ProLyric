using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ProLyric.Models;
using ProLyric.Services;
using ProLyric.Views;

namespace ProLyric.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly SettingsService _settingsService;
    private readonly SongLibraryDbService _libraryDb;
    private readonly ProPresenterService _proService;

    [ObservableProperty]
    private int _currentSongId;

    [ObservableProperty]
    private string _songTitle = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDualMode))]
    private LanguageMode _languageMode = LanguageMode.Dual;

    public bool IsDualMode => LanguageMode == LanguageMode.Dual;

    [ObservableProperty]
    private string _templatePath = string.Empty;

    [ObservableProperty]
    private bool _isTemplateValid = true;

    [ObservableProperty]
    private string _templateStatusText = "Built-in ProPresenter Template";

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private SlideViewModel? _selectedSlide;

    public ObservableCollection<SlideViewModel> Slides { get; } = [];

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _libraryDb = new SongLibraryDbService();
        _proService = new ProPresenterService();

        // Built-in internal template is always ready
        IsTemplateValid = true;
        TemplateStatusText = "Built-in ProPresenter Template";

        // Add 1 default slide
        AddSlide();
    }

    // -----------------------------------------------------------------------
    //  Mode & Template
    // -----------------------------------------------------------------------

    partial void OnLanguageModeChanged(LanguageMode value)
    {
        RevalidateTemplate();
    }

    public void RevalidateTemplate()
    {
        var result = _proService.ValidateTemplate(TemplatePath, LanguageMode);
        IsTemplateValid = result.IsValid;
        TemplateStatusText = result.IsValid
            ? "Built-in ProPresenter Template"
            : $"✗ {result.ErrorMessage}";
    }

    [RelayCommand]
    private void BrowseTemplate()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select ProPresenter 7+ Template (.pro)",
            Filter = "ProPresenter Presentation (*.pro)|*.pro|All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            TemplatePath = dlg.FileName;
            _settingsService.Update(s => s.DefaultTemplatePath = dlg.FileName);
            RevalidateTemplate();
            StatusMessage = $"Template loaded: {Path.GetFileName(dlg.FileName)}";
        }
    }

    [RelayCommand]
    private void SetSingleMode() => LanguageMode = LanguageMode.Single;

    [RelayCommand]
    private void SetDualMode() => LanguageMode = LanguageMode.Dual;

    // -----------------------------------------------------------------------
    //  Slide Operations
    // -----------------------------------------------------------------------

    [RelayCommand]
    private void AddSlide()
    {
        string defaultGroup = SelectedSlide?.GroupName ?? (Slides.Count == 0 ? "Verse 1" : "Verse");
        var newSlide = new SlideViewModel
        {
            SortOrder = Slides.Count,
            GroupName = defaultGroup
        };
        Slides.Add(newSlide);
        SelectedSlide = newSlide;
        RenumbersSlides();
    }

    [RelayCommand]
    private void RemoveSlide()
    {
        if (SelectedSlide is null) return;
        int idx = Slides.IndexOf(SelectedSlide);
        Slides.Remove(SelectedSlide);
        RenumbersSlides();

        if (Slides.Count > 0)
        {
            int nextIdx = Math.Clamp(idx, 0, Slides.Count - 1);
            SelectedSlide = Slides[nextIdx];
        }
        else
        {
            SelectedSlide = null;
        }
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedSlide is null) return;
        int idx = Slides.IndexOf(SelectedSlide);
        if (idx <= 0) return;

        Slides.Move(idx, idx - 1);
        RenumbersSlides();
        SelectedSlide = Slides[idx - 1];
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedSlide is null) return;
        int idx = Slides.IndexOf(SelectedSlide);
        if (idx < 0 || idx >= Slides.Count - 1) return;

        Slides.Move(idx, idx + 1);
        RenumbersSlides();
        SelectedSlide = Slides[idx + 1];
    }

    [RelayCommand]
    public void TransliterateAllSlides()
    {
        if (Slides.Count == 0) return;
        int count = 0;
        foreach (var slide in Slides)
        {
            if (!string.IsNullOrWhiteSpace(slide.Lang1Text))
            {
                slide.Lang2Text = MalayalamTransliteratorService.Transliterate(slide.Lang1Text);
                count++;
            }
        }
        StatusMessage = $"Transliterated {count} slide(s) from Malayalam to Manglish.";
    }

    [RelayCommand]
    public void TransliterateCurrentSlide()
    {
        if (SelectedSlide is null) return;
        if (string.IsNullOrWhiteSpace(SelectedSlide.Lang1Text))
        {
            StatusMessage = "Lang 1 text is empty.";
            return;
        }

        SelectedSlide.Lang2Text = MalayalamTransliteratorService.Transliterate(SelectedSlide.Lang1Text);
        StatusMessage = "Transliterated Lang 1 to Lang 2.";
    }

    [RelayCommand]
    public void SplitCurrentSlide()
    {
        SplitSlide(-1, isLang1: true);
    }

    /// <summary>
    /// Splits the currently selected slide into two distinct slides at the given caret/click position.
    /// In Dual mode, splits the secondary language text proportionally by line count.
    /// If caretPosition &lt; 0, splits text by line midpoint.
    /// </summary>
    public void SplitSlide(int caretPosition = -1, bool isLang1 = true)
    {
        if (SelectedSlide is null) return;

        int currentIndex = Slides.IndexOf(SelectedSlide);
        if (currentIndex < 0) return;

        string curLang1 = SelectedSlide.Lang1Text ?? string.Empty;
        string curLang2 = SelectedSlide.Lang2Text ?? string.Empty;

        string newLang1Part1;
        string newLang1Part2;
        string newLang2Part1;
        string newLang2Part2;

        if (isLang1)
        {
            (newLang1Part1, newLang1Part2) = SplitStringAtCaret(curLang1, caretPosition);

            // In Dual Mode, split Lang2 proportionally based on line count of Lang1
            if (IsDualMode && !string.IsNullOrEmpty(curLang2))
            {
                int linesInPart1 = CountLines(newLang1Part1);
                (newLang2Part1, newLang2Part2) = SplitLinesAtCount(curLang2, linesInPart1);
            }
            else
            {
                newLang2Part1 = curLang2;
                newLang2Part2 = string.Empty;
            }
        }
        else
        {
            // Caret / Click was in Lang 2
            (newLang2Part1, newLang2Part2) = SplitStringAtCaret(curLang2, caretPosition);

            // Split Lang1 proportionally based on line count of Lang2
            if (!string.IsNullOrEmpty(curLang1))
            {
                int linesInPart1 = CountLines(newLang2Part1);
                (newLang1Part1, newLang1Part2) = SplitLinesAtCount(curLang1, linesInPart1);
            }
            else
            {
                newLang1Part1 = curLang1;
                newLang1Part2 = string.Empty;
            }
        }

        // Update current slide with first part
        SelectedSlide.Lang1Text = newLang1Part1;
        SelectedSlide.Lang2Text = newLang2Part1;

        // Create new slide with second part
        var newSlide = new SlideViewModel
        {
            GroupName = SelectedSlide.GroupName, // Inherit section
            Lang1Text = newLang1Part2,
            Lang2Text = newLang2Part2,
            SortOrder = currentIndex + 1
        };

        int insertIndex = currentIndex + 1;
        Slides.Insert(insertIndex, newSlide);
        RenumbersSlides();
        SelectedSlide = newSlide;

        StatusMessage = $"Slide split into #{currentIndex + 1} and #{insertIndex + 1}.";
    }

    private static (string Part1, string Part2) SplitStringAtCaret(string text, int caret)
    {
        if (string.IsNullOrEmpty(text))
            return (string.Empty, string.Empty);

        if (caret < 0)
        {
            // No caret specified: split by lines at midpoint
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            if (lines.Length <= 1)
            {
                return (text, string.Empty);
            }
            int mid = (lines.Length + 1) / 2;
            string p1 = string.Join("\n", lines.Take(mid)).TrimEnd();
            string p2 = string.Join("\n", lines.Skip(mid)).TrimStart();
            return (p1, p2);
        }

        if (caret >= text.Length)
        {
            return (text.TrimEnd(), string.Empty);
        }

        if (caret <= 0)
        {
            return (string.Empty, text.TrimStart());
        }

        string part1 = text[..caret].TrimEnd('\r', '\n');
        string part2 = text[caret..].TrimStart('\r', '\n');
        return (part1, part2);
    }

    private static (string Part1, string Part2) SplitLinesAtCount(string text, int lineCount)
    {
        if (string.IsNullOrEmpty(text) || lineCount <= 0)
            return (string.Empty, text);

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lineCount >= lines.Length)
        {
            return (text, string.Empty);
        }

        string p1 = string.Join("\n", lines.Take(lineCount)).TrimEnd();
        string p2 = string.Join("\n", lines.Skip(lineCount)).TrimStart();
        return (p1, p2);
    }

    private static int CountLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Length;
    }

    private void RenumbersSlides()
    {
        for (int i = 0; i < Slides.Count; i++)
        {
            Slides[i].SortOrder = i;
        }
    }

    // -----------------------------------------------------------------------
    //  New / Save / Export
    // -----------------------------------------------------------------------

    [RelayCommand]
    private void NewSong()
    {
        CurrentSongId = 0;
        SongTitle = string.Empty;
        Slides.Clear();
        AddSlide();
        StatusMessage = "New song started.";
    }

    [RelayCommand]
    private void SaveSong()
    {
        var project = BuildProject();
        _libraryDb.SaveSong(project);
        CurrentSongId = project.Id;
        StatusMessage = $"Saved '{project.Title}' to local library.";
        MessageBox.Show($"Song '{project.Title}' saved to library successfully!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    [RelayCommand]
    private void ExportPro()
    {
        if (Slides.Count == 0)
        {
            MessageBox.Show("Please add at least one slide before exporting.", "No Slides", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var project = BuildProject();
        string resolvedTitle = LyricParserService.ResolveSongTitle(project.Title, project.Slides);

        // Determine destination folder
        string defaultFolder = _settingsService.Current.DefaultExportFolder;
        if (string.IsNullOrEmpty(defaultFolder) || !Directory.Exists(defaultFolder))
        {
            defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ProLyric");
            Directory.CreateDirectory(defaultFolder);
        }

        var sfd = new SaveFileDialog
        {
            Title = "Export ProPresenter Presentation",
            Filter = "ProPresenter Presentation (*.pro)|*.pro",
            InitialDirectory = defaultFolder,
            FileName = $"{resolvedTitle}.pro"
        };

        if (sfd.ShowDialog() != true) return;

        try
        {
            string outPath = sfd.FileName;
            _proService.GeneratePresentation(TemplatePath, project, outPath);

            // Update project with export metadata and persist to library
            project.ExportedFileName = Path.GetFileName(outPath);
            project.ExportedFilePath = outPath;
            project.Title = resolvedTitle;
            _libraryDb.SaveSong(project);
            CurrentSongId = project.Id;
            SongTitle = resolvedTitle;

            // Remember export folder
            string? exportDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(exportDir))
            {
                _settingsService.Update(s => s.DefaultExportFolder = exportDir);
            }

            StatusMessage = $"Successfully exported '{Path.GetFileName(outPath)}' and saved to library.";
            MessageBox.Show($"Presentation successfully exported to:\n{outPath}", "Export Succeeded", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // -----------------------------------------------------------------------
    //  Dialog Launchers
    // -----------------------------------------------------------------------

    [RelayCommand]
    private void OpenLibrary()
    {
        var dlg = new LibraryDialog(_libraryDb)
        {
            Owner = Application.Current.MainWindow
        };

        if (dlg.ShowDialog() == true && dlg.SelectedSongProject is not null)
        {
            LoadFromProject(dlg.SelectedSongProject);
            StatusMessage = $"Loaded '{SongTitle}' from library.";
        }
    }

    [RelayCommand]
    private void OpenSongDb()
    {
        var dlg = new VerseViewDialog(_settingsService)
        {
            Owner = Application.Current.MainWindow
        };

        if (dlg.ShowDialog() == true && dlg.ResultSlides.Count > 0)
        {
            SongTitle = dlg.ResultTitle;
            LanguageMode = dlg.ResultMode;
            Slides.Clear();
            foreach (var s in dlg.ResultSlides)
            {
                Slides.Add(SlideViewModel.FromModel(s));
            }
            RenumbersSlides();
            if (Slides.Count > 0) SelectedSlide = Slides[0];
            CurrentSongId = 0; // Fresh imported song
            StatusMessage = $"Imported '{SongTitle}' from SongDB ({Slides.Count} slides).";
        }
    }

    [RelayCommand]
    private void OpenVerseView() => OpenSongDb();

    [RelayCommand]
    private void OpenBulkPaste()
    {
        var dlg = new BulkPasteDialog(LanguageMode)
        {
            Owner = Application.Current.MainWindow
        };

        if (dlg.ShowDialog() == true && dlg.ResultSlides.Count > 0)
        {
            // If current editor only has 1 empty slide, replace it
            if (Slides.Count == 1 && string.IsNullOrWhiteSpace(Slides[0].Lang1Text))
            {
                Slides.Clear();
            }

            foreach (var s in dlg.ResultSlides)
            {
                Slides.Add(SlideViewModel.FromModel(s));
            }
            RenumbersSlides();
            if (Slides.Count > 0) SelectedSlide = Slides[0];

            if (string.IsNullOrWhiteSpace(SongTitle))
            {
                SongTitle = LyricParserService.ResolveSongTitle("", dlg.ResultSlides);
            }

            StatusMessage = $"Imported {dlg.ResultSlides.Count} slides via Bulk Paste.";
        }
    }

    // -----------------------------------------------------------------------
    //  Helpers
    // -----------------------------------------------------------------------

    private SongProject BuildProject()
    {
        var p = new SongProject
        {
            Id = CurrentSongId,
            Title = SongTitle.Trim(),
            LanguageMode = LanguageMode,
            TemplatePath = TemplatePath
        };

        foreach (var s in Slides)
        {
            p.Slides.Add(s.ToModel());
        }

        return p;
    }

    private void LoadFromProject(SongProject project)
    {
        CurrentSongId = project.Id;
        SongTitle = project.Title;
        LanguageMode = project.LanguageMode;
        if (!string.IsNullOrEmpty(project.TemplatePath) && File.Exists(project.TemplatePath))
        {
            TemplatePath = project.TemplatePath;
        }

        Slides.Clear();
        foreach (var s in project.Slides)
        {
            Slides.Add(SlideViewModel.FromModel(s));
        }
        RenumbersSlides();
        if (Slides.Count > 0) SelectedSlide = Slides[0];
        RevalidateTemplate();
    }
}
