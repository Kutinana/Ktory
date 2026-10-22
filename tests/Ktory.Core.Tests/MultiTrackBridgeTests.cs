using System.Text.Json;
using Ktory.Core.Runtime;
using Ktory.Wasm;

namespace Ktory.Core.Tests;

public class MultiTrackBridgeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyMainAcceptsInputWhileAuxiliaryOutputsAdvanceOnClock(bool wasm)
    {
        var host = new Host(wasm);
        var started = host.Start("""
            #.clock(name="song")
            #.track(source="Lyrics", name="lyric")
            #choice
              * [Enter]
                : first .dialogue
                : second
            #.awaitTrack(name="lyric")
            : joined
            === Lyrics ===
              : song one .at(value=0, clock="song").lyric
              : song two .at(value=1, clock="song")
            """);
        var menu = MainPresentation(started);
        var lyric = Assert.Single(TextOutputs(started), output => !output.GetProperty("isMain").GetBoolean());
        Assert.Equal("song one", Content(lyric));
        Assert.Single(started.GetProperty("activePresentations").EnumerateArray());
        var option = menu.GetProperty("choice").GetProperty("options")[0].GetProperty("id").GetString()!;
        var selected = host.Choice(option, Token(menu));
        Assert.Equal("first", Content(MainPresentation(selected)));
        Assert.Single(selected.GetProperty("visitedItems").EnumerateArray());
        Assert.Equal(Token(MainPresentation(selected)), Token(MainPresentation(host.Step(Token(lyric)))));
        Assert.Equal(Token(MainPresentation(selected)), Token(MainPresentation(host.Choice(option, Token(menu)))));
        var lyricsDone = host.Advance(1, Token(menu).SessionId);
        Assert.Contains(TextOutputs(lyricsDone), output => Content(output) == "song two");
        Assert.Contains(Outputs(lyricsDone), output => Kind(output) == StoryOutputKind.TrackEnded && !output.GetProperty("isMain").GetBoolean());
        Assert.Equal("first", Content(MainPresentation(lyricsDone)));
        var second = host.Step(Token(MainPresentation(lyricsDone)));
        Assert.Equal("second", Content(MainPresentation(second)));
        var joined = host.Step(Token(MainPresentation(second)));
        Assert.Equal("joined", Content(MainPresentation(joined)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalTimedContentIsDeliveredBeforeTrackAndSessionEnd(bool wasm)
    {
        var host = new Host(wasm);
        var started = host.Start("""
            #.clock(name="song")
            #.track(source="Lyrics", name="lyric")
            #.awaitTrack(name="lyric")
            === Lyrics ===
              : final .at(value=10, clock="song").duration(seconds=2)
            """);
        Assert.Equal("WaitingForTrack", started.GetProperty("status").GetString());
        var finished = host.Advance(10, started.GetProperty("sessionId").GetString()!);
        Assert.Equal("Completed", finished.GetProperty("status").GetString());
        Assert.Empty(finished.GetProperty("activePresentations").EnumerateArray());
        var outputs = Outputs(finished).ToArray();
        var contentIndex = Array.FindIndex(outputs, o => Kind(o) == StoryOutputKind.Content && o.GetProperty("payload").ValueKind != JsonValueKind.Null && Content(o) == "final");
        var auxiliaryEnd = Array.FindIndex(outputs, o => Kind(o) == StoryOutputKind.TrackEnded && !o.GetProperty("isMain").GetBoolean());
        var sessionEnd = Array.FindIndex(outputs, o => Kind(o) == StoryOutputKind.SessionEnded);
        Assert.True(contentIndex >= 0 && auxiliaryEnd > contentIndex && sessionEnd > auxiliaryEnd);
        Assert.Contains(finished.GetProperty("retainedPresentations").EnumerateArray(), p => Content(p) == "final");
        Assert.Empty(Outputs(host.Advance(0, started.GetProperty("sessionId").GetString()!)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LanguageRefreshReachesCompletedAuxiliaryWithoutReplayingTags(bool wasm)
    {
        var host = new Host(wasm);
        var initial = host.Start("""
            @defaultLang: en
            #.track(source="Lyrics", name="lyric")
            : Hello
              @zh: 你好
              .effect
            === Lyrics ===
              : Song
                @zh: 歌词
                .effect
            """);
        var translated = host.Secondary("zh", Token(MainPresentation(initial)));
        var refreshes = Outputs(translated).Where(o => Kind(o) == StoryOutputKind.LanguageRefresh).ToArray();
        Assert.Equal(2, refreshes.Length);
        Assert.All(refreshes, output => Assert.Equal("zh", output.GetProperty("payload").GetProperty("text").GetProperty("secondary").GetProperty("actualLanguage").GetString()));
        Assert.Equal(initial.GetProperty("recentLaneTags").GetArrayLength(), translated.GetProperty("recentLaneTags").GetArrayLength());
        Assert.Single(translated.GetProperty("activePresentations").EnumerateArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedSessionRetainedTextRefreshesBothLanguagesWithoutExecution(bool wasm)
    {
        var host = new Host(wasm);
        var initial = host.Start("""
            @defaultLang: en
            @speaker alice: en="Alice" | zh="爱丽丝"
            #.clock(name="song")
            #.track(source="Lyrics", name="lyric")
            alice: Hello
              @zh: 你好
              .effect
            === Lyrics ===
              alice: Song
                @zh: 歌词
                .at(value=0, clock="song").duration(seconds=2).effect
            """);
        var previousMainToken = Token(MainPresentation(initial));
        var ended = host.Step(previousMainToken);
        Assert.Equal("Completed", ended.GetProperty("status").GetString());
        Assert.Empty(ended.GetProperty("activePresentations").EnumerateArray());
        var retainedTokens = ended.GetProperty("retainedPresentations").EnumerateArray().Select(Token).ToArray();
        Assert.Equal(2, retainedTokens.Length);

        // A same-session language preference survives the last beat's input authority.
        var primary = host.Language("zh", previousMainToken);
        var secondary = host.Secondary("en", previousMainToken);
        foreach (var refreshed in new[] { primary, secondary })
        {
            Assert.Equal("Completed", refreshed.GetProperty("status").GetString());
            Assert.Equal(ended.GetProperty("sessionId").GetString(), refreshed.GetProperty("sessionId").GetString());
            Assert.Equal(ended.GetProperty("presentationId").GetInt64(), refreshed.GetProperty("presentationId").GetInt64());
            Assert.Empty(refreshed.GetProperty("activePresentations").EnumerateArray());
            Assert.Empty(refreshed.GetProperty("pendingClockGates").EnumerateArray());
            Assert.Empty(refreshed.GetProperty("clocks").EnumerateArray());
            Assert.Equal(ended.GetProperty("visitedItems").GetRawText(), refreshed.GetProperty("visitedItems").GetRawText());
            Assert.Equal(ended.GetProperty("callStackDepth").GetInt32(), refreshed.GetProperty("callStackDepth").GetInt32());
            Assert.Equal(ended.GetProperty("recentTags").GetRawText(), refreshed.GetProperty("recentTags").GetRawText());
            Assert.Equal(ended.GetProperty("recentLaneTags").GetRawText(), refreshed.GetProperty("recentLaneTags").GetRawText());
            Assert.Equal(retainedTokens, refreshed.GetProperty("retainedPresentations").EnumerateArray().Select(Token));
            var refreshes = Outputs(refreshed).ToArray();
            Assert.Equal(2, refreshes.Length);
            Assert.All(refreshes, output => Assert.Equal(StoryOutputKind.LanguageRefresh, Kind(output)));
            Assert.Equal(retainedTokens, refreshes.Select(Token));
            foreach (var retained in refreshed.GetProperty("retainedPresentations").EnumerateArray())
            {
                Assert.False(retained.GetProperty("requiresInput").GetBoolean());
                Assert.Equal("zh", retained.GetProperty("payload").GetProperty("text").GetProperty("primary").GetProperty("actualLanguage").GetString());
                Assert.Equal("爱丽丝", retained.GetProperty("payload").GetProperty("speakerName").GetProperty("primary").GetProperty("content").GetString());
            }
        }
        foreach (var retained in secondary.GetProperty("retainedPresentations").EnumerateArray())
        {
            Assert.Equal("en", retained.GetProperty("payload").GetProperty("text").GetProperty("secondary").GetProperty("actualLanguage").GetString());
            Assert.Equal("Alice", retained.GetProperty("payload").GetProperty("speakerName").GetProperty("secondary").GetProperty("content").GetString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbsoluteClockReleasesAuxiliaryAndOldSessionCannotReleaseReplacement(bool wasm)
    {
        var host = new Host(wasm);
        const string script = """
            #.clock(name="animation", source="progress")
            #.track(source="Animation", name="animation")
            : talk
            === Animation ===
              : cue .at(value=0.25, clock="animation").effect
            """;
        var initial = host.Start(script);
        string session = initial.GetProperty("sessionId").GetString()!;
        Assert.Single(initial.GetProperty("pendingClockGates").EnumerateArray());
        Assert.Single(host.Clock("progress", -1, session).GetProperty("pendingClockGates").EnumerateArray());
        var ready = host.Clock("progress", 0.25, session);
        Assert.Contains(TextOutputs(ready), output => Content(output) == "cue");
        Assert.Empty(ready.GetProperty("pendingClockGates").EnumerateArray());
        var backward = host.Clock("progress", -1, session);
        Assert.Empty(TextOutputs(backward));
        Assert.Equal(ready.GetProperty("recentTags").GetArrayLength(), backward.GetProperty("recentTags").GetArrayLength());
        var replacement = host.Start(script);
        var stale = host.Clock("progress", 100, session);
        Assert.Equal(replacement.GetProperty("sessionId").GetString(), stale.GetProperty("sessionId").GetString());
        Assert.Single(stale.GetProperty("pendingClockGates").EnumerateArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnonymousDefaultClockGateIsNotAnActivePresentation(bool wasm)
    {
        var host = new Host(wasm);
        var initial = host.Start("#.clock()\n: later .at(value=2)\n: after");
        Assert.Equal("WaitingForClock", initial.GetProperty("status").GetString());
        Assert.Empty(initial.GetProperty("activePresentations").EnumerateArray());
        Assert.Single(initial.GetProperty("pendingClockGates").EnumerateArray());
        var ready = host.Advance(2, initial.GetProperty("sessionId").GetString()!);
        Assert.Equal("later", Content(MainPresentation(ready)));
        Assert.Equal("after", Content(MainPresentation(host.Step(Token(MainPresentation(ready))))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InputDeltaPrecedesDeclarationAndResetPreservesClockIdentity(bool wasm)
    {
        var host = new Host(wasm);
        var initial = host.Start("""
            : prelude
            #.clock(name="scene")
            : first .at(value=2, clock="scene")
            #.resetClock(name="scene")
            : second .at(value=2, clock="scene")
            """);
        string session = initial.GetProperty("sessionId").GetString()!;
        var firstGate = host.Step(Token(MainPresentation(initial)), 10);
        var firstClock = Clock(firstGate, "scene");
        Assert.Equal(0, firstClock.GetProperty("position").GetDouble());
        Assert.Equal(10, firstClock.GetProperty("declaredAtElapsedSeconds").GetDouble());
        var first = host.Advance(2, session);
        var secondGate = host.Step(Token(MainPresentation(first)), 3);
        var secondClock = Clock(secondGate, "scene");
        Assert.Equal(0, secondClock.GetProperty("position").GetDouble());
        Assert.Equal(firstClock.GetProperty("clockId").GetInt64(), secondClock.GetProperty("clockId").GetInt64());
        Assert.Equal("second", Content(MainPresentation(host.Advance(2, session))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaleInputCannotApplyDeltaOrPublishSourcesToAuxiliaryGates(bool wasm)
    {
        var host = new Host(wasm);
        var initial = host.Start("""
            #.clock(name="timer")
            #.clock(name="animation", source="progress")
            #.track(source="Timer", name="timer")
            #.track(source="Animation", name="animation")
            #choice
              * [Enter]
                : first
                : second
            === Timer ===
              : timer .at(value=5, clock="timer")
            === Animation ===
              : animation .at(value=5, clock="animation")
            """);
        var menu = MainPresentation(initial);
        var option = menu.GetProperty("choice").GetProperty("options")[0].GetProperty("id").GetString()!;
        var first = host.Choice(option, Token(menu));
        foreach (var stale in new[] {
            host.Step(Token(menu), 100, new() { ["progress"] = 100 }),
            host.Choice(option, Token(menu), 100, new() { ["progress"] = 100 }),
            host.Break(Token(menu), 100, new() { ["progress"] = 100 }) })
        {
            Assert.Equal(Token(MainPresentation(first)), Token(MainPresentation(stale)));
            Assert.Equal(2, stale.GetProperty("pendingClockGates").GetArrayLength());
            Assert.Equal(0, Clock(stale, "timer").GetProperty("position").GetDouble());
            Assert.Equal(JsonValueKind.Null, Clock(stale, "animation").GetProperty("position").ValueKind);
            Assert.Contains(stale.GetProperty("diagnostics").EnumerateArray(), diagnostic => diagnostic.GetProperty("kind").GetInt32() == (int)ExecutionTraceKind.InputIgnored);
        }
        var accepted = host.Step(Token(MainPresentation(first)), 5, new() { ["progress"] = 5 });
        Assert.Equal("second", Content(MainPresentation(accepted)));
        Assert.Contains(TextOutputs(accepted), o => Content(o) == "timer");
        Assert.Contains(TextOutputs(accepted), o => Content(o) == "animation");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalResetWithoutCallbackPreservesRawPositionAndIdentity(bool wasm)
    {
        var host = new Host(wasm);
        var initial = host.Start("""
            #.clock(name="animation", source="progress")
            : first .at(value=5, clock="animation")
            #.resetClock(name="animation")
            : second .at(value=5, clock="animation")
            """);
        var first = host.Clock("progress", 7, initial.GetProperty("sessionId").GetString()!);
        var second = host.Step(Token(MainPresentation(first)));
        Assert.Equal("second", Content(MainPresentation(second)));
        Assert.Equal(7, Clock(second, "animation").GetProperty("position").GetDouble());
        Assert.Equal(Clock(first, "animation").GetProperty("clockId").GetInt64(), Clock(second, "animation").GetProperty("clockId").GetInt64());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LanguageAndObservationDoNotSampleSourcesOrReleasePendingGates(bool wasm)
    {
        var host = new Host(wasm);
        var initial = host.Start("""
            @defaultLang: en
            #.clock(name="animation", source="progress")
            #.track(source="Animation", name="animation")
            : Hello
              @zh: 你好
            === Animation ===
              : later .at(value=5, clock="animation").effect
            """);
        var getterCalls = 0;
        host.Sequencer.BindClock("progress", () => { getterCalls++; return 10; });
        var token = Token(MainPresentation(initial));
        var refreshed = host.Language("zh", token);
        var secondary = host.Secondary("en", token);
        Assert.Equal(0, getterCalls);
        Assert.Single(refreshed.GetProperty("pendingClockGates").EnumerateArray());
        Assert.Single(secondary.GetProperty("pendingClockGates").EnumerateArray());
        Assert.Equal(initial.GetProperty("recentTags").GetArrayLength(), secondary.GetProperty("recentTags").GetArrayLength());
        var sampled = host.Advance(0, token.SessionId);
        Assert.True(getterCalls > 0);
        Assert.Contains(TextOutputs(sampled), o => Content(o) == "later");
    }

    private static JsonElement MainPresentation(JsonElement state) => Assert.Single(state.GetProperty("activePresentations").EnumerateArray(), p => p.GetProperty("isMain").GetBoolean());
    private static IEnumerable<JsonElement> Outputs(JsonElement state) => state.GetProperty("outputEvents").EnumerateArray();
    private static StoryOutputKind Kind(JsonElement output) => (StoryOutputKind)output.GetProperty("kind").GetInt32();
    private static IEnumerable<JsonElement> TextOutputs(JsonElement state) => Outputs(state).Where(o => Kind(o) == StoryOutputKind.Content && o.GetProperty("payload").ValueKind != JsonValueKind.Null && o.GetProperty("payload").GetProperty("stepType").GetInt32() == (int)StepType.Text);
    private static string? Content(JsonElement value) => value.GetProperty("payload").GetProperty("content").GetString();
    private static JsonElement Clock(JsonElement state, string name) => state.GetProperty("clocks").EnumerateArray().Single(clock => clock.GetProperty("name").GetString() == name);
    private static PresentationToken Token(JsonElement value) => new(value.GetProperty("token").GetProperty("sessionId").GetString()!, value.GetProperty("token").GetProperty("presentationId").GetInt64());
    private sealed class Host(bool wasm)
    {
        private readonly KtoryWasmBridge _wasm = new();
        private readonly RunnerSessionService _http = new();
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private static JsonElement State(string json) => JsonSerializer.Deserialize<JsonElement>(json);
        private static JsonElement State(RunnerSessionState state) => JsonSerializer.SerializeToElement(state, JsonOptions);
        public JsonElement Start(string script) => wasm ? State(_wasm.Start(script, "en", null)) : State(_http.Start(script, "en", null));
        public KtorySequencer Sequencer => (KtorySequencer)(wasm ? typeof(KtoryWasmBridge).GetField("_sequencer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(_wasm)!
            : typeof(RunnerSessionService).GetField("_sequencer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(_http)!);
        public JsonElement Step(PresentationToken token, double? delta = null, Dictionary<string, double?>? sources = null) => wasm
            ? State(delta.HasValue ? _wasm.StepWithClock(token.PresentationId, token.SessionId, delta.Value, sources) : _wasm.Step(token.PresentationId, token.SessionId))
            : State(_http.Step(token, delta, sources));
        public JsonElement Choice(string id, PresentationToken token, double? delta = null, Dictionary<string, double?>? sources = null) => wasm
            ? State(delta.HasValue ? _wasm.ChoiceWithClock(id, token.PresentationId, token.SessionId, delta.Value, sources) : _wasm.Choice(id, token.PresentationId, token.SessionId))
            : State(_http.SubmitChoice(id, token, delta, sources));
        public JsonElement Break(PresentationToken token, double delta, Dictionary<string, double?> sources) => wasm
            ? State(_wasm.BreakWithClock(token.PresentationId, token.SessionId, delta, sources)) : State(_http.Break(token, delta, sources));
        public JsonElement Language(string locale, PresentationToken token) => wasm
            ? State(_wasm.SetLanguage(locale, token.PresentationId, token.SessionId)) : State(_http.SetLanguage(locale, token));
        public JsonElement Secondary(string locale, PresentationToken token) => wasm
            ? State(_wasm.SetSecondaryLanguage(locale, token.PresentationId, token.SessionId)) : State(_http.SetSecondaryLanguage(locale, token));
        public JsonElement Clock(string name, double value, string session) => wasm
            ? State(_wasm.ReportClockPosition(name, value, session)) : State(_http.ReportClockPosition(name, value, session));
        public JsonElement Advance(double delta, string session) => wasm
            ? State(_wasm.AdvanceClocks(delta, session)) : State(_http.AdvanceClocks(delta, session));
    }
}
