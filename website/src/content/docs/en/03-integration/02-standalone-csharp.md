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
| Advance a normal beat | `Step(currentPayload.PresentationId)` |
| Submit the active menu | `SubmitChoice(option.Id, currentChoice.PresentationId)` already executes to the branch's first output |
| Switch language | `SetLanguage("en")`, then reread the current payload or menu |
| Read output | `Status`, `CurrentPayload`, `CurrentChoice` |
| Receive presentation cues | `OnTagsDispatched` |

## Conditions and game state

`IExpressionEvaluator` connects host conditions, interpolation and operations. The current default evaluator serves basic preview and permits unknown conditions by default. Games should provide their own implementation; permissive preview results are not inventory or quest state. The core keeps session choice-consumption history, not game world state.

## Timing and input

A raw click, a timer completion and a core advance are distinct operations. The console example stops manually. `.next`, `.wait` and `.skippable` require presentation timing support. See the [presentation timing table](/en/03-integration/01-unity-upm/#shared-presentation-timing) for the supplied `PresentationController`; its API has no Unity dependency.

`PresentationId` associates input with current output; it is not a permanent content identifier or save format. Hosts must cancel old callbacks when restarting. Catch formatting, invalid-choice and control-flow errors and report source locations. Missing host capabilities and malformed script syntax are different cases.

The [real Web reader](https://ktory.vercel.app) uses the shared C# core. The homepage interaction is an illustrative simulation and cannot validate semantics or host presentation.
