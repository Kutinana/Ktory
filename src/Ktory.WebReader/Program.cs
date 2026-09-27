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
        var state = sessionService.Start(req.Script, req.RequestedLocale ?? "zh", req.EntryBlock);
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
        var state = sessionService.Step(new PresentationToken(req?.SessionId ?? "", req?.PresentationId ?? 0));
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
        var state = sessionService.SubmitChoice(req.ChoiceId, new PresentationToken(req.SessionId ?? "", req.PresentationId ?? 0));
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
        var state = sessionService.Break(new PresentationToken(req.SessionId ?? "", req.PresentationId ?? 0));
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

app.MapGet("/api/samples", () =>
{
    return Results.Ok(SampleScripts.Catalog);
});

app.Run();

// Data Models
public record StartSessionRequest(string Script, string? RequestedLocale, string? EntryBlock);
public record SubmitChoiceRequest(string ChoiceId, long? PresentationId = null, string? SessionId = null);
public record StepRequest(long? PresentationId = null, string? SessionId = null);
public record SetLanguageRequest(string Locale, long? PresentationId = null, string? SessionId = null);

public class RunnerSessionState
{
    public string Status { get; set; } = "Ready";
    public string SessionId { get; set; } = "";
    public long PresentationId { get; set; }
    public TextPayload? Payload { get; set; }
    public ChoicePayload? Choice { get; set; }
    public string RequestedLanguage { get; set; } = "zh";
    public string? DefaultLanguage { get; set; }
    public List<string> VisitedItems { get; set; } = new List<string>();
    public int CallStackDepth { get; set; }
    public List<TagData> RecentTags { get; set; } = new List<TagData>();
    public List<ExecutionTrace> Diagnostics { get; set; } = new List<ExecutionTrace>();
    public AutoPolicyData? AutoPolicy { get; set; }
    public string? ErrorMessage { get; set; }
}

public class RunnerSessionService
{
    private readonly object _lock = new object();
    private KtorySequencer? _sequencer;
    private readonly List<TagData> _recentTags = new List<TagData>();
    private readonly List<ExecutionTrace> _diagnostics = new List<ExecutionTrace>();

    public RunnerSessionState Start(string script, string requestedLocale, string? entryBlock)
    {
        lock (_lock)
        {
            // Reload always ends the old session, even when parsing or startup fails.
            _sequencer?.InvalidateSession();
            _sequencer = null;
            _recentTags.Clear();
            _diagnostics.Clear();
            var file = KtoryParser.Parse(script);
            var sequencer = new KtorySequencer(file)
            {
                Evaluator = new DefaultExpressionEvaluator { IgnoreUnknownConditions = true }
            };
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

            try
            {
                sequencer.Start(entryBlock, requestedLocale);
            }
            catch
            {
                sequencer.InvalidateSession();
                _recentTags.Clear();
                _diagnostics.Clear();
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

    public RunnerSessionState Step(PresentationToken token)
    {
        lock (_lock)
        {
            if (_sequencer == null) return GetStateInternal();
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

    public RunnerSessionState SubmitChoice(string choiceId, PresentationToken token)
    {
        lock (_lock)
        {
            if (_sequencer == null) return GetStateInternal();
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

    public RunnerSessionState Break(PresentationToken token)
    {
        lock (_lock)
        {
            if (_sequencer == null) return GetStateInternal();
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
            RequestedLanguage = _sequencer.RequestedLanguage,
            DefaultLanguage = _sequencer.DefaultLanguage,
            VisitedItems = new List<string>(_sequencer.VisitedItemIds),
            CallStackDepth = _sequencer.CallStack.Count,
            RecentTags = new List<TagData>(_recentTags),
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
