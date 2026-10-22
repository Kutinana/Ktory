using Ktory.Core.Ast;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;

namespace Ktory.Core.Tests;

public class DisplayLifetimeTests
{
    private static TextPayload Text(double duration) => new TextPayload
    {
        Content = "Lyric", StepType = StepType.Text,
        Tags = new[] { new TagData("duration") { NamedArgs = new() { ["seconds"] = duration } } }
    };

    [Fact]
    public void DisplayLifetimeStartsAfterPrintingAndNeedsNoLiveSequencer()
    {
        var display = new DisplayLifetimeController();
        var token = new PresentationToken("story", 1);
        var hidden = new List<PresentationToken>();
        display.OnHideRequested += hidden.Add;
        display.Show(Text(3), token);
        display.Update(10);
        Assert.Empty(hidden);
        display.NotifyPrintingFinished(token);
        display.Update(2.9);
        Assert.Empty(hidden);
        display.Update(0.1);
        Assert.Equal(new[] { token }, hidden);
        display.Update(10);
        Assert.Single(hidden);
    }

    [Fact]
    public void OldPrintingCompletionCannotStartReplacementLifetime()
    {
        var display = new DisplayLifetimeController();
        var old = new PresentationToken("story", 1);
        var current = new PresentationToken("story", 2);
        display.Show(Text(1), old);
        display.NotifyPrintingFinished(old);
        display.Update(0.5);
        display.Show(Text(2), current);
        display.NotifyPrintingFinished(old);
        display.Update(10);
        Assert.False(display.IsHidden);
        display.NotifyPrintingFinished(current);
        display.Update(2);
        Assert.True(display.IsHidden);
        Assert.Equal(current, display.Token);
    }

    [Theory]
    [InlineData(".next(seconds=1)", 1)]
    [InlineData(".next(seconds=1).wait(seconds=2)", 2)]
    public void DurationDoesNotDelayMainAdvance(string timing, double seconds)
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(": A .duration(seconds=3)" + timing + "\n: B"));
        var controller = new PresentationController(sequencer);
        sequencer.Start();
        controller.SetupForCurrentBeat();
        controller.NotifyPrintingFinished();
        controller.Update(seconds);
        Assert.Equal("B", sequencer.CurrentPayload!.Content);
    }

    [Fact]
    public void DefaultAutoDoesNotWaitForDisplayDuration()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse("@defaultLang: en\n#AUTO\n: One .duration(seconds=60)\n: Two\n#AUTO_END"));
        var controller = new PresentationController(sequencer);
        var display = new DisplayLifetimeController();
        sequencer.Start();
        var first = sequencer.CurrentPresentationToken;
        display.Show(sequencer.CurrentPayload!, first);
        controller.SetupForCurrentBeat();
        display.NotifyPrintingFinished(first);
        controller.NotifyPrintingFinished(first);
        var readingTime = controller.HoldDuration;
        Assert.InRange(readingTime, 0, 59);
        controller.Update(readingTime);
        display.Update(readingTime);
        Assert.Equal("Two", sequencer.CurrentPayload!.Content);
        Assert.False(display.IsHidden);
        Assert.Equal(first, display.Token);
    }

    [Fact]
    public void EmptyTextAnchorKeepsManualInputWithoutPrinting()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(":\n: After"));
        var controller = new PresentationController(sequencer);
        sequencer.Start();
        controller.SetupForCurrentBeat();
        Assert.Equal(PresentationPhase.Holding, controller.Phase);
        Assert.False(controller.AutoAdvanceOnHoldEnd);
        controller.HandleUserClick();
        Assert.Equal("After", sequencer.CurrentPayload!.Content);
    }
}
