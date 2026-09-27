using Ktory.Core.Ast;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;

namespace Ktory.Core.Tests;

public class LocalizationFallbackTests
{
    [Fact]
    public void DoubleMissingTextAndSpeaker_KeepIndependentActualLanguagesAndWarnAtUseSite()
    {
        const string source = "@defaultLang: zh\n@speaker alice: ja=\"アリス\" | es=\"Alicia\"\nalice:\n  @fr: Bonjour\n  @de: Guten Tag\n  .effect";
        var seq = new KtorySequencer(KtoryParser.Parse(source));
        var warnings = ObserveWarnings(seq);
        int effects = 0;
        seq.OnTagsDispatched += _ => effects++;

        seq.Start(requestedLocale: "en");

        Assert.Equal("Bonjour", seq.CurrentPayload!.Content);
        Assert.Equal("fr", seq.CurrentPayload.ActualLanguage);
        Assert.Equal("アリス", seq.CurrentPayload.Speaker);
        Assert.Equal("ja", seq.CurrentPayload.SpeakerActualLanguage);
        Assert.Equal("en", seq.CurrentPayload.RequestedLanguage);
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, warning =>
        {
            Assert.Equal(3, warning.LineNumber);
            Assert.Equal("root", warning.Block);
            Assert.Contains("en", warning.Message);
            Assert.Contains("zh", warning.Message);
        });
        Assert.Contains(warnings, warning => warning.Message.Contains("fr"));
        Assert.Contains(warnings, warning => warning.Message.Contains("ja") && warning.Message.Contains("alice"));

