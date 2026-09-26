---
title: Design Principles & Boundaries
description: Ktory positioning, ownership, relationship to Ink and phase scope
sidebar:
  order: 3
---

# Ktory Design Principles

**Ktory is a dialogue-first, host-driven narrative language and runtime organized around beats.** It serves independent authors who want dialogue, choices, presentation cues and inline translations to share one portable narrative structure.

This page summarizes the [canonical design charter](https://github.com/Kutinana/Ktory/blob/main/docs/ktory-design-charter.md). Phase specifications define implementation details. An acceptance requirement describes the behavior to achieve; it does not establish that every host and edge case has passed validation.

## Core and host responsibilities

| Layer | Responsibility |
| --- | --- |
| Ktory core | Narrative topology, choice submission, calls and loops, session narrative history, localized text selection and decorator dispatch |
| Host | World state, resource binding, text and audiovisual rendering, clocks, raw input and external conditions that restrict advancement |

A **beat is a logical stopping point**, not an animation lifecycle or a global timeline. The host decides when to call `Step()`; a choice menu requires `SubmitChoice()`. The core does not wait for movement, audio or typewriter effects.

**Forward-only** means the core does not automatically undo external side effects. Backward jumps, loops and calls remain valid, and future save restoration is not ruled out. `@speaker` manages localized names and aliases; it does not automatically bind portraits, audio or game character entities.

## Relationship to Ink

[Ink](https://github.com/inkle/ink/blob/master/Documentation/RunningYourInk.md) already provides line-by-line `Continue()`, choices, tags and external functions. [Ink Unity Integration](https://github.com/inkle/ink-unity-integration) provides Unity integration and preview tools. Host-driven execution and separation from rendering are not unique to Ktory.

Ktory chooses to standardize structured beats, parameterized decorators, inline translations, fallback and language switching as shared contracts, reducing the conventions each project has to establish around them. This is a language and workflow choice, not evidence that Ink cannot support these workflows. Ktory has not established greater maturity or lower integration effort in real projects.

## Current scope and longer-term goals

Phase 1 centers on dialogue, choices, inline translations, standalone reading and basic Unity presentation. Core quality needs ongoing validation using feedback from existing host integrations.

The longer-term goal is to reduce repetitive per-scene signals and coordination code. This does not add full Timeline integration, persistent saves, voice systems, translation extraction/import or other engine adapters to Phase 1. Unknown-handler diagnostics, input boundaries and rich text across renderers still need verification. Parsing or preview success does not validate actual host presentation.
