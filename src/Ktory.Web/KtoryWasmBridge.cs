using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Microsoft.JSInterop;
using Ktory.Core.Ast;
using Ktory.Core.Parser;
using Ktory.Core.Runtime;

namespace Ktory.Web;

public class KtoryWasmBridge
{
    private KtorySequencer? _sequencer;
    private readonly List<TagData> _recentTags = new();
    private readonly List<ExecutionTrace> _diagnostics = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [JSInvokable]
    public string Start(string script, string requestedLocale, string? entryBlock)
    {
        // A failed reload must not leave the previous story available to later input.
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
    public string Choice(string choiceId, long? expectedPresentationId = null, string? expectedSessionId = null)
    {
        if (_sequencer == null) return "{}";
        _sequencer.SubmitChoice(choiceId, new PresentationToken(expectedSessionId ?? "", expectedPresentationId ?? 0));
        return SerializeState();
    }

    [JSInvokable]
    public string Break(long? expectedPresentationId = null, string? expectedSessionId = null)
    {
        if (_sequencer == null) return "{}";
        _sequencer.Break(new PresentationToken(expectedSessionId ?? "", expectedPresentationId ?? 0));
        return SerializeState();
    }

    [JSInvokable]
    public string SetLanguage(string locale, long? expectedPresentationId = null, string? expectedSessionId = null)
    {
        if (_sequencer == null) return "{}";
        _sequencer.SetLanguage(locale, new PresentationToken(expectedSessionId ?? "", expectedPresentationId ?? 0));
        return SerializeState();
    }

    [JSInvokable]
    public string GetSamples()
    {
        return JsonSerializer.Serialize(EmbeddedSamples.Catalog);
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
            requestedLanguage = _sequencer.RequestedLanguage,
            defaultLanguage = _sequencer.DefaultLanguage,
            visitedItems = new List<string>(_sequencer.VisitedItemIds),
            callStackDepth = _sequencer.CallStack.Count,
            recentTags = new List<TagData>(_recentTags),
            diagnostics = new List<ExecutionTrace>(_diagnostics),
            autoPolicy = _sequencer.ActiveAutoPolicy?.ToData()
        };
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }
}
