using Ktory.Core.Common;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;

namespace Ktory.Core.Tests;

public class ClockDeclarationRuntimeTests
{
    [Fact]
    public void LateDeclarationStartsAtZero_WhenEarlierSessionTimeHasElapsed()
    {
        var seq = Create(": before\n#.clock(name=\"scene\")\n: after .at(value=2, clock=\"scene\")");
        seq.Start(); seq.UpdateClocks(10, seq.CurrentSessionId);
        Assert.Empty(seq.GetClocks());
        seq.Step();
        Assert.Equal(10, Clock(seq, "scene").DeclaredAtElapsedSeconds);
        Assert.Equal(0, Clock(seq, "scene").Position);
        seq.UpdateClocks(1.5, seq.CurrentSessionId);
        Assert.Empty(seq.GetActivePresentations());
        seq.UpdateClocks(.5, seq.CurrentSessionId);
        Assert.Equal("after", seq.CurrentPayload!.Content);
        Assert.Equal(12, seq.ElapsedSeconds);
    }

    [Fact]
    public void ResetRetainsIdentityAndChangesValueForEveryPendingAuxiliary()
    {
        var seq = Create("""
            #.clock(name="shared").track(source="First", name="first").track(source="Second", name="second")
            : ready
            #.resetClock(name="shared")
            : after reset
            === First ===
              : first .at(value=5, clock="shared")
            === Second ===
              : second .at(value=7, clock="shared")
            """);
        seq.Start();
        long original = Clock(seq, "shared").ClockId;
        seq.UpdateClocks(4, seq.CurrentSessionId); seq.Step();
        Assert.Equal(original, Clock(seq, "shared").ClockId);
        Assert.Equal(0, Clock(seq, "shared").Position);
        Assert.Equal(0, Clock(seq, "shared").DeclaredAtElapsedSeconds);
        Assert.Equal(2, seq.GetPendingClockGates().Count);
        Assert.All(seq.GetPendingClockGates(), gate => Assert.Equal(original, gate.ClockId));
        seq.UpdateClocks(5, seq.CurrentSessionId);
        Assert.Equal("second", Assert.Single(seq.GetPendingClockGates()).LaneName);
        seq.UpdateClocks(2, seq.CurrentSessionId);
        Assert.Empty(seq.GetPendingClockGates());
        Assert.Equal("after reset", seq.CurrentPayload!.Content);
    }

    [Fact]
    public void ClockOnlyDirective_IsTransparentAndNeverDispatchesClockOperationsToHost()
    {
        var seq = Create("#.clock(name=\"a\").clock(name=\"b\").resetClock(name=\"a\")\n: visible");
        int legacy = 0, lane = 0;
        seq.OnTagsDispatched += _ => legacy++;
        seq.OnLaneTagsDispatched += _ => lane++;
        seq.Start();
        Assert.Equal("visible", seq.CurrentPayload!.Content);
        Assert.Equal(1, seq.CurrentPresentationId);
        Assert.Equal(2, seq.GetClocks().Count);
        Assert.Equal(0, legacy); Assert.Equal(0, lane);
        Assert.Equal(1, Clock(seq, "a").ClockId);
    }

    [Fact]
    public void ClockOnlyScript_CompletesWithoutProducingAClickStop()
    {
        var seq = Create("#.clock(name=\"a\")\n#.resetClock(name=\"a\")");
        seq.Start();
        Assert.Equal(ExecutionStatus.Completed, seq.Status);
        Assert.Empty(seq.GetActivePresentations());
        Assert.Empty(seq.GetClocks());
    }

    [Theory]
    [InlineData(".bgm(\"new\").clock(name=\"song\", source=\"music.position\")")]
    [InlineData(".clock(name=\"song\", source=\"music.position\").bgm(\"new\")")]
    public void MusicAndClockOnSameBeat_SampleAfterHostTags(string tags)
    {
        var seq = Create("#music" + tags + "\n: lyric .at(value=1, clock=\"song\")");
        double audioPosition = 40;
        int reads = 0;
        seq.BindClock("music.position", () => { reads++; return audioPosition; });
        seq.OnTagsDispatched += dispatched =>
        {
            if (dispatched.Any(tag => tag.Name == "bgm")) audioPosition = 0;
            Assert.DoesNotContain(dispatched, tag => tag.Name == "clock");
        };
        seq.Start();
        var manager = new MultiTrackPresentationController(seq);
        manager.Synchronize();
        Assert.Single(seq.GetPendingClockGates());
        Assert.Empty(seq.GetActivePresentations());
        Assert.Equal(0, Clock(seq, "song").Position);
        Assert.Equal(1, reads);
        audioPosition = 1; seq.PollClocks(seq.CurrentSessionId);
        Assert.Equal("lyric", seq.CurrentPayload!.Content);
    }

