using Ktory.Core.Ast;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;

namespace Ktory.Core.Tests;

public class LanguageOutputTests
{
    [Fact]
    public void Dialogue_ResolvesBodyAndNameIndependently_AndProjectsPrimaryForExistingHosts()
    {
        var seq = Create("@speaker alice: zh=\"爱丽丝\" | en=\"Alice\"\nalice:\n  @zh: 你好\n  @en: Hello\n: next");
        seq.Start(requestedLocale: "zh", secondaryLocale: "en");
        var payload = seq.CurrentPayload!;

        AssertValue(payload.Text.Primary, "你好", "zh", "zh");
        AssertValue(payload.Text.Secondary, "Hello", "en", "en");
        AssertValue(payload.SpeakerName!.Primary, "爱丽丝", "zh", "zh");
        AssertValue(payload.SpeakerName.Secondary, "Alice", "en", "en");
        Assert.Equal("alice", payload.SpeakerId);
        Assert.Equal(payload.Text.Primary.Content, payload.Content);
        Assert.Equal(payload.Text.Primary.ActualLanguage, payload.ActualLanguage);
        Assert.Equal(payload.SpeakerName.Primary.Content, payload.Speaker);
        Assert.Equal(payload.SpeakerName.Primary.ActualLanguage, payload.SpeakerActualLanguage);
        var token = seq.CurrentPresentationToken;

        seq.SetLanguage("en");
        Assert.Equal("Hello", payload.Content);
        Assert.Null(payload.Text.Secondary);
        Assert.Null(payload.SpeakerName.Secondary);
        Assert.Equal("alice", payload.SpeakerId);
        Assert.Equal(token, seq.CurrentPresentationToken);
        seq.SetSecondaryLanguage("zh");
        AssertValue(payload.Text.Secondary, "你好", "zh", "zh");
        seq.SetSecondaryLanguage(null);
        Assert.Null(payload.Text.Secondary);
        Assert.Null(payload.SpeakerName.Secondary);
        Assert.Equal("Hello", payload.Content);
    }

    [Fact]
    public void Secondary_MissingExactTranslation_IsAbsentWithoutFallbackOrWarning()
    {
        var seq = Create("@defaultLang: zh\n@speaker alice: zh=\"爱丽丝\" | en=\"Alice\"\nalice:\n  @en: Hello\n  @fr: Bonjour");
        var trace = new List<ExecutionTrace>();
        seq.OnTrace += trace.Add;
        seq.Start(requestedLocale: "en", secondaryLocale: "de");

        Assert.Null(seq.CurrentPayload!.Text.Secondary);
        Assert.Null(seq.CurrentPayload.SpeakerName!.Secondary);
        Assert.Equal("de", seq.SecondaryLanguage);
        Assert.DoesNotContain(trace, entry => entry.Kind == ExecutionTraceKind.Warning);
        seq.SetSecondaryLanguage("en-US");
        Assert.Null(seq.CurrentPayload.Text.Secondary); // No implicit en-US -> en chain.
    }

    [Fact]
    public void Secondary_SameResolvedLocale_IsSuppressedFieldByField()
    {
        var seq = Create("@speaker alice: zh=\"爱丽丝\" | en=\"Alice\"\nalice:\n  @zh: 你好\nalice:\n  @zh: 再见\n  @en: Bye");
        seq.Start(requestedLocale: "en", secondaryLocale: "zh");

        AssertValue(seq.CurrentPayload!.Text.Primary, "你好", "en", "zh");
        Assert.Null(seq.CurrentPayload.Text.Secondary);
        AssertValue(seq.CurrentPayload.SpeakerName!.Secondary, "爱丽丝", "zh", "zh");

        seq.Step();
        AssertValue(seq.CurrentPayload!.Text.Secondary, "再见", "zh", "zh");
        seq.SetLanguageOutput("de", "ZH");
        Assert.Equal("zh", seq.CurrentPayload.ActualLanguage);
        Assert.Null(seq.CurrentPayload.Text.Secondary);
        Assert.Null(seq.CurrentPayload.SpeakerName!.Secondary);
    }

    [Fact]
    public void Secondary_DifferentLocalesWithSameCharacters_AreDistinctTranslations()
    {
        var seq = Create(":\n  @en: OK\n  @ja: OK");
        seq.Start(requestedLocale: "en", secondaryLocale: "ja");
        AssertValue(seq.CurrentPayload!.Text.Secondary, "OK", "ja", "ja");
        Assert.Null(seq.CurrentPayload.SpeakerName);
    }

