using Ktory.Core.Parser;
using Ktory.Core.Runtime;

namespace Ktory.Core.Tests;

public class ConditionPolicyTests
{
    private const string Menu = "#choice\n  + ? {has_key} [Unknown]\n    : unknown branch\n  + ? {false} [False]\n    : false branch\n  + [Open]\n    : open branch";

    [Fact]
    public void GameDefault_RejectsUnknownWithSourceWarningAndKeepsKnownFalseQuiet()
    {
        var seq = new KtorySequencer(KtoryParser.Parse(Menu));
        var warnings = new List<ExecutionTrace>();
        seq.OnTrace += entry => { if (entry.Kind == ExecutionTraceKind.Warning) warnings.Add(entry); };
        seq.Start();
        Assert.False(seq.CurrentChoice!.Options[0].CanSelect);
        Assert.False(seq.CurrentChoice.Options[1].CanSelect);
        Assert.True(seq.CurrentChoice.Options[2].CanSelect);
        var warning = Assert.Single(warnings);
        Assert.Equal(2, warning.LineNumber);
        Assert.Contains("has_key", warning.Message);
        seq.SetLanguage("en");
        seq.SetLanguage("ja");
        Assert.Single(warnings);
        seq.SubmitChoice(seq.CurrentChoice.Options[2].Id);
        Assert.Equal("open branch", seq.CurrentPayload!.Content);
    }

    [Fact]
    public void ExplicitPreview_IgnoresOnlyUnknownConditions()
    {
        var seq = new KtorySequencer(KtoryParser.Parse(Menu))
        {
            Evaluator = new DefaultExpressionEvaluator { IgnoreUnknownConditions = true }
        };
        seq.Start();
        Assert.True(seq.CurrentChoice!.Options[0].CanSelect);
        Assert.False(seq.CurrentChoice.Options[1].CanSelect);
        Assert.True(seq.CurrentChoice.Options[2].CanSelect);
    }

    [Fact]
    public void CustomEvaluatorFalse_IsAuthoritativeAndDoesNotMeanUnknown()
    {
        var seq = new KtorySequencer(KtoryParser.Parse(Menu)) { Evaluator = new KnownFalseEvaluator() };
        var warnings = new List<ExecutionTrace>();
        seq.OnTrace += entry => { if (entry.Kind == ExecutionTraceKind.Warning) warnings.Add(entry); };
        seq.Start();
        Assert.False(seq.CurrentChoice!.Options[0].CanSelect);
        Assert.Empty(warnings);
    }

    [Fact]
    public void UnknownGuardOnNode_IsSkippedWithWarning()
    {
        var file = KtoryParser.Parse(": guarded\n: after");
        file.RootBlock.Steps[0].GuardCondition = "not external";
        var seq = new KtorySequencer(file);
        var warnings = new List<ExecutionTrace>();
        seq.OnTrace += entry => { if (entry.Kind == ExecutionTraceKind.Warning) warnings.Add(entry); };
        seq.Start();
        Assert.Equal("after", seq.CurrentPayload!.Content);
        Assert.Equal(1, Assert.Single(warnings).LineNumber);
    }

    private sealed class KnownFalseEvaluator : IExpressionEvaluator
    {
        public bool EvaluateCondition(string expression) => false;
    }
}
