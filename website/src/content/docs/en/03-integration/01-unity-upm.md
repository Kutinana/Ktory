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
    public PresentationToken DisplayedToken { get; private set; }

    private void OnEnable()
    {
        player = new KtorySequencer(KtoryParser.Parse(script.text));
        player.OnTagsDispatched += tags =>
        {
            foreach (var tag in tags) Debug.Log($"[Ktory tag] {tag.Name}");
        };
        player.Start(requestedLocale: "zh");
        LogCurrent();
    }

    public void AdvanceDisplayedBeat(PresentationToken token)
    {
        player.Step(token);
        LogCurrent();
    }

    public void SubmitOption(string optionId, PresentationToken token)
    {
        player.SubmitChoice(optionId, token);
        LogCurrent();
    }

    private void OnDisable() => player?.InvalidateSession();

    private void LogCurrent()
    {
        DisplayedToken = player.CurrentPresentationToken;
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

Retain the full `DisplayedToken` when showing content; option buttons keep that menu token and the option `Id`. Capture the token in a local variable before creating a button callback, rather than rereading a changed property when it runs. `SubmitChoice()` already produces the selected branch's first beat; do not add `Step()` for that same selection. Existing host input code must handle fast-forwarding and external advancement conditions before a normal advance is sent.

## Shared presentation timing

A host may use `PresentationController` as follows, or implement the same contracts itself. In this table, `token` is the `presentation.CurrentPresentationToken` captured when creating the display job or queueing input; it includes both session and presentation identity:

| Host action | Controller API |
| --- | --- |
| Bind an existing player | `new PresentationController(player)` |
| Establish state after `Start()` or a valid choice | `SetupForCurrentBeat()`, then read the current output |
| Tick the host clock | `Update(Time.unscaledDeltaTime)` |
| Handle a normal raw click | `HandleUserClick(token)` |
| Report natural typewriter completion | `NotifyPrintingFinished(token)` |
| Redraw after an automatic or click advance | Subscribe to `OnBeatChanged` |
| Reveal the current text immediately | Subscribe to `OnFastForwardRequested`; reveal text without notifying completion again |
| Change the active language | `player.SetLanguage(...)`, then `RefreshLanguage()` and redraw |

Minimum holds and automatic advancement are separate: `.wait` only unlocks input at its end and never queues clicks. `.wait.next` advances automatically afterwards; an AUTO region or host autoplay can also provide that policy. `.wait(s).next(t)` starts both timers when printing finishes, regardless of order. Route raw input through `HandleUserClick(token)` rather than bypassing presentation policy with `Step()`. `QueuedAdvance` remains for compatibility and is always `false`.

With the default `AutoStepSequencer == true`, the controller advances the player itself. Do not call `Step()` again from either advance event. For host-managed advancement, set it to `false` and capture the token through `OnAdvanceRequestedWithToken`; after accepting the request, call `Step(token)` and establish the next beat. A stale callback must not run Setup again or reset current timers. Resource and portrait bindings, world state and external gates remain host responsibilities.

Each `Start()` creates a new session identity. Call `InvalidateSession()` before closing, replacing or restarting a player, and cancel old input, subscriptions and asynchronous work. Stale or repeat input produces only background `InputIgnored` diagnostics; an invalid option for the active menu remains an error. Legacy calls without a token or with only a presentation number remain synchronous compatibility APIs without cross-session guarantees. Actual Unity lifecycle behavior still needs validation in Unity.

## Play Mode debugging window

Open **Window → Ktory → Debugging**. Implement `IKtoryDebugTarget` on the existing host, register it with `KtoryDebugRegistry` before starting the real session, and dispose the registration on disable, destruction or session replacement. Reference `Ktory.Unity` from the host asmdef and guard adapter code with `#if UNITY_EDITOR`. Package Manager includes a **Debugging host probe** wiring sample.

The window shows multiple players, nodes and source lines, content and speaker, real controller input gates and AUTO timers, language selection including the script default, normal reveal/advance, legal choices and restart, call stacks, loops and consumed options. Language changes use the host's `SetLanguage`, `RefreshLanguage(false)` and UI refresh path without stepping or replaying tags. Standard timing is available only when the host exposes its real `PresentationController`.

Execution history starts at registration and retains at most 2000 entries across all players, with filters, clear and copy. Opening or closing the window does not change playback. Projects expose animation state, extra input gates and custom AUTO via `InputBlockReason` and optional `IKtoryDebugInfoProvider`; the package does not interpret custom modifiers. There is no forced skip, and the UI, registry and log are excluded from Player builds. See the generated package's `DEBUGGING.md` for wiring and Unity acceptance checks. Core tests do not establish Unity UI or presentation validation.
