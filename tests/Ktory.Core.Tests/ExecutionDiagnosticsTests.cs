using Ktory.Core.Common;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;

namespace Ktory.Core.Tests;

public class ExecutionDiagnosticsTests
{
    [Fact]
    public void ObservationAndLanguageRefresh_DoNotEnterNodesOrReplayTags()
    {
        var seq = new KtorySequencer(KtoryParser.Parse("@defaultLang: zh\n:\n  @zh: 你好\n  @en: Hello\n  .effect(1)\n: 后续"));
        var trace = new List<ExecutionTrace>();
        int dispatched = 0;
        seq.OnTrace += trace.Add;
        seq.OnTagsDispatched += _ => dispatched++;
        seq.Start();
        var p = new PresentationController(seq);
        p.SetupForCurrentBeat();
        long id = seq.CurrentPresentationId;
        int events = trace.Count;
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(2, seq.CurrentLineNumber);
            Assert.Empty(seq.GetCallStackSnapshot());
            Assert.Empty(seq.GetActiveLoopsSnapshot());
            Assert.Equal(0, p.MinimumHoldRemaining);
        }
        seq.SetLanguage("en");
        p.RefreshLanguage(completePrintingOnLanguageSwitch: false);
        Assert.Equal("Hello", seq.CurrentPayload!.Content);
        Assert.Equal(id, seq.CurrentPresentationId);
        Assert.Equal(events, trace.Count);
        Assert.Equal(1, dispatched);
        Assert.Equal(PresentationPhase.Printing, p.Phase);
        Assert.Single(trace, e => e.Kind == ExecutionTraceKind.Node);
        Assert.Equal(2, Assert.Single(trace, e => e.Kind == ExecutionTraceKind.Tag).LineNumber);
    }

    [Fact]
    public void Trace_RecordsControlFlowAndSource_AndDoesNotSwallowExecutionErrors()
    {
        const string source = "=> Sub\n-> Finish\n=== Sub ===\n    : sub\n    -> return\n=== Finish ===\n    : finish\n    -> return";
        var seq = new KtorySequencer(KtoryParser.Parse(source));
        var trace = new List<ExecutionTrace>();
        seq.OnTrace += _ => throw new InvalidOperationException("Broken observer");
        seq.OnTrace += trace.Add;
        seq.Start();
        var frames = seq.GetCallStackSnapshot();
        Assert.Equal("root", Assert.Single(frames).Block);
        Assert.Equal(2, frames[0].ResumeLine);
        seq.Step();
        Assert.Equal("finish", seq.CurrentPayload!.Content);
        Assert.Empty(seq.GetCallStackSnapshot());
        Assert.Single(frames); // Detached snapshot is unchanged after returning.
        Assert.Throws<KtoryControlFlowException>(() => seq.Step());
        Assert.Contains(trace, e => e.Kind == ExecutionTraceKind.Call && e.LineNumber == 1 && e.Block == "root");
        Assert.Contains(trace, e => e.Kind == ExecutionTraceKind.Return && e.LineNumber == 5 && e.Block == "Sub");
        Assert.Contains(trace, e => e.Kind == ExecutionTraceKind.Jump && e.LineNumber == 2);
        Assert.Equal(8, Assert.Single(trace, e => e.Kind == ExecutionTraceKind.Error).LineNumber);
    }

    [Fact]
    public void LoopSnapshotsAndTrace_ReflectEachRealEntry()
    {
        var seq = new KtorySequencer(KtoryParser.Parse(": repeat .loop(2).effect\n: after"));
        var trace = new List<ExecutionTrace>();
        seq.OnTrace += trace.Add;
        seq.Start();
        var first = Assert.Single(seq.GetActiveLoopsSnapshot());
        Assert.Equal(1, first.Iteration);
        Assert.Equal(2, first.Limit);
        seq.Step();
        Assert.Equal(2, Assert.Single(seq.GetActiveLoopsSnapshot()).Iteration);
        Assert.Equal(1, first.Iteration);
        seq.Step();
        Assert.Empty(seq.GetActiveLoopsSnapshot());
        Assert.Equal(2, trace.Count(e => e.Kind == ExecutionTraceKind.Tag && e.Message == ".effect"));
        Assert.Equal(2, trace.Count(e => e.Kind == ExecutionTraceKind.Node && e.LineNumber == 1));
    }

    [Fact]
    public void ChoiceRefresh_ReportsActualLanguagesAndPreservesConsumption()
    {
        const string source = "@defaultLang: zh\n#choice.loop\n    * [@zh: 一次]\n      [@en: Once]\n      .picked\n      : branch\n    + [继续]\n      : again";
        var seq = new KtorySequencer(KtoryParser.Parse(source));
        var trace = new List<ExecutionTrace>();
        seq.OnTrace += trace.Add;
        seq.Start(requestedLocale: "en");
        var menu = seq.CurrentChoice!;
        var once = menu.Options[0];
        Assert.Equal("en", once.ActualLanguage);
        Assert.Equal("zh", menu.Options[1].ActualLanguage);
        Assert.Equal(2, menu.LineNumber);
        Assert.Equal(3, once.LineNumber);
        int events = trace.Count;
        seq.SetLanguage("ja");
        Assert.Equal(menu.PresentationId, seq.CurrentChoice!.PresentationId);
        Assert.Equal("zh", seq.CurrentChoice.Options[0].ActualLanguage);
        Assert.Equal(events, trace.Count);
        seq.SubmitChoice(once.Id, menu.PresentationId);
        Assert.Equal("branch", seq.CurrentPayload!.Content);
        Assert.Contains(once.Id, seq.VisitedItemIds);
        seq.Step();
        Assert.True(seq.CurrentChoice!.Options[0].IsConsumed);
        Assert.False(seq.CurrentChoice.Options[0].CanSelect);
        seq.SetLanguage("en");
        Assert.True(seq.CurrentChoice!.Options[0].IsConsumed);
        Assert.Equal(3, Assert.Single(trace, e => e.Kind == ExecutionTraceKind.Choice).LineNumber);
        Assert.Contains(trace, e => e.Kind == ExecutionTraceKind.Tag && e.Message == ".picked" && e.LineNumber == 3);
    }

    [Fact]
    public void TimingTelemetry_UsesLiveClockAndResetsAtChoice()
    {
        var seq = new KtorySequencer(KtoryParser.Parse(": first .skippable(false, 2).wait(3).next(5)\n#choice\n    + [pick]\n      : branch"));
        var p = new PresentationController(seq);
        seq.Start();
        p.SetupForCurrentBeat();
        Assert.Equal(".next", p.AutoAdvanceSource);
        Assert.True(p.AutoAdvanceOnHoldEnd);
        p.Update(1);
        Assert.False(p.CanHandleUserClick);
        Assert.Equal(1, p.FastForwardLockRemaining);
        Assert.Equal(3, p.MinimumHoldRemaining); // Not counting down while printing.
        Assert.Equal(5, p.AutoAdvanceRemaining);
        p.HandleUserClick();
        Assert.Equal("first", seq.CurrentPayload!.Content);
        p.Update(1);
        Assert.True(p.CanHandleUserClick);
        p.HandleUserClick();
        Assert.Equal(PresentationPhase.Holding, p.Phase);
        p.Update(2);
        Assert.Equal(1, p.MinimumHoldRemaining);
        Assert.Equal(3, p.AutoAdvanceRemaining);
        seq.SetLanguage("en");
        p.RefreshLanguage();
        Assert.Equal(1, p.MinimumHoldRemaining);
        Assert.Equal(3, p.AutoAdvanceRemaining);
        p.Update(1);
        Assert.True(p.CanHandleUserClick);
        p.HandleUserClick();
        Assert.Equal(ExecutionStatus.AwaitingChoice, seq.Status);
        Assert.False(p.AutoAdvanceOnHoldEnd);
        Assert.Equal("None", p.AutoAdvanceSource);
        Assert.Equal(0, p.MinimumHoldRemaining);
        Assert.Equal(0, p.AutoAdvanceRemaining);
        Assert.False(p.CanHandleUserClick);
    }

    [Fact]
    public void EstimatedTelemetry_PreservesLanguageProgressAndInfiniteRevealLock()
    {
        var seq = new KtorySequencer(KtoryParser.Parse(":\n  @zh: 一二三四五六七八九十一二三四\n  @en: one two three four five six seven eight nine ten eleven twelve thirteen fourteen\n  .skippable(false).wait.next(8)"));
        var p = new PresentationController(seq);
        seq.Start();
        p.SetupForCurrentBeat();
        Assert.True(double.IsPositiveInfinity(p.FastForwardLockRemaining));
        p.Update(0.5);
        seq.SetLanguage("en");
        p.RefreshLanguage(false);
        Assert.Equal(4, p.MinimumHoldRemaining);
        Assert.Equal(0.5, p.ElapsedInPhase);
        Assert.Equal(PresentationPhase.Printing, p.Phase);
        seq.SetLanguage("zh");
        p.RefreshLanguage(false);
        p.NotifyPrintingFinished();
        p.Update(1);
        Assert.Equal(1, p.MinimumHoldRemaining);
        seq.SetLanguage("en");
        p.RefreshLanguage();
        Assert.Equal(2, p.MinimumHoldRemaining); // Still 50% read, of four seconds.
        Assert.Equal(7, p.AutoAdvanceRemaining); // Independent fixed timer.
        Assert.Equal(0, p.FastForwardLockRemaining);
    }

    [Fact]
    public void NaturalSectionEnd_RecordsTheSectionBeforeResumingRoot()
    {
        var seq = new KtorySequencer(KtoryParser.Parse("=> Sub\n=== Sub ===\n    : inside"));
        var trace = new List<ExecutionTrace>();
        seq.OnTrace += trace.Add;
        seq.Start();
        seq.Step();
        Assert.Equal(ExecutionStatus.Completed, seq.Status);
        var end = Assert.Single(trace, e => e.Kind == ExecutionTraceKind.End);
        Assert.Equal("Sub", end.Block);
        Assert.Equal(3, end.LineNumber);
    }

    [Fact]
    public void HostTagFailure_IsRecordedAndStillThrown()
    {
        var seq = new KtorySequencer(KtoryParser.Parse(": hello .host"));
        var trace = new List<ExecutionTrace>();
        seq.OnTrace += trace.Add;
        seq.OnTagsDispatched += _ => throw new InvalidOperationException("host failed");
        Assert.Throws<InvalidOperationException>(() => seq.Start());
        Assert.Equal(new[] { ExecutionTraceKind.Node, ExecutionTraceKind.Tag, ExecutionTraceKind.Error }, trace.Select(e => e.Kind));
        Assert.All(trace, e => Assert.Equal(1, e.LineNumber));
    }
}
