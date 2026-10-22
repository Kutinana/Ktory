using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ktory.Core.Ast;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<RunnerSessionService>();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
// Linked build assets live beside the executable when running from the source tree.
var highlightingRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot", "highlighting");
if (Directory.Exists(highlightingRoot))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(highlightingRoot),
        RequestPath = "/highlighting"
    });
}

// Session APIs
app.MapPost("/api/session/start", (StartSessionRequest req, RunnerSessionService sessionService) =>
{
    try
    {
        var state = sessionService.Start(req.Script, req.RequestedLocale ?? "en", req.EntryBlock, req.SecondaryLocale);
        return Results.Ok(state);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/session/step", async (HttpRequest request, RunnerSessionService sessionService) =>
{
    try
    {
        StepRequest? req = null;
        if (request.ContentLength > 0 && request.HasJsonContentType())
        {
            req = await request.ReadFromJsonAsync<StepRequest>();
        }
        var state = sessionService.Step(new PresentationToken(req?.SessionId ?? "", req?.PresentationId ?? 0), req?.DeltaTime, req?.SourcePositions);
        return Results.Ok(state);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/session/choice", (SubmitChoiceRequest req, RunnerSessionService sessionService) =>
{
    try
    {
        var state = sessionService.SubmitChoice(req.ChoiceId, new PresentationToken(req.SessionId ?? "", req.PresentationId ?? 0), req.DeltaTime, req.SourcePositions);
        return Results.Ok(state);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/session/break", (StepRequest req, RunnerSessionService sessionService) =>
{
    try
    {
        var state = sessionService.Break(new PresentationToken(req.SessionId ?? "", req.PresentationId ?? 0), req.DeltaTime, req.SourcePositions);
        return Results.Ok(state);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/session/language", (SetLanguageRequest req, RunnerSessionService sessionService) =>
{
    try
    {
        var state = sessionService.SetLanguage(req.Locale, new PresentationToken(req.SessionId ?? "", req.PresentationId ?? 0));
        return Results.Ok(state);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/session/state", (RunnerSessionService sessionService) =>
{
    return Results.Ok(sessionService.GetState());
});

app.MapPost("/api/session/secondary-language", (SetSecondaryLanguageRequest req, RunnerSessionService sessionService) =>
{
    try
    {
        var state = sessionService.SetSecondaryLanguage(req.Locale, new PresentationToken(req.SessionId ?? "", req.PresentationId ?? 0));
        return Results.Ok(state);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/samples", () =>
{
    return Results.Ok(SampleScripts.Catalog);
});

app.MapPost("/api/session/clock", (ReportClockRequest req, RunnerSessionService sessionService) =>
{
    try { return Results.Ok(sessionService.ReportClockPosition(req.SourceName, req.Position, req.SessionId, req.DeltaTime, req.SourcePositions)); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.MapPost("/api/session/clocks/advance", (AdvanceClocksRequest req, RunnerSessionService sessionService) =>
{
    try { return Results.Ok(sessionService.AdvanceClocks(req.DeltaTime, req.SessionId, req.SourcePositions)); }
    catch (Exception ex) { return Results.BadRequest(new { error = ex.Message }); }
});

app.Run();

// Data Models
public record StartSessionRequest(string Script, string? RequestedLocale, string? EntryBlock, string? SecondaryLocale = null);
public record SubmitChoiceRequest(string ChoiceId, long? PresentationId = null, string? SessionId = null, double? DeltaTime = null, Dictionary<string, double?>? SourcePositions = null);
public record StepRequest(long? PresentationId = null, string? SessionId = null, double? DeltaTime = null, Dictionary<string, double?>? SourcePositions = null);
public record SetLanguageRequest(string Locale, long? PresentationId = null, string? SessionId = null);
public record SetSecondaryLanguageRequest(string? Locale, long? PresentationId = null, string? SessionId = null);
public record ReportClockRequest(string SourceName, double Position, string SessionId, double? DeltaTime = null, Dictionary<string, double?>? SourcePositions = null);
public record AdvanceClocksRequest(double DeltaTime, string SessionId, Dictionary<string, double?>? SourcePositions = null);

public class RunnerSessionState
{
    public string Status { get; set; } = "Ready";
    public string SessionId { get; set; } = "";
    public long PresentationId { get; set; }
    public TextPayload? Payload { get; set; }
    public ChoicePayload? Choice { get; set; }
    public IReadOnlyList<LanePresentation> ActivePresentations { get; set; } = Array.Empty<LanePresentation>();
    public IReadOnlyList<LanePresentation> RetainedPresentations { get; set; } = Array.Empty<LanePresentation>();
    public IReadOnlyList<StoryOutput> OutputEvents { get; set; } = Array.Empty<StoryOutput>();
    public IReadOnlyList<LaneClockGate> PendingClockGates { get; set; } = Array.Empty<LaneClockGate>();
    public IReadOnlyList<ClockSnapshot> Clocks { get; set; } = Array.Empty<ClockSnapshot>();
    public string RequestedLanguage { get; set; } = "en";
    public string? SecondaryLanguage { get; set; }
    public string? DefaultLanguage { get; set; }
    public List<string> VisitedItems { get; set; } = new List<string>();
    public int CallStackDepth { get; set; }
    public List<TagData> RecentTags { get; set; } = new List<TagData>();
    public List<LaneTagDispatch> RecentLaneTags { get; set; } = new List<LaneTagDispatch>();
    public List<ExecutionTrace> Diagnostics { get; set; } = new List<ExecutionTrace>();
    public AutoPolicyData? AutoPolicy { get; set; }
}

public class RunnerSessionService
{
    private readonly object _lock = new object();
    private KtorySequencer? _sequencer;
    private readonly List<TagData> _recentTags = new List<TagData>();
    private readonly List<LaneTagDispatch> _recentLaneTags = new List<LaneTagDispatch>();
    private readonly List<ExecutionTrace> _diagnostics = new List<ExecutionTrace>();
    private readonly List<StoryOutput> _outputs = new List<StoryOutput>();

    public RunnerSessionState Start(string script, string requestedLocale, string? entryBlock, string? secondaryLocale = null)
    {
        lock (_lock)
        {
            // Reload always ends the old session, even when parsing or startup fails.
            _sequencer?.InvalidateSession();
            _sequencer = null;
            _recentTags.Clear();
            _recentLaneTags.Clear();
            _diagnostics.Clear();
            _outputs.Clear();
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file)
            {
                Evaluator = new DefaultExpressionEvaluator { IgnoreUnknownConditions = true }
            };
            sequencer.OnOutput += output => _outputs.Add(output);
            sequencer.OnTrace += trace =>
            {
                if (trace.Kind != ExecutionTraceKind.Warning && trace.Kind != ExecutionTraceKind.InputIgnored) return;
                _diagnostics.Add(trace);
                if (_diagnostics.Count > 50) _diagnostics.RemoveAt(0);
            };
            sequencer.OnTagsDispatched += tags =>
            {
                _recentTags.AddRange(tags);
                if (_recentTags.Count > 50) _recentTags.RemoveRange(0, _recentTags.Count - 50);
            };
            sequencer.OnLaneTagsDispatched += dispatch =>
            {
                _recentLaneTags.Add(dispatch);
                if (_recentLaneTags.Count > 50) _recentLaneTags.RemoveAt(0);
            };

            try
            {
                sequencer.Start(entryBlock, requestedLocale, secondaryLocale);
            }
            catch
            {
                sequencer.InvalidateSession();
                _recentTags.Clear();
                _recentLaneTags.Clear();
                _diagnostics.Clear();
            _outputs.Clear();
                throw;
            }

            _sequencer = sequencer;
            return GetStateInternal();
        }
    }

    public RunnerSessionState Step(long? expectedPresentationId = null)
    {
        lock (_lock)
        {
            if (_sequencer == null) throw new InvalidOperationException("No active session.");
            _sequencer.Step(expectedPresentationId);
            return GetStateInternal();
        }
    }

    public RunnerSessionState Step(PresentationToken token, double? deltaTime = null, Dictionary<string, double?>? sourcePositions = null)
    {
        lock (_lock)
        {
            if (_sequencer == null) return GetStateInternal();
            AdvanceForPresentationInput(token, deltaTime, sourcePositions);
            _sequencer.Step(token);
            return GetStateInternal();
        }
    }

    public RunnerSessionState SubmitChoice(string choiceId, long? expectedPresentationId = null)
    {
        lock (_lock)
        {
            if (_sequencer == null) throw new InvalidOperationException("No active session.");
            _sequencer.SubmitChoice(choiceId, expectedPresentationId);
            return GetStateInternal();
        }
    }

    public RunnerSessionState SubmitChoice(string choiceId, PresentationToken token, double? deltaTime = null, Dictionary<string, double?>? sourcePositions = null)
    {
        lock (_lock)
        {
            if (_sequencer == null) return GetStateInternal();
            AdvanceForPresentationInput(token, deltaTime, sourcePositions);
            _sequencer.SubmitChoice(choiceId, token);
            return GetStateInternal();
        }
    }

    public RunnerSessionState Break()
    {
        lock (_lock)
        {
            if (_sequencer == null) throw new InvalidOperationException("No active session.");
            _sequencer.Break();
            return GetStateInternal();
        }
    }

    public RunnerSessionState Break(PresentationToken token, double? deltaTime = null, Dictionary<string, double?>? sourcePositions = null)
    {
        lock (_lock)
        {
            if (_sequencer == null) return GetStateInternal();
            AdvanceForPresentationInput(token, deltaTime, sourcePositions);
            _sequencer.Break(token);
            return GetStateInternal();
        }
    }

    public RunnerSessionState SetLanguage(string locale)
    {
        lock (_lock)
        {
            if (_sequencer == null) throw new InvalidOperationException("No active session.");
            _sequencer.SetLanguage(locale);
            return GetStateInternal();
        }
    }

    public RunnerSessionState SetLanguage(string locale, PresentationToken token)
    {
        lock (_lock)
        {
            if (_sequencer == null) return GetStateInternal();
            _sequencer.SetLanguage(locale, token);
            return GetStateInternal();
        }
    }

    public RunnerSessionState GetState()
    {
        lock (_lock)
        {
            return GetStateInternal();
        }
    }

    public RunnerSessionState ReportClockPosition(string sourceName, double position, string expectedSessionId, double? deltaTime = null, Dictionary<string, double?>? sourcePositions = null)
    {
        lock (_lock)
        {
            if (_sequencer == null) return GetStateInternal();
            if (deltaTime.HasValue)
            {
                var batch = sourcePositions == null ? new Dictionary<string, double?>() : new Dictionary<string, double?>(sourcePositions);
                batch[sourceName] = position;
                _sequencer.UpdateClocks(deltaTime.Value, batch, expectedSessionId);
            }
            else _sequencer.PublishClockPosition(sourceName, position, expectedSessionId);
            return GetStateInternal();
        }
    }

    public RunnerSessionState SetSecondaryLanguage(string? locale, PresentationToken token)
    {
        lock (_lock)
        {
            if (_sequencer == null) return GetStateInternal();
            _sequencer.SetSecondaryLanguage(locale, token);
            return GetStateInternal();
        }
    }

    public RunnerSessionState AdvanceClocks(double deltaTime, string expectedSessionId, Dictionary<string, double?>? sourcePositions = null)
    {
        lock (_lock)
        {
            if (_sequencer == null) return GetStateInternal();
            _sequencer.UpdateClocks(deltaTime, sourcePositions ?? new Dictionary<string, double?>(), expectedSessionId);
            return GetStateInternal();
        }
    }

    private void AdvanceForPresentationInput(PresentationToken token, double? deltaTime, Dictionary<string, double?>? sourcePositions)
    {
        if (deltaTime.HasValue && _sequencer?.GetPresentation(token) != null)
            _sequencer.UpdateClocks(deltaTime.Value, sourcePositions ?? new Dictionary<string, double?>(), token.SessionId);
    }

    private StoryOutput[] DrainOutputs()
    {
        var batch = _outputs.ToArray();
        _outputs.Clear();
        return batch;
    }

    private RunnerSessionState GetStateInternal()
    {
        if (_sequencer == null)
        {
            return new RunnerSessionState { Status = "NotStarted", PresentationId = 0 };
        }

        return new RunnerSessionState
        {
            Status = _sequencer.Status.ToString(),
            SessionId = _sequencer.CurrentSessionId,
            PresentationId = _sequencer.CurrentPresentationId,
            Payload = _sequencer.CurrentPayload,
            Choice = _sequencer.CurrentChoice,
            ActivePresentations = _sequencer.GetActivePresentations(),
            RetainedPresentations = _sequencer.GetRetainedPresentations(),
            OutputEvents = DrainOutputs(),
            PendingClockGates = _sequencer.GetPendingClockGates(),
            Clocks = _sequencer.GetClocks(),
            RequestedLanguage = _sequencer.RequestedLanguage,
            SecondaryLanguage = _sequencer.SecondaryLanguage,
            DefaultLanguage = _sequencer.DefaultLanguage,
            VisitedItems = new List<string>(_sequencer.VisitedItemIds),
            CallStackDepth = _sequencer.CallStack.Count,
            RecentTags = new List<TagData>(_recentTags),
            RecentLaneTags = new List<LaneTagDispatch>(_recentLaneTags),
            Diagnostics = new List<ExecutionTrace>(_diagnostics),
            AutoPolicy = _sequencer.ActiveAutoPolicy?.ToData()
        };
    }
}

public static class SampleScripts
{
    public static IReadOnlyDictionary<string, string> Catalog => LoadCatalog();

    public static Dictionary<string, string> LoadCatalog()
    {
        var result = new Dictionary<string, string>();
        var samplesDir = FindSamplesDirectory();

        if (samplesDir != null && Directory.Exists(samplesDir))
        {
            var files = Directory.GetFiles(samplesDir, "*.ktr")
                .Concat(Directory.GetFiles(samplesDir, "*.ktory"))
                .OrderBy(f => Path.GetFileName(f));

            foreach (var file in files)
            {
                try
                {
                    var content = File.ReadAllText(file);
                    var title = ExtractTitle(content, Path.GetFileNameWithoutExtension(file));
                    result[title] = content;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[SampleScripts] Error reading {file}: {ex.Message}");
                }
            }
        }

        return result;
    }

    private static string ExtractTitle(string content, string fallback)
    {
        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.StartsWith("//") && line.Contains("title:", StringComparison.OrdinalIgnoreCase))
            {
                var idx = line.IndexOf("title:", StringComparison.OrdinalIgnoreCase);
                var title = line.Substring(idx + 6).Trim();
                if (!string.IsNullOrEmpty(title))
                {
                    return title;
                }
            }
            if (!string.IsNullOrEmpty(line) && !line.StartsWith("//"))
            {
                break;
            }
        }

        return fallback;
    }

    private static string? FindSamplesDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "samples"),
            Path.Combine(Directory.GetCurrentDirectory(), "sample"),
            Path.Combine(AppContext.BaseDirectory, "samples"),
            Path.Combine(AppContext.BaseDirectory, "sample"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "samples"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "samples"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "samples"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "samples"),
        };

        foreach (var path in candidates)
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }
}