        long presentation = seq.CurrentPresentationId;
        seq.SetLanguage("de");
        Assert.Equal("Guten Tag", seq.CurrentPayload.Content);
        Assert.Equal("de", seq.CurrentPayload.ActualLanguage);
        Assert.Equal("ja", seq.CurrentPayload.SpeakerActualLanguage);
        seq.SetLanguage("en");
        seq.SetLanguage("en");
        Assert.Equal(3, warnings.Count); // Only the new missing speaker locale adds a warning.
        Assert.Equal(presentation, seq.CurrentPresentationId);
        Assert.Equal(1, effects);
    }

    [Fact]
    public void DoubleMissingChoice_UsesFirstDeclaredTranslationAndWarnsWithoutReplayingSelection()
    {
        const string source = "@defaultLang: zh\n#choice\n  * [@ja: 一度]\n    [@fr: Une fois]\n    .selected\n    : branch";
        var seq = new KtorySequencer(KtoryParser.Parse(source));
        var warnings = ObserveWarnings(seq);
        int selected = 0;
        seq.OnTagsDispatched += _ => selected++;
        seq.Start(requestedLocale: "en");
        var choice = Assert.Single(seq.CurrentChoice!.Options);
        Assert.Equal("一度", choice.Label);
        Assert.Equal("ja", choice.ActualLanguage);
        Assert.Equal(3, Assert.Single(warnings).LineNumber);
        seq.SetLanguage("fr");
        Assert.Equal("Une fois", seq.CurrentChoice.Options[0].Label);
        seq.SetLanguage("en");
        Assert.Single(warnings);
        Assert.Empty(seq.VisitedItemIds);
        Assert.Equal(0, selected);
        seq.SubmitChoice(choice.Id);
        Assert.Equal("branch", seq.CurrentPayload!.Content);
        Assert.Contains(choice.Id, seq.VisitedItemIds);
        Assert.Equal(1, selected);
    }

    [Theory]
    [InlineData("en", "Hello", "Alice", "en")]
    [InlineData("de", "你好", "爱丽丝", "zh")]
    public void PreferredOrDefaultTranslation_DoesNotWarn(string requested, string text, string name, string actual)
    {
        const string source = "@speaker alice: zh=\"爱丽丝\" | en=\"Alice\"\nalice:\n  @zh: 你好\n  @en: Hello";
        var seq = new KtorySequencer(KtoryParser.Parse(source));
        var warnings = ObserveWarnings(seq);
        seq.Start(requestedLocale: requested);
        Assert.Equal(text, seq.CurrentPayload!.Content);
        Assert.Equal(name, seq.CurrentPayload.Speaker);
        Assert.Equal(actual, seq.CurrentPayload.ActualLanguage);
        Assert.Equal(actual, seq.CurrentPayload.SpeakerActualLanguage);
        Assert.Empty(warnings);
    }

    [Fact]
    public void LiteralSpeakerAndEmptyText_PreserveExistingValuesWithoutInventedNameLocale()
    {
        var seq = new KtorySequencer(KtoryParser.Parse("literal: text\n:"));
        var warnings = ObserveWarnings(seq);
        seq.Start(requestedLocale: "en");
        Assert.Equal("literal", seq.CurrentPayload!.Speaker);
        Assert.Null(seq.CurrentPayload.SpeakerActualLanguage);
        seq.Step();
        Assert.Equal(string.Empty, seq.CurrentPayload!.Content);
        Assert.Null(seq.CurrentPayload.SpeakerActualLanguage);
        Assert.Empty(warnings);

        // Direct AST construction retains its legacy fallback; .ktr declarations with
        // no localized name are rejected separately by ParserSpeakerValidationTests.
        var emptySpeaker = new SpeakerDefinition { Id = "id" };
        Assert.Equal("id", emptySpeaker.GetDisplayName("en", "zh", out var actual));
        Assert.Null(actual);
    }

    [Fact]
    public void UnlocalizedScript_WithoutDefaultLangOrLocale_OutputsNullActualLanguageAndNoWarnings()
    {
        const string source = "Alice: Hello world\n#choice\n  * [Continue]\n    : branch";
        var file = KtoryParser.Parse(source);
        Assert.Null(file.DefaultLang);
        Assert.False(file.HasExplicitDefaultLang);
        Assert.False(file.HasLocalization);

        var seq = new KtorySequencer(file);
        var warnings = ObserveWarnings(seq);
        seq.Start();

        Assert.Equal("Hello world", seq.CurrentPayload!.Content);
        Assert.Null(seq.CurrentPayload.ActualLanguage);
        Assert.Null(seq.CurrentPayload.SpeakerActualLanguage);
        Assert.Equal("zh", seq.CurrentPayload.RequestedLanguage);
        Assert.Empty(warnings);

        seq.Step();
        var opt = Assert.Single(seq.CurrentChoice!.Options);
        Assert.Equal("Continue", opt.Label);
        Assert.Null(opt.ActualLanguage);
        Assert.Empty(warnings);

        seq.SetLanguage("en");
        Assert.Equal("Continue", seq.CurrentChoice.Options[0].Label);
        Assert.Null(seq.CurrentChoice.Options[0].ActualLanguage);
        Assert.Empty(warnings);

        seq.SubmitChoice(opt.Id);
        Assert.Equal("branch", seq.CurrentPayload.Content);
        Assert.Null(seq.CurrentPayload.ActualLanguage);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ScriptWithLocale_WithoutDefaultLang_PreservesBackwardCompatibleZhDefault()
    {
        const string source = "Alice:\n  @en: Hello\n  @zh: 你好";
        var file = KtoryParser.Parse(source);
        Assert.Equal("zh", file.DefaultLang);
        Assert.False(file.HasExplicitDefaultLang);
        Assert.True(file.HasLocalization);

        var seq = new KtorySequencer(file);
        seq.Start(requestedLocale: "en");
        Assert.Equal("en", seq.CurrentPayload!.ActualLanguage);
    }

    [Fact]
    public void ScriptWithExplicitDefaultLang_OutputsDeclaredLanguage()
    {
        const string source = "@defaultLang: en\nAlice: Hello";
        var file = KtoryParser.Parse(source);
        Assert.Equal("en", file.DefaultLang);
        Assert.True(file.HasExplicitDefaultLang);

        var seq = new KtorySequencer(file);
        seq.Start();
        Assert.Equal("en", seq.CurrentPayload!.ActualLanguage);
    }

    private static List<ExecutionTrace> ObserveWarnings(KtorySequencer seq)
    {
        var result = new List<ExecutionTrace>();
        seq.OnTrace += trace => { if (trace.Kind == ExecutionTraceKind.Warning) result.Add(trace); };
        return result;
    }
}
