---
title: Unity UPM Integration
description: Generated Unity package, real core API and existing host integration boundaries
sidebar:
  order: 1
---

> This page follows the [Ktory design principles](/en/01-overview/03-design-principles/), which distinguish current contracts from longer-term goals.

# Unity UPM Integration

The shared core and Unity package have separate source directories. `scripts/publish-upm.ps1` combines `src/Ktory.Core`, the Unity importers and assembly definitions into `com.ktory.unity`, published on the `upm` branch.

## Install the generated package

Merge this dependency into `Packages/manifest.json`, retaining existing dependencies:

```json
{
  "dependencies": {
    "com.ktory.unity": "https://github.com/Kutinana/Ktory.git#upm"
  }
}
```

`#upm` is a convenient moving branch. For reproducible cross-device work, replace the fragment with a **generated UPM package commit or a tag for that package**, such as `#<upm-package-commit>`. Do not substitute a source `main` commit for a generated package. `?path=/src/Ktory.Core` is not the current package entry point.

## Core call order

This minimal logging probe demonstrates the real API and submission order inside a host project. It does not implement a dialogue box or portraits and does not replace an existing Unity integration. Assign the `TextAsset` imported from a `.ktr` file in the Inspector.

```csharp
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using UnityEngine;

public sealed class KtoryCoreProbe : MonoBehaviour
{
    [SerializeField] private TextAsset script;
    private KtorySequencer player;

    private void Start()
    {
        player = new KtorySequencer(KtoryParser.Parse(script.text));
        player.OnTagsDispatched += tags =>
        {
            foreach (var tag in tags) Debug.Log($"[Ktory tag] {tag.Name}");
        };
        player.Start(requestedLocale: "zh");
        LogCurrent();
    }

    public void AdvanceDisplayedBeat(long presentationId)
    {
        player.Step(presentationId);
        LogCurrent();
    }

    public void SubmitOption(string optionId, long presentationId)
    {
        player.SubmitChoice(optionId, presentationId);
        LogCurrent();
    }

    private void LogCurrent()
    {
        if (player.Status == ExecutionStatus.AwaitingChoice)
        {
            foreach (var option in player.CurrentChoice.Options)
                Debug.Log($"{option.Id}: {option.Label} / {option.CanSelect}");
        }
        else if (player.CurrentPayload != null)
            Debug.Log(player.CurrentPayload.Content);
    }
}
```

Pass the displayed beat's `PresentationId` when advancing. Option buttons retain the menu's `PresentationId` and the option `Id`. `SubmitChoice()` already produces the selected branch's first beat; do not add `Step()` for that same selection. Existing host input code must handle fast-forwarding and external advancement conditions before a normal advance is sent.

## Shared presentation timing

A host may use `PresentationController` as follows, or implement the same contracts itself:

| Host action | Controller API |
| --- | --- |
| Bind an existing player | `new PresentationController(player)` |
| Establish state after `Start()` or a valid choice | `SetupForCurrentBeat()`, then read the current output |
| Tick the host clock | `Update(Time.unscaledDeltaTime)` |
| Handle a normal raw click | `HandleUserClick()` |
| Report natural typewriter completion | `NotifyPrintingFinished()` |
| Redraw after an automatic or click advance | Subscribe to `OnBeatChanged` |
| Reveal the current text immediately | Subscribe to `OnFastForwardRequested`; reveal text without notifying completion again |
| Change the active language | `player.SetLanguage(...)`, then `RefreshLanguage()` and redraw |

With the default `AutoStepSequencer == true`, the controller advances the player itself. Do not call `Step()` again from `OnAdvanceRequested`. Resource and portrait bindings, world state and external gates remain host responsibilities. Cancel old inputs and callbacks when replacing or restarting a session or destroying its owner, and use feedback from actual Unity projects to validate core boundaries.
