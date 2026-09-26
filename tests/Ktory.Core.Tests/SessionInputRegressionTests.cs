using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using Ktory.Core.Common;

namespace Ktory.Core.Tests;

public class SessionInputRegressionTests
{
    [Fact]
    public void StaleNumericChoice_IsDiscardedWithoutThrowingOrDispatchingTags()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(
            ": first\n#choice\n  + [pick]\n    : branch .effect"));
        var trace = new List<ExecutionTrace>();
        int effects = 0;
        sequencer.OnTrace += trace.Add;
        sequencer.OnTagsDispatched += _ => effects++;
        sequencer.Start();
        long first = sequencer.CurrentPresentationId;
        sequencer.Step();

        var error = Record.Exception(() => sequencer.SubmitChoice("pick", first));

        Assert.Null(error);
        Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
        Assert.Equal(0, effects);
        Assert.Single(trace, IsIgnoredInput);
        Assert.DoesNotContain(trace, entry => entry.Kind == ExecutionTraceKind.Error);
    }

    [Fact]
    public void ControllerBoundToOldSession_CannotFinishPrintingAfterRestart()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(": first .next\n: second .effect"));
        var controller = new PresentationController(sequencer);
        var trace = new List<ExecutionTrace>();
        sequencer.OnTrace += trace.Add;
        sequencer.Start();
        controller.SetupForCurrentBeat();
        sequencer.Start();

        controller.NotifyPrintingFinished();

        Assert.Equal("first", sequencer.CurrentPayload!.Content);
        Assert.Equal(PresentationPhase.Printing, controller.Phase);
        Assert.Single(trace, IsIgnoredInput);
        Assert.DoesNotContain(trace, entry => entry.Kind == ExecutionTraceKind.Tag && entry.Message == ".effect");
    }

    [Fact]
    public void HostManagedController_DoesNotEmitDuplicatePendingAdvance()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(": first\n: second"));
        var controller = new PresentationController(sequencer) { AutoStepSequencer = false };
        int requests = 0;
        controller.OnAdvanceRequested += () => requests++;
        sequencer.Start();
        controller.SetupForCurrentBeat();
        controller.NotifyPrintingFinished();

        controller.RequestAdvance();
        controller.RequestAdvance();

        Assert.Equal(1, requests);
        Assert.Equal("first", sequencer.CurrentPayload!.Content);
        Assert.Equal(PresentationPhase.Completed, controller.Phase);
    }

    [Fact]
    public void AdvanceSubscriberRestart_DoesNotAdvanceTheNewSession()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(": first\n: second .effect"));
        var controller = new PresentationController(sequencer);
        int effects = 0;
        sequencer.OnTagsDispatched += _ => effects++;
        sequencer.Start();
        controller.SetupForCurrentBeat();
        controller.OnAdvanceRequested += () => sequencer.Start();
        controller.NotifyPrintingFinished();

        controller.HandleUserClick();

        Assert.Equal("first", sequencer.CurrentPayload!.Content);
        Assert.Equal(0, effects);
        Assert.Equal(PresentationPhase.Printing, controller.Phase);
    }

    [Fact]
    public void FastForwardSubscriberRestart_DoesNotFinishPrintingTheNewSession()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(": first .next\n: second .effect"));
        var controller = new PresentationController(sequencer);
        sequencer.Start();
        controller.SetupForCurrentBeat();
        controller.OnFastForwardRequested += () =>
        {
            sequencer.Start();
            controller.SetupForCurrentBeat();
        };

        controller.HandleUserClick();

        Assert.Equal("first", sequencer.CurrentPayload!.Content);
        Assert.Equal(PresentationPhase.Printing, controller.Phase);
    }

    [Fact]
    public void Restart_ReusesNumericIdButRejectsTheOldSessionToken()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(": first\n: second .effect"));
        var trace = new List<ExecutionTrace>();
        int effects = 0;
        sequencer.OnTrace += trace.Add;
        sequencer.OnTagsDispatched += _ => effects++;
        sequencer.Start();
        var old = sequencer.CurrentPresentationToken;
        sequencer.Start();
        var current = sequencer.CurrentPresentationToken;
        Assert.Equal(old.PresentationId, current.PresentationId);
        Assert.NotEqual(old.SessionId, current.SessionId);

        sequencer.Step(old);
        sequencer.SetLanguage("en", old);
        sequencer.Break(old);

        Assert.Equal("first", sequencer.CurrentPayload!.Content);
        Assert.Equal("zh", sequencer.RequestedLanguage);
        Assert.Equal(current, sequencer.CurrentPresentationToken);
        Assert.Equal(0, effects);
        Assert.Equal(3, trace.Count(IsIgnoredInput));
        Assert.DoesNotContain(trace, entry => entry.Kind == ExecutionTraceKind.Error);
        sequencer.Step(current);
        Assert.Equal("second", sequencer.CurrentPayload!.Content);
        Assert.Equal(1, effects);
    }

    [Fact]
    public void ReplacementSequencer_RejectsOldMenuInputEvenWhenAstAndNumericIdMatch()
    {
        var file = KtoryParser.Parse("#choice\n  + [pick]\n    : branch .effect");
        var previous = new KtorySequencer(file);
        previous.Start();
        var token = previous.CurrentPresentationToken;
        string choice = previous.CurrentChoice!.Options[0].Id;
        var current = new KtorySequencer(file);
        var trace = new List<ExecutionTrace>();
        current.OnTrace += trace.Add;
        current.Start();
        Assert.Equal(token.PresentationId, current.CurrentPresentationId);
        Assert.Equal(choice, current.CurrentChoice!.Options[0].Id);

        current.SubmitChoice(choice, token);
        current.Step(token);

        Assert.Equal(ExecutionStatus.AwaitingChoice, current.Status);
        Assert.Equal(2, trace.Count(IsIgnoredInput));
        Assert.DoesNotContain(trace, entry => entry.Kind == ExecutionTraceKind.Tag);
        current.SubmitChoice(choice, current.CurrentPresentationToken);
        Assert.Equal("branch", current.CurrentPayload!.Content);
    }

    [Fact]
    public void DuplicateChoice_DoesNotReplayTags_ButInvalidCurrentChoiceStillFails()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(
            "#choice\n  + [pick] .picked\n    : branch\n#choice\n  + [next]\n    : final"));
        var trace = new List<ExecutionTrace>();
        int tags = 0;
        sequencer.OnTrace += trace.Add;
        sequencer.OnTagsDispatched += _ => tags++;
        sequencer.Start();
        var menu = sequencer.CurrentPresentationToken;
        string choice = sequencer.CurrentChoice!.Options[0].Id;
        sequencer.SubmitChoice(choice, menu);

        sequencer.SubmitChoice(choice, menu);
        Assert.Equal("branch", sequencer.CurrentPayload!.Content);
        Assert.Equal(1, tags);
        sequencer.Step(sequencer.CurrentPresentationToken);
        sequencer.SubmitChoice(choice, menu);
        Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
        Assert.Equal(2, trace.Count(IsIgnoredInput));
        Assert.Equal(1, tags);

        Assert.Throws<KtoryException>(() => sequencer.SubmitChoice("invalid", sequencer.CurrentPresentationToken));
        Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
    }

    [Fact]
    public void DelayedPresentationCallbacks_DoNotChangeTheNextBeatOrItsTimers()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(": first .next(1)\n: second .next(1)\n: third .effect"));
        var controller = new PresentationController(sequencer);
        var trace = new List<ExecutionTrace>();
        sequencer.OnTrace += trace.Add;
        sequencer.Start();
        controller.SetupForCurrentBeat();
        var first = controller.CurrentPresentationToken;
        controller.NotifyPrintingFinished(first);
        controller.Update(1, first);
        var second = controller.CurrentPresentationToken;
        Assert.NotEqual(first, second);

        controller.NotifyPrintingFinished(first);
        controller.Update(100, first);
        controller.HandleUserClick(first);
        controller.RequestAdvance(first);

        Assert.Equal("second", sequencer.CurrentPayload!.Content);
        Assert.Equal(PresentationPhase.Printing, controller.Phase);
        Assert.Equal(0, controller.ElapsedInPhase);
        Assert.Equal(4, trace.Count(IsIgnoredInput));
        Assert.DoesNotContain(trace, entry => entry.Kind == ExecutionTraceKind.Tag && entry.Message == ".effect");
        controller.NotifyPrintingFinished(second);
        controller.Update(1, second);
        Assert.Equal("third", sequencer.CurrentPayload!.Content);
    }

    [Fact]
    public void RestartAndSetup_RejectCapturedOldPrintingAndTimerCallbacks()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(": first .next(1)\n: second"));
        var controller = new PresentationController(sequencer);
        sequencer.Start();
        controller.SetupForCurrentBeat();
        var old = controller.CurrentPresentationToken;
        controller.NotifyPrintingFinished(old);
        sequencer.Start();
        controller.SetupForCurrentBeat();

        controller.NotifyPrintingFinished(old);
        controller.Update(100, old);

        Assert.Equal("first", sequencer.CurrentPayload!.Content);
        Assert.Equal(PresentationPhase.Printing, controller.Phase);
        Assert.Equal(0, controller.ElapsedInPhase);
    }

    [Fact]
    public void HostManagedAdvanceEvent_CarriesTheCapturedToken_ForDelayedExecution()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(": first\n: second .effect"));
        var controller = new PresentationController(sequencer) { AutoStepSequencer = false };
        var requests = new List<PresentationToken>();
        controller.OnAdvanceRequestedWithToken += requests.Add;
        sequencer.Start();
        controller.SetupForCurrentBeat();
        var first = controller.CurrentPresentationToken;
        controller.NotifyPrintingFinished(first);
        controller.RequestAdvance(first);
        controller.RequestAdvance(first);
        Assert.Equal(first, Assert.Single(requests));

        sequencer.Start();
        controller.SetupForCurrentBeat();
        sequencer.Step(requests[0]);

        Assert.Equal("first", sequencer.CurrentPayload!.Content);
        Assert.Equal(PresentationPhase.Printing, controller.Phase);
    }

    [Fact]
    public void InvalidatingSession_IsIdempotent_AndPreventsOldObjectCallbacks()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(": first .next\n: second .effect"));
        var controller = new PresentationController(sequencer);
        var trace = new List<ExecutionTrace>();
        sequencer.OnTrace += trace.Add;
        sequencer.Start();
        controller.SetupForCurrentBeat();
        var token = controller.CurrentPresentationToken;
        int events = trace.Count;

        sequencer.InvalidateSession();
        sequencer.InvalidateSession();
        Assert.Equal(events, trace.Count);
        Assert.Null(sequencer.CurrentPayload);
        Assert.Null(sequencer.CurrentChoice);
        Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        Assert.Empty(sequencer.CallStack);
        Assert.Equal(0, sequencer.ActiveLoopCount);
        controller.NotifyPrintingFinished(token);
        controller.Update(100, token);
        controller.RequestAdvance(token);
        sequencer.Step(token);
        Assert.Equal(4, trace.Count(IsIgnoredInput));
        Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        Assert.DoesNotContain(trace, entry => entry.Kind == ExecutionTraceKind.Tag && entry.Message == ".effect");

        sequencer.Start();
        Assert.NotEqual(token.SessionId, sequencer.CurrentSessionId);
        sequencer.Step(token);
        Assert.Equal("first", sequencer.CurrentPayload!.Content);
    }

    [Fact]
    public void DuplicatePrintingCompletion_DoesNotResetAnActiveHoldTimer()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(": first .next(2)\n: second"));
        var controller = new PresentationController(sequencer);
        var trace = new List<ExecutionTrace>();
        sequencer.OnTrace += trace.Add;
        sequencer.Start();
        controller.SetupForCurrentBeat();
        var token = controller.CurrentPresentationToken;
        controller.NotifyPrintingFinished(token);
        controller.Update(1, token);

        controller.NotifyPrintingFinished(token);

        Assert.Equal(1, controller.ElapsedInPhase);
        Assert.Single(trace, IsIgnoredInput);
        controller.Update(1, token);
        Assert.Equal("second", sequencer.CurrentPayload!.Content);
    }

    [Fact]
    public void IgnoredInputObserverFailure_CannotInterruptPlayback()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(": first\n: second"));
        var trace = new List<ExecutionTrace>();
        sequencer.OnTrace += entry =>
        {
            if (IsIgnoredInput(entry)) throw new InvalidOperationException("observer");
        };
        sequencer.OnTrace += trace.Add;
        sequencer.Start();
        var token = sequencer.CurrentPresentationToken;

        sequencer.Step(default(PresentationToken));

        Assert.Equal("first", sequencer.CurrentPayload!.Content);
        Assert.Single(trace, IsIgnoredInput);
        sequencer.Step(token);
        Assert.Equal("second", sequencer.CurrentPayload!.Content);
    }

    [Fact]
    public void DeferredAutoBatch_FromOldSession_DoesNotRunAgainstRestartedSession()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse("#pause.loop.next"));
        var controller = new PresentationController(sequencer) { MaxAutoAdvancesPerBatch = 1 };
        sequencer.Start();
        controller.SetupForCurrentBeat();
        Assert.True(controller.HasDeferredAdvance);
        var old = controller.CurrentPresentationToken;
        sequencer.Start();
        var current = sequencer.CurrentPresentationToken;

        controller.Update(1, old);

        Assert.Equal(current, sequencer.CurrentPresentationToken);
        Assert.Equal(1, Assert.Single(sequencer.GetActiveLoopsSnapshot()).Iteration);
    }

    [Fact]
    public void FailedStart_InvalidatesCoreSession_AndCannotResumePriorStory()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(": first\n: second .effect"));
        int effects = 0;
        sequencer.OnTagsDispatched += _ => effects++;
        sequencer.Start();
        var token = sequencer.CurrentPresentationToken;

        Assert.Throws<KtoryException>(() => sequencer.Start("Missing"));
        sequencer.Step();
        sequencer.Step(token);

        Assert.Equal(ExecutionStatus.Error, sequencer.Status);
        Assert.Null(sequencer.CurrentPayload);
        Assert.Equal(string.Empty, sequencer.CurrentSessionId);
        Assert.Equal(0, effects);
        sequencer.Start();
        Assert.Equal("first", sequencer.CurrentPayload!.Content);
    }

    [Fact]
    public void LanguageChange_IsSessionScoped_IncludingAfterNaturalCompletion()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(": first\n: second"));
        sequencer.Start();
        var first = sequencer.CurrentPresentationToken;
        sequencer.Step(first);

        sequencer.SetLanguage("en", first);
        Assert.Equal("en", sequencer.RequestedLanguage);
        sequencer.Step(sequencer.CurrentPresentationToken);
        Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
        sequencer.SetLanguage("ja", sequencer.CurrentPresentationToken);
        Assert.Equal("ja", sequencer.RequestedLanguage);

        sequencer.InvalidateSession();
        sequencer.SetLanguage("fr", first);
        Assert.Equal("ja", sequencer.RequestedLanguage);
    }

    [Fact]
    public void AdvanceSubscriberRestart_DoesNotContinueAnAutoBatchInTheNewSession()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse(
            ": first\n=== Again ===\n  #pause.next\n  : after .effect"));
        var controller = new PresentationController(sequencer);
        int requests = 0;
        int effects = 0;
        sequencer.OnTagsDispatched += tags => effects += tags.Count(tag => tag.Name == "effect");
        controller.OnAdvanceRequested += () =>
        {
            if (++requests == 1) sequencer.Start("Again");
        };
        sequencer.Start();
        controller.SetupForCurrentBeat();
        controller.NotifyPrintingFinished();

        controller.HandleUserClick();

        Assert.Equal(1, requests);
        Assert.Equal(StepType.Directive, sequencer.CurrentPayload!.StepType);
        Assert.Equal("pause", sequencer.CurrentPayload.Content);
        Assert.Equal(0, effects);
        controller.Update(0, controller.CurrentPresentationToken);
        Assert.Equal("after", sequencer.CurrentPayload!.Content);
        Assert.Equal(1, effects);
    }

    [Fact]
    public void DeferredAutoBatches_ContinueAcrossTicks_InTheSameSession()
    {
        var sequencer = new KtorySequencer(KtoryParser.Parse("#pause.loop(5).next\n: after"));
        var controller = new PresentationController(sequencer) { MaxAutoAdvancesPerBatch = 1 };
        sequencer.Start();
        controller.SetupForCurrentBeat();
        string session = sequencer.CurrentSessionId;
        Assert.True(controller.HasDeferredAdvance);

        for (int tick = 0; tick < 5 && controller.HasDeferredAdvance; tick++)
            controller.Update(0, controller.CurrentPresentationToken);

        Assert.Equal("after", sequencer.CurrentPayload!.Content);
        Assert.Equal(session, sequencer.CurrentSessionId);
        Assert.False(controller.HasDeferredAdvance);
        Assert.Equal(PresentationPhase.Printing, controller.Phase);
    }

    private static bool IsIgnoredInput(ExecutionTrace entry) => entry.Kind == ExecutionTraceKind.InputIgnored;
}
