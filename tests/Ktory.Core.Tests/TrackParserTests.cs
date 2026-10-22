using Ktory.Core.Ast;
using Ktory.Core.Common;
using Ktory.Core.Parser;

namespace Ktory.Core.Tests;

public class TrackParserTests
{
    [Fact]
    public void AwaitSuffixKeepsAnonymousAndNamedTrackOperationsExplicit()
    {
        var file = KtoryParser.Parse("#.track(source=\"Init\").await.track(source=\"Init\", name=\"init\").await\n: ready\n=== Init ===\n  : initialized");
        var step = file.RootBlock.Steps[0];
        var operations = TrackOperation.FromTags(step.Tags, step.LineNumber);
        Assert.Equal(new[] { TrackOperationKind.Create, TrackOperationKind.AwaitCreated, TrackOperationKind.Create, TrackOperationKind.AwaitCreated }, operations.Select(x => x.Kind));
        Assert.Null(operations[0].Name);
        Assert.Equal("init", operations[2].Name);
    }

    [Theory]
    [InlineData("#.await")]
    [InlineData("#.track(source=\"Init\").effect.await")]
    [InlineData("#.track(source=\"Init\").await.await")]
    [InlineData("#.track(source=\"Init\").await(name=\"init\")")]
    [InlineData("#.track(source=\"Init\").await(1)")]
    [InlineData("#.track(source=\"Init\")\n#.await")]
    [InlineData(": text .track(source=\"Init\").await")]
    public void AwaitSuffixRequiresTheImmediatelyPrecedingTrackOnItsDirective(string source) =>
        Assert.Throws<KtoryException>(() => KtoryParser.Parse(source + "\n=== Init ===\n  : initialized"));

    [Theory]
    [InlineData("#.defaultClock(name=\"initialized\")")]
    [InlineData(": ready .at(value=0, clock=\"initialized\")")]
    public void QueuedTrackCannotInitializeClockBeforeMainYields(string reference)
    {
        var error = Assert.Throws<KtoryException>(() => KtoryParser.Parse("#.track(source=\"Init\", name=\"init\")\n" + reference + "\n=== Init ===\n  #.clock(name=\"initialized\")"));
        Assert.Equal(2, error.Line);
        Assert.Contains(".track(...).await", error.Message);
    }

    [Fact]
    public void TracksReferenceNeutralSectionsAndPreserveOperationOrder()
    {
        var file = KtoryParser.Parse("""
            #.track(source="Lyrics", name="lyric").track(source="Lyrics").stopTrack(name="lyric").awaitTrack(name="lyric")
            : after
            === Lyrics ===
              : lyric
            """);
        var step = Assert.IsType<DirectiveStep>(file.RootBlock.Steps[0]);
        var operations = TrackOperation.FromTags(step.Tags, step.LineNumber);
        Assert.Equal(new[] { TrackOperationKind.Create, TrackOperationKind.Create, TrackOperationKind.Stop, TrackOperationKind.Await }, operations.Select(x => x.Kind));
        Assert.Equal("Lyrics", operations[0].Source);
        Assert.Equal("lyric", operations[0].Name);
        Assert.Null(operations[1].Name);
        Assert.Same(step, file.TrackDeclarations["lyric"]);
        Assert.IsType<TextStep>(Assert.Single(file.Blocks["Lyrics"].Steps));
    }

    [Fact]
    public void AutoScopesStayLexicalToSections_AndSpeakerDefaultsReachChoiceBodies()
    {
        var file = KtoryParser.Parse("""
            @speaker alice: en="Alice"
              .portrait(normal).voice(soft)
            #AUTO.wait(seconds=5)
            #.track(source="Lyrics")
            : parent
            === Lyrics ===
              alice: lyric .portrait(happy)
              #choice
                + [go]
                  Alice: inner
              #AUTO.wait(seconds=1)
              : later
            """);
        Assert.Equal(5, file.RootBlock.Steps[2].LexicalAutoPolicy!.DefaultWaitSeconds);
        var body = file.Blocks["Lyrics"].Steps;
        Assert.Null(body[0].LexicalAutoPolicy);
        var first = Assert.IsType<TextStep>(body[0]);
        Assert.Equal("happy", first.GetTag("portrait")!.GetPositional<string>(0));
        Assert.NotNull(first.GetTag("voice"));
        var inner = Assert.IsType<TextStep>(Assert.IsType<ContainerStep>(body[1]).Items[0].InlineSteps[0]);
        Assert.Equal("normal", inner.GetTag("portrait")!.GetPositional<string>(0));
        Assert.NotNull(inner.GetTag("voice"));
        Assert.Equal(1, body[3].LexicalAutoPolicy!.DefaultWaitSeconds);
    }

    [Theory]
    [InlineData("#parallel")]
    [InlineData("#lane dialogue\n  : text")]
    [InlineData("#parallel.loop\n  #lane first\n    : text")]
    [InlineData("#parallel extra\n  #lane first\n    : text")]
    [InlineData("# parallel\n  #lane first\n    : text")]
    [InlineData("? {ready} #parallel\n  #lane first\n    : text")]
    [InlineData("#PARALLEL\n  #LANE first\n    : text")]
    public void LegacyParallelSyntaxAlwaysProducesMigrationError(string source)
    {
        var error = Assert.Throws<KtoryException>(() => KtoryParser.Parse(source));
        Assert.Equal(1, error.Line);
        Assert.Contains("Migrate", error.Message);
        Assert.Contains("awaitTrack", error.Message);
    }

