---
title: What is Ktory
description: Positioning and core philosophy of the Ktory narrative script system
sidebar:
  order: 1
---

**Ktory is an authoring and integration system for dialogue-driven games.** A Ktory script is like a scene in a play: it unifies text, dialogue sequence, choices, and branching, annotated with cues for music and visual performance. Meanwhile, the game engine acts as the stage director—responsible for concrete presentation, world state, and the evaluation of real in-game events.

Compared to traditional scripting solutions, Ktory is better suited for developing games with rich and complex narratives.

## Organize Content Like a Play

```ktory
@defaultLang: en
@speaker alice: en="Alice" | zh="爱丽丝"

alice:
  @en: The wind feels different today.
  @zh: 今天的风，似乎有点不同寻常。
  .expression(pensive)

#.sfx("wind.ogg").wait(1).next()

#choice.loop
  * [Depart]
    alice: Let's go.
    -> break
  + [Look around]
    : Leaves swirl outside the window.
```

Dialogue and narration form beats, and decorators attach to these beats. It is just like writing a screenplay: every beat represents a user interaction, and every decorator adds expressive cues to that line.

## One Core, Countless Hosts

Ktory uses C# as its parsing and runtime core, yet it can be used across environments of all sizes. From complex, feature-rich engines like Unity and Godot, to lightweight and flexible contexts like web browsers and VS Code extensions—even the command line can read and run Ktory scripts.

The C# core manages branching, calls, loops, and session history. The host environment manages presentation, resources, clocks, player input, and game state.

Ktory is still in early development. Currently, Ktory supports the following platforms:
- Unity: Use "Install Package from git URL" and enter `https://github.com/Kutinana/Ktory.git#upm`.
- VS Code: [VS Code Extension](vscode:extension/ktory.ktory).
- Web: [Web Reader](https://reader.ktory.ink/).

> A portable core does not imply that ready-made adapters have been delivered for every engine. Support for additional platforms is under active development.
