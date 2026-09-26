---
title: Design Principles & Boundaries
description: Ktory positioning, ownership, relationship to Ink and phase scope
sidebar:
  order: 3
---

# Ktory Design Principles

**Ktory is an authoring and integration system for dialogue-driven games.** The recommended workflow keeps text and story flow in `.ktr`. Shared presentation settings, local overrides and explicit presentation handoffs aim to reduce repeated configuration and scene-specific code.

This page summarizes the [design charter](https://github.com/Kutinana/Ktory/blob/main/docs/ktory-design-charter.md). The current runtime uses host-driven beats. **Shared defaults, configurable bindings and explicit signal dependencies are confirmed goals awaiting design and implementation; their syntax and APIs are not frozen.** See [specification §1.4](https://github.com/Kutinana/Ktory/blob/main/docs/ktory-implementation_v1.md#14-2026-09-26-需求修订已确认待落实). Product goals are not a list of shipped features.

## Story and presentation responsibilities

| Layer | Responsibility |
| --- | --- |
| `.ktr` and core | The recommended script owns text, dialogue order, choices and branches; the core handles control flow, session history, language selection and decorator dispatch |
| Host and presentation tools | World state, resource creation, animation and audiovisual rendering, clocks, raw input, and detecting and reporting actual events |
| Integration tools (new goal) | Editor bindings between resources, objects and script declarations, with reusable handoff handling; the dependency module and protocol remain undecided |

Timeline may temporarily control playback timing without taking over later dialogue and branches or maintaining duplicate story flow. A host may also call separate sections; ordinary authoring does not require that arrangement.

A **beat is a logical stopping point**, not a complete animation lifecycle. Currently the host advances with `Step()` and submits choices with `SubmitChoice()`; the core does not wait for movement, audio or typewriter effects. This current boundary does not permanently prohibit explicit signal dependencies in scripts.

**Forward-only** means external side effects are not automatically undone. Backward jumps, loops and calls remain valid; future restoration needs a separate design.

## Confirmed goals awaiting implementation

Shared settings should cover localized names, a default dialogue box, default sound or sound group, and expression resource mappings. Authors should write only local changes. Names and resources can be maintained separately and optionally associated, without requiring a complete game character entity. Current `@speaker` declarations still provide only names and aliases.

The confirmed expression rule is **current line → applicable global default → no expression**. A previous line's local setting never participates in fallback. No expression must actually clear the old display, rather than merely omit a tag. This remains to be implemented and does not imply that omitted BGM stops music. Explicitly cancelling a default, invalid-resource handling and shared-definition scope remain undecided.

Scripts should be able to declare that later story flow depends on a presentation node or completion signal, including passages without dialogue. The host detects and reports actual events. Dependency storage, notification matching and interaction with clicks or skipping remain undecided. Time requirements use time; event requirements use signals. Prefer a completion signal when the intent is to continue after completion. Existing `.wait(t)` remains a minimum hold that does not advance by itself, and `.next` keeps its current rules. Presentations do not all block by default.

Authors should bind resources, objects and declarations in the editor. A new capability may require one integration implementation; additional scenes using it should mainly require content and configuration, reducing repeated listeners, pauses, resumes and cleanup. Presentation authors still place meaningful nodes. General events remain an extension mechanism; this is not a promise of no-code support for every behavior.

## Relationship to Ink

[Ink](https://github.com/inkle/ink/blob/master/Documentation/RunningYourInk.md) already supports host advancement, choices, tags, external functions and project wrappers; its [Unity integration](https://github.com/inkle/ink-unity-integration) supplies integration and preview tools. Ktory aims to maintain reusable dialogue presentation, resource binding and handoff conventions as product capabilities. This does not establish that Ink cannot do the same. Reduced work must demonstrate product value; a separate runtime still needs justification through semantics and maintenance cost.

## Current scope and validation

Phase 1 still centers on dialogue, choices, inline translations, standalone reading and basic Unity presentation. The new goals do not add full Timeline integration, persistent saves, voice systems, translation extraction/import or other engine adapters. Selecting a suitable story from current circumstances and resuming a story after returning control to the player are recorded but deferred requirements, not a task system or general game state machine.

Skipping unknown presentation in preview does not mean a signal occurred. Bypassing or manually confirming new handoff points still needs a defined preview policy. Validation should check expression clearing, shared settings without line-by-line edits, reused bindings, duration-independent completion dependencies, visible changes to story handoffs, and rejection of stale notifications. Core tests and preview do not replace actual Unity presentation checks.
