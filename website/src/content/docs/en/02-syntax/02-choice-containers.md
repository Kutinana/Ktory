---
title: Choices & Containers
description: Consumable and persistent options, first-line suspension, and zero-penetration mechanism
sidebar:
  order: 2
---

> This page follows the [Ktory design principles](/en/01-overview/03-design-principles/), which distinguish current contracts from longer-term goals.

# Choices and Containers

`#choice` is the conventional name for a basic choice container. The parser recognizes its indented children; the core determines which options can be submitted. The host decides whether to show buttons, a list or another presentation.

```ktory
#choice.loop
  * [Ask about the tower]
    Alice: It has stood here for hundreds of years.
  + [Look outside]
    : The wind has not stopped.
  + [Leave] -> break

: We open the door.
```

## Consumable and persistent options

A `*` option is consumed after a valid submission and remains unavailable when the menu is revisited in the current session. A `+` option is never consumed. Read `ChoiceOption.CanSelect` and `IsConsumed` when rendering; presentation must not re-enable consumed options. `Start()` clears session history. Disk persistence is outside this phase.

## Submission includes advancement

An active menu has `Status == AwaitingChoice`. `Step()` cannot bypass it. Submit an available option with `SubmitChoice(option.Id, menu.PresentationId)` and immediately read the new output. The core stops at the selected branch's first beat, which may be text, a textless directive or another choice.

`.loop` reopens the menu after a branch ends; `-> break` exits the nearest active loop. Basic choices with no selectable options are skipped. Language switching updates option labels, so use an option's `Id` rather than treating translated text as its stable identity.

Check implemented support for custom Handler registration and unknown-container diagnostics. Permissive external-condition handling in the standalone reader does not establish that a condition is true in a game.
