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
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            e.Handled = true;
            if (TxtLang1.IsFocused)
            {
                SplitActiveSlide(TxtLang1.SelectionStart, isLang1: true);
            }
            else if (TxtLang2.IsFocused)
            {
                SplitActiveSlide(TxtLang2.SelectionStart, isLang1: false);
            }
            else
            {
                SplitActiveSlide(-1, isLang1: true);
            }
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
