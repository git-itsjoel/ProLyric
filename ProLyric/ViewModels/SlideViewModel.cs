using CommunityToolkit.Mvvm.ComponentModel;
using ProLyric.Models;

namespace ProLyric.ViewModels;

public partial class SlideViewModel : ObservableObject
{
    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SlideNumber))]
    private int _sortOrder;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _groupName = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayPreview))]
    private string _lang1Text = string.Empty;

    [ObservableProperty]
    private string _lang2Text = string.Empty;

    public int SlideNumber => SortOrder + 1;

    public string DisplayName => string.IsNullOrWhiteSpace(GroupName)
        ? $"Slide {SlideNumber}"
        : $"{GroupName} (Slide {SlideNumber})";

    public string DisplayPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Lang1Text)) return "(Empty slide)";
            var firstLine = Lang1Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return string.IsNullOrWhiteSpace(firstLine) ? "(Empty slide)" : firstLine.Trim();
        }
    }

    public LyricSlide ToModel() => new()
    {
        Id = Id,
        SortOrder = SortOrder,
        GroupName = GroupName,
        Lang1Text = Lang1Text,
        Lang2Text = Lang2Text
    };

    public static SlideViewModel FromModel(LyricSlide model) => new()
    {
        Id = model.Id,
        SortOrder = model.SortOrder,
        GroupName = model.GroupName,
        Lang1Text = model.Lang1Text,
        Lang2Text = model.Lang2Text
    };
}
