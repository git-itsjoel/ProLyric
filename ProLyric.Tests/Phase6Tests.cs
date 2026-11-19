using System.IO;
using Pro.SerializationInterop.RVProtoData;
using ProLyric.Models;
using ProLyric.Services;
using ProLyric.ViewModels;

namespace ProLyric.Tests;

public class Phase6Tests
{
    [Fact]
    public void SlideViewModel_ModelConversion_PreservesAllProperties()
    {
        var model = new LyricSlide
        {
            Id = 42,
            SortOrder = 3,
            GroupName = "Bridge",
            Lang1Text = "Holy Spirit fall on us",
            Lang2Text = "Parishuddhathmaave"
        };

        var vm = SlideViewModel.FromModel(model);

        Assert.Equal(42, vm.Id);
        Assert.Equal(3, vm.SortOrder);
        Assert.Equal(4, vm.SlideNumber);
        Assert.Equal("Bridge", vm.GroupName);
        Assert.Equal("Bridge (Slide 4)", vm.DisplayName);
        Assert.Equal("Holy Spirit fall on us", vm.DisplayPreview);
        Assert.Equal("Holy Spirit fall on us", vm.Lang1Text);
        Assert.Equal("Parishuddhathmaave", vm.Lang2Text);

        var roundTrip = vm.ToModel();
        Assert.Equal(model.Id, roundTrip.Id);
        Assert.Equal(model.SortOrder, roundTrip.SortOrder);
        Assert.Equal(model.GroupName, roundTrip.GroupName);
        Assert.Equal(model.Lang1Text, roundTrip.Lang1Text);
        Assert.Equal(model.Lang2Text, roundTrip.Lang2Text);
    }

    [Fact]
    public void SlideViewModel_EmptySlide_DisplayPreviewFallback()
    {
        var vm = new SlideViewModel();
        Assert.Equal("(Empty slide)", vm.DisplayPreview);
        Assert.Equal("Slide 1", vm.DisplayName);
    }

    [Fact]
    public async Task MainViewModel_Initialization_StartsCleanWithOneSlide()
    {
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                var vm = new MainViewModel();
                Assert.Single(vm.Slides);
                Assert.NotNull(vm.SelectedSlide);
                Assert.Equal(LanguageMode.Dual, vm.LanguageMode);
                Assert.True(vm.IsDualMode);
                Assert.True(vm.IsTemplateValid);
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(await tcs.Task);
    }

    [Fact]
    public async Task MainViewModel_AddRemoveReorderSlides_UpdatesSortOrders()
    {
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                var vm = new MainViewModel();
                vm.AddSlideCommand.Execute(null);
                vm.AddSlideCommand.Execute(null);
                Assert.Equal(3, vm.Slides.Count);

                Assert.Equal(0, vm.Slides[0].SortOrder);
                Assert.Equal(1, vm.Slides[1].SortOrder);
                Assert.Equal(2, vm.Slides[2].SortOrder);

                // Move slide 2 up
                vm.SelectedSlide = vm.Slides[2];
                vm.MoveUpCommand.Execute(null);
                Assert.Equal(1, vm.SelectedSlide.SortOrder);

                // Remove slide
                vm.RemoveSlideCommand.Execute(null);
                Assert.Equal(2, vm.Slides.Count);
                Assert.Equal(0, vm.Slides[0].SortOrder);
                Assert.Equal(1, vm.Slides[1].SortOrder);

                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(await tcs.Task);
    }

    [Fact]
    public async Task MainViewModel_TransliterateAllSlides_ConvertsAllSlidesFromMalayalamToManglish()
    {
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                var vm = new MainViewModel();
                // Slide 0
                vm.Slides[0].Lang1Text = "ദൈവം വലിയവൻ";
                vm.Slides[0].Lang2Text = "";

                // Slide 1
                vm.AddSlideCommand.Execute(null);
                vm.Slides[1].Lang1Text = "യേശു ജീവിക്കുന്നു";
                vm.Slides[1].Lang2Text = "";

                // Slide 2
                vm.AddSlideCommand.Execute(null);
                vm.Slides[2].Lang1Text = "നന്ദി ദൈവമേ";
                vm.Slides[2].Lang2Text = "";

                // Transliterate every slide
                vm.TransliterateAllSlidesCommand.Execute(null);

                Assert.Equal("Daivam Valiyavan", vm.Slides[0].Lang2Text);
                Assert.Equal("Yeshu Jeevikkunnu", vm.Slides[1].Lang2Text);
                Assert.Equal("Nandi Daivame", vm.Slides[2].Lang2Text);

                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(await tcs.Task);
    }

    [Fact]
    public async Task MainViewModel_TransliterateCurrentSlide_ConvertsMalayalamToManglish()
    {
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                var vm = new MainViewModel();
                vm.SelectedSlide!.Lang1Text = "യേശു കർത്താവ്";
                vm.TransliterateCurrentSlideCommand.Execute(null);

                Assert.Equal("Yeshu Karthavu", vm.SelectedSlide.Lang2Text);
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(await tcs.Task);
    }

    [Fact]
    public async Task MainViewModel_ModeToggle_SwitchesState()
    {
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                var vm = new MainViewModel();
                vm.SetSingleModeCommand.Execute(null);
                Assert.Equal(LanguageMode.Single, vm.LanguageMode);
                Assert.False(vm.IsDualMode);

                vm.SetDualModeCommand.Execute(null);
                Assert.Equal(LanguageMode.Dual, vm.LanguageMode);
                Assert.True(vm.IsDualMode);

                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(await tcs.Task);
    }

