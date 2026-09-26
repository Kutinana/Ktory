using Ktory.Core.Ast;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;

namespace Ktory.Core.Tests;

public class RuntimeBoundaryRegressionTests
{
    [Fact]
    public void DefaultStart_WithOnlyNamedSections_CompletesWithoutEnteringThem()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(
            "=== First ===\n  : first .effect\n=== Second ===\n  : second"));
        var trace = new List<ExecutionTrace>();
        int tags = 0;
        sequencer.OnTrace += trace.Add;
        sequencer.OnTagsDispatched += _ => tags++;

        sequencer.Start();

        Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        Assert.Equal("root", sequencer.CurrentBlockLabel);
        Assert.Null(sequencer.CurrentPayload);
        Assert.Null(sequencer.CurrentChoice);
        Assert.Equal(0, tags);
        Assert.DoesNotContain(trace, entry => entry.Kind == ExecutionTraceKind.Node);
    }

    [Theory]
    [InlineData("First", "first")]
    [InlineData("Second", "second")]
    public void ExplicitStart_WithOnlyNamedSections_EntersRequestedSection(string entry, string content)
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(
            "=== First ===\n  : first\n=== Second ===\n  : second"));

        sequencer.Start(entry);

        Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
        Assert.Equal(entry, sequencer.CurrentBlockLabel);
        Assert.Equal(content, sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
    }

    [Fact]
    public void UnknownContainer_WarnsWithOriginalNameAndSource_AndKeepsChoiceBoundary()
    {
        const string source = "=> Menu\n: after\n=== Menu ===\n  #MysteryMenu\n    + [@zh: 选择]\n      [@en: Choose]\n      : branch .picked\n      : second\n  -> return";
        var sequencer = new KtorySequencer(KtoryParser.Parse(source));
        var trace = new List<ExecutionTrace>();
        int tags = 0;
        sequencer.OnTrace += entry =>
        {
            if (IsWarning(entry)) throw new InvalidOperationException("Broken warning observer");
        };
        sequencer.OnTrace += trace.Add;
        sequencer.OnTagsDispatched += _ => tags++;

        sequencer.Start();

        Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
        Assert.Equal("MysteryMenu", sequencer.CurrentChoice!.ContainerName);
        var warning = Assert.Single(trace, IsWarning);
        Assert.Equal("Menu", warning.Block);
        Assert.Equal(4, warning.LineNumber);
        Assert.Contains("MysteryMenu", warning.Message);
        Assert.Contains("4", warning.Message);
        Assert.True((ExecutionTraceKind.All & warning.Kind) != 0);

        long presentation = sequencer.CurrentPresentationId;
        sequencer.SetLanguage("en");
        sequencer.GetCallStackSnapshot();
        sequencer.GetActiveLoopsSnapshot();
        Assert.Equal("Choose", sequencer.CurrentChoice.Options[0].Label);
        Assert.Equal(presentation, sequencer.CurrentPresentationId);
        Assert.Single(trace, IsWarning);
        Assert.Equal(0, tags);

        sequencer.SubmitChoice(sequencer.CurrentChoice.Options[0].Id, presentation);
        Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
        Assert.Equal("branch", sequencer.CurrentPayload!.Content);
        Assert.Equal(1, tags);
        Assert.Single(trace, IsWarning);
        sequencer.Step();
        Assert.Equal("second", sequencer.CurrentPayload!.Content);
    }

    [Fact]
    public void UnknownContainerLoop_WarnsOncePerRealEntry_NotAfterLoopExhaustion()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(
            "#mystery.loop(2)\n  + [pick]\n    : branch\n: after"));
        var trace = new List<ExecutionTrace>();
        sequencer.OnTrace += trace.Add;
        sequencer.Start();

        for (int entry = 1; entry <= 2; entry++)
        {
            Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
            Assert.Equal(entry, trace.Count(IsWarning));
            sequencer.SetLanguage("en");
            Assert.Equal(entry, trace.Count(IsWarning));
            sequencer.SubmitChoice(sequencer.CurrentChoice!.Options[0].Id);
            Assert.Equal("branch", sequencer.CurrentPayload!.Content);
            sequencer.Step();
        }

        Assert.Equal("after", sequencer.CurrentPayload!.Content);
        Assert.Equal(2, trace.Count(IsWarning));
    }

    [Fact]
    public void UnknownContainer_WithNoSelectableItems_WarnsAboutHandlerAndContinues()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(
            "#mystery\n  + ? {false} [hidden]\n    : unreachable\n: after"));
        var trace = new List<ExecutionTrace>();
        sequencer.OnTrace += trace.Add;

        sequencer.Start();

        Assert.Equal(ExecutionStatus.SuspendedAtBeat, sequencer.Status);
        Assert.Equal("after", sequencer.CurrentPayload!.Content);
        Assert.Null(sequencer.CurrentChoice);
        Assert.Single(trace, IsWarning);
        Assert.DoesNotContain(trace, entry => entry.Kind == ExecutionTraceKind.Error);
    }

    [Fact]
    public void UnknownContainer_WithNoItemsInAst_WarnsAndContinues()
    {
        var file = KtoryParser.Parse(": after");
        file.RootBlock.Steps.Insert(0, new ContainerStep { Name = "mystery", LineNumber = 1 });
        var sequencer = new KtorySequencer(file);
        var trace = new List<ExecutionTrace>();
        sequencer.OnTrace += trace.Add;

        sequencer.Start();

        Assert.Equal("after", sequencer.CurrentPayload!.Content);
        Assert.Single(trace, IsWarning);
    }

    [Fact]
    public void BuiltInChoice_WithNoSelectableItems_ContinuesWithoutWarning()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(
            "#choice\n  + ? {false} [hidden]\n    : unreachable\n: after"));
        var trace = new List<ExecutionTrace>();
        sequencer.OnTrace += trace.Add;

        sequencer.Start();

        Assert.Equal("after", sequencer.CurrentPayload!.Content);
        Assert.DoesNotContain(trace, IsWarning);
        Assert.DoesNotContain(trace, entry => entry.Kind == ExecutionTraceKind.Error);
    }

    [Fact]
    public void UnknownContainer_WithFailedGuard_IsNotEnteredAndDoesNotWarn()
    {
        var file = KtoryParser.Parse("#mystery\n  + [pick]\n    : unreachable\n: after");
        Assert.IsType<ContainerStep>(file.RootBlock.Steps[0]).GuardCondition = "false";
        var sequencer = new KtorySequencer(file);
        var trace = new List<ExecutionTrace>();
        sequencer.OnTrace += trace.Add;

        sequencer.Start();

        Assert.Equal("after", sequencer.CurrentPayload!.Content);
        Assert.DoesNotContain(trace, IsWarning);
    }

    private static bool IsWarning(ExecutionTrace entry) => entry.Kind == ExecutionTraceKind.Warning;
}
