using Ktory.Core.Ast;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using Ktory.Core.Common;

namespace Ktory.Core.Tests;

public class PlayheadExecutionTests
{
    private static KtorySequencer Create(string source) => new(KtoryParser.Parse(source));
    private static IEnumerable<StoryOutput> Texts(IEnumerable<StoryOutput> events) => events.Where(e => e.Kind == StoryOutputKind.Content && e.Payload?.StepType == StepType.Text);

    [Theory]
    [InlineData("")]
    [InlineData(", name=\"init\"")]
    public void AwaitSuffixCompletesInitializationBeforeMainUsesItsClock(string name)
    {
        var seq = Create("#.track(source=\"Init\"" + name + ").await\n#.defaultClock(name=\"initialized\")\n: ready .at(value=0)\n=== Init ===\n  #.clock(name=\"initialized\")");
        seq.Start();
        Assert.Equal("ready", seq.CurrentPayload!.Content);
        Assert.Equal(ExecutionStatus.SuspendedAtBeat, seq.Status);
        Assert.Single(seq.GetClocks());
        Assert.Equal(new[] { StoryOutputKind.TrackEnded, StoryOutputKind.Content }, seq.LastOutputs.Select(x => x.Kind));
    }

    [Theory]
    [InlineData("", 2)]
    [InlineData(", name=\"init\"", 1)]
    public void AwaitSuffixPreservesAnonymousPerEntryAndNamedOnceCreation(string name, int expected)
    {
        var seq = Create("#.track(source=\"Init\"" + name + ").await.loop(count=2)\n: ready\n=== Init ===\n  : initialized");
        seq.Start();
        Assert.Equal("ready", seq.CurrentPayload!.Content);
        Assert.Equal(expected, Texts(seq.LastOutputs).Count(x => !x.IsMain));
        Assert.Equal(expected, seq.LastOutputs.Count(x => x.Kind == StoryOutputKind.TrackEnded));
    }

    [Fact]
    public void AnonymousAwaitTargetsItsOwnInstanceAndResumesTagsOnce()
    {
        var seq = Create("""
            #.clock(name="time")
            #.track(source="Short")
            #.before.track(source="Long").await.after
            : ready
            === Short ===
              : short .at(value=1, clock="time")
            === Long ===
              : long .at(value=3, clock="time")
            """);
        var tags = new List<string>();
        seq.OnTagsDispatched += batch => tags.AddRange(batch.Select(tag => tag.Name));
        seq.Start();
        Assert.Equal(new[] { "before" }, tags);
        seq.UpdateClocks(1, seq.CurrentSessionId);
        Assert.Equal(ExecutionStatus.WaitingForTrack, seq.Status);
        Assert.Equal(new[] { "before", "at" }, tags);
        seq.Step();
        Assert.Equal(ExecutionStatus.WaitingForTrack, seq.Status);
        seq.UpdateClocks(2, seq.CurrentSessionId);
        Assert.Equal(ExecutionStatus.SuspendedAtBeat, seq.Status);
        Assert.Equal(new[] { "before", "at", "at", "after" }, tags);
        Assert.Equal(StepType.Directive, seq.CurrentPayload!.StepType);
        seq.Step();
        Assert.Equal("ready", seq.CurrentPayload!.Content);
    }

    [Fact]
    public void AuxiliaryIgnoresTrackAndAwaitSuffixTogether()
    {
        var seq = Create("""
            #.track(source="Work").await
            : ready
            === Work ===
              #.track(source="Uncreated").await
              : work
            === Uncreated ===
              : forbidden
            """);
        seq.Start();
        Assert.Equal(new[] { "work", "ready" }, Texts(seq.LastOutputs).Select(x => x.Payload!.Content));
        Assert.Single(seq.LastOutputs, x => x.Kind == StoryOutputKind.TrackEnded);
    }

