---
title: Discrete Steps & Forward-Only Philosophy
description: Current beat advancement, host responsibilities and pending explicit signal dependencies
sidebar:
  order: 1
---

> This page follows the [Ktory design principles](/en/01-overview/03-design-principles/), which distinguish current contracts from longer-term goals.

# Discrete Beats and Host-Driven Execution

The current Ktory runtime defines an advance as execution to the next logical stopping point. “Discrete” describes the narrative protocol; it does not make the whole game run in discrete time or require audio and animation to stop at beat boundaries.

## What an advance does

`Start()`, `Step()` and a valid `SubmitChoice()` synchronously execute control flow, update `Status` and `CurrentPayload` / `CurrentChoice`, and dispatch decorators. They do not return a visual snapshot. The host reads the payload and presents it.

Jumps and calls may execute consecutively within one advance until text, a textless directive, a choice or completion is reached. An ordinary `Step()` cannot bypass a required choice. Determinism depends on the same script, inputs and host evaluation results; external randomness and side effects do not become pure functions.

## Who owns time

Currently, typewriter effects, fast-forwarding, reading holds and external presentation gates belong to the host. Although distributed in the shared C# library, `PresentationController` is an optional presentation policy helper; it does not make the Sequencer wait for animations. Ambient sound and movement may continue while the host decides when another advance is allowed.

The new goals allow explicit dependencies on presentation nodes or completion signals in `.ktr`, including passages without dialogue. The host still detects actual events. Dependency storage, matching and advancement protocols remain undecided; the current no-wait boundary must not rule them out permanently. Time and signals express time and event requirements respectively. Existing `.wait/.next` rules remain unchanged, and presentations do not all block by default. See the [design principles](/en/01-overview/03-design-principles/).

[Ink can also be driven line by line by a host](https://github.com/inkle/ink/blob/master/Documentation/RunningYourInk.md) and provides choices and extension interfaces. Ktory chooses to standardize beat data, parameterized decorators and inline localization contracts. Its design does not depend on other engines being unable to separate rendering.

## State and restoration

The core owns its execution position, call stack and session choice history. External state changes are not automatically rolled back; backward jumps execute the content again. Future line-level restoration requires stable identities and host restoration contracts. The current `PresentationId` does not substitute for that design.

Reducing per-scene wiring remains a longer-term goal to test in actual projects. Judge phase delivery through concrete cases and integration feedback, rather than inferring completed presentation coordination from the architecture alone.