    [Fact]
    public void Secondary_UnlocalizedTextLiteralNamesAndEmptyValues_AreAbsent()
    {
        var seq = Create("Alice: Hello\n:\n#choice\n  + [Continue]\n    : end");
        seq.Start(requestedLocale: "en", secondaryLocale: "zh");
        Assert.Null(seq.CurrentPayload!.ActualLanguage);
        Assert.Null(seq.CurrentPayload.Text.Secondary);
        Assert.Null(seq.CurrentPayload.SpeakerName!.Primary.ActualLanguage);
        Assert.Null(seq.CurrentPayload.SpeakerName.Secondary);
        seq.Step();
        Assert.Empty(seq.CurrentPayload!.Content);
        Assert.Null(seq.CurrentPayload.Text.Secondary);
        seq.Step();
        Assert.Null(Assert.Single(seq.CurrentChoice!.Options).Text.Secondary);
    }

    [Fact]
    public void Secondary_UnadornedValueWithDeclaredDefaultLanguage_IsAnExactTranslation()
    {
        var seq = Create("@defaultLang: zh\n: 你好\n  @en: Hello");
        seq.Start(requestedLocale: "en", secondaryLocale: "zh");
        AssertValue(seq.CurrentPayload!.Text.Secondary, "你好", "zh", "zh");
    }

    [Fact]
    public void Secondary_PrimaryDoubleMissingFallback_RemainsTruthfulWithoutDuplicatingWarnings()
    {
        var seq = Create("@defaultLang: zh\n:\n  @fr: Bonjour\n  @de: Hallo");
        var warnings = new List<ExecutionTrace>();
        seq.OnTrace += entry => { if (entry.Kind == ExecutionTraceKind.Warning) warnings.Add(entry); };
        seq.Start(requestedLocale: "en", secondaryLocale: "fr");
        AssertValue(seq.CurrentPayload!.Text.Primary, "Bonjour", "en", "fr");
        Assert.Null(seq.CurrentPayload.Text.Secondary);
        Assert.Single(warnings);
        seq.SetSecondaryLanguage("de");
        AssertValue(seq.CurrentPayload.Text.Secondary, "Hallo", "de", "de");
        seq.SetSecondaryLanguage("it");
        Assert.Null(seq.CurrentPayload.Text.Secondary);
        Assert.Single(warnings);
    }

    [Fact]
    public void ChoiceRefresh_PreservesCurrentGuardSnapshotAndOptionIdentity_WithoutEvaluatingHostAgain()
    {
        var seq = Create("#choice\n  * ? {allowed} [@zh: 继续]\n    [@en: Continue]\n    .picked\n    : branch\n  + [@zh: 等待]\n    [@en: Wait]\n    : wait");
        var evaluator = new CountingEvaluator();
        seq.Evaluator = evaluator;
        int effects = 0;
        seq.OnTagsDispatched += _ => effects++;
        seq.Start(requestedLocale: "zh");
        var choice = seq.CurrentChoice!;
        var option = choice.Options[0];
        var token = seq.CurrentPresentationToken;
        Assert.Equal(1, evaluator.Count);
        evaluator.Result = false;

        seq.SetSecondaryLanguage("en");
        seq.SetLanguage("en");
        seq.SetLanguageOutput("zh", "en");
        Assert.Same(choice, seq.CurrentChoice);
        Assert.Same(option, seq.CurrentChoice!.Options[0]);
        AssertValue(option.Text.Secondary, "Continue", "en", "en");
        Assert.Equal(1, evaluator.Count);
        Assert.True(option.CanSelect);
        Assert.Empty(seq.VisitedItemIds);
        Assert.Equal(token, seq.CurrentPresentationToken);
        Assert.Equal(0, effects);

        seq.SubmitChoice(option.Id, token);
        Assert.Equal("branch", seq.CurrentPayload!.Content);
        Assert.Equal(ExecutionStatus.SuspendedAtBeat, seq.Status);
        Assert.Contains(option.Id, seq.VisitedItemIds);
        Assert.Equal(1, effects);
    }

