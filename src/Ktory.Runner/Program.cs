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

app.MapPost("/api/session/step", (RunnerSessionService sessionService) =>
{
    try
    {
        var state = sessionService.Step();
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
        var state = sessionService.SubmitChoice(req.ChoiceId);
        return Results.Ok(state);
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/session/break", (RunnerSessionService sessionService) =>
{
    try
    {
        var state = sessionService.Break();
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
        var state = sessionService.SetLanguage(req.Locale);
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
public record SubmitChoiceRequest(string ChoiceId);
public record SetLanguageRequest(string Locale);

public class RunnerSessionState
{
    public string Status { get; set; } = "Ready";
    public TextPayload? Payload { get; set; }
    public ChoicePayload? Choice { get; set; }
    public string RequestedLanguage { get; set; } = "zh";
    public string DefaultLanguage { get; set; } = "zh";
    public List<string> VisitedItems { get; set; } = new List<string>();
    public int CallStackDepth { get; set; }
    public List<TagData> RecentTags { get; set; } = new List<TagData>();
    public string? ErrorMessage { get; set; }
}

public class RunnerSessionService
{
    private KtorySequencer? _sequencer;
    private readonly List<TagData> _recentTags = new List<TagData>();

    public RunnerSessionState Start(string script, string requestedLocale, string? entryBlock)
    {
        _recentTags.Clear();
        var file = KtoryParser.Parse(script);
        _sequencer = new KtorySequencer(file);
        _sequencer.OnTagsDispatched += tags =>
        {
            _recentTags.AddRange(tags);
            if (_recentTags.Count > 50) _recentTags.RemoveRange(0, _recentTags.Count - 50);
        };

        _sequencer.Start(entryBlock, requestedLocale);
        return GetState();
    }

    public RunnerSessionState Step()
    {
        if (_sequencer == null) throw new InvalidOperationException("No active session.");
        _sequencer.Step();
        return GetState();
    }

    public RunnerSessionState SubmitChoice(string choiceId)
    {
        if (_sequencer == null) throw new InvalidOperationException("No active session.");
        _sequencer.SubmitChoice(choiceId);
        return GetState();
    }

    public RunnerSessionState Break()
    {
        if (_sequencer == null) throw new InvalidOperationException("No active session.");
        _sequencer.Break();
        return GetState();
    }

    public RunnerSessionState SetLanguage(string locale)
    {
        if (_sequencer == null) throw new InvalidOperationException("No active session.");
        _sequencer.SetLanguage(locale);
        return GetState();
    }

    public RunnerSessionState GetState()
    {
        if (_sequencer == null)
        {
            return new RunnerSessionState { Status = "NotStarted" };
        }

        return new RunnerSessionState
        {
            Status = _sequencer.Status.ToString(),
            Payload = _sequencer.CurrentPayload,
            Choice = _sequencer.CurrentChoice,
            RequestedLanguage = _sequencer.RequestedLanguage,
            DefaultLanguage = _sequencer.DefaultLanguage,
            VisitedItems = new List<string>(_sequencer.VisitedItemIds),
            CallStackDepth = _sequencer.CallStack.Count,
            RecentTags = new List<TagData>(_recentTags)
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
