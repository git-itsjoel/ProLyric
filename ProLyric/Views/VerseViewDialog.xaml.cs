using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ProLyric.Models;
using ProLyric.Services;

namespace ProLyric.Views;

public partial class VerseViewDialog : Window
{
    private readonly SettingsService _settingsService;
    private readonly LyricParserService _parser = new();
    private VerseViewDbService? _dbService;

    public string ResultTitle { get; private set; } = string.Empty;
    public IReadOnlyList<LyricSlide> ResultSlides { get; private set; } = [];
    public LanguageMode ResultMode { get; private set; } = LanguageMode.Dual;

    public VerseViewDialog(SettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;

        Loaded += OnLoaded;
        Closed += (_, _) => _dbService?.Dispose();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        string path = _settingsService.Current.VerseViewDbPath;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            LoadDb(path);
        }
        else
        {
            TxtDbPath.Text = "No database connected";
            BtnChangeDb.Content = "+ Add SongDB...";
            if (BrdNoDbNotice is not null) BrdNoDbNotice.Visibility = Visibility.Visible;
            PromptSelectDb();
        }
    }

    private void PromptSelectDb()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select SongDB Database File",
            Filter = "Song Database Files (*.db;*.sqlite;*.xml)|*.db;*.sqlite;*.xml|All Files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == true)
        {
            _settingsService.Update(s => s.VerseViewDbPath = dlg.FileName);
            LoadDb(dlg.FileName);
        }
        else if (string.IsNullOrEmpty(_settingsService.Current.VerseViewDbPath) || !File.Exists(_settingsService.Current.VerseViewDbPath))
        {
            TxtDbPath.Text = "No database connected";
            BtnChangeDb.Content = "+ Add SongDB...";
            if (BrdNoDbNotice is not null) BrdNoDbNotice.Visibility = Visibility.Visible;
        }
    }

    private void LoadDb(string path)
    {
        try
        {
            _dbService?.Dispose();
            _dbService = new VerseViewDbService(path);
            _dbService.Open();

            TxtDbPath.Text = Path.GetFileName(path);
            TxtDbPath.ToolTip = path;
            BtnChangeDb.Content = "Change DB...";
            if (BrdNoDbNotice is not null) BrdNoDbNotice.Visibility = Visibility.Collapsed;

            PerformSearch("");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open SongDB database:\n{ex.Message}", "SongDB Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnChangeDbClick(object sender, RoutedEventArgs e)
    {
        PromptSelectDb();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        PerformSearch(TxtSearch.Text.Trim());
    }

    private void OnSearchClick(object sender, RoutedEventArgs e)
    {
        PerformSearch(TxtSearch.Text.Trim());
    }

    private void PerformSearch(string query)
    {
        if (_dbService is null) return;
        try
        {
            var songs = _dbService.SearchSongs(query);
            LstSongs.ItemsSource = songs;
            if (songs.Count > 0)
            {
                LstSongs.SelectedIndex = 0;
                LstSongs.ScrollIntoView(songs[0]);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Search error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSongSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstSongs.SelectedItem is not VerseViewSongSummary summary || _dbService is null)
        {
            TxtPreviewTitle.Text = "Select a song to preview";
            TxtPreviewPrimary.Text = "";
            TxtPreviewSecondary.Text = "";
            return;
        }

        try
        {
            var content = _dbService.LoadSong(summary.Id, VerseViewLanguageChoice.Both);
            if (content is not null)
            {
                TxtPreviewTitle.Text = summary.DisplayTitle;
                TxtPreviewPrimary.Text = content.PrimaryLyrics;
                TxtPreviewSecondary.Text = content.SecondaryLyrics;
                TxtPreviewPrimary.ScrollToHome();
                TxtPreviewSecondary.ScrollToHome();
            }
        }
        catch { }
    }

    private void OnLoadSongClick(object sender, RoutedEventArgs e)
    {
        if (LstSongs.SelectedItem is not VerseViewSongSummary summary || _dbService is null)
        {
            MessageBox.Show("Please select a song first.", "No Song Selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var langChoice = CmbLanguage.SelectedIndex switch
        {
            1 => VerseViewLanguageChoice.PrimaryOnly,
            2 => VerseViewLanguageChoice.SecondaryOnly,
            _ => VerseViewLanguageChoice.Both
        };

        var content = _dbService.LoadSong(summary.Id, langChoice);
        if (content is null)
        {
            MessageBox.Show("Could not load song content.", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        bool byLineCount = RbLineCount.IsChecked == true;
        int linesPerSlide = int.TryParse(TxtLinesPerSlide.Text, out int lps) && lps > 0 ? lps : 4;

        ResultTitle = !string.IsNullOrWhiteSpace(content.Title) ? content.Title : summary.Title;

        if (langChoice == VerseViewLanguageChoice.PrimaryOnly)
        {
            ResultMode = LanguageMode.Single;
            ResultSlides = byLineCount
                ? _parser.ParseByLineCount(content.PrimaryLyrics, linesPerSlide)
                : _parser.ParseByParagraph(content.PrimaryLyrics);
        }
        else if (langChoice == VerseViewLanguageChoice.SecondaryOnly)
        {
            ResultMode = LanguageMode.Single;
            ResultSlides = byLineCount
                ? _parser.ParseByLineCount(content.SecondaryLyrics, linesPerSlide)
                : _parser.ParseByParagraph(content.SecondaryLyrics);
        }
        else // Both
        {
            ResultMode = LanguageMode.Dual;
            var s1 = byLineCount
                ? _parser.ParseByLineCount(content.PrimaryLyrics, linesPerSlide)
                : _parser.ParseByParagraph(content.PrimaryLyrics);

            var s2 = byLineCount
                ? _parser.ParseByLineCount(content.SecondaryLyrics, linesPerSlide)
                : _parser.ParseByParagraph(content.SecondaryLyrics);

            int count = Math.Max(s1.Count, s2.Count);
            var merged = new List<LyricSlide>(count);
            for (int i = 0; i < count; i++)
            {
                var slide1 = i < s1.Count ? s1[i] : null;
                var slide2 = i < s2.Count ? s2[i] : null;

                merged.Add(new LyricSlide
                {
                    SortOrder = i,
                    GroupName = slide1?.GroupName ?? slide2?.GroupName ?? string.Empty,
                    Lang1Text = slide1?.Lang1Text ?? string.Empty,
                    Lang2Text = slide2?.Lang1Text ?? string.Empty
                });
            }
            ResultSlides = merged;
        }

        DialogResult = true;
        Close();
    }
}