    [Fact]
    public void InterleavedHostTagsObserveClockOperationsInSourceOrder()
    {
        var seq = Create("#music.before.clock(name=\"a\").middle.resetClock(name=\"a\").after");
        var observations = new List<(string Name, long Clock)>();
        seq.OnTagsDispatched += tags =>
        {
            foreach (var tag in tags) observations.Add((tag.Name, seq.GetClocks().SingleOrDefault()?.ClockId ?? 0));
        };
        seq.Start();
        Assert.Equal(new[] { ("before", 0L), ("middle", 1L), ("after", 1L) }, observations);
    }

    [Fact]
    public void LanguageRefreshDoesNotDeclareOrResetClockAgain()
    {
        var seq = Create(":\n  @zh: 你好\n  @en: Hello\n  .clock(name=\"scene\").effect");
        int effects = 0; seq.OnTagsDispatched += _ => effects++;
        seq.Start(requestedLocale: "zh"); seq.UpdateClocks(3, seq.CurrentSessionId);
        var before = Clock(seq, "scene"); var token = seq.CurrentPresentationToken;
        seq.SetLanguageOutput("en", "zh"); seq.SetSecondaryLanguage(null);
        var after = Clock(seq, "scene");
        Assert.Equal(before.ClockId, after.ClockId); Assert.Equal(before.Position, after.Position);
        Assert.Equal(before.DeclaredAtElapsedSeconds, after.DeclaredAtElapsedSeconds);
        Assert.Equal(token, seq.CurrentPresentationToken); Assert.Equal(1, effects);
    }

    [Fact]
    public void NamedClockOutlivesItsCreatingAuxiliaryUntilSessionInvalidation()
    {
        var seq = Create("""
            #.track(source="Declare", name="declare").awaitTrack(name="declare")
            #.track(source="Use", name="use")
            : ready
            : parent .at(value=2, clock="shared")
            === Declare ===
              #.clock(name="shared")
            === Use ===
              : child .at(value=1, clock="shared")
            """);
        seq.Start();
        var id = Clock(seq, "shared").ClockId;
        Assert.Equal("use", Assert.Single(seq.GetPendingClockGates()).LaneName);
        seq.UpdateClocks(1, seq.CurrentSessionId); seq.Step();
        Assert.Equal("root", Assert.Single(seq.GetPendingClockGates()).LaneId);
        Assert.Equal(id, Clock(seq, "shared").ClockId);
        seq.UpdateClocks(1, seq.CurrentSessionId);
        Assert.Equal("parent", seq.CurrentPayload!.Content);
        seq.InvalidateSession();
        Assert.Empty(seq.GetClocks()); Assert.Empty(seq.GetPendingClockGates());
    }

    [Fact]
    public void RestartClearsInstancesAndSamplesButRetainsHostBindings()
    {
        var seq = Create(": before\n#.clock(name=\"inner\").clock(name=\"source\", source=\"host.value\")\n: wait .at(value=2, clock=\"source\")");
        int reads = 0; double position = 1;
        seq.BindClock("host.value", () => { reads++; return position; });
        seq.Start(); seq.Step(); seq.UpdateClocks(10, seq.CurrentSessionId);
        Assert.Equal(10, Clock(seq, "inner").Position);
        string old = seq.CurrentSessionId; seq.Start();
        Assert.NotEqual(old, seq.CurrentSessionId); Assert.Empty(seq.GetClocks());
        Assert.Equal(0, seq.ElapsedSeconds);
        position = 2; seq.Step();
        Assert.Equal("wait", seq.CurrentPayload!.Content);
        Assert.Equal(0, Clock(seq, "inner").Position); Assert.Equal(3, reads);
    }

