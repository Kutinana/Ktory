using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using Ktory.Core.Common;

namespace Ktory.Core.Tests;

public class ClockExecutionTests
{
    private static KtorySequencer Create(string source) => new(KtoryParser.Parse(source));

    [Fact]
    public void NullSourceSampleClearsPreviousPositionWithoutAdvancingGate()
    {
        var seq = Create("#.clock(name=\"song\", source=\"music.position\")\n: first .at(value=1, clock=\"song\")\n: second .at(value=2, clock=\"song\")");
        seq.Start();
        var session = seq.CurrentSessionId;
        seq.UpdateClocks(0, new Dictionary<string, double?> { ["music.position"] = 30 }, session);
        Assert.Equal("first", seq.CurrentPayload!.Content);
        seq.UpdateClocks(0, new Dictionary<string, double?> { ["music.position"] = null }, session);
        Assert.Null(Assert.Single(seq.GetClocks()).Position);
        seq.Step();
        Assert.Equal(ExecutionStatus.WaitingForClock, seq.Status);
        seq.UpdateClocks(100, new Dictionary<string, double?> { ["music.position"] = null }, session);
        Assert.Single(seq.GetPendingClockGates());
        seq.UpdateClocks(0, new Dictionary<string, double?> { ["music.position"] = 2 }, session);
        Assert.Equal("second", seq.CurrentPayload!.Content);
        seq.Start();
        seq.UpdateClocks(0, new Dictionary<string, double?> { ["music.position"] = 1 }, seq.CurrentSessionId);
        seq.UpdateClocks(0, new Dictionary<string, double?> { ["music.position"] = null }, session);
        Assert.Equal(1, Assert.Single(seq.GetClocks()).Position);
    }

    [Fact]
    public void InvalidNullableSourceBatchCannotPartiallyClearValuesOrAdvanceTime()
    {
        var seq = Create("#.clock(name=\"song\", source=\"music.position\")\n: first");
        seq.Start();
        seq.UpdateClocks(0, new Dictionary<string, double?> { ["music.position"] = 9 }, seq.CurrentSessionId);
        Assert.Throws<ArgumentException>(() => seq.UpdateClocks(2,
            new Dictionary<string, double?> { ["music.position"] = null, ["bad"] = double.NaN }, seq.CurrentSessionId));
        Assert.Equal(0, seq.ElapsedSeconds);
        Assert.Equal(9, Assert.Single(seq.GetClocks()).Position);
    }

    [Fact]
    public void WhitespaceSourceIsAnOpaqueKeyAndIsNotTheEmptySourceSentinel()
    {
        var seq = Create("#.clock(source=\"  \")\n: held .at(value=2)");
        int factoryCalls = 0;
        seq.ClockFactory = _ => { factoryCalls++; return null; };
        seq.BindOptionalClock("  ", () => null);
        seq.Start();
        Assert.Equal(0, factoryCalls);
        Assert.True(Assert.Single(seq.GetClocks()).IsExternal);
        Assert.Equal("  ", Assert.Single(seq.GetClocks()).SourceName);
        seq.UpdateClocks(1, new Dictionary<string, double> { ["  "] = 1 }, seq.CurrentSessionId);
        Assert.Single(seq.GetPendingClockGates());
        seq.PublishClockPosition("  ", 2, seq.CurrentSessionId);
        Assert.Equal("held", seq.CurrentPayload!.Content);
    }

    [Fact]
    public void BuiltInClockGatesEntryAndDoesNotDispatchEarly()
    {
        var seq = Create("#.clock()\n: hello .at(value=2).effect");
        int effects = 0; seq.OnTagsDispatched += _ => effects++;
        seq.Start();
        var gate = Assert.Single(seq.GetPendingClockGates());
        Assert.Null(gate.ClockName);
        Assert.Equal(0, effects); Assert.Empty(seq.GetActivePresentations());
        seq.Step(gate.Token);
        seq.UpdateClocks(1.9, seq.CurrentSessionId); Assert.Single(seq.GetPendingClockGates());
        seq.UpdateClocks(.1, seq.CurrentSessionId);
        Assert.Empty(seq.GetPendingClockGates()); Assert.Equal("hello", seq.CurrentPayload!.Content);
        Assert.Equal(1, effects); Assert.NotEqual(gate.Token, seq.CurrentPresentationToken);
        seq.PollClocks(seq.CurrentSessionId); Assert.Equal(1, effects);
    }

