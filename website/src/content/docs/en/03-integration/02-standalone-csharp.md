---
title: C# Standalone Application Integration
description: Shared core API, evaluation extensions and host timing
sidebar:
  order: 2
---

> This page follows the [Ktory design principles](/en/01-overview/03-design-principles/), which distinguish current contracts from longer-term goals.

# Standalone C# Integration

`Ktory.Core` targets `netstandard2.1` without external NuGet dependencies. Hosts need a compatible runtime; the repository's console example and Web tools use .NET 9. Other engine integrations may be explored, but portability does not establish that their adapters have shipped.

See the [Quickstart](/en/01-overview/02-quickstart/) for a complete manual console program. The core API is:

| Operation | API and result |
| --- | --- |
| Parse | `KtoryParser.Parse(source)` returns `KtoryFile` |
| Create | `new KtorySequencer(file)` |
| Start | `Start(requestedLocale: "en")` executes to the first output |
| Advance a normal beat | `Step(token)` |
| Submit the active menu | `SubmitChoice(option.Id, token)` already executes to the branch's first output |
| Switch language | `SetLanguage("en", token)`, then reread the current payload or menu |
| Read output | `Status`, `CurrentPayload`, `CurrentChoice` |
| Receive presentation cues | `OnTagsDispatched` |

## Conditions and game state

`IExpressionEvaluator` connects host condition evaluation. When a game sequencer uses the built-in `DefaultExpressionEvaluator`, an unknown condition produces a `Warning` and evaluates to `false`; known conditions retain their actual result. Standalone preview explicitly sets `IgnoreUnknownConditions = true`. Games must supply the real conditions they need; permissive preview results are not inventory or quest state. The core keeps session choice-consumption history, not game world state.

## Timing and input

A raw click, a timer completion and a core advance are distinct operations. The console example stops manually. `.next`, `.wait` and `.skippable` require presentation timing support. See the [presentation timing table](/en/03-integration/01-unity-upm/#shared-presentation-timing) for the supplied `PresentationController`; its API has no Unity dependency.

Capture `var token = player.CurrentPresentationToken` when displaying content or scheduling work, and retain that full token in asynchronous callbacks. Every Start creates a new session. Before closing or replacing a player, call `InvalidateSession()` and clean up host callbacks. Stale or repeat submissions produce only background `InputIgnored` diagnostics; an invalid option for the current menu remains an error. Legacy calls without a token or with only `PresentationId` are for synchronous compatibility and do not guarantee cross-session isolation. Tokens are not permanent content IDs or save data.

The [real Web reader](https://ktory.vercel.app) uses the shared C# core. The homepage interaction is an illustrative simulation and cannot validate semantics or host presentation.


`SetLanguage(locale, token)` changes a session preference: it accepts an earlier beat or natural completion within the same valid session, while rejecting requests from an old session.
