using System.Text.Json;
using Ktory.Core.Runtime;
using Ktory.Web;

namespace Ktory.Core.Tests;

public class ReaderBridgeContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadRejectsOldTokensAndDuplicateChoicePreservesFirstBeat(bool wasm)
    {
        var host = new Host(wasm);
        const string script = "#choice\n  * [Enter]\n    .selected\n    : first\n    : second";
        var old = host.Start(script);
        string oldOption = Option(old, 0).GetProperty("id").GetString()!;
        var current = host.Start(script);
        Assert.NotEqual(Token(old).SessionId, Token(current).SessionId);

        var rejected = host.Choice(oldOption, Token(old));
        Assert.Equal("AwaitingChoice", rejected.GetProperty("status").GetString());
        Assert.Equal(Token(current), Token(rejected));
        Assert.Empty(rejected.GetProperty("visitedItems").EnumerateArray());
        Assert.Empty(rejected.GetProperty("recentTags").EnumerateArray());
        Assert.Contains(rejected.GetProperty("diagnostics").EnumerateArray(), item =>
            item.GetProperty("kind").GetInt32() == (int)ExecutionTraceKind.InputIgnored);

        Assert.Equal(Token(current), Token(host.Step(Token(old))));
        Assert.Equal(Token(current), Token(host.Step(new PresentationToken("", 0))));
        string option = Option(current, 0).GetProperty("id").GetString()!;
        var first = host.Choice(option, Token(current));
        Assert.Equal("first", Content(first));
        Assert.Single(first.GetProperty("recentTags").EnumerateArray());
        var duplicate = host.Choice(option, Token(current));
        Assert.Equal("first", Content(duplicate));
        Assert.Equal(Token(first), Token(duplicate));
        Assert.Single(duplicate.GetProperty("recentTags").EnumerateArray());
        Assert.Equal("second", Content(host.Step(Token(first))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DiagnosticsAndIndependentSpeakerLanguageSurviveBothBridges(bool wasm)
    {
        var host = new Host(wasm);
        const string script = "@speaker alice: ja=\"アリス\"\nalice:\n  @fr: Bonjour\n  .effect\n#mystery\n  + [@ja: 次へ]\n    : branch";
        var first = host.Start(script, "en");
        var payload = first.GetProperty("payload");
        Assert.Equal("Bonjour", payload.GetProperty("content").GetString());
        Assert.Equal("fr", payload.GetProperty("actualLanguage").GetString());
        Assert.Equal("ja", payload.GetProperty("speakerActualLanguage").GetString());
        var warnings = first.GetProperty("diagnostics");
        Assert.Equal(2, warnings.GetArrayLength());
        Assert.All(warnings.EnumerateArray(), item => Assert.Equal(2, item.GetProperty("lineNumber").GetInt32()));
        var refresh = host.Language("en", Token(first));
        Assert.Equal(2, refresh.GetProperty("diagnostics").GetArrayLength());
        Assert.Single(refresh.GetProperty("recentTags").EnumerateArray());

        var menu = host.Step(Token(refresh));
        Assert.Equal("ja", Option(menu, 0).GetProperty("actualLanguage").GetString());
        Assert.Contains(menu.GetProperty("diagnostics").EnumerateArray(), item =>
            item.GetProperty("message").GetString()!.Contains("mystery") &&
            item.GetProperty("lineNumber").GetInt32() == 5);
        var clean = host.Start(": clean");
        Assert.Empty(clean.GetProperty("diagnostics").EnumerateArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewPolicyIsExplicitAndSessionCommandsRejectOldSession(bool wasm)
    {
        var host = new Host(wasm);
        const string script = "#choice.loop\n  + ? {has_key} [Unknown]\n    : unknown branch\n  + ? {false} [False]\n    : false branch\n: after";
        var old = host.Start(script);
        var current = host.Start(script);
        Assert.True(Option(current, 0).GetProperty("canSelect").GetBoolean());
        Assert.False(Option(current, 1).GetProperty("canSelect").GetBoolean());
        Assert.Empty(current.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("zh", host.Language("en", Token(old)).GetProperty("requestedLanguage").GetString());
        Assert.Equal("AwaitingChoice", host.Break(Token(old)).GetProperty("status").GetString());
        var translated = host.Language("en", Token(current));
        Assert.Equal("en", translated.GetProperty("requestedLanguage").GetString());
        Assert.Equal("after", Content(host.Break(Token(translated))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpeakerDefaultsResolveBeforeSerializationAndRefreshDoesNotReplay(bool wasm)
    {
        var host = new Host(wasm);
        var first = host.Start("@speaker alice: zh=Alice\n  .emotion(normal).voice(soft)\nalice: first .emotion(happy)\nalice: second");
        var tags = first.GetProperty("payload").GetProperty("tags");
        Assert.Equal("happy", tags[1].GetProperty("positionalArgs")[0].GetString());
        var refreshed = host.Language("en", Token(first));
        Assert.Equal(first.GetProperty("recentTags").GetArrayLength(), refreshed.GetProperty("recentTags").GetArrayLength());
        var second = host.Step(Token(refreshed));
        Assert.Equal("normal", second.GetProperty("payload").GetProperty("tags")[0].GetProperty("positionalArgs")[0].GetString());
    }

    private static JsonElement Option(JsonElement state, int index) => state.GetProperty("choice").GetProperty("options")[index];
    private static string? Content(JsonElement state) => state.GetProperty("payload").GetProperty("content").GetString();
    private static PresentationToken Token(JsonElement state) => new(
        state.GetProperty("sessionId").GetString()!, state.GetProperty("presentationId").GetInt64());

    private sealed class Host(bool wasm)
    {
        private readonly KtoryWasmBridge _wasm = new();
        private readonly RunnerSessionService _http = new();
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private JsonElement State(string? json, RunnerSessionState? state) => json != null
            ? JsonSerializer.Deserialize<JsonElement>(json) : JsonSerializer.SerializeToElement(state, JsonOptions);
        public JsonElement Start(string script, string locale = "zh") => wasm
            ? State(_wasm.Start(script, locale, null), null) : State(null, _http.Start(script, locale, null));
        public JsonElement Step(PresentationToken token) => wasm
            ? State(_wasm.Step(token.PresentationId, token.SessionId), null) : State(null, _http.Step(token));
        public JsonElement Choice(string id, PresentationToken token) => wasm
            ? State(_wasm.Choice(id, token.PresentationId, token.SessionId), null) : State(null, _http.SubmitChoice(id, token));
        public JsonElement Language(string locale, PresentationToken token) => wasm
            ? State(_wasm.SetLanguage(locale, token.PresentationId, token.SessionId), null) : State(null, _http.SetLanguage(locale, token));
        public JsonElement Break(PresentationToken token) => wasm
            ? State(_wasm.Break(token.PresentationId, token.SessionId), null) : State(null, _http.Break(token));
    }
}
