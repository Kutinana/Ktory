using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.JSInterop;
using Ktory.Core.Ast;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;

namespace Ktory.Wasm;

public class KtoryWasmBridge
{
    private KtorySequencer? _sequencer;
    private readonly List<TagData> _recentTags = new();
    private readonly List<LaneTagDispatch> _recentLaneTags = new();
    private readonly List<ExecutionTrace> _diagnostics = new();
    private readonly List<StoryOutput> _outputs = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [JSInvokable]
    public string Start(string script, string requestedLocale, string? entryBlock)
        => StartWithLanguages(script, requestedLocale, entryBlock, null);

    [JSInvokable]
    public string StartWithLanguages(string script, string requestedLocale, string? entryBlock, string? secondaryLocale)
    {
        // A failed reload must not leave the previous story available to later input.
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
        return SerializeState();
    }

    [JSInvokable]
    public string Step(long? expectedPresentationId = null, string? expectedSessionId = null)
    {
        if (_sequencer == null) return "{}";
        _sequencer.Step(new PresentationToken(expectedSessionId ?? "", expectedPresentationId ?? 0));
        return SerializeState();
    }

    [JSInvokable]
    public string StepWithClock(long expectedPresentationId, string expectedSessionId, double deltaTime, Dictionary<string, double?>? sourcePositions = null)
    {
        AdvanceForPresentationInput(expectedPresentationId, expectedSessionId, deltaTime, sourcePositions);
        return Step(expectedPresentationId, expectedSessionId);
    }

    [JSInvokable]
    public string Choice(string choiceId, long? expectedPresentationId = null, string? expectedSessionId = null)
    {
        if (_sequencer == null) return "{}";
        _sequencer.SubmitChoice(choiceId, new PresentationToken(expectedSessionId ?? "", expectedPresentationId ?? 0));
        return SerializeState();
    }

    [JSInvokable]
    public string ChoiceWithClock(string choiceId, long expectedPresentationId, string expectedSessionId, double deltaTime, Dictionary<string, double?>? sourcePositions = null)
    {
        AdvanceForPresentationInput(expectedPresentationId, expectedSessionId, deltaTime, sourcePositions);
        return Choice(choiceId, expectedPresentationId, expectedSessionId);
    }

    [JSInvokable]
    public string Break(long? expectedPresentationId = null, string? expectedSessionId = null)
    {
        if (_sequencer == null) return "{}";
        _sequencer.Break(new PresentationToken(expectedSessionId ?? "", expectedPresentationId ?? 0));
        return SerializeState();
    }

    [JSInvokable]
    public string BreakWithClock(long expectedPresentationId, string expectedSessionId, double deltaTime, Dictionary<string, double?>? sourcePositions = null)
    {
        AdvanceForPresentationInput(expectedPresentationId, expectedSessionId, deltaTime, sourcePositions);
        return Break(expectedPresentationId, expectedSessionId);
    }

    [JSInvokable]
    public string SetLanguage(string locale, long? expectedPresentationId = null, string? expectedSessionId = null)
    {
        if (_sequencer == null) return "{}";
        _sequencer.SetLanguage(locale, new PresentationToken(expectedSessionId ?? "", expectedPresentationId ?? 0));
        return SerializeState();
    }

    [JSInvokable]
    public string SetSecondaryLanguage(string? locale, long? expectedPresentationId = null, string? expectedSessionId = null)
    {
        if (_sequencer == null) return "{}";
        _sequencer.SetSecondaryLanguage(locale, new PresentationToken(expectedSessionId ?? "", expectedPresentationId ?? 0));
        return SerializeState();
    }

    [JSInvokable]
    public string GetSamples()
    {
        return JsonSerializer.Serialize(EmbeddedSamples.Catalog);
    }

    [JSInvokable]
    public string ReportClockPosition(string sourceName, double position, string expectedSessionId)
    {
        if (_sequencer == null) return "{}";
        _sequencer.PublishClockPosition(sourceName, position, expectedSessionId);
        return SerializeState();
    }

    [JSInvokable]
    public string ReportClockPositionWithClock(string sourceName, double position, string expectedSessionId, double deltaTime, Dictionary<string, double?>? sourcePositions = null)
    {
        var batch = sourcePositions == null ? new Dictionary<string, double?>() : new Dictionary<string, double?>(sourcePositions);
        batch[sourceName] = position;
        if (_sequencer == null) return "{}";
        _sequencer.UpdateClocks(deltaTime, batch, expectedSessionId);
        return SerializeState();
    }

    [JSInvokable]
    public string AdvanceClocks(double deltaTime, string expectedSessionId)
    {
        if (_sequencer == null) return "{}";
        _sequencer.UpdateClocks(deltaTime, expectedSessionId);
        return SerializeState();
    }

    [JSInvokable]
    public string AdvanceClocksWithSources(double deltaTime, string expectedSessionId, Dictionary<string, double?> sourcePositions)
    {
        if (_sequencer == null) return "{}";
        _sequencer.UpdateClocks(deltaTime, sourcePositions, expectedSessionId);
        return SerializeState();
    }

    private void AdvanceForPresentationInput(long presentationId, string sessionId, double deltaTime, Dictionary<string, double?>? sourcePositions)
    {
        if (_sequencer?.GetPresentation(new PresentationToken(sessionId, presentationId)) != null)
            _sequencer.UpdateClocks(deltaTime, sourcePositions ?? new Dictionary<string, double?>(), sessionId);
    }

    [JSInvokable]
    public string Lint(string script)
    {
        var diagnostics = KtoryLinter.Analyze(script);
        return JsonSerializer.Serialize(diagnostics, JsonOptions);
    }

    private StoryOutput[] DrainOutputs()
    {
        var batch = _outputs.ToArray();
        _outputs.Clear();
        return batch;
    }

    private string SerializeState()
    {
        if (_sequencer == null)
        {
            return JsonSerializer.Serialize(new { status = "NotStarted", presentationId = 0L }, JsonOptions);
        }

        var snapshot = new
        {
            status = _sequencer.Status.ToString(),
            sessionId = _sequencer.CurrentSessionId,
            presentationId = _sequencer.CurrentPresentationId,
            payload = _sequencer.CurrentPayload,
            choice = _sequencer.CurrentChoice,
            activePresentations = _sequencer.GetActivePresentations(),
            retainedPresentations = _sequencer.GetRetainedPresentations(),
            outputEvents = DrainOutputs(),
            pendingClockGates = _sequencer.GetPendingClockGates(),
            clocks = _sequencer.GetClocks(),
            requestedLanguage = _sequencer.RequestedLanguage,
            secondaryLanguage = _sequencer.SecondaryLanguage,
            defaultLanguage = _sequencer.DefaultLanguage,
            visitedItems = new List<string>(_sequencer.VisitedItemIds),
            callStackDepth = _sequencer.CallStack.Count,
            recentTags = new List<TagData>(_recentTags),
            recentLaneTags = new List<LaneTagDispatch>(_recentLaneTags),
            diagnostics = new List<ExecutionTrace>(_diagnostics),
            autoPolicy = _sequencer.ActiveAutoPolicy?.ToData()
        };
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }
}
