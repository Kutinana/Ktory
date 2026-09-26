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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [JSInvokable]
    public string Start(string script, string requestedLocale, string? entryBlock)
    {
        // A failed reload must not leave the previous story available to later input.
        _sequencer = null;
        _recentTags.Clear();
        var file = KtoryParser.Parse(script);
        var sequencer = new KtorySequencer(file);
        sequencer.OnTagsDispatched += tags =>
        {
            _recentTags.AddRange(tags);
            if (_recentTags.Count > 50) _recentTags.RemoveRange(0, _recentTags.Count - 50);
        };

        sequencer.Start(entryBlock, requestedLocale);
        _sequencer = sequencer;
        return SerializeState();
    }

    [JSInvokable]
    public string Step(long? expectedPresentationId = null)
    {
        if (_sequencer == null) return "{}";
        _sequencer.Step(expectedPresentationId);
        return SerializeState();
    }

    [JSInvokable]
    public string Choice(string choiceId, long? expectedPresentationId = null)
    {
        if (_sequencer == null) return "{}";
        _sequencer.SubmitChoice(choiceId, expectedPresentationId);
        return SerializeState();
    }

    [JSInvokable]
    public string Break()
    {
        if (_sequencer == null) return "{}";
        _sequencer.Break();
        return SerializeState();
    }

    [JSInvokable]
    public string SetLanguage(string locale)
    {
        if (_sequencer == null) return "{}";
        _sequencer.SetLanguage(locale);
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
            presentationId = _sequencer.CurrentPresentationId,
            payload = _sequencer.CurrentPayload,
            choice = _sequencer.CurrentChoice,
            requestedLanguage = _sequencer.RequestedLanguage,
            defaultLanguage = _sequencer.DefaultLanguage,
            visitedItems = new List<string>(_sequencer.VisitedItemIds),
            callStackDepth = _sequencer.CallStack.Count,
            recentTags = new List<TagData>(_recentTags),
            autoPolicy = _sequencer.ActiveAutoPolicy?.ToData()
        };
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }
}
