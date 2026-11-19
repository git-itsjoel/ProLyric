using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProLyric;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void TxtLang_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (sender is TextBox tb)
            {
                e.Handled = true;
                tb.Focus();
                Point pt = e.GetPosition(tb);
                int charIndex = tb.GetCharacterIndexFromPoint(pt, true);
                if (charIndex >= 0 && charIndex < tb.Text.Length)
                {
                    Rect rect = tb.GetRectFromCharacterIndex(charIndex);
                    if (pt.X > rect.Left + rect.Width / 2)
                    {
                        charIndex++;
                    }
                }
                else if (charIndex < 0 || pt.Y > tb.ActualHeight / 2)
                {
                    charIndex = tb.Text.Length;
                }

                bool isLang1 = (tb == TxtLang1);
                SplitActiveSlide(charIndex, isLang1);
            }
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0;
        bool shift = (mods & ModifierKeys.Shift) != 0;
        bool alt = (mods & ModifierKeys.Alt) != 0;

        Key key = e.Key == Key.System ? e.SystemKey : (e.Key == Key.ImeProcessed ? e.ImeProcessedKey : e.Key);

        if (ctrl && !shift && !alt && key == Key.Enter)
        {
            e.Handled = true;
            if (TxtLang1.IsFocused)
                SplitActiveSlide(TxtLang1.SelectionStart, isLang1: true);
            else if (TxtLang2.IsFocused)
                SplitActiveSlide(TxtLang2.SelectionStart, isLang1: false);
            else
                SplitActiveSlide(-1, isLang1: true);
            return;
        }

        if (ctrl && !shift && !alt && key == Key.N)
        {
            e.Handled = true;
            if (DataContext is ViewModels.MainViewModel vm)
                vm.NewSongCommand.Execute(null);
            return;
        }

        if (ctrl && !shift && !alt && key == Key.S)
        {
            e.Handled = true;
            if (DataContext is ViewModels.MainViewModel vm)
                vm.SaveSongCommand.Execute(null);
            return;
        }

        if (ctrl && !shift && !alt && key == Key.E)
        {
            e.Handled = true;
            if (DataContext is ViewModels.MainViewModel vm)
                vm.ExportProCommand.Execute(null);
            return;
        }

        if (ctrl && !shift && !alt && key == Key.O)
        {
            e.Handled = true;
            if (DataContext is ViewModels.MainViewModel vm)
                vm.OpenLibraryCommand.Execute(null);
            return;
        }

        if (ctrl && !shift && !alt && key == Key.D)
        {
            e.Handled = true;
            if (DataContext is ViewModels.MainViewModel vm)
                vm.OpenSongDbCommand.Execute(null);
            return;
        }

        if (ctrl && !shift && !alt && key == Key.B)
        {
            e.Handled = true;
            if (DataContext is ViewModels.MainViewModel vm)
                vm.OpenBulkPasteCommand.Execute(null);
            return;
        }

        if (ctrl && shift && !alt && key == Key.T)
        {
            e.Handled = true;
            if (DataContext is ViewModels.MainViewModel vm)
                vm.TransliterateAllSlidesCommand.Execute(null);
            return;
        }

        if (ctrl && !shift && !alt && key == Key.T)
        {
            e.Handled = true;
            if (DataContext is ViewModels.MainViewModel vm)
                vm.TransliterateCurrentSlideCommand.Execute(null);
            return;
        }

        if (ctrl && !shift && !alt && (key == Key.M || key == Key.OemPlus || key == Key.Add))
        {
            e.Handled = true;
            if (DataContext is ViewModels.MainViewModel vm)
                vm.AddSlideCommand.Execute(null);
            return;
        }

        if (alt && !ctrl && !shift && key == Key.Up)
        {
            e.Handled = true;
            if (DataContext is ViewModels.MainViewModel vm)
                vm.MoveUpCommand.Execute(null);
            return;
        }

        if (alt && !ctrl && !shift && key == Key.Down)
        {
            e.Handled = true;
            if (DataContext is ViewModels.MainViewModel vm)
                vm.MoveDownCommand.Execute(null);
            return;
        }
    }

    private void SplitActiveSlide(int caret, bool isLang1)
    {
        if (DataContext is not ViewModels.MainViewModel vm || vm.SelectedSlide is null)
            return;

        vm.SplitSlide(caret, isLang1);

        Dispatcher.BeginInvoke(() =>
        {
            if (isLang1)
            {
                TxtLang1.Focus();
                TxtLang1.CaretIndex = 0;
            }
            else
            {
                TxtLang2.Focus();
                TxtLang2.CaretIndex = 0;
            }
        });
    }
}