    [Theory]
    [InlineData(".wait(seconds=2).next(seconds=6)", false)]
    [InlineData(".wait.next(seconds=6)", false)]
    [InlineData(".wait(seconds=2).next", false)]
    [InlineData(".wait.next", true)]
    public void SecondaryRefresh_PreservesPrimaryTimingAndPrinting(string tags, bool printing)
    {
        var seq = Create(":\n  @zh: 你好\n  @en: This is a much longer secondary translation with a different reading speed.\n  " + tags + "\n: next");
        seq.Start(requestedLocale: "zh");
        var controller = new PresentationController(seq);
        controller.SetupForCurrentBeat();
        if (!printing) controller.NotifyPrintingFinished();
        controller.Update(0.4);
        var before = (controller.Phase, controller.ElapsedInPhase, controller.HoldDuration,
            controller.MinimumHoldDuration, controller.AutoAdvanceDuration, controller.AutoAdvanceRemaining,
            controller.MinimumHoldRemaining, controller.FastForwardLockRemaining);
        var token = seq.CurrentPresentationToken;

        seq.SetSecondaryLanguage("en");
        controller.RefreshSecondaryLanguage();
        seq.SetSecondaryLanguage("de");
        controller.RefreshSecondaryLanguage();
        seq.SetSecondaryLanguage(null);
        controller.RefreshSecondaryLanguage();

        Assert.Equal(before, (controller.Phase, controller.ElapsedInPhase, controller.HoldDuration,
            controller.MinimumHoldDuration, controller.AutoAdvanceDuration, controller.AutoAdvanceRemaining,
            controller.MinimumHoldRemaining, controller.FastForwardLockRemaining));
        Assert.Equal(token, seq.CurrentPresentationToken);
        Assert.Equal("你好", seq.CurrentPayload!.Content);
    }

    [Fact]
    public void SecondaryRefresh_PreservesCallLoopAndTagState()
    {
        var seq = Create("=> Sub\n: after\n=== Sub ===\n  :\n    @zh: 你好\n    @en: Hello\n    .loop(count=2).effect\n  -> return");
        int effects = 0;
        seq.OnTagsDispatched += _ => effects++;
        seq.Start(requestedLocale: "zh");
        var stack = seq.CallStack.ToArray();
        Assert.Single(stack);
        var token = seq.CurrentPresentationToken;
        Assert.Equal(1, seq.ActiveLoopCount);
        seq.SetSecondaryLanguage("en");
        seq.SetSecondaryLanguage(null);
        Assert.Equal(stack, seq.CallStack.ToArray());
        Assert.Equal(1, seq.ActiveLoopCount);
        Assert.Equal(token, seq.CurrentPresentationToken);
        Assert.Equal(1, effects);
        seq.Step();
        Assert.Equal("你好", seq.CurrentPayload!.Content);
        Assert.Equal(2, effects);
        seq.Step();
        Assert.Equal("after", seq.CurrentPayload!.Content);
    }

    [Fact]
    public void SessionAwareLanguageChanges_AcceptSameSessionOldBeat_AndRejectRestartedOrClosedSession()
    {
        var seq = Create(": one\n: two");
        var traces = new List<ExecutionTrace>();
        seq.OnTrace += traces.Add;
        seq.Start(requestedLocale: "en");
        var oldBeat = seq.CurrentPresentationToken;
        seq.Step();
        seq.SetSecondaryLanguage("zh", oldBeat);
        Assert.Equal("zh", seq.SecondaryLanguage);
        seq.SetLanguageOutput("ja", "en", oldBeat);
        Assert.Equal("ja", seq.RequestedLanguage);
        Assert.Equal("en", seq.SecondaryLanguage);

        seq.Start(requestedLocale: "en");
        Assert.Null(seq.SecondaryLanguage);
        seq.SetSecondaryLanguage("zh", oldBeat);
        Assert.Null(seq.SecondaryLanguage);
        var current = seq.CurrentPresentationToken;
        seq.InvalidateSession();
        seq.SetLanguageOutput("ja", "fr", current);
        Assert.Equal("en", seq.RequestedLanguage);
        Assert.Null(seq.SecondaryLanguage);
        Assert.Equal(2, traces.Count(entry => entry.Kind == ExecutionTraceKind.InputIgnored));
    }

