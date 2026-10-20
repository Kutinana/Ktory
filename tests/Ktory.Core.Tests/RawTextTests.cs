using System.Text.Json;
using Ktory.Core.Ast;
using Ktory.Core.Common;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using Ktory.Wasm;

namespace Ktory.Core.Tests;

public class RawTextTests
{
    private static TextStep Text(string source) => Assert.IsType<TextStep>(KtoryParser.Parse(source).RootBlock.Steps[0]);

    [Fact]
    public void InlineSpansProtectSyntaxAndPreserveSurroundingFormattingAndRealTags()
    {
        var step = Text("Alice: **我想写'''<b>.wait // [字]{音} **原文** \\x</b>'''。** .wait(1).next");
        Assert.Equal("<b>我想写<b>.wait // [字]{音} **原文** \\x</b>。</b>", step.TextVariants[""]);
        var pieces = step.TextVariantSegments[""];
        Assert.Equal(new[] { false, true, false }, pieces.Select(piece => piece.IsLiteral));
        Assert.Equal("<b>.wait // [字]{音} **原文** \\x</b>", pieces[1].Content);
        Assert.Equal(new[] { "wait", "next" }, step.Tags.Select(tag => tag.Name));
        Assert.Equal(1L, step.Tags[0].PositionalArgs[0]);
    }

    [Theory]
    [InlineData("我想写'''.wait'''", "我想写.wait")]
    [InlineData("'''literal: colon'''", "literal: colon")]
    [InlineData("a'''x'''b'''y'''c", "axbyc")]
    [InlineData("\"请写'''.wait'''。\"", "\"请写.wait。\"")]
    [InlineData("''''这里有 ''' 三个单引号''''", "这里有 ''' 三个单引号")]
    [InlineData("'''''a '''' b ''' c'''''", "a '''' b ''' c")]
    [InlineData("'''  leading and trailing  '''", "  leading and trailing  ")]
    [InlineData("‘’‘普通弯引号’‘’", "‘’‘普通弯引号’‘’")]
    public void InlineFenceLengthsAndLiteralWhitespace(string source, string expected)
    {
        var step = Text(source);
        Assert.Null(step.Speaker);
        Assert.Equal(expected, step.TextVariants[""]);
        Assert.Empty(step.Tags);
    }

    [Fact]
    public void MultilineBlockIsOneBeatAndPreservesBlankLinesRelativeIndentAndSourcePositions()
    {
        var file = KtoryParser.Parse(": '''\r\n  first  \r\n\r\n    #choice\r\n  @en: literal // comment\r\n  .wait\r\n''' .next\r\n: after");
        var first = Assert.IsType<TextStep>(file.RootBlock.Steps[0]);
        Assert.Equal("first  \n\n  #choice\n@en: literal // comment\n.wait", first.TextVariants[""]);
        Assert.Equal(1, first.LineNumber);
        Assert.Single(first.Tags);
        Assert.Equal("next", first.Tags[0].Name);
        var after = Assert.IsType<TextStep>(file.RootBlock.Steps[1]);
        Assert.Equal(8, after.LineNumber);
        Assert.False(file.HasLocalization);
    }

    [Fact]
    public void BlockCanDisplayOnlyFenceCharactersAndAllowsEmptyBodyAndTabs()
    {
        Assert.Equal("'''", Text(": ''''\n  '''\n''''").TextVariants[""]);
        Assert.Equal("", Text(": '''\n'''").TextVariants[""]);
        Assert.Equal("a\n b", Text(": '''\n a\n\tb\n'''").TextVariants[""]);
    }

    [Fact]
    public void CommentsAndDecoratorStringsDoNotOpenFencesAndTokensCannotCollideWithSourceText()
    {
        var file = KtoryParser.Parse("// ''' ignored\n#.sfx(\"'''\")\n: normal // ''' ignored\n: \uF000raw0\uF001 '''literal''' .sfx(\"'''\")");
        Assert.Equal(3, file.RootBlock.Steps.Count);
        Assert.Equal("normal", Assert.IsType<TextStep>(file.RootBlock.Steps[1]).TextVariants[""]);
        var raw = Assert.IsType<TextStep>(file.RootBlock.Steps[2]);
        Assert.Equal("\uF000raw0\uF001 literal", raw.TextVariants[""]);
        Assert.Equal("'''", raw.Tags.Single().PositionalArgs[0]);
        var escaped = Text(": " + string.Concat(Enumerable.Repeat("\\*", 300)) + "'''raw'''");
        Assert.Equal(new string('*', 300) + "raw", escaped.TextVariants[""]);
        Assert.Equal("raw", escaped.TextVariantSegments[""][1].Content);
    }