    [Fact]
    public void LongerDirectiveNamesRemainOrdinaryHostInstructions()
    {
        var file = KtoryParser.Parse("#parallelized\n#lanes");
        Assert.Equal(new[] { "parallelized", "lanes" }, file.RootBlock.Steps.Cast<DirectiveStep>().Select(x => x.Name));
    }

    [Theory]
    [InlineData("#.track(\"Lyrics\")")]
    [InlineData("#.track()")]
    [InlineData("#.track(source=\"\")")]
    [InlineData("#.track(source=1)")]
    [InlineData("#.track(source=\"Lyrics\", name=\"\")")]
    [InlineData("#.track(source=\"Lyrics\", name=\"a\", NAME=\"b\")")]
    [InlineData("#.track(source=\"Lyrics\", channel=\"ui\")")]
    [InlineData("#.stopTrack()")]
    [InlineData("#.awaitTrack()")]
    [InlineData(": text .track(source=\"Lyrics\")")]
    [InlineData("#AUTO.track(source=\"Lyrics\")")]
    public void InvalidTrackArgumentsAndAnchorsFailAtLoad(string statement) =>
        Assert.Throws<KtoryException>(() => KtoryParser.Parse(statement + "\n=== Lyrics ===\n  : lyric"));

    [Theory]
    [InlineData("#.track(source=\"Lyrics\", name=\"a\")\n#.track(source=\"Lyrics\", name=\"a\")")]
    [InlineData("#.track(source=\"Missing\")")]
    [InlineData("#.track(source=\"https://example.com/story.ktr\")")]
    [InlineData("#.stopTrack(name=\"missing\")")]
    [InlineData("#.awaitTrack(name=\"missing\")")]
    [InlineData("#.awaitTrack(name=\"a\")\n#.track(source=\"Lyrics\", name=\"a\")")]
    public void DuplicateMissingAndProvablyLateTrackReferencesFailAtLoad(string statements) =>
        Assert.Throws<KtoryException>(() => KtoryParser.Parse(statements + "\n=== Lyrics ===\n  : lyric"));

    [Fact]
    public void StopBeforeCreationIsLegalAndDeclarationCanBeReentered()
    {
        var file = KtoryParser.Parse("#.stopTrack(name=\"a\")\n#.track(source=\"Lyrics\", name=\"a\").loop(count=3)\n=== Lyrics ===\n  : lyric");
        Assert.Single(file.TrackDeclarations);
        Assert.Equal(3, file.RootBlock.Steps[1].GetLoopLimit());
    }

    [Theory]
    [InlineData(".wait(1)")]
    [InlineData(".wait(seconds=-1)")]
    [InlineData(".next(seconds=true)")]
    [InlineData(".duration()")]
    [InlineData(".duration(seconds=\"1\")")]
    [InlineData(".skippable(enabled=1)")]
    [InlineData(".skippable(seconds=-1)")]
    [InlineData(".loop(count=1.5)")]
    [InlineData(".next(seconds=1, seconds=2)")]
    [InlineData(".wait(unknown=1)")]
    public void BuiltInPresentationArgumentsAreExplicitAndTyped(string tag)
    {
        var error = Assert.Throws<KtoryException>(() => KtoryParser.Parse(": before\n: text " + tag));
        Assert.Equal(2, error.Line);
    }

    [Fact]
    public void UnknownHostTagsKeepPositionalArguments_AndZeroLoopKeepsExistingClamp()
    {
        var file = KtoryParser.Parse(": text .host(\"x\", 2).wait.next.duration(seconds=0).skippable.loop(count=0)");
        Assert.Equal(2, file.RootBlock.Steps[0].GetTag("host")!.PositionalArgs.Count);
        Assert.Equal(1, file.RootBlock.Steps[0].GetLoopLimit());
    }

    [Fact]
    public void LinterCountsTrackReferencesAndStillChecksSectionBodies()
    {
        var diagnostics = KtoryLinter.Analyze("""
            @defaultLang: zh
            @speaker alice: zh="爱丽丝"
            #.track(source="Dialogue")
            : parent
            === Dialogue ===
              alice:
                @en: Hello
              => Helper
              stranger: text
              -> end
              : unreachable
            === Helper ===
              -> return
            """);
        Assert.DoesNotContain(diagnostics, item => item.Severity == KtoryDiagnosticSeverity.Error);
        Assert.DoesNotContain(diagnostics, item => item.Code is "KTR_W002" or "KTR_W004");
        Assert.Equal(6, Assert.Single(diagnostics, item => item.Code == "KTR_W003").Line);
        Assert.Equal(9, Assert.Single(diagnostics, item => item.Code == "KTR_W005").Line);
        Assert.Equal(11, Assert.Single(diagnostics, item => item.Code == "KTR_W001").Line);
        var legacy = Assert.Single(KtoryLinter.Analyze("#parallel\n  #lane\n    : text"));
        Assert.Equal("KTR_E011", legacy.Code);
        Assert.Equal(1, legacy.Line);
    }
}
