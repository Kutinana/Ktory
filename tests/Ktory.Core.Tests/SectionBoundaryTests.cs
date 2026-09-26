using Ktory.Core.Ast;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using Ktory.Web;
using Xunit;

namespace Ktory.Core.Tests;

public class SectionBoundaryTests
{
    [Fact]
    public void UnindentedTextAfterNamedHeader_BelongsToRoot()
    {
        var file = KtoryParser.Parse("=== Aside ===\n: root first\n: root second");

        Assert.Empty(file.Blocks["Aside"].Steps);
        Assert.Collection(file.RootBlock.Steps,
            step => Assert.Equal("root first", Assert.IsType<TextStep>(step).TextVariants["zh"]),
            step => Assert.Equal("root second", Assert.IsType<TextStep>(step).TextVariants["zh"]));

        var sequencer = new KtorySequencer(file);
        sequencer.Start();
        Assert.Equal("root first", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Equal("root second", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
    }

    [Fact]
    public void EmptyNamedSection_DoesNotCaptureLaterIndentedRootText()
    {
        var file = KtoryParser.Parse("=== Empty ===\n// comment\n: root\n  : still root\n=== Aside ===\n  : aside\n: after");

        Assert.Empty(file.Blocks["Empty"].Steps);
        Assert.Equal(3, file.RootBlock.Steps.Count);
        Assert.Equal(new[] { 3, 4, 7 }, file.RootBlock.Steps.ConvertAll(step => step.LineNumber));
        Assert.Equal(6, Assert.Single(file.Blocks["Aside"].Steps).LineNumber);
    }

    [Theory]
    [InlineData("", "  ")]
    [InlineData("  ", "    ")]
    [InlineData("", "\t")]
    public void NamedSectionEndsAtHeaderIndent_AndRootSkipsItsBody(string headerIndent, string bodyIndent)
    {
        var file = KtoryParser.Parse($": first\n{headerIndent}=== Aside ===\n{bodyIndent}: aside\n{headerIndent}: after");

        Assert.Equal(3, Assert.Single(file.Blocks["Aside"].Steps).LineNumber);
        Assert.Equal(new[] { 1, 4 }, file.RootBlock.Steps.ConvertAll(step => step.LineNumber));

        var sequencer = new KtorySequencer(file);
        sequencer.Start();
        Assert.Equal("first", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Equal("after", sequencer.CurrentPayload!.Content);
    }

    [Fact]
    public void LighthouseSample_DefaultEntryAndDrawerCall_PreserveStoryOrder()
    {
        var source = EmbeddedSamples.Catalog["灯塔办公室 (Lighthouse Office)"];
        var sequencer = new KtorySequencer(KtoryParser.Parse(source));
        // The sample Reader exposes unknown game conditions for branch preview.
        sequencer.Evaluator = new DefaultExpressionEvaluator { IgnoreUnknownConditions = true };
        sequencer.Start();
        Assert.Contains("窗外风雪交加", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        sequencer.Step();
        sequencer.Step();
        sequencer.Step();
        Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);

        sequencer.SubmitChoice("用钥匙打开抽屉");
        Assert.Contains("抽屉被拉开了", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Contains("机密文件", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Contains("一直在隐瞒的真相", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
        sequencer.SubmitChoice("离开办公室");
        Assert.Contains("我们先走吧", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
    }

    [Fact]
    public void InvestigationSample_DefaultEntryAndBreak_PreserveStoryOrder()
    {
        var source = EmbeddedSamples.Catalog["分支选择与循环调查 (Choice & Loop Hub)"];
        var sequencer = new KtorySequencer(KtoryParser.Parse(source));
        sequencer.Start();
        Assert.Equal(ExecutionStatus.AwaitingChoice, sequencer.Status);
        sequencer.SubmitChoice("放弃思考");
        Assert.Equal("我们离开这里吧。", sequencer.CurrentPayload!.Content);
        sequencer.Step();
        Assert.Equal(ExecutionStatus.Completed, sequencer.Status);
    }
}
