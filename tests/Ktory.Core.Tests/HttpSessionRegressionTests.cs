using Ktory.Core.Common;

namespace Ktory.Core.Tests;

public class HttpSessionRegressionTests
{
    [Fact]
    public void ParseFailureInvalidatesPreviousStoryAndAllowsFreshReload()
    {
        var service = new RunnerSessionService();
        var previous = service.Start(": old first\n.effect(old)\n: old second", "zh", null);
        Assert.Single(previous.RecentTags);

        Assert.Throws<KtoryException>(() => service.Start("-> Missing", "zh", null));

        AssertNoSession(service);
        Assert.Throws<InvalidOperationException>(() => service.Step(previous.PresentationId));
        Assert.Throws<InvalidOperationException>(() => service.SetLanguage("en"));

        var recovered = service.Start(": new first\n.effect(new)\n: new second", "en", null);
        Assert.Equal("new first", recovered.Payload?.Content);
        Assert.Equal("en", recovered.RequestedLanguage);
        Assert.Equal("new", Assert.Single(recovered.RecentTags).PositionalArgs[0]);
        Assert.Equal("new second", service.Step(recovered.PresentationId).Payload?.Content);
    }

    [Fact]
    public void EntryFailureDoesNotExposePartiallyStartedSessionOrPreviousChoice()
    {
        var service = new RunnerSessionService();
        var menu = service.Start("#choice.loop\n  + [old choice]\n    : old branch\n: after old loop", "zh", null);
        var oldChoice = Assert.Single(menu.Choice!.Options);

        Assert.Throws<KtoryException>(() => service.Start(": incomplete replacement", "en", "Missing"));

        AssertNoSession(service);
        Assert.Throws<InvalidOperationException>(() => service.SubmitChoice(oldChoice.Id, menu.PresentationId));
        Assert.Throws<InvalidOperationException>(() => service.Break());
        Assert.Throws<InvalidOperationException>(() => service.Step());

        var recovered = service.Start("=== New ===\n  : recovered entry", "zh", "New");
        Assert.Equal("recovered entry", recovered.Payload?.Content);
        Assert.Empty(recovered.RecentTags);
        Assert.Null(recovered.Choice);
    }

    private static void AssertNoSession(RunnerSessionService service)
    {
        var state = service.GetState();
        Assert.Equal("NotStarted", state.Status);
        Assert.Equal(0, state.PresentationId);
        Assert.Null(state.Payload);
        Assert.Null(state.Choice);
        Assert.Empty(state.RecentTags);
        Assert.Empty(state.VisitedItems);
        Assert.Equal(0, state.CallStackDepth);
        Assert.Null(state.AutoPolicy);
    }
}