    [Fact]
    public void ArbitraryFloatingPointProviderUsesItsOwnUnitsAndDoesNotRewindStory()
    {
        var seq = Create("#.clock(source=\"progress\")\n: one .at(value=.25)\n: two .at(value=.75)");
        double progress = .1; int reads = 0;
        seq.BindClock("progress", () => { reads++; return progress; }); seq.Start();
        Assert.Equal(1, reads);
        seq.UpdateClocks(500, seq.CurrentSessionId); Assert.Single(seq.GetPendingClockGates());
        progress = .3; seq.PollClocks(seq.CurrentSessionId); Assert.Equal("one", seq.CurrentPayload!.Content);
        seq.Step(); progress = -.1; seq.PollClocks(seq.CurrentSessionId); Assert.Single(seq.GetPendingClockGates());
        progress = .8; seq.PollClocks(seq.CurrentSessionId); Assert.Equal("two", seq.CurrentPayload!.Content);
        progress = 0; var token = seq.CurrentPresentationToken; seq.PollClocks(seq.CurrentSessionId);
        Assert.Equal(token, seq.CurrentPresentationToken);
    }

    [Fact]
    public void GatesReleaseInCreationOrderAndOldSessionsCannotReleaseThem()
    {
        var seq = Create("""
            #.clock(name="song", source="song").track(source="First", name="a").track(source="Second", name="b")
            : main
            === First ===
              : first .at(value=2, clock="song").effect
            === Second ===
              : second .at(value=2, clock="song").effect
            """);
        var order = new List<string>(); seq.OnLaneTagsDispatched += t => order.Add(t.LaneName);
        seq.Start(); string old = seq.CurrentSessionId; seq.Start();
        seq.PublishClockPosition("song", 20, old);
        Assert.Equal(2, seq.GetPendingClockGates().Count);
        seq.PublishClockPosition("song", 2, seq.CurrentSessionId);
        Assert.Equal(new[] { "a", "b" }, order); Assert.Empty(seq.GetPendingClockGates());
        Assert.Single(seq.GetActivePresentations());
    }

    [Fact]
    public void SessionTimeResetsButBindingsSurviveRestart()
    {
        var seq = Create("#.clock(name=\"custom\", source=\"custom\")\n: waiting .at(value=3, clock=\"custom\")");
        double value = 1; seq.BindClock("custom", () => value);
        seq.Start(); seq.UpdateClocks(10, seq.CurrentSessionId); Assert.Equal(10, seq.ElapsedSeconds);
        value = 3; seq.Start(); Assert.Equal(0, seq.ElapsedSeconds); Assert.Equal("waiting", seq.CurrentPayload!.Content);
    }

    [Fact]
    public void ControllerTicksBuiltInClockButNewBeatDoesNotReceiveOldDelta()
    {
        var seq = Create("#.clock()\n: hello .at(value=2).next(seconds=1)"); seq.Start();
        var host = new MultiTrackPresentationController(seq); host.Synchronize();
        int changes = 0; host.OnPresentationsChanged += () => changes++;
        host.Update(2); Assert.Equal(1, changes);
        var output = Assert.Single(seq.GetActivePresentations()); var controller = host.GetController(output.LaneId)!;
        Assert.Equal(PresentationPhase.Printing, controller.Phase); Assert.Equal(0, controller.ElapsedInPhase);
        host.NotifyPrintingFinished(output.Token); host.Update(.9); Assert.Single(seq.GetActivePresentations());
        host.Update(.1); Assert.Equal(ExecutionStatus.Completed, seq.Status);
    }

