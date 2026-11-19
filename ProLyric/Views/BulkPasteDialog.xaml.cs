using System.Windows;
using ProLyric.Models;
using ProLyric.Services;

namespace ProLyric.Views;

public partial class BulkPasteDialog : Window
{
    private readonly LanguageMode _mode;
    private readonly LyricParserService _parser = new();

    public IReadOnlyList<LyricSlide> ResultSlides { get; private set; } = [];

    public BulkPasteDialog(LanguageMode mode)
    {
        InitializeComponent();
        _mode = mode;

        if (_mode == LanguageMode.Single)
        {
            Lang2Column.Width = new GridLength(0);
            Lang2Panel.Visibility = Visibility.Collapsed;
            DividerPanel.Visibility = Visibility.Collapsed;
            Width = 560;
        }
    }

    private void OnTransliterateClick(object sender, RoutedEventArgs e)
    {
        string l1 = TxtLang1.Text;
        if (string.IsNullOrWhiteSpace(l1))
        {
            MessageBox.Show("Language 1 text is empty.", "Transliterate", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        TxtLang2.Text = MalayalamTransliteratorService.Transliterate(l1);
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        string text1 = TxtLang1.Text;
        if (string.IsNullOrWhiteSpace(text1))
        {
            MessageBox.Show("Please enter some lyric text before importing.", "Empty Text", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool byLineCount = RbLineCount.IsChecked == true;
        int linesPerSlide = int.TryParse(TxtLinesPerSlide.Text, out int lps) && lps > 0 ? lps : 4;

        IReadOnlyList<LyricSlide> slides1 = byLineCount
            ? _parser.ParseByLineCount(text1, linesPerSlide)
            : _parser.ParseByParagraph(text1);

        if (_mode == LanguageMode.Dual)
        {
            string text2 = TxtLang2.Text;
            IReadOnlyList<LyricSlide> slides2 = byLineCount
                ? _parser.ParseByLineCount(text2, linesPerSlide)
                : _parser.ParseByParagraph(text2);

            // Pair slides up
            int count = Math.Max(slides1.Count, slides2.Count);
            var merged = new List<LyricSlide>(count);
            for (int i = 0; i < count; i++)
            {
                var s1 = i < slides1.Count ? slides1[i] : null;
                var s2 = i < slides2.Count ? slides2[i] : null;

                merged.Add(new LyricSlide
                {
                    SortOrder = i,
                    GroupName = s1?.GroupName ?? s2?.GroupName ?? string.Empty,
                    Lang1Text = s1?.Lang1Text ?? string.Empty,
                    Lang2Text = s2?.Lang1Text ?? string.Empty // in s2, its parsed content is in Lang1Text
                });
            }
            ResultSlides = merged;
        }
        else
        {
            ResultSlides = slides1;
        }

        DialogResult = true;
        Close();
    }
}
