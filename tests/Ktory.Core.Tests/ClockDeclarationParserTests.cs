using Ktory.Core.Ast;
using Ktory.Core.Common;
using Ktory.Core.Parser;

namespace Ktory.Core.Tests;

public class ClockDeclarationParserTests
{
    [Fact]
    public void OrdinaryBeat_PreservesDeclarationsSelectionAndResetInSourceOrder()
    {
        var file = KtoryParser.Parse("#music.bgm(\"song\").clock(name=\"scene\").clock(name=\"music\", source=\"audio.position\").defaultClock(name=\"scene\").resetClock()");
        var step = Assert.IsType<DirectiveStep>(Assert.Single(file.RootBlock.Steps));
        var operations = ClockOperation.FromTags(step.Tags, step.LineNumber);
        Assert.Collection(operations,
            operation => Check(operation, ClockOperationKind.Declare, "scene", null),
            operation => Check(operation, ClockOperationKind.Declare, "music", "audio.position"),
            operation => Check(operation, ClockOperationKind.SelectDefault, "scene", null),
            operation => Check(operation, ClockOperationKind.Reset, null, null));
        Assert.Equal(new[] { "bgm", "clock", "clock", "defaultClock", "resetClock" }, step.Tags.Select(tag => tag.Name));
    }

    [Theory]
    [InlineData("#.clock()")]
    [InlineData("# .clock(source=\"host.key\")")]
    [InlineData("#.clock().resetClock()")]
    [InlineData(": text .clock(name=\"scene\").resetClock(name=\"scene\")")]
    [InlineData(": text .CLOCK(NAME=\"Scene\", SOURCE=\"Host.Key\").RESETCLOCK(NAME=\"Scene\")")]
    public void TextAndOrdinaryDirectiveBeats_AcceptClockOperations(string source)
    {
        var file = KtoryParser.Parse(source);
        Assert.NotEmpty(ClockOperation.FromTags(Assert.Single(file.RootBlock.Steps).Tags, 1));
    }

    [Fact]
    public void NamesAreNotNormalized_EmptySourceMatchesOmission_AndHostTagsAreIgnored()
    {
        var tags = TagParser.ParseTags(".clockwise(1).clock(name=\"Scene\", source=\"Host.Key\").resetClock(name=\"scene\").clock(source=\"\").next");
        var operations = ClockOperation.FromTags(tags, 1);
        Check(operations[0], ClockOperationKind.Declare, "Scene", "Host.Key");
        Check(operations[1], ClockOperationKind.Reset, "scene", null);
        Check(operations[2], ClockOperationKind.Declare, null, null);
        Assert.Empty(ClockOperation.FromTags(TagParser.ParseTags(".bgm(\"song\").next"), 1));
    }

    [Theory]
    [InlineData(".clock(\"scene\")")]
    [InlineData(".clock(name=\"\")")]
    [InlineData(".clock(name=\"   \")")]
    [InlineData(".clock(name=1)")]
    [InlineData(".clock(source=2)")]
    [InlineData(".clock(source=false)")]
    [InlineData(".clock(clock=\"b\")")]
    [InlineData(".clock(name=\"a\", source=\"b\", extra=\"c\")")]
    [InlineData(".clock(name=\"a\", NAME=\"b\")")]
    [InlineData(".resetClock(\"a\")")]
    [InlineData(".resetClock(name=\"\")")]
    [InlineData(".resetClock(name=2)")]
    [InlineData(".resetClock(name=false)")]
    [InlineData(".resetClock(name=\"a\", source=\"b\")")]
    [InlineData(".defaultClock()")]
    [InlineData(".defaultClock(name=\"a\", unknown=1)")]
    public void InvalidArguments_FailAtOwningBeat(string decorator)
    {
        var error = Assert.Throws<KtoryException>(() => KtoryParser.Parse(": before\n: current " + decorator));
        Assert.Equal(2, error.Line);
    }

    [Theory]
    [InlineData("#AUTO.clock(name=\"a\")\n: text")]
    [InlineData("#AUTO_END.resetClock(name=\"a\")\n: text")]
    [InlineData("#choice.clock(name=\"a\")\n  + [pick]\n    : branch")]
    [InlineData("#choice\n  + [pick] .clock(name=\"a\")\n    : branch")]
    [InlineData("@speaker alice: en=\"Alice\"\n  .clock()\nalice: text")]
    public void UnsupportedAnchors_RejectClockOperations(string source) =>
        Assert.Throws<KtoryException>(() => KtoryParser.Parse(source));

    [Fact]
    public void NestedOptionBodies_ValidateClockOperations()
    {
        var valid = KtoryParser.Parse("#choice\n  + [pick]\n    : body .clock(name=\"a\").resetClock(name=\"a\")");
        Assert.Single(valid.RootBlock.Steps);
        var error = Assert.Throws<KtoryException>(() => KtoryParser.Parse("#choice\n  + [pick]\n    : body .clock(name=\"a\", source=3)"));
        Assert.Equal(3, error.Line);
    }

    [Theory]
    [InlineData("#.clock(name=\"a\")\n#.clock(name=\"a\")", 2)]
    [InlineData("#.clock(name=\"a\").clock(name=\"a\")", 1)]
    [InlineData("#.clock(name=\"a\")\n=== Other ===\n  #.clock(name=\"a\")", 3)]
    [InlineData("#.resetClock(name=\"missing\")", 1)]
    [InlineData("#.defaultClock(name=\"missing\")", 1)]
    [InlineData(": missing .at(value=1, clock=\"missing\")", 1)]
    [InlineData(": no default .at(value=1)", 1)]
    [InlineData(": before .at(value=1, clock=\"later\")\n#.clock(name=\"later\")", 1)]
    [InlineData(": before .clock(name=\"same\").at(value=1, clock=\"same\")", 1)]
    public void DuplicateMissingAndProvablyLateReferencesFailBeforePlayback(string source, int line)
    {
        var error = Assert.Throws<KtoryException>(() => KtoryParser.Parse(source));
        Assert.Equal(line, error.Line);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectAstControlFlow_RejectsClockOperationsIncludingOptionShorthand(bool optionShorthand)
    {
        var file = new KtoryFile();
        var flow = new ControlFlowStep { FlowType = ControlFlowType.End, LineNumber = 7 };
        flow.Tags.Add(new TagData("clock") { NamedArgs = new() { ["name"] = "a" } });
        if (optionShorthand)
        {
            var container = new ContainerStep();
            container.Items.Add(new ContainerItem { TargetJump = flow });
            file.RootBlock.Steps.Add(container);
        }
        else file.RootBlock.Steps.Add(flow);
        var error = Assert.Throws<KtoryException>(() => KtoryValidator.Validate(file));
        Assert.Equal(7, error.Line);
        Assert.Contains("ordinary directive", error.Message);
    }

    private static void Check(ClockOperation operation, ClockOperationKind kind, string? name, string? source)
    {
        Assert.Equal(kind, operation.Kind);
        Assert.Equal(name, operation.Name);
        Assert.Equal(source, operation.SourceName);
    }
}
