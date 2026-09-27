using Ktory.Core.Ast;
using Ktory.Core.Common;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;

namespace Ktory.Core.Tests;

public class SpeakerDefaultTests
{
    [Fact]
    public void DefaultsAreDeclarations_LocalGroupsReplaceWholeDefaultGroup_AndNextLineRestoresDefaults()
    {
        var file = KtoryParser.Parse("""
            @speaker alice: zh="爱丽丝" | en="Alice"
              .portrait(normal, side=left).voice(soft).sfx(a).sfx(b)
            alice: first
              .PORTRAIT(happy).sfx(c).sfx(d)
            爱丽丝: second
            : narration
            stranger: literal
            """);
        Assert.Equal(4, file.RootBlock.Steps.Count);
        var first = (TextStep)file.RootBlock.Steps[0];
        Assert.Equal(new[] { "voice", "PORTRAIT", "sfx", "sfx" }, first.Tags.Select(t => t.Name));
        Assert.Empty(first.Tags[1].NamedArgs); // Entire decorator is replaced, not individual parameters.
        var seq = new KtorySequencer(file);
        var events = new List<string>();
        seq.OnTagsDispatched += tags => events.AddRange(tags.Select(t => t.ToString()));
        seq.Start();
        Assert.Equal("happy", seq.CurrentPayload!.Tags.Single(t => t.Name == "PORTRAIT").GetPositional<string>(0));
        seq.Step();
        Assert.Equal("normal", seq.CurrentPayload!.Tags.First().GetPositional<string>(0));
        Assert.Equal(8, events.Count);
        seq.Step(); Assert.Empty(seq.CurrentPayload!.Tags);
        seq.Step(); Assert.Empty(seq.CurrentPayload!.Tags);
        Assert.Equal(8, events.Count);
    }

    [Fact]
    public void DefaultsAreFileScoped_ForForwardReferencesNamedSectionsAndInlineChoices()
    {
        var seq = new KtorySequencer(KtoryParser.Parse("""
            -> Scene
            === Scene ===
              #choice
                + [go]
                  Alice: first
                  => Sub
            === Sub ===
              alice: second
              -> return
            @speaker alice: zh="爱丽丝" | en="Alice"
              .style(common)
            """));
        seq.Start();
        seq.SubmitChoice(seq.CurrentChoice!.Options[0].Id);
        Assert.Equal("common", seq.CurrentPayload!.Tags.Single().GetPositional<string>(0));
        seq.Step();
        Assert.Equal("second", seq.CurrentPayload!.Content);
        Assert.Equal("common", seq.CurrentPayload.Tags.Single().GetPositional<string>(0));
    }

    [Fact]
    public void LanguageRefreshDoesNotDispatchDefaultsAgain()
    {
        var seq = new KtorySequencer(KtoryParser.Parse("""
            @speaker alice: zh="爱丽丝" | en="Alice"
              .sfx(bell).wait(2)
            alice:
              @zh: 你好
              @en: Hello
            """));
        int batches = 0;
        seq.OnTagsDispatched += _ => batches++;
        seq.Start(); seq.SetLanguage("en"); seq.SetLanguage("zh");
        Assert.Equal(1, batches);
        Assert.Equal(2, seq.CurrentPayload!.Tags.Count);
    }

    [Fact]
    public void TimingAndLoopModifiersBehaveLikeExplicitDialogueModifiers()
    {
        var seq = new KtorySequencer(KtoryParser.Parse("""
            @speaker alice: zh="爱丽丝"
              .loop(2).wait(2).next(3)
            alice: repeated
            : done
            """));
        seq.Start();
        var controller = new PresentationController(seq);
        controller.SetupForCurrentBeat();
        Assert.Equal(3, seq.CurrentPayload!.Tags.Count);
        Assert.Equal(2, controller.MinimumHoldDuration);
        Assert.Equal(3, controller.AutoAdvanceDuration);
        seq.Step(); Assert.Equal("repeated", seq.CurrentPayload!.Content);
        seq.Step(); Assert.Equal("done", seq.CurrentPayload!.Content);
    }

    [Fact]
    public void LocalOnlyModifierDoesNotCarryToTheNextLine()
    {
        var seq = new KtorySequencer(KtoryParser.Parse("""
            @speaker alice: zh="爱丽丝"
              .voice(soft)
            alice: first .emotion(happy)
            alice: second
            """));
        seq.Start(); Assert.Contains(seq.CurrentPayload!.Tags, t => t.Name == "emotion");
        seq.Step(); Assert.DoesNotContain(seq.CurrentPayload!.Tags, t => t.Name == "emotion");
        Assert.Single(seq.CurrentPayload.Tags);
    }

    [Theory]
    [InlineData("@speaker alice: zh=Alice\n.portrait(normal)", 2)]
    [InlineData("@speaker alice: zh=Alice\n  .portrait(normal) garbage", 2)]
    [InlineData("@speaker alice: zh=Alice\n  .portrait(\"unfinished)", 2)]
    [InlineData("@speaker alice: zh=Alice\n  .-portrait", 2)]
    [InlineData("@speaker alice:\n  .portrait(normal)", 1)]
    public void MalformedDefaultsFailWithSourceLocation(string source, int line)
    {
        var error = Assert.Throws<KtoryException>(() => KtoryParser.Parse(source));
        Assert.Equal(line, error.Line);
    }
}