    [Fact]
    public void EntryGateCanScheduleInitializationBeforeLaterTagsOfThatDirective()
    {
        var seq = Create("""
            #.clock(name="time")
            #.track(source="Init")
            #.at(value=1, clock="time").defaultClock(name="initialized")
            : ready .at(value=0)
            === Init ===
              #.clock(name="initialized")
            """);
        seq.Start();
        Assert.Equal(ExecutionStatus.WaitingForClock, seq.Status);
        seq.UpdateClocks(1, seq.CurrentSessionId);
        Assert.Equal("ready", seq.CurrentPayload!.Content);
    }

    [Fact]
    public void AuxiliarySkipsChoiceContainerGuardWhileMainEvaluatesIt()
    {
        var file = KtoryParser.Parse("""
            #.track(source="Shared", name="child")
            : pause
            -> Shared
            === Shared ===
              #choice
                + [Pick]
                  : branch
              : after
              -> end
            """);
        file.Blocks["Shared"].Steps[0].GuardCondition = "player_state";
        var evaluator = new CountingEvaluator();
        var seq = new KtorySequencer(file) { Evaluator = evaluator };
        seq.Start();
        Assert.Equal(0, evaluator.Reads);
        Assert.Contains(seq.GetRetainedPresentations(), p => !p.IsMain && p.Payload?.Content == "after");
        seq.Step();
        Assert.Equal(1, evaluator.Reads);
        Assert.Equal(ExecutionStatus.AwaitingChoice, seq.Status);
    }

    private sealed class CountingEvaluator : IExpressionEvaluator
    {
        internal int Reads;
        public bool EvaluateCondition(string expression) { Reads++; return true; }
    }

    [Fact]
    public void OnlyMainAcceptsInputAndAuxiliaryIgnoresPresentationTiming()
    {
        var seq = Create("""
            #.clock(name="song")
            #.track(source="Lyrics", name="lyric")
            : first
            : second
            -> end
            === Lyrics ===
              : A .at(value=3.5, clock="song").wait(seconds=20).next(seconds=20).duration(seconds=3)
              : B .at(value=6, clock="song")
            """);
        var outputs = new List<StoryOutput>(); seq.OnOutput += outputs.Add;
        seq.Start();
        var main = Assert.Single(seq.GetActivePresentations());
        Assert.True(main.IsMain); Assert.True(main.RequiresInput);
        seq.UpdateClocks(3.5, seq.CurrentSessionId);
        var lyric = Assert.Single(Texts(outputs), e => !e.IsMain);
        Assert.Equal("A", lyric.Payload!.Content);
        seq.Step(lyric.Token);
        Assert.Equal(main.Token, seq.CurrentPresentationToken);
        seq.UpdateClocks(2.5, seq.CurrentSessionId);
        Assert.Equal(new[] { "A", "B" }, Texts(outputs).Where(e => !e.IsMain).Select(e => e.Payload!.Content));
        Assert.Contains(outputs, e => e.Kind == StoryOutputKind.TrackEnded);
        Assert.Equal("first", seq.CurrentPayload!.Content);
        seq.Step(main.Token);
        Assert.Equal("second", seq.CurrentPayload!.Content);
        seq.Step(main.Token);
        Assert.Equal("second", seq.CurrentPayload!.Content);
    }

    [Fact]
    public void AwaitCompletesAtLastOutputWithoutWaitingForDuration()
    {
        var seq = Create("""
            #.clock(name="song")
            #.track(source="Lyrics", name="lyric")
            #.awaitTrack(name="lyric")
            === Lyrics ===
              : last .at(value=10, clock="song").duration(seconds=2)
            """);
        var outputs = new List<StoryOutput>(); seq.OnOutput += outputs.Add;
        seq.Start();
        Assert.Equal(ExecutionStatus.WaitingForTrack, seq.Status);
        seq.Step(); Assert.Equal(ExecutionStatus.WaitingForTrack, seq.Status);
        seq.UpdateClocks(10, seq.CurrentSessionId);
        Assert.Equal(ExecutionStatus.Completed, seq.Status);
        Assert.Equal(new[] { StoryOutputKind.Content, StoryOutputKind.TrackEnded, StoryOutputKind.SessionEnded }, outputs.Select(e => e.Kind));
        Assert.Equal("last", Assert.Single(seq.GetRetainedPresentations()).Payload!.Content);
        Assert.Empty(seq.GetClocks());
    }

