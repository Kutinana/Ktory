---
title: Main, auxiliary playheads and clocks
description: Execution order, input ownership, display lifetime and clock gates
sidebar:
  order: 1
---

This page describes the contract confirmed on 2026-10-22. Implementation and validation are in progress; source changes do not update deployed Readers or installed packages.

## Separate execution from display

A session has one Main playhead, which owns player input, and automatic auxiliary playheads. Sections have no Main/auxiliary role: multiple playheads can execute the same section. Each retains its position, calls, loops and default clock reference.

Main text and empty `:` beats default to manual input; ordinary standalone `#` directives default to automatic advancement. A player click first passes through presentation policy and may only reveal text. Auxiliaries continue immediately after output until the next `at` or Core control-flow boundary. They never wait for typing, reading estimates, wait/next/AUTO or duration.

```ktory
#.clock(name="song")
#.track(source="Lyrics", name="lyric")
: Listen to this melody.
#.awaitTrack(name="lyric")
: Lyrics have finished executing.
-> end

=== Lyrics ===
  : First line. .at(value=1, clock="song").duration(seconds=1)
  : Second line. .at(value=3, clock="song")
```

Auxiliary text without at may produce several outputs in one batch, leaving only the last statement per playhead regardless of display destination. This is legal. Past thresholds execute their outputs and side effects normally; Core does not discard effects by type. Execution budgets bound zero-time loops.

## Create, stop and await

Only Main may track, stopTrack or awaitTrack; auxiliaries ignore these operations. They also skip an entire choice, including branches and attached tags, without consuming options. Main choices retain their normal rules.

`#.track(source="Lyrics", name="lyric")` creates a named instance. Omitting name creates an anonymous instance; the section name is never an implicit instance name. Multiple instances may share a section. A named instance has one declaration location and starts once per session, even if the declaration is reached again after completion. Anonymous declarations create a fresh instance each time and cannot be stopped or awaited by name externally.

track creates and queues an instance without guaranteeing that initialization has run. If Main depends on initialization, use `#.track(source="Init", name="init").await` to wait for the section to finish, or declare shared resources in Main first. Anonymous `#.track(source="Init").await` waits for that instance; `awaitTrack(name=...)` waits for a named auxiliary started earlier. `.await` takes no arguments and must immediately follow track on the same ordinary # anchor. Plain track remains concurrent.

`#.stopTrack(name="lyric")` stops a running instance; not-yet-created and ended instances are no-ops, without cancelling future creation. `#.awaitTrack(name="lyric")` is an unskippable Core gate until the instance stops; ended instances pass immediately. An uncreated target produces a located diagnostic without waiting for future creation. Unknown names are diagnosed.

Main completion ends the session and stops auxiliaries; there is no automatic join-all. Use explicit await when needed. Execution stopping does not clear the screen. An auxiliary reaching EOF after its last output at second 10 completes then, even if that display remains until second 12. Last content is delivered before termination.

## Clock identity and entry gates

- `#.clock()` creates this playhead's anonymous default clock. New playheads start without a default and do not inherit one. Replacing an anonymous default releases it; defaults are not a history stack.
- `#.clock(name="song")` declares a global named clock without selecting it as default. One name has one declaration location. First execution creates it; later execution reuses it without resetting until session end.
- `#.defaultClock(name="song")` selects an already-created clock without creating or resetting it. Jump/call do not change the default implicitly; return does not restore it.
- `#.resetClock()` resets the current default; `#.resetClock(name="song")` resets a named clock. ID stays stable. Built-ins return to zero; external resets belong to the host.

`at(value=6)` uses the playhead's current default; `at(value=6, clock="song")` uses a named clock. The gate applies only after execution reaches that entry and requires a known value >= threshold. It does not generate an advance for the previous sentence. Main confirming at second 3 may wait at the gate until second 6; without confirmation, the threshold cannot preempt the previous beat. A gate precedes all tags on its anchor, so a same-anchor declaration cannot satisfy its first reference.

`source` is opaque host data: `#.clock(name="song", source="music.position")` never starts audio. Empty source equals omission; the host may take over creation, otherwise a built-in seconds clock is used. External values can be any finite numbers with host-defined units. Temporarily unavailable values wait without blocking other runnable playheads. Backward values never undo execution. Getter/reset exceptions are background diagnostics, not guessed values or permission to bypass gates.

Web/WASM Reader explicitly resolves `music.position` to the current BGM player’s actual playback position in seconds. Start playback with `.bgm(...)`, then reference the source. Pausing and seeking follow the real player; stopped, loading or failed audio supplies no value. Other undefined external sources are never simulated, and clicking cannot supply a threshold value or bypass a gate. The VS Code preview currently disables audio playback, so this source remains unavailable there.

Start or change BGM in a separate Main directive, then create auxiliaries that depend on it at a later anchor. A same-anchor or auxiliary audio change has no media acknowledgement boundary, so the current Core batch may still read the previous track’s sample.

## Output and host integration

Subscribe to `Sequencer.OnOutput` and process StoryOutput in Sequence order: Content, LanguageRefresh, TrackEnded and SessionEnded. Metadata identifies the source playhead, optional name, IsMain and content Token. A final active-playhead snapshot cannot replace delivery events; it may omit an auxiliary's last output. `LastOutputs` retains only the latest operation batch.

`GetRetainedPresentations()` retains each playhead’s current display, including a final statement from an ended auxiliary. Call `ReleaseOutput(token)` when the renderer removes it. Tags and host configuration choose display destinations; names do not implicitly identify UI elements. Entering the next statement withdraws the previous one on that playhead, even across different destinations. Empty :, directive output and choices also replace it. Waiting at the next at/await gate may retain it. Old duration/fade callbacks must not clear replacements; retained display grants no input authority.

Input targets Main's current identity. Printing completion is a presentation-controller concern and never blocks auxiliary execution. Submit clock delta once per frame; do not duplicate an update already driven by a multi-track wrapper. Language refresh neither advances, samples sources nor replays effects, including for retained displays from completed auxiliaries.

## Control flow and migration

Jump clears the call stack; call pushes it; every playhead needs explicit return. Main named-section EOF/end resumes subsequent root content; auxiliary named-section completion ends that auxiliary.

Old `#parallel/#lane` requires explicit migration to sections, track and intended await calls. Old runtime semantics are not retained, and nested or repeated joins may require restructuring. External files require an explicit URL, but its format and engine loading protocol remain deferred; source is not guessed to be a file path. Event signals, persistence, race and pause protocols remain separate work.
