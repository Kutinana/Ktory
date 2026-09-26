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

Minimum holds and automatic advancement are separate: `.wait` only unlocks input at its end and never queues clicks. `.wait.next` advances automatically afterwards; an AUTO region or host autoplay can also provide that policy. `.wait(s).next(t)` starts both timers when printing finishes, regardless of order. Route raw input through `HandleUserClick()` rather than bypassing presentation policy with `Step()`. `QueuedAdvance` remains for compatibility and is always `false`.

With the default `AutoStepSequencer == true`, the controller advances the player itself. Do not call `Step()` again from `OnAdvanceRequested`. Resource and portrait bindings, world state and external gates remain host responsibilities. Cancel old inputs and callbacks when replacing or restarting a session or destroying its owner, and use feedback from actual Unity projects to validate core boundaries.

## Play Mode debugging window

Open **Window → Ktory → Debugging**. Implement `IKtoryDebugTarget` on the existing host, register it with `KtoryDebugRegistry` before starting the real session, and dispose the registration on disable, destruction or session replacement. Reference `Ktory.Unity` from the host asmdef and guard adapter code with `#if UNITY_EDITOR`. Package Manager includes a **Debugging host probe** wiring sample.

The window shows multiple players, nodes and source lines, content and speaker, real controller input gates and AUTO timers, language selection including the script default, normal reveal/advance, legal choices and restart, call stacks, loops and consumed options. Language changes use the host's `SetLanguage`, `RefreshLanguage(false)` and UI refresh path without stepping or replaying tags. Standard timing is available only when the host exposes its real `PresentationController`.

Execution history starts at registration and retains at most 2000 entries across all players, with filters, clear and copy. Opening or closing the window does not change playback. Projects expose animation state, extra input gates and custom AUTO via `InputBlockReason` and optional `IKtoryDebugInfoProvider`; the package does not interpret custom modifiers. There is no forced skip, and the UI, registry and log are excluded from Player builds. See the generated package's `DEBUGGING.md` for wiring and Unity acceptance checks. Core tests do not establish Unity UI or presentation validation.