    [Fact]
    public void AwaitResumesRemainingTagsInOrderAndThenAppliesPresentationWait()
    {
        var seq = Create("""
            #.clock(name="time")
            #.track(source="First", name="first")
            #.track(source="Second", name="second")
            #.before.awaitTrack(name="first").between.awaitTrack(name="second").after.wait(seconds=2)
            : done
            -> end
            === First ===
              : A .at(value=1, clock="time")
            === Second ===
              : B .at(value=3, clock="time")
            """);
        var tags = new List<string>();
        seq.OnTagsDispatched += batch => tags.AddRange(batch.Select(t => t.Name).Where(n => n is "before" or "between" or "after"));
        seq.Start();
        Assert.Equal(new[] { "before" }, tags);
        seq.UpdateClocks(1, seq.CurrentSessionId);
        Assert.Equal(new[] { "before", "between" }, tags);
        Assert.Equal(ExecutionStatus.WaitingForTrack, seq.Status);
        seq.UpdateClocks(2, seq.CurrentSessionId);
        Assert.Equal(new[] { "before", "between", "after" }, tags);
        Assert.Equal(ExecutionStatus.SuspendedAtBeat, seq.Status);
        var controller = new PresentationController(seq);
        controller.SetupForCurrentBeat();
        controller.Update(1.9);
        Assert.Equal(StepType.Directive, seq.CurrentPayload!.StepType);
        controller.Update(0.1);
        Assert.Equal("done", seq.CurrentPayload!.Content);
        Assert.Equal(3, tags.Count);
    }

    [Fact]
    public void MainEndingStopsAuxiliaryWithoutStartingItsFutureContent()
    {
        var seq = Create("""
            #.clock(name="song")
            #.track(source="Lyrics", name="lyric")
            : main
            === Lyrics ===
              : now
              : never .at(value=10, clock="song")
            """);
        var outputs = new List<StoryOutput>(); seq.OnOutput += outputs.Add;
        seq.Start(); seq.Step();
        Assert.Equal(ExecutionStatus.Completed, seq.Status);
        Assert.Equal(new[] { "main", "now" }, Texts(outputs).Select(e => e.Payload!.Content));
        Assert.Equal(StoryOutputKind.SessionEnded, outputs.Last().Kind);
        seq.UpdateClocks(100, seq.CurrentSessionId);
        Assert.DoesNotContain(Texts(outputs), e => e.Payload!.Content == "never");
    }

    [Fact]
    public void SharedSectionChoicesAndTrackControlsAreIgnoredOnlyByAuxiliary()
    {
        var script = """
            #.track(source="Wrapper", name="child")
            : main pause
            => Shared
            -> end
            === Wrapper ===
              => Shared
              -> end
            === Shared ===
              #.track(source="Leaf", name="nested")
              #choice .menuEffect
                * [Go] .chosen
                  : branch first
                  : branch second
              : shared .effect
              -> return
            === Leaf ===
              : leaf
            """;
        var seq = Create(script);
        var tags = new List<LaneTagDispatch>(); seq.OnLaneTagsDispatched += tags.Add;
        seq.Start();
        Assert.Empty(seq.VisitedItemIds);
        Assert.DoesNotContain(tags, t => t.Tags.Any(x => x.Name == "menuEffect"));
        Assert.Single(tags, t => t.Tags.Any(x => x.Name == "effect"));
        seq.Step();
        Assert.Equal(ExecutionStatus.AwaitingChoice, seq.Status);
        var menu = seq.CurrentChoice!; var token = seq.CurrentPresentationToken;
        seq.SubmitChoice(menu.Options[0].Id, token);
        Assert.Equal("branch first", seq.CurrentPayload!.Content);
        Assert.Single(seq.VisitedItemIds);
        seq.SubmitChoice(menu.Options[0].Id, token);
        Assert.Equal("branch first", seq.CurrentPayload!.Content);
        Assert.Contains(seq.GetRetainedPresentations(), p => p.LaneName == "nested" && p.Payload?.Content == "leaf");
    }