    [Fact]
    public void NextStatementReplacesPreviousDisplayRegardlessOfHostSlot()
    {
        var seq = Create("""
            @defaultLang: zh
            : 左边
              @en: Left
              .slot("left")
            : 右边
              @en: Right
              .slot("right")
            : 后续
            """);
        int effects = 0;
        seq.OnTagsDispatched += _ => effects++;
        seq.Start(requestedLocale: "zh");
        var left = Assert.Single(seq.LastOutputs);
        seq.Step();
        var right = Assert.Single(seq.LastOutputs);
        var input = seq.CurrentPresentationToken;

        seq.SetLanguageOutput("en", "zh");

        var refreshed = Assert.Single(seq.LastOutputs);
        Assert.Equal(right.Token, refreshed.Token);
        Assert.Equal("Right", refreshed.Payload!.Content);
        Assert.Equal("右边", refreshed.Payload.Text.Secondary!.Content);
        Assert.Equal(right.Token, Assert.Single(seq.GetRetainedPresentations()).Token);
        Assert.Equal(input, seq.CurrentPresentationToken);
        Assert.Equal(2, effects);
        Assert.Equal("左边", left.Payload!.Content);
        seq.ReleaseOutput(left.Token);
        seq.Step(left.Token);
        Assert.Equal(input, seq.CurrentPresentationToken);
        Assert.Equal(right.Token, Assert.Single(seq.GetRetainedPresentations()).Token);
    }

    [Fact]
    public void AuxiliaryBatchKeepsOnlyItsLastStatementAfterCompletion()
    {
        var seq = Create("""
            @defaultLang: zh
            #.track(source="Captions").await
            === Captions ===
              : 左边
                @en: Left
              : 中间
                @en: Center
              : 右边
                @en: Right
            """);
        seq.Start(requestedLocale: "zh");
        Assert.Equal(ExecutionStatus.Completed, seq.Status);
        var right = Assert.Single(seq.LastOutputs, output => output.Payload?.Content == "右边");
        Assert.Equal(right.Token, Assert.Single(seq.GetRetainedPresentations()).Token);

        seq.SetLanguageOutput("en", "zh");

        var refreshed = Assert.Single(seq.LastOutputs);
        Assert.Equal(right.Token, refreshed.Token);
        Assert.Equal("Right", refreshed.Payload!.Content);
        Assert.Equal("右边", refreshed.Payload.Text.Secondary!.Content);
        Assert.Empty(seq.GetActivePresentations());
        seq.ReleaseOutput(right.Token);
        seq.SetLanguage("zh");
        Assert.Empty(seq.LastOutputs);
        seq.Start(requestedLocale: "en");
        seq.ReleaseOutput(right.Token);
        Assert.Equal("Right", Assert.Single(seq.GetRetainedPresentations()).Payload!.Content);
    }

    [Fact]
    public void RetainedLanguageRefreshDuringClockWait_DoesNotSampleReleaseGateOrReexecuteEffects()
    {
        var seq = Create("""
            @defaultLang: zh
            #.clock(name="song", source="player")
            #.track(source="Captions", name="captions")
            : Main
              @en: Main translated
            : Future .at(value=10, clock="song")
            === Captions ===
              : 字幕
                @en: Caption
                .effect
              : Future caption .at(value=10, clock="song")
            """);
        int reads = 0, effects = 0;
        double position = 0;
        seq.BindClock("player", () => { reads++; return position; });
        seq.OnTagsDispatched += _ => effects++;
        seq.Start(requestedLocale: "zh");
        seq.Step();
        Assert.Equal(ExecutionStatus.WaitingForClock, seq.Status);
        var before = (reads, effects);
        position = 10;

        seq.SetLanguageOutput("en", "zh");

        Assert.Equal(before, (reads, effects));
        Assert.Equal(2, seq.GetPendingClockGates().Count);
        Assert.Equal(ExecutionStatus.WaitingForClock, seq.Status);
        Assert.Equal(2, seq.LastOutputs.Count);
        Assert.All(seq.LastOutputs, output => Assert.Equal(StoryOutputKind.LanguageRefresh, output.Kind));
        Assert.Contains(seq.LastOutputs, output => output.Payload?.Content == "Caption" && output.Payload.Text.Secondary?.Content == "字幕");
        seq.UpdateClocks(0, seq.CurrentSessionId);
        Assert.Equal("Future", seq.CurrentPayload!.Content);
        Assert.Empty(seq.GetPendingClockGates());
    }

    private static KtorySequencer Create(string source) => new(KtoryParser.Parse(source));

    private static void AssertValue(ResolvedLocalizedValue? value, string content, string requested, string actual)
    {
        Assert.NotNull(value);
        Assert.Equal(content, value.Content);
        Assert.Equal(requested, value.RequestedLanguage);
        Assert.Equal(actual, value.ActualLanguage);
    }

    private sealed class CountingEvaluator : IExpressionEvaluator
    {
        public int Count { get; private set; }
        public bool Result { get; set; } = true;
        public bool EvaluateCondition(string expression) { Count++; return Result; }
    }
}