    [Fact]
    public void ProPresenterService_CreateInternalTemplate_HasRequiredElements()
    {
        var singleTmpl = ProPresenterService.CreateInternalTemplate(LanguageMode.Single);
        Assert.NotNull(singleTmpl);
        var slide1 = singleTmpl.Cues[0].Actions[0].Slide.Presentation.BaseSlide;
        Assert.Contains(slide1.Elements, e => e.Element_.Name == "Lang1");
        Assert.DoesNotContain(slide1.Elements, e => e.Element_.Name == "Lang2");

        var dualTmpl = ProPresenterService.CreateInternalTemplate(LanguageMode.Dual);
        Assert.NotNull(dualTmpl);
        var slide2 = dualTmpl.Cues[0].Actions[0].Slide.Presentation.BaseSlide;
        Assert.Contains(slide2.Elements, e => e.Element_.Name == "Lang1");
        Assert.Contains(slide2.Elements, e => e.Element_.Name == "Lang2");
    }

    [Fact]
    public void ProPresenterService_InternalTemplate_GeneratesValidPresentationWithoutExternalFile()
    {
        var svc = new ProPresenterService();
        var project = new SongProject
        {
            Title = "Internal Template Test",
            LanguageMode = LanguageMode.Dual,
            Slides =
            {
                new LyricSlide { SortOrder = 0, GroupName = "Verse 1", Lang1Text = "ദൈവം സ്നേഹമാകുന്നു", Lang2Text = "Daivam Snehamakunnu" },
                new LyricSlide { SortOrder = 1, GroupName = "Chorus", Lang1Text = "ഹല്ലേലൂയ്യാ", Lang2Text = "Hallelooyya" }
            }
        };

        string outPath = Path.Combine(Path.GetTempPath(), $"internal_test_{Guid.NewGuid():N}.pro");
        try
        {
            // Pass null for templatePath to use internal template structure
            svc.GeneratePresentation(null, project, outPath);
            Assert.True(File.Exists(outPath));

            using var stream = File.OpenRead(outPath);
            var presentation = Presentation.Parser.ParseFrom(stream);

            Assert.Equal("Internal Template Test", presentation.Name);
            Assert.Equal(2, presentation.Cues.Count);

            var firstSlide = presentation.Cues[0].Actions[0].Slide.Presentation.BaseSlide;
            var lang1El = firstSlide.Elements.First(e => e.Element_.Name == "Lang1");
            var lang2El = firstSlide.Elements.First(e => e.Element_.Name == "Lang2");

            string rtf1 = lang1El.Element_.Text.RtfData.ToStringUtf8();
            string rtf2 = lang2El.Element_.Text.RtfData.ToStringUtf8();

            Assert.Contains(@"\u", rtf1); // Contains escaped Malayalam Unicode
            Assert.Contains("Daivam Snehamakunnu", rtf2);
        }
        finally
        {
            try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
        }
    }

    [Fact]
    public async Task MainViewModel_SplitSlide_AtClickPosition_SplitsIntoTwoSlides()
    {
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                var vm = new MainViewModel();
                vm.SelectedSlide!.GroupName = "Chorus";
                vm.SelectedSlide.Lang1Text = "Line 1\nLine 2\nLine 3\nLine 4";

                // Simulate Ctrl+Click after "Line 1\nLine 2"
                int clickPosition = "Line 1\nLine 2".Length;
                vm.SplitSlide(clickPosition, isLang1: true);

                Assert.Equal(2, vm.Slides.Count);
                Assert.Equal("Line 1\nLine 2", vm.Slides[0].Lang1Text);
                Assert.Equal("Line 3\nLine 4", vm.Slides[1].Lang1Text);
                Assert.Equal("Chorus", vm.Slides[1].GroupName);
                Assert.Equal(1, vm.Slides[1].SortOrder);
                Assert.Same(vm.Slides[1], vm.SelectedSlide);

                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(await tcs.Task);
    }

    [Fact]
    public async Task MainViewModel_SplitSlide_DualMode_SplitsLang2Proportionally()
    {
        var tcs = new TaskCompletionSource<bool>();
        var thread = new Thread(() =>
        {
            try
            {
                var vm = new MainViewModel();
                vm.SelectedSlide!.GroupName = "Verse 1";
                vm.SelectedSlide.Lang1Text = "L1_A\nL1_B\nL1_C\nL1_D";
                vm.SelectedSlide.Lang2Text = "L2_A\nL2_B\nL2_C\nL2_D";

                // Split at line 2 of Lang1
                int clickPosition = "L1_A\nL1_B".Length;
                vm.SplitSlide(clickPosition, isLang1: true);

                Assert.Equal(2, vm.Slides.Count);
                Assert.Equal("L1_A\nL1_B", vm.Slides[0].Lang1Text);
                Assert.Equal("L2_A\nL2_B", vm.Slides[0].Lang2Text);
                Assert.Equal("L1_C\nL1_D", vm.Slides[1].Lang1Text);
                Assert.Equal("L2_C\nL2_D", vm.Slides[1].Lang2Text);

                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(await tcs.Task);
    }
}