    [Theory]
    [InlineData("-> end")]
    [InlineData("")]
    public void AuxiliaryNamedSectionCompletionNeverFallsThroughToRoot(string ending)
    {
        var seq = Create("#.track(source=\"Helper\", name=\"child\")\n: main\n-> end\n=== Helper ===\n  : helper\n  " + ending + "\n: forbidden");
        var outputs = new List<StoryOutput>(); seq.OnOutput += outputs.Add;
        seq.Start();
        Assert.Equal(new[] { "main", "helper" }, Texts(outputs).Select(e => e.Payload!.Content));
        Assert.Equal("main", seq.CurrentPayload!.Content);
    }

    [Fact]
    public void TwoHeadsCanCallSameSectionWithIndependentStacksInFifoOrder()
    {
        var seq = Create("""
            #.track(source="Work", name="a")
            #.track(source="Work", name="b")
            : main
            -> end
            === Work ===
              => Helper
              : after
              -> end
            === Helper ===
              : inside
              -> return
            """);
        var outputs = new List<StoryOutput>(); seq.OnOutput += outputs.Add;
        seq.Start();
        Assert.Equal(new[] { "a:inside", "b:inside", "a:after", "b:after" }, Texts(outputs).Where(e => !e.IsMain).Select(e => e.TrackName + ":" + e.Payload!.Content));
    }

    [Fact]
    public void NamedDeclarationOnlyStartsOnceEvenAfterTerminationWhileAnonymousCanRepeat()
    {
        var seq = Create("""
            -> Again
            === Again ===
              #.track(source="Work", name="once")
              #.track(source="Work")
              : pause
              -> Again
            === Work ===
              : work .effect
            """);
        var outputs = new List<StoryOutput>(); seq.OnOutput += outputs.Add;
        seq.Start(); seq.Step();
        var auxiliary = Texts(outputs).Where(e => !e.IsMain).ToArray();
        Assert.Equal(3, auxiliary.Length);
        Assert.Single(auxiliary, e => e.TrackName == "once");
        Assert.Equal(3, auxiliary.Select(e => e.TrackId).Distinct().Count());
    }

    [Fact]
    public void StopBeforeCreationIsNotPreCancellationAndAwaitUncreatedDoesNotDeadlock()
    {
        var seq = Create("""
            -> Entry
            === Entry ===
              #.stopTrack(name="child")
              #.awaitTrack(name="child")
              #.track(source="Work", name="child")
              : pause
              #.stopTrack(name="child")
              #.awaitTrack(name="child")
              : after
              -> end
            === Work ===
              #.clock()
              : first
              : future .at(value=100)
            """);
        var trace = new List<ExecutionTrace>(); seq.OnTrace += trace.Add;
        seq.Start();
        Assert.Contains(trace, t => t.Kind == ExecutionTraceKind.Warning && t.Message.Contains("has not been started"));
        Assert.Contains(seq.GetRetainedPresentations(), p => p.Payload?.Content == "first");
        seq.Step();
        Assert.Equal("after", seq.CurrentPayload!.Content);
        Assert.Empty(seq.GetPendingClockGates());
        Assert.Empty(seq.GetClocks());
    }

