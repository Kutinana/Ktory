using Ktory.Core.Common;
using Ktory.Web;
using System.Text.Json;

namespace Ktory.Core.Tests;

public class PreviewBridgeTests
{
    [Fact]
    public void FailedReloadCannotContinueThePreviousStory()
    {
        var bridge = new KtoryWasmBridge();
        bridge.Start("主角: 旧第一句。\n主角: 旧第二句。", "zh", null);
        Assert.Throws<KtoryException>(() => bridge.Start("-> Missing", "zh", null));
        Assert.Equal("{}", bridge.Step());
        Assert.Equal("{}", bridge.SetLanguage("en"));
        using var result = JsonDocument.Parse(bridge.Start("主角: 新故事", "zh", null));
        Assert.Equal("新故事", result.RootElement.GetProperty("payload").GetProperty("content").GetString());
    }
}