    [Fact]
    public void FrameTimeAndPushedValuesAreValidatedAndPublishedAtomically()
    {
        var seq = Create("#.clock(name=\"internal\").clock(name=\"external\", source=\"host.value\")\n: visible");
        int reads = 0; seq.BindClock("host.value", () => { reads++; return 1; });
        seq.Start(); seq.PollClocks(seq.CurrentSessionId);
        var invalid = new Dictionary<string, double> { ["host.value"] = double.NaN };
        Assert.Throws<ArgumentException>(() => seq.UpdateClocks(4, invalid, seq.CurrentSessionId));
        Assert.Equal(0, seq.ElapsedSeconds); Assert.Equal(0, Clock(seq, "internal").Position);
        Assert.Equal(1, Clock(seq, "external").Position);
        seq.UpdateClocks(2, new Dictionary<string, double> { ["host.value"] = 5 }, seq.CurrentSessionId);
        Assert.Equal(2, seq.ElapsedSeconds); Assert.Equal(2, Clock(seq, "internal").Position);
        Assert.Equal(5, Clock(seq, "external").Position); Assert.Equal(1, reads);
    }

    [Fact]
    public void FailedProviderIsIsolatedFromBuiltInClocksAndDoesNotFabricateAValue()
    {
        var seq = Create("#.clock(name=\"internal\").clock(name=\"external\", source=\"host.value\")\n: visible");
        seq.BindClock("host.value", () => throw new InvalidOperationException("source unavailable"));
        var warnings = new List<ExecutionTrace>();
        seq.OnTrace += x => { if (x.Kind == ExecutionTraceKind.Warning) warnings.Add(x); };
        seq.Start(); seq.UpdateClocks(4, seq.CurrentSessionId); seq.PollClocks(seq.CurrentSessionId);
        Assert.Equal(4, seq.ElapsedSeconds); Assert.Equal(4, Clock(seq, "internal").Position);
        Assert.Null(Clock(seq, "external").Position); Assert.Single(warnings);
        Assert.Equal(ExecutionStatus.SuspendedAtBeat, seq.Status);
    }

    [Fact]
    public void ClockCreatedDuringReleaseDoesNotReceivePrecedingFrameDelta()
    {
        var seq = Create("#.clock(name=\"timer\").defaultClock(name=\"timer\")\n#.at(value=5).clock(name=\"late\")\n: visible");
        seq.Start(); seq.UpdateClocks(5, seq.CurrentSessionId);
        Assert.Equal("visible", seq.CurrentPayload!.Content);
        Assert.Equal(5, Clock(seq, "timer").Position); Assert.Equal(0, Clock(seq, "late").Position);
        Assert.Equal(5, Clock(seq, "late").DeclaredAtElapsedSeconds);
        seq.UpdateClocks(.5, seq.CurrentSessionId); Assert.Equal(.5, Clock(seq, "late").Position);
    }

    [Fact]
    public void ObservationNeverCallsProviderAndSharedSourceIsSampledOncePerUpdate()
    {
        var seq = Create("#.clock(name=\"a\", source=\"host.value\").clock(name=\"b\", source=\"host.value\")\n: visible");
        int reads = 0; seq.BindClock("host.value", () => ++reads); seq.Start();
        for (int i = 0; i < 5; i++) { Assert.Equal(2, seq.GetClocks().Count); Assert.Empty(seq.GetPendingClockGates()); }
        Assert.Equal(0, reads);
        seq.PollClocks(seq.CurrentSessionId);
        Assert.Equal(1, reads); Assert.Equal(1, Clock(seq, "a").Position); Assert.Equal(1, Clock(seq, "b").Position);
        seq.PollClocks(seq.CurrentSessionId);
        Assert.Equal(2, reads); Assert.Equal(2, Clock(seq, "a").Position); Assert.Equal(2, Clock(seq, "b").Position);
    }

    [Theory]
    [InlineData(": missing .at(value=1)")]
    [InlineData(": missing .at(value=1, clock=\"declared\")")]
    [InlineData("#.resetClock(name=\"declared\")")]
    public void DynamicEntryStillDefendsAgainstUncreatedInstances(string statement)
    {
        var seq = Create("#.clock(name=\"declared\")\n: root\n=== Entry ===\n  " + statement);
        int reads = 0; seq.BindClock("declared", () => { reads++; return 100; });
        var error = Assert.Throws<KtoryException>(() => seq.Start("Entry"));
        Assert.Equal(4, error.Line); Assert.Equal(ExecutionStatus.Error, seq.Status); Assert.Equal(0, reads);
    }