    [Fact]
    public void EndedAuxiliaryOutputRefreshesLanguageWithoutReplayingEffectsOrMutatingEarlierDelivery()
    {
        var seq = Create("""
            @defaultLang: en
            #.track(source="Song", name="lyric")
            : Main
            -> end
            === Song ===
              : .effect
                @en: Song
                @zh: 歌
            """);
        int dispatches = 0; seq.OnTagsDispatched += _ => dispatches++;
        var outputs = new List<StoryOutput>(); seq.OnOutput += outputs.Add;
        seq.Start(requestedLocale: "en");
        var old = Assert.Single(Texts(outputs), e => !e.IsMain);
        int before = dispatches;
        seq.SetLanguage("zh");
        var refreshed = Assert.Single(seq.LastOutputs, e => !e.IsMain);
        Assert.Equal(old.Token, refreshed.Token);
        Assert.Equal("歌", refreshed.Payload!.Content);
        Assert.Equal("Song", old.Payload!.Content);
        Assert.Equal(before, dispatches);
        seq.ReleaseOutput(old.Token);
        seq.SetLanguage("en");
        Assert.DoesNotContain(seq.LastOutputs, e => !e.IsMain);
    }

    [Fact]
    public void EmptyMainBeatReplacesPreviousTextWithoutGivingItsTokenInputAuthority()
    {
        var seq = Create(": old\n:\n: next");
        seq.Start(); var old = seq.CurrentPresentationToken;
        seq.Step();
        Assert.Empty(seq.GetRetainedPresentations());
        Assert.Equal("", seq.CurrentPayload!.Content);
        seq.Step(old); Assert.Equal("", seq.CurrentPayload.Content);
        seq.Step(seq.CurrentPresentationToken); Assert.Equal("next", seq.CurrentPayload.Content);
    }

    [Fact]
    public void AuxiliaryLoopHasFiniteBatchBudgetAndPreservesSourceDiagnostics()
    {
        var seq = Create("#.track(source=\"Work\", name=\"spin\")\n: main\n=== Work ===\n  : repeated .loop");
        seq.MaxInstructionBudgetPerBatch = 30;
        var trace = new List<ExecutionTrace>(); seq.OnTrace += trace.Add;
        Assert.Throws<KtoryInstructionBudgetExceededException>(() => seq.Start());
        Assert.Equal(ExecutionStatus.Error, seq.Status);
        Assert.Contains(trace, t => t.Kind == ExecutionTraceKind.Error && t.LaneName == "spin");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReentrantCloseOrStartIsRejectedWithoutResurrectingSession(bool restart)
    {
        var seq = Create(": beat .effect");
        seq.OnTagsDispatched += _ => { if (restart) seq.Start(); else seq.InvalidateSession(); };
        Assert.Throws<KtoryControlFlowException>(() => seq.Start());
        Assert.Equal(ExecutionStatus.Error, seq.Status);
        Assert.Empty(seq.GetActivePresentations()); Assert.Empty(seq.CurrentSessionId);
    }

    [Fact]
    public void OutputAndTraceObserversCannotReenterExecution()
    {
        var seq = Create(": one\n: two");
        seq.OnTrace += _ => seq.Step();
        seq.OnOutput += _ => seq.Step();
        seq.Start(requestedLocale: "en");
        Assert.Equal("one", seq.CurrentPayload!.Content);
        var token = seq.CurrentPresentationToken;
        seq.Step(new PresentationToken("expired", 1));
        Assert.Equal(token, seq.CurrentPresentationToken);
    }

    [Fact]
    public void StaleManagerInputCannotDrainDeferredAutomaticWork()
    {
        var seq = Create("#spin.loop.next");
        seq.Start(); var old = seq.CurrentPresentationToken;
        var manager = new MultiTrackPresentationController(seq) { MaxAdvancesPerBatch = 1 };
        manager.Synchronize(); var current = seq.CurrentPresentationToken;
        Assert.True(manager.HasDeferredAdvance);
        manager.HandleUserClick(old); manager.NotifyPrintingFinished(old);
        manager.NotifyPrintingFinished(current); manager.HandleUserClick(current);
        Assert.Equal(current, seq.CurrentPresentationToken);
        manager.Update(0); Assert.NotEqual(current, seq.CurrentPresentationToken);
    }
}