    [Theory]
    [InlineData(": x .at()")]
    [InlineData(": x .at(1)")]
    [InlineData(": x .at(value=\"2\")")]
    [InlineData(": x .at(value=1, clock=2)")]
    [InlineData(": x .at(value=1, missing=\"clock\")")]
    [InlineData(": x .at(value=1).at(value=2)")]
    [InlineData("#AUTO.at(value=2)\n: x")]
    [InlineData("#choice.at(value=1)\n  + [x]\n    : x")]
    [InlineData("#choice\n  + [x].at(value=1)\n    : x")]
    public void InvalidClockAnchorsFailAtLoad(string source) =>
        Assert.Throws<KtoryException>(() => KtoryParser.Parse(source));

    [Fact]
    public void NonFiniteSourceReportsSourceNameWithoutReleasingGates()
    {
        var seq = Create("#.clock(name=\"progress\", source=\"progress\")\n: hello .at(value=1, clock=\"progress\")");
        var diagnostics = new List<ExecutionTrace>(); seq.OnTrace += t => diagnostics.Add(t);
        seq.Start(); seq.BindClock("progress", () => double.NaN); seq.PollClocks(seq.CurrentSessionId);
        Assert.Contains(diagnostics, d => d.Kind == ExecutionTraceKind.Warning && d.Message.Contains("progress"));
        Assert.Single(seq.GetPendingClockGates()); Assert.Empty(seq.GetActivePresentations());
        Assert.Equal(ExecutionStatus.WaitingForClock, seq.Status);
    }

    [Fact]
    public void TemporarilyMissingSamplesDoNotReuseStaleValues_AndOtherHeadsStillRun()
    {
        var seq = Create("""
            #.clock(name="external", source="host").clock(name="internal")
            #.track(source="Unavailable", name="blocked").track(source="Available", name="ready")
            : main
            === Unavailable ===
              : held .at(value=2, clock="external")
            === Available ===
              : released .at(value=1, clock="internal")
            """);
        double? value = 1; seq.BindOptionalClock("host", () => value); seq.Start();
        value = null; seq.UpdateClocks(1, seq.CurrentSessionId);
        var gate = Assert.Single(seq.GetPendingClockGates()); Assert.Equal("blocked", gate.LaneName); Assert.Null(gate.ClockPosition);
        value = 2; seq.PollClocks(seq.CurrentSessionId); Assert.Empty(seq.GetPendingClockGates());
        Assert.Equal("main", seq.CurrentPayload!.Content);
    }

    [Fact]
    public void ReentrantSourceCannotAdvanceTheStoryOrConvertFailureIntoClockValue()
    {
        var seq = Create("#.clock(source=\"host\")\n: held .at(value=1)");
        var warnings = new List<ExecutionTrace>(); seq.OnTrace += x => { if (x.Kind == ExecutionTraceKind.Warning) warnings.Add(x); };
        seq.BindClock("host", () => { seq.Step(); return 10; }); seq.Start();
        Assert.Single(seq.GetPendingClockGates()); Assert.Null(Assert.Single(seq.GetClocks()).Position);
        Assert.Single(warnings); Assert.Contains("Reentrant", warnings[0].Message);
    }

    [Fact]
    public void AuxiliaryAdvancesToNextGateImmediatelyWithoutWaitingForDurationOrPrinting()
    {
        var seq = Create("""
            #.clock(name="song").track(source="Lyrics", name="lyrics")
            : main
            === Lyrics ===
              : first .at(value=3.5, clock="song").duration(seconds=3)
              : second .at(value=6, clock="song")
            """);
        seq.Start(); seq.UpdateClocks(3.5, seq.CurrentSessionId);
        var pending = Assert.Single(seq.GetPendingClockGates()); Assert.Equal(6, pending.Position);
        Assert.Equal("main", seq.CurrentPayload!.Content);
        seq.UpdateClocks(2.5, seq.CurrentSessionId); Assert.Empty(seq.GetPendingClockGates());
    }
}