    [Fact]
    public void SameNamedDeclarationReusesInstanceAndLoopsStillHaveInstructionBudget()
    {
        var finite = Create("#.clock(name=\"a\").loop(count=3)\n: after"); finite.Start();
        Assert.Equal("after", finite.CurrentPayload!.Content); Assert.Equal(1, Clock(finite, "a").ClockId);
        Assert.Equal(0, finite.ActiveLoopCount);
        var infinite = Create("#.clock(name=\"a\").loop"); infinite.MaxInstructionBudgetPerAdvance = 16;
        var error = Assert.Throws<KtoryInstructionBudgetExceededException>(() => infinite.Start());
        Assert.Contains("budget", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ExecutionStatus.Error, infinite.Status);
    }

    [Fact]
    public void ExternalFirstCreationAndRepeatedDeclarationDoNotReset_ExplicitResetKeepsIdentity()
    {
        var seq = Create("=> Declare\n: first\n=> Declare\n: second\n#.resetClock(name=\"song\")\n: third\n=== Declare ===\n  #.clock(name=\"song\", source=\"music.position\")\n  -> return");
        double position = 40; int resets = 0;
        seq.BindClock("music.position", () => position, () => { resets++; position = 0; });
        seq.Start(); seq.PollClocks(seq.CurrentSessionId); long id = Clock(seq, "song").ClockId;
        Assert.Equal(0, resets); Assert.Equal(40, Clock(seq, "song").Position);
        seq.Step(); seq.PollClocks(seq.CurrentSessionId);
        Assert.Equal(0, resets); Assert.Equal(id, Clock(seq, "song").ClockId); Assert.Equal(40, Clock(seq, "song").Position);
        seq.Step(); seq.PollClocks(seq.CurrentSessionId);
        Assert.Equal(1, resets); Assert.Equal(id, Clock(seq, "song").ClockId); Assert.Equal(0, Clock(seq, "song").Position);
        Assert.Equal("third", seq.CurrentPayload!.Content);
    }

    [Fact]
    public void NoResetCallbackPreservesRawValueWithoutRebasing()
    {
        var seq = Create("=> Declare\n: first\n=> Declare\n#.resetClock(name=\"song\")\n: after .at(value=39, clock=\"song\")\n=== Declare ===\n  #.clock(name=\"song\", source=\"music.position\")\n  -> return");
        seq.BindClock("music.position", () => 40); seq.Start(); seq.PollClocks(seq.CurrentSessionId);
        long id = Clock(seq, "song").ClockId; seq.Step();
        Assert.Equal("after", seq.CurrentPayload!.Content); Assert.Equal(id, Clock(seq, "song").ClockId);
        Assert.Equal(40, Clock(seq, "song").Position);
    }

    [Fact]
    public void DistinctExternalSourcesDoNotResetEachOtherOnCreation()
    {
        var seq = Create("#.clock(name=\"first\", source=\"first\")\n: before\n#.clock(name=\"second\", source=\"second\").resetClock(name=\"second\")\n: after");
        int first = 0, second = 0; double value = 17;
        seq.BindClock("first", () => 40, () => first++);
        seq.BindClock("second", () => value, () => { second++; value = 0; });
        seq.Start(); seq.Step(); seq.PollClocks(seq.CurrentSessionId);
        Assert.Equal(0, first); Assert.Equal(1, second); Assert.Equal(0, Clock(seq, "second").Position);
    }

    [Fact]
    public void SourceResetInvalidatesOtherSampledViewsBeforeLaterGatesRelease()
    {
        var seq = Create("""
            #.clock(name="timer").clock(name="a", source="view.a").clock(name="b", source="view.b")
            #.track(source="Reset", name="reset").track(source="Wait", name="wait")
            : main
            === Reset ===
              #.at(value=1, clock="timer").resetClock(name="a")
            === Wait ===
              : visible .at(value=20, clock="b")
            """);
        double position = 0; int readsB = 0;
        seq.BindClock("view.a", () => position, () => position = 0);
        seq.BindClock("view.b", () => { readsB++; return position; });
        seq.Start(); Assert.Equal(2, seq.GetPendingClockGates().Count);
        position = 40; seq.UpdateClocks(1, seq.CurrentSessionId);
        Assert.Equal(0, position);
        var pending = Assert.Single(seq.GetPendingClockGates());
        Assert.Equal("wait", pending.LaneName); Assert.Equal(0, pending.ClockPosition); Assert.Equal(3, readsB);
    }

