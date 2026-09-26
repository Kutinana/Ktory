using Ktory.Core.Parser;
using Ktory.Core.Runtime;

namespace Ktory.Core.Tests;

public class PresentationTimingTests
{
    private static (KtorySequencer Player, PresentationController Controller) Start(string tags, bool directive = false, bool auto = false)
    {
        var script = (auto ? "#AUTO\n" : "") + (directive ? "#pause " : ": First ") + tags + "\n" +
                     (auto ? "#AUTO_END\n" : "") + ": Second\n: Third";
        var player = new KtorySequencer(KtoryParser.Parse(script));
        var controller = new PresentationController(player);
        player.Start();
        controller.SetupForCurrentBeat();
        return (player, controller);
    }

    [Theory]
    [InlineData(".wait(2)", 2)]
    [InlineData(".wait", 1)]
    [InlineData(".wait(0)", 0)]
    public void Wait_DiscardsEarlyInput_RequiresFreshClick(string tags, double duration)
    {
        var (player, controller) = Start(tags);
        controller.NotifyPrintingFinished();
        Assert.False(controller.AutoAdvanceOnHoldEnd);
        if (duration > 0)
        {
            controller.HandleUserClick();
            controller.HandleUserClick();
            controller.RequestAdvance();
            Assert.False(controller.QueuedAdvance);
            Assert.Equal("First", player.CurrentPayload!.Content);
        }
        controller.Update(duration + 10);
        Assert.Equal("First", player.CurrentPayload!.Content);
        Assert.True(controller.AllowClickInterruptHold);
        controller.HandleUserClick();
        Assert.Equal("Second", player.CurrentPayload!.Content);
    }

    [Theory]
    [InlineData(".wait(2).next(2)", 2)]
    [InlineData(".next(2).wait(2)", 2)]
    [InlineData(".wait(2).next(5)", 5)]
    [InlineData(".next(5).wait(2)", 5)]
    [InlineData(".wait(5).next(2)", 5)]
    [InlineData(".next(2).wait(5)", 5)]
    [InlineData(".wait(2).next", 2)]
    [InlineData(".next.wait", 1)]
    [InlineData(".wait.next", 1)]
    public void WaitAndNext_UseConcurrentTimers_RegardlessOfOrder(string tags, double duration)
    {
        var (player, controller) = Start(tags);
        controller.NotifyPrintingFinished();
        controller.HandleUserClick();
        Assert.False(controller.QueuedAdvance);
        Assert.Equal("First", player.CurrentPayload!.Content);
        controller.Update(duration - 0.25);
        Assert.Equal("First", player.CurrentPayload!.Content);
        controller.Update(0.25);
        Assert.Equal("Second", player.CurrentPayload!.Content);
        controller.Update(10);
        Assert.Equal("Second", player.CurrentPayload!.Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WaitEndsBeforeNext_FreshClickCanAdvance(bool hostManaged)
    {
        var (player, controller) = Start(".next(5).wait(2)");
        controller.AutoStepSequencer = !hostManaged;
        int requests = 0;
        controller.OnAdvanceRequested += () => requests++;
        controller.NotifyPrintingFinished();
        controller.Update(1);
        controller.HandleUserClick();
        Assert.Equal(0, requests);
        controller.Update(1);
        Assert.True(controller.AllowClickInterruptHold);
        controller.HandleUserClick();
        Assert.Equal(1, requests);
        if (hostManaged)
        {
            Assert.Equal("First", player.CurrentPayload!.Content);
            player.Step();
            controller.SetupForCurrentBeat();
        }
        Assert.Equal("Second", player.CurrentPayload!.Content);
    }

    [Theory]
    [InlineData(".skippable(false)")]
    [InlineData(".skippable(false, 2)")]
    public void SkippableLock_DoesNotRememberInput(string tags)
    {
        var (player, controller) = Start(tags);
        int reveals = 0;
        controller.OnFastForwardRequested += () => reveals++;
        controller.HandleUserClick();
        controller.RequestAdvance();
        controller.Update(2);
        Assert.Equal(0, reveals);
        Assert.Equal("First", player.CurrentPayload!.Content);
        controller.NotifyPrintingFinished();
        controller.Update(10);
        Assert.Equal("First", player.CurrentPayload!.Content);
        controller.HandleUserClick();
        Assert.Equal("Second", player.CurrentPayload!.Content);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DirectiveWait_OnlyAutoAdvancesWithNextOrAuto(bool next, bool auto)
    {
        var (player, controller) = Start(".wait(2)" + (next ? ".next" : ""), directive: true, auto: auto);
        long first = player.CurrentPresentationId;
        controller.HandleUserClick();
        controller.Update(1);
        Assert.Equal(first, player.CurrentPresentationId);
        controller.Update(1);
        if (!next && !auto)
        {
            Assert.Equal(first, player.CurrentPresentationId);
            controller.HandleUserClick();
        }
        Assert.Equal("Second", player.CurrentPayload!.Content);
    }

    [Fact]
    public void EstimatedWait_LanguageRefreshPreservesNextClock_AndDoesNotRelock()
    {
        var player = new KtorySequencer(KtoryParser.Parse("@defaultLang: zh\n:\n  @zh: 一二三四五六七\n  @en: one two three four five six seven\n  .wait.next(3)\n: Second"));
        var controller = new PresentationController(player);
        player.Start();
        controller.SetupForCurrentBeat();
        controller.NotifyPrintingFinished();
        controller.Update(0.5);
        player.SetLanguage("en");
        controller.RefreshLanguage();
        Assert.Equal(1, controller.ElapsedInPhase); // Half of the new two-second wait.
        controller.Update(1);
        Assert.True(controller.AllowClickInterruptHold);
        player.SetLanguage("zh");
        controller.RefreshLanguage();
        Assert.True(controller.AllowClickInterruptHold);
        controller.Update(1.25); // 2.75 actual seconds since printing finished.
        Assert.NotEqual("Second", player.CurrentPayload!.Content);
        controller.Update(0.25);
        Assert.Equal("Second", player.CurrentPayload!.Content);
    }
}
