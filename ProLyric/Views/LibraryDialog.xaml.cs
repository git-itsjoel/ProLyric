using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProLyric.Models;
using ProLyric.Services;

namespace ProLyric.Views;

public partial class LibraryDialog : Window
{
    private readonly SongLibraryDbService _dbService;

    public SongProject? SelectedSongProject { get; private set; }

    public LibraryDialog(SongLibraryDbService dbService)
    {
        InitializeComponent();
        _dbService = dbService;

        Loaded += (_, _) => RefreshList();
    }

    private void RefreshList()
    {
        string q = TxtSearch.Text.Trim();
        var songs = string.IsNullOrEmpty(q)
            ? _dbService.GetAllSongs()
            : _dbService.SearchSongs(q);

        DgSongs.ItemsSource = songs;
        TxtSongCount.Text = $"{songs.Count} {(songs.Count == 1 ? "song" : "songs")} saved";

        UpdateButtons();
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshList();
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool hasSelection = DgSongs.SelectedItem is SongProject;
        BtnOpen.IsEnabled = hasSelection;
        BtnDuplicate.IsEnabled = hasSelection;
        BtnDelete.IsEnabled = hasSelection;
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        OpenSelectedSong();
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedSong();
    }

    private void OpenSelectedSong()
    {
        if (DgSongs.SelectedItem is not SongProject summary) return;

        var full = _dbService.LoadSong(summary.Id);
        if (full is null)
        {
            MessageBox.Show("Could not load song from library.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SelectedSongProject = full;
        DialogResult = true;
        Close();
    }

    private void OnDuplicateClick(object sender, RoutedEventArgs e)
    {
        if (DgSongs.SelectedItem is not SongProject summary) return;

        var full = _dbService.LoadSong(summary.Id);
        if (full is null) return;

        full.Id = 0;
        full.Title = $"{full.Title} (Copy)";
        foreach (var s in full.Slides) s.Id = 0;

        _dbService.SaveSong(full);
        RefreshList();
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (DgSongs.SelectedItem is not SongProject summary) return;

        var confirm = MessageBox.Show(
            $"Are you sure you want to delete '{summary.Title}' from the library?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            _dbService.DeleteSong(summary.Id);
            RefreshList();
        }
    }
}