    [Fact]
    public void RestartRetainsResetConfigurationButDoesNotResetFirstDeclaration()
    {
        var seq = Create("#.clock(name=\"song\", source=\"music.position\")\n: before\n#.resetClock(name=\"song\")\n: after");
        int resets = 0; seq.BindClock("music.position", () => 40, () => resets++);
        for (int session = 0; session < 2; session++)
        { seq.Start(); Assert.Equal(session, resets); seq.Step(); Assert.Equal(session + 1, resets); }
    }

    [Fact]
    public void RebindingWithoutResetRemovesPreviousResetConfiguration()
    {
        var seq = Create("#.clock(name=\"song\", source=\"music.position\")\n: before\n#.resetClock(name=\"song\")\n: after");
        int resets = 0;
        seq.BindClock("music.position", () => 1, () => resets++); seq.BindClock("music.position", () => 40);
        seq.Start(); seq.Step(); seq.PollClocks(seq.CurrentSessionId);
        Assert.Equal(0, resets); Assert.Equal(40, Clock(seq, "song").Position);
    }

    [Fact]
    public void UnbindingRemovesResetCallbackButAllowsPushedValues()
    {
        var seq = Create("#.clock(name=\"song\", source=\"music.position\")\n: before\n#.resetClock(name=\"song\")\n: after .at(value=4, clock=\"song\")");
        int resets = 0; seq.BindClock("music.position", () => 1, () => resets++); seq.UnbindClock("music.position");
        seq.Start(); seq.PublishClockPosition("music.position", 4, seq.CurrentSessionId); seq.Step();
        Assert.Equal("after", seq.CurrentPayload!.Content); Assert.Equal(0, resets); Assert.Equal(4, Clock(seq, "song").Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedOrReentrantResetIsDiagnosedWithoutChangingIdOrTerminatingPlayback(bool reentrant)
    {
        var seq = Create("#.clock(name=\"song\", source=\"music.position\")\n: before\n#.resetClock(name=\"song\")\n: after");
        var warnings = new List<ExecutionTrace>(); seq.OnTrace += x => { if (x.Kind == ExecutionTraceKind.Warning) warnings.Add(x); };
        seq.BindClock("music.position", () => 40, () => { if (reentrant) seq.Step(); else throw new InvalidOperationException("reset failed"); });
        seq.Start(); long id = Clock(seq, "song").ClockId; seq.Step();
        Assert.Equal("after", seq.CurrentPayload!.Content); Assert.Equal(id, Clock(seq, "song").ClockId);
        Assert.Single(warnings); Assert.Equal(3, warnings[0].LineNumber);
        Assert.Equal(ExecutionStatus.SuspendedAtBeat, seq.Status);
    }

    [Fact]
    public void AnonymousReplacementAndSelectingNamedDefaultReclaimsOnlyAnonymousInstances()
    {
        var seq = Create("#.clock()\n: first\n#.clock()\n: second\n#.clock(name=\"song\").defaultClock(name=\"song\")\n: third");
        seq.Start(); var first = Assert.Single(seq.GetClocks()); Assert.Null(first.Name);
        seq.UpdateClocks(4, seq.CurrentSessionId); seq.Step();
        var second = Assert.Single(seq.GetClocks()); Assert.Null(second.Name);
        Assert.NotEqual(first.ClockId, second.ClockId); Assert.Equal(0, second.Position);
        seq.Step(); Assert.Equal("song", Assert.Single(seq.GetClocks()).Name);
    }

    [Fact]
    public void AnonymousResetKeepsIdAndDefaultDoesNotRestoreOnCallReturn()
    {
        var seq = Create("#.clock()\n: first\n#.resetClock()\n: second\n=> Switch\n: after .at(value=2)\n=== Switch ===\n  #.clock(name=\"selected\").defaultClock(name=\"selected\")\n  -> return");
        seq.Start(); long id = Assert.Single(seq.GetClocks()).ClockId; seq.UpdateClocks(3, seq.CurrentSessionId); seq.Step();
        Assert.Equal(id, Assert.Single(seq.GetClocks()).ClockId); Assert.Equal(0, Assert.Single(seq.GetClocks()).Position);
        seq.Step(); Assert.Equal("selected", Assert.Single(seq.GetPendingClockGates()).ClockName);
        Assert.Single(seq.GetClocks()); seq.UpdateClocks(2, seq.CurrentSessionId);
        Assert.Equal("after", seq.CurrentPayload!.Content);
    }

    [Fact]
    public void NamedDeclarationDoesNotChangeCurrentDefault()
    {
        var seq = Create("#.clock().clock(name=\"named\")\n: waiting .at(value=1)"); seq.Start();
        Assert.Null(Assert.Single(seq.GetPendingClockGates()).ClockName);
        Assert.Equal(2, seq.GetClocks().Count);
    }

    [Fact]
    public void NewAuxiliaryDoesNotInheritMainDefault()
    {
        var seq = Create("#.clock().track(source=\"Aux\")\n: main\n=== Aux ===\n  : waiting .at(value=1)");
        var error = Assert.Throws<KtoryException>(() => seq.Start());
        Assert.Contains("no default clock", error.Message);
    }

    [Fact]
    public void AnonymousDefaultsAreIndependentPerPlayheadAndReclaimedWhenAuxiliaryStops()
    {
        var seq = Create("#.clock().track(source=\"Aux\", name=\"aux\")\n: main\n#.stopTrack(name=\"aux\")\n: after\n=== Aux ===\n  #.clock()\n  : waiting .at(value=10)");
        seq.Start(); Assert.Equal(2, seq.GetClocks().Count);
        Assert.Equal(2, seq.GetClocks().Select(x => x.OwnerLaneId).Distinct().Count());
        seq.Step(); Assert.Single(seq.GetClocks()); Assert.Empty(seq.GetPendingClockGates());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceLessFactoryCanOwnAnonymousOrNamedClockWithoutCoreInterpretingUnits(bool named)
    {
        var seq = Create(named
            ? "#.clock(name=\"host\", source=\"\").defaultClock(name=\"host\")\n: waiting .at(value=.5)\n#.resetClock()\n: after"
            : "#.clock()\n: waiting .at(value=.5)\n#.resetClock()\n: after");
        int creates = 0, reads = 0, resets = 0; double? value = null;
        seq.ClockFactory = request =>
        {
            creates++; Assert.Equal(named ? "host" : null, request.Name); Assert.Equal("root", request.LaneId);
            return new ClockSourceBinding(() => { reads++; return value; }, () => resets++);
        };
        seq.Start(); var clock = Assert.Single(seq.GetClocks()); long id = clock.ClockId;
        Assert.True(clock.IsExternal); Assert.Null(clock.Position); Assert.Equal(1, reads);
        seq.GetClocks(); seq.GetPendingClockGates(); Assert.Equal(1, reads);
        seq.UpdateClocks(100, seq.CurrentSessionId); Assert.Single(seq.GetPendingClockGates());
        value = .5; seq.PollClocks(seq.CurrentSessionId); Assert.Equal("waiting", seq.CurrentPayload!.Content);
        seq.Step(); Assert.Equal(1, resets); Assert.Equal(id, Assert.Single(seq.GetClocks()).ClockId); Assert.Equal(1, creates);
    }

    [Fact]
    public void FactoryDecliningUsesBuiltIn_ButThrowingDoesNotFallbackOrTerminate()
    {
        var builtin = Create("#.clock()\n: waiting .at(value=1)"); builtin.ClockFactory = _ => null;
        builtin.Start(); builtin.UpdateClocks(1, builtin.CurrentSessionId); Assert.Equal("waiting", builtin.CurrentPayload!.Content);
        var failed = Create("#.clock()\n: waiting .at(value=1)");
        var warnings = new List<ExecutionTrace>(); failed.OnTrace += x => { if (x.Kind == ExecutionTraceKind.Warning) warnings.Add(x); };
        failed.ClockFactory = _ => throw new InvalidOperationException("unavailable"); failed.Start(); failed.UpdateClocks(100, failed.CurrentSessionId);
        Assert.Single(failed.GetPendingClockGates()); Assert.Null(Assert.Single(failed.GetClocks()).Position); Assert.Single(warnings);
    }

    private static KtorySequencer Create(string script) => new(KtoryParser.Parse(script));
    private static ClockSnapshot Clock(KtorySequencer seq, string name) => Assert.Single(seq.GetClocks(), clock => clock.Name == name);
}
