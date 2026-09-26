using Ktory.Core.Ast;
using Ktory.Core.Common;
using Ktory.Core.Parser;
using Xunit;

namespace Ktory.Core.Tests;

public class DecoratorBoundaryTests
{
    [Theory]
    [InlineData(".effect\n: first", 1)]
    [InlineData("// comment\n\n.effect\n: first", 3)]
    [InlineData(": root\n=== Aside ===\n  .effect\n  : aside", 3)]
    [InlineData("=== Aside ===\n  .effect\n: root", 2)]
    [InlineData("=== Aside ===\n  : aside\n.effect\n: root", 3)]
    [InlineData("=== Aside ===\n  #do\n.effect\n: root", 3)]
    [InlineData("=== Aside ===\n  -> end\n.effect\n: root", 3)]
    public void DecoratorWithoutAnchorInItsBlock_StopsParsingAtSourceLine(string source, int line)
    {
        var error = Assert.Throws<KtoryException>(() => KtoryParser.Parse(source));

        Assert.Equal(line, error.Line);
        Assert.Contains("Decorator has no preceding anchor", error.Message);
    }

    [Fact]
    public void SameLevelDecoratorInsideBranch_AttachesToPreviousBranchAnchor()
    {
        var file = KtoryParser.Parse("#choice\n  + [A]\n    .option\n    : first\n    .effect\n    : second");
        var item = Assert.Single(Assert.IsType<ContainerStep>(Assert.Single(file.RootBlock.Steps)).Items);

        Assert.Equal("option", Assert.Single(item.Tags).Name);
        Assert.Equal("effect", Assert.Single(item.InlineSteps[0].Tags).Name);
        Assert.Empty(item.InlineSteps[1].Tags);
    }

    [Fact]
    public void SameLevelDecorators_StayWithTheirCurrentBlockAnchor()
    {
        var file = KtoryParser.Parse(": root\n.effect\n=== Aside ===\n  : aside\n  .asideEffect\n: after");

        Assert.Equal("effect", Assert.Single(file.RootBlock.Steps[0].Tags).Name);
        Assert.Equal("asideEffect", Assert.Single(Assert.Single(file.Blocks["Aside"].Steps).Tags).Name);
        Assert.Empty(file.RootBlock.Steps[1].Tags);
    }
}
