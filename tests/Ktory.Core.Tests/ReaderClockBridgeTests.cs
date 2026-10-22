using System.Text.Json;
using Ktory.Core.Runtime;
using Ktory.Wasm;

namespace Ktory.Core.Tests;

public class ReaderClockBridgeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void MusicSourceUsesPlayerPositionAndUnavailableValueCannotReleaseNextGate(bool wasm, bool anonymous)
    {
        var http = new RunnerSessionService();
        var browser = new KtoryWasmBridge();
        string declaration = anonymous
            ? "#.clock(source=\"music.position\")"
            : "#.clock(name=\"song\", source=\"music.position\")";
        string clock = anonymous ? "" : ", clock=\"song\"";
        string script = $"{declaration}\n: first .at(value=6{clock})\n: second .at(value=8{clock})";
        JsonElement Serialize(RunnerSessionState state) => JsonSerializer.SerializeToElement(state,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
        JsonElement started = wasm ? Parse(browser.Start(script, "en", null)) : Serialize(http.Start(script, "en", null));
        string session = started.GetProperty("sessionId").GetString()!;
        JsonElement Advance(double? position) => wasm
            ? Parse(browser.AdvanceClocksWithSources(0, session, new() { ["music.position"] = position }))
            : Serialize(http.AdvanceClocks(0, session, new() { ["music.position"] = position }));

        Assert.Equal("WaitingForClock", started.GetProperty("status").GetString());
        Assert.Equal("music.position", started.GetProperty("pendingClockGates")[0].GetProperty("sourceName").GetString());
        Assert.Equal("WaitingForClock", Advance(5.5).GetProperty("status").GetString());
        var first = Advance(20);
        Assert.Equal("SuspendedAtBeat", first.GetProperty("status").GetString());
        long token = first.GetProperty("presentationId").GetInt64();
        // The old song was beyond both cues; changing to an unavailable player must
        // clear that sample before the player's advance enters the next sentence.
        JsonElement waiting = wasm
            ? Parse(browser.StepWithClock(token, session, 0, new() { ["music.position"] = null }))
            : Serialize(http.Step(new PresentationToken(session, token), 0, new() { ["music.position"] = null }));
        Assert.Equal("WaitingForClock", waiting.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, waiting.GetProperty("clocks")[0].GetProperty("position").ValueKind);
        Assert.Equal("WaitingForClock", Advance(2).GetProperty("status").GetString());
        Assert.Equal("SuspendedAtBeat", Advance(8).GetProperty("status").GetString());
    }

    [Fact]
    public void HttpClockReportNamesTheExternalSource()
    {
        var request = JsonSerializer.Deserialize<ReportClockRequest>(
            """{"sourceName":"music.position","position":6,"sessionId":"session"}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Equal("music.position", request!.SourceName);
    }
}
