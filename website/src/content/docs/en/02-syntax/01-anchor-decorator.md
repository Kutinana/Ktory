---
title: Anchor & Decorator System
description: Ktory's core syntax philosophy and execution beat model
sidebar:
  order: 1
---

> This page follows the [Ktory design principles](/en/01-overview/03-design-principles/), which distinguish current contracts from longer-term goals.

# Anchors and Decorators

Anchors create executable beats or options. Decorators such as `.tag(args)` attach parameters to them. A beat defines where the narrative stops; the host implements presentation and its duration.

## Nodes and structure

| Syntax | Parsed structure | Meaning |
| --- | --- | --- |
| `Speaker: Line` | `TextStep` | Text beat with a speaker |
| `: Narration` or text without a reserved prefix | `TextStep` | Anonymous text beat with an empty `Speaker` |
| `#do` without child options | `DirectiveStep` | Textless directive that still stops execution |
| `#choice` with indented `*` / `+` children | `ContainerStep` | Option container |

The child structure distinguishes a directive from a container; `choice` is not a parser keyword. Check actual runtime support for custom container names. A name alone does not establish that a business Handler exists.

```ktory
Alice: That's unbelievable...
  .expression(surprised)
  .next(0.5)

#do .sfx("door")
```

Decorators attach to their owning anchor; indentation bounds options and branch bodies. `.tag` and `.tag()` both have no arguments, while `.tag(0)` explicitly passes integer zero. Positional and named arguments are supported, for example `.bgm("main", volume=0.6)`. These names do not supply audio or portrait implementations.

Presentation timing conventions use seconds. `.next(0.5)` requests advancement half a second after text finishes displaying. `.skippable(false, 3)` restricts text fast-forwarding for the first three seconds. The core parses these declarations; the host or presentation controller implements their timing.

`.wait(t)` only enforces a minimum hold after printing; it does not enable automatic advancement. Clicks during that hold are discarded, so manual playback needs a fresh click afterwards. Bare `.wait` uses estimated reading time. `.next(t)` schedules automatic advancement after printing; an omitted argument means zero seconds.

The timers run concurrently, regardless of decorator order. Both `.wait(2).next(2)` and `.next(2).wait(2)` advance after two seconds. `.wait(2).next(5)` advances at five seconds, with fresh clicks allowed from two seconds onwards. `.wait(5).next(2)` cannot advance until five seconds. `.wait.next` advances after the estimated reading hold. `#AUTO` or host autoplay can also supply the automatic policy, but must respect the minimum hold.

Clicks blocked by `.skippable(false[, t])` are also discarded, never replayed when the lock expires or printing finishes.

## Inline formatting

Supported forms include `*italic*`, `**bold**`, `~~strikethrough~~` and `[desk]{pronunciation}`. Desugaring currently produces tag strings such as `<ruby="pronunciation">desk</ruby>`, not a separate structured Ruby object. Native tags pass through; the renderer determines whether it supports them.

Escape reserved characters with a backslash when they should appear literally. Do not use Markdown headings, lists or code fences as dialogue structure. Rich-text presentation must be checked separately in Web and Unity renderers.