    [Theory]
    [InlineData(": before\nAlice: '''missing", 2, 8)]
    [InlineData(": '''\nbody\n''''", 1, 3)]
    public void UnclosedFenceReportsOpeningPosition(string source, int line, int column)
    {
        var error = Assert.Throws<KtoryException>(() => KtoryParser.Parse(source));
        Assert.Equal(line, error.Line);
        Assert.Equal(column, error.Column);
        var diagnostic = Assert.Single(KtoryLinter.Analyze(source));
        Assert.Equal("KTR_E012", diagnostic.Code);
        Assert.Equal(line, diagnostic.Line);
    }

    [Fact]
    public void LanguageSwitchAndFallbackKeepFragmentsWithoutReplayingTags()
    {
        var seq = new KtorySequencer(KtoryParser.Parse("@defaultLang: zh\nAlice: '''<b>same</b>'''\n  @en: <b>same</b>\n  @ja: '''\n    .wait\n    @fr: literal\n  '''\n  .effect"));
        int effects = 0;
        seq.OnTagsDispatched += _ => effects++;
        seq.Start(requestedLocale: "fr");
        var token = seq.CurrentPresentationToken;
        Assert.Equal("zh", seq.CurrentPayload!.ActualLanguage);
        Assert.True(seq.CurrentPayload.Segments!.Single().IsLiteral);
        var content = seq.CurrentPayload.Content;
        seq.SetLanguage("en");
        Assert.Equal(token, seq.CurrentPresentationToken);
        Assert.Equal(content, seq.CurrentPayload.Content);
        Assert.Null(seq.CurrentPayload.Segments);
        seq.SetLanguage("ja");
        Assert.Equal(".wait\n@fr: literal", seq.CurrentPayload.Segments!.Single().Content);
        Assert.Equal(1, effects);
    }

    [Fact]
    public void RawBlockDoesNotCreateSectionsAndDedentingContentDoesNotExitBranch()
    {
        var file = KtoryParser.Parse("#choice\n  + [Open]\n    Alice: '''\n=== Fake ===\n-> end\n#choice literal\n'''\n      .wait(1)\n    : after\n: joined");
        Assert.Single(file.Blocks);
        var menu = Assert.IsType<ContainerStep>(file.RootBlock.Steps[0]);
        Assert.Equal(2, menu.Items[0].InlineSteps.Count);
        var raw = Assert.IsType<TextStep>(menu.Items[0].InlineSteps[0]);
        Assert.Equal("=== Fake ===\n-> end\n#choice literal", raw.TextVariants[""]);
        Assert.Single(raw.Tags);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BridgesSerializeFragmentsForLocalizedTextAndChoices(bool wasm)
    {
        const string script = "@defaultLang: zh\nAlice:\n  @zh: 前'''<b>.wait</b>'''后\n  @en: '''\n    **literal**\n    // text\n  '''\n#choice\n  + [@zh: '''[literal]'''] [@en: '''<i>literal</i>''']\n    : branch";
        var bridge = new KtoryWasmBridge();
        var http = new RunnerSessionService();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var first = wasm ? JsonSerializer.Deserialize<JsonElement>(bridge.Start(script, "zh", null))
            : JsonSerializer.SerializeToElement(http.Start(script, "zh", null), options);
        Assert.True(first.GetProperty("payload").GetProperty("segments")[1].GetProperty("isLiteral").GetBoolean());
        var token = new PresentationToken(first.GetProperty("sessionId").GetString()!, first.GetProperty("presentationId").GetInt64());
        var translated = wasm ? JsonSerializer.Deserialize<JsonElement>(bridge.SetLanguage("en", token.PresentationId, token.SessionId))
            : JsonSerializer.SerializeToElement(http.SetLanguage("en", token), options);
        Assert.Equal("**literal**\n// text", translated.GetProperty("payload").GetProperty("segments")[0].GetProperty("content").GetString());
        var next = wasm ? JsonSerializer.Deserialize<JsonElement>(bridge.Step(token.PresentationId, token.SessionId))
            : JsonSerializer.SerializeToElement(http.Step(token), options);
        var choice = next.GetProperty("choice").GetProperty("options")[0];
        Assert.Equal("<i>literal</i>", choice.GetProperty("segments")[0].GetProperty("content").GetString());
        Assert.True(choice.GetProperty("segments")[0].GetProperty("isLiteral").GetBoolean());
    }
}
