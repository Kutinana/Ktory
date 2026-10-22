using System.Text.Json;
using Ktory.Core.Runtime;
using Ktory.Wasm;

namespace Ktory.Core.Tests;

public class SecondaryLanguageBridgeTests
{
    private const string Script = """
        @defaultLang: zh
        @speaker alice: zh="爱丽丝" | ja="アリス"
        alice:
          @zh: 你好
          @en: Hello
          .effect
        #choice
          * [@zh: 继续] [@en: Continue]
            : first
            : second
          + [@zh: 留下]
            : stay
        """;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SecondaryOutputIsIndependentAndRefreshPreservesTokenAndEffects(bool wasm)
    {
        var host = new Host(wasm);
        var first = host.Start(Script, "zh", "en");
        Assert.Equal("en", first.GetProperty("secondaryLanguage").GetString());
        var payload = first.GetProperty("payload");
        Assert.Equal("Hello", payload.GetProperty("text").GetProperty("secondary").GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("speakerName").GetProperty("secondary").ValueKind);
        var names = host.Secondary("ja", Token(first));
        Assert.Equal(Token(first), Token(names));
        Assert.Equal("你好", names.GetProperty("payload").GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, names.GetProperty("payload").GetProperty("text").GetProperty("secondary").ValueKind);
        Assert.Equal("アリス", names.GetProperty("payload").GetProperty("speakerName").GetProperty("secondary").GetProperty("content").GetString());
        Assert.Single(names.GetProperty("recentTags").EnumerateArray());
        Assert.Empty(names.GetProperty("visitedItems").EnumerateArray());

        var disabled = host.Secondary(null, Token(names));
        Assert.Equal(JsonValueKind.Null, disabled.GetProperty("secondaryLanguage").ValueKind);
        Assert.Equal(JsonValueKind.Null, disabled.GetProperty("payload").GetProperty("speakerName").GetProperty("secondary").ValueKind);
        Assert.Equal(Token(first), Token(disabled));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MenuTranslationPreservesChoicesAndOldRefreshCannotAffectReplacementSession(bool wasm)
    {
        var host = new Host(wasm);
        var first = host.Start(Script, "zh", null);
        var menu = host.Step(Token(first));
        var options = menu.GetProperty("choice").GetProperty("options");
        var refreshed = host.Secondary("en", Token(menu));
        var translatedOptions = refreshed.GetProperty("choice").GetProperty("options");
        Assert.Equal(Token(menu), Token(refreshed));
        Assert.Equal(options[0].GetProperty("id").GetString(), translatedOptions[0].GetProperty("id").GetString());
        Assert.Equal("Continue", translatedOptions[0].GetProperty("text").GetProperty("secondary").GetProperty("content").GetString());
        Assert.Equal(JsonValueKind.Null, translatedOptions[1].GetProperty("text").GetProperty("secondary").ValueKind);
        Assert.Empty(refreshed.GetProperty("visitedItems").EnumerateArray());
        var branch = host.Choice(translatedOptions[0].GetProperty("id").GetString()!, Token(refreshed));
        Assert.Equal("first", branch.GetProperty("payload").GetProperty("content").GetString());
        Assert.Single(branch.GetProperty("visitedItems").EnumerateArray());

        var replacement = host.Start(Script, "zh", null);
        var stale = host.Secondary("ja", Token(menu));
        Assert.Equal(Token(replacement), Token(stale));
        Assert.Equal(JsonValueKind.Null, stale.GetProperty("secondaryLanguage").ValueKind);
        Assert.Contains(stale.GetProperty("diagnostics").EnumerateArray(), value =>
            value.GetProperty("kind").GetInt32() == (int)ExecutionTraceKind.InputIgnored);
    }

    private static PresentationToken Token(JsonElement state) => new(
        state.GetProperty("sessionId").GetString()!, state.GetProperty("presentationId").GetInt64());

    private sealed class Host(bool wasm)
    {
        private readonly KtoryWasmBridge _wasm = new();
        private readonly RunnerSessionService _http = new();
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
        private static JsonElement State(string json) => JsonSerializer.Deserialize<JsonElement>(json);
        private static JsonElement State(RunnerSessionState state) => JsonSerializer.SerializeToElement(state, JsonOptions);
        public JsonElement Start(string script, string locale, string? secondary) => wasm
            ? State(_wasm.StartWithLanguages(script, locale, null, secondary)) : State(_http.Start(script, locale, null, secondary));
        public JsonElement Step(PresentationToken token) => wasm
            ? State(_wasm.Step(token.PresentationId, token.SessionId)) : State(_http.Step(token));
        public JsonElement Secondary(string? locale, PresentationToken token) => wasm
            ? State(_wasm.SetSecondaryLanguage(locale, token.PresentationId, token.SessionId)) : State(_http.SetSecondaryLanguage(locale, token));
        public JsonElement Choice(string id, PresentationToken token) => wasm
            ? State(_wasm.Choice(id, token.PresentationId, token.SessionId)) : State(_http.SubmitChoice(id, token));
    }
}
