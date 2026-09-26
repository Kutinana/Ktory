---
title: What is Ktory
description: Ktory narrative script system positioning and core philosophy
sidebar:
  order: 1
---

> This page follows the [Ktory design principles](/en/01-overview/03-design-principles/), which distinguish current contracts from longer-term goals.

# What Is Ktory?

**Ktory is a dialogue-first, host-driven narrative language and runtime organized around beats.** It lets independent authors keep dialogue, choices, presentation cues and inline translations in one script. The host decides how to present those cues; the core determines the next narrative output.

## Organize content like a screenplay

```ktory
@defaultLang: en
@speaker alice: en="Alice" | zh="爱丽丝"

alice:
  @en: The wind feels different today.
  @zh: 今天的风，似乎有点不同寻常。
  .expression(pensive)

#choice
  * [Set off]
    alice: Let's go.
  + [Look around]
    : Leaves swirl outside the window.
```

Dialogue and narration form beats; decorators attach to them. `.expression(pensive)` expresses an intent for the host. It does not create a portrait, look up an image or establish that a resource binding exists.

## One shared core, explicit host responsibilities

`src/Ktory.Core` is the shared C# parser and runtime. It targets `netstandard2.1` with no external NuGet dependencies. The repository's standalone and WebAssembly tools use `net9.0`; the Unity publishing workflow packages the shared sources as `com.ktory.unity`.

The core owns branches, calls, loops and session history. The host owns rendering, resources, clocks, player input and game state. Text, textless directives and choices are observable logical stopping points. The host calls `Step()` or `SubmitChoice()` and reads the resulting output.

Phase 1 focuses on visual-novel-style dialogue and lightweight branching. Standalone reading checks basic narrative flow and language; host integration validates portraits, resources and other presentation. A portable core does not imply a shipped adapter for every engine.
