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
        _recentTags.Clear();
        var file = KtoryParser.Parse(script);
        _sequencer = new KtorySequencer(file);
        _sequencer.OnTagsDispatched += tags =>
        {
            _recentTags.AddRange(tags);
            if (_recentTags.Count > 50) _recentTags.RemoveRange(0, _recentTags.Count - 50);
        };

        _sequencer.Start(entryBlock, requestedLocale);
        return SerializeState();
    }

    [JSInvokable]
    public string Step()
    {
        if (_sequencer == null) return "{}";
        _sequencer.Step();
        return SerializeState();
    }

    [JSInvokable]
    public string Choice(string choiceId)
    {
        if (_sequencer == null) return "{}";
        _sequencer.SubmitChoice(choiceId);
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
            return JsonSerializer.Serialize(new { status = "NotStarted" }, JsonOptions);
        }

        var snapshot = new
        {
            status = _sequencer.Status.ToString(),
            payload = _sequencer.CurrentPayload,
            choice = _sequencer.CurrentChoice,
            requestedLanguage = _sequencer.RequestedLanguage,
            defaultLanguage = _sequencer.DefaultLanguage,
            visitedItems = new List<string>(_sequencer.VisitedItemIds),
            callStackDepth = _sequencer.CallStack.Count,
            recentTags = new List<TagData>(_recentTags)
        };
        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }
}
