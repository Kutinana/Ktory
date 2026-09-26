using Ktory.Core.Common;
using Ktory.Core.Parser;

namespace Ktory.Core.Tests;

public class ParserSpeakerValidationTests
{
    [Theory]
    [InlineData("@speaker alice:")]
    [InlineData("@speaker alice:    ")]
    [InlineData("@speaker alice: |   | ")]
    [InlineData("@speaker:")]
    [InlineData("@speaker alice: // names have not been added")]
    public void DeclarationWithoutLocalizedNames_IsRejectedAtDeclarationLine(string declaration)
    {
        var error = Assert.Throws<KtoryException>(() => KtoryParser.Parse("// header\n\n" + declaration + "\n: body"));
        Assert.Equal(3, error.Line);
        Assert.Contains("speaker", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("@speaker alice: zh=''")]
    [InlineData("@speaker alice: zh=\"   \"")]
    public void ExplicitEmptyLocalizedName_RemainsInvalid(string declaration)
    {
        var error = Assert.Throws<KtoryException>(() => KtoryParser.Parse(declaration));
        Assert.Equal(1, error.Line);
    }

    [Fact]
    public void ValidDeclarations_PreserveIdAndLocalizedAliases()
    {
        var file = KtoryParser.Parse("@speaker alice: zh=\"爱丽丝\" | en=\"Alice\"\n@speaker: ja=\"ボブ\"\nalice: body");
        Assert.Equal(2, file.Speakers.Count);
        Assert.Same(file.SpeakersByAlias["alice"], file.SpeakersByAlias["爱丽丝"]);
        Assert.Same(file.SpeakersByAlias["alice"], file.SpeakersByAlias["Alice"]);
        Assert.Equal("Alice", file.ResolveSpeaker("爱丽丝", "en", "zh"));
        Assert.Equal("ボブ", file.ResolveSpeaker("ボブ", "ja", "zh"));
        Assert.Equal("literal", file.ResolveSpeaker("literal", "en", "zh"));
    }
}
