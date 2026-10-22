---
title: Anchor & Decorator System
description: Ktory's core syntax philosophy and execution beat model
sidebar:
  order: 1
---

Anchors declare executable beats or options; decorators carry parameters. This page follows the contract confirmed on 2026-10-22. Implementation and validation are in progress, so installed versions may differ.

## Nodes and advancement

| Syntax | Structure | Default behavior |
| --- | --- | --- |
| `Speaker: Line` / `: Narration` | TextStep | Main waits for valid input |
| Empty `:` / `: .effect()` | TextStep | Ends the previous statement on this playhead, dispatches tags, then waits; persistent BGM is unaffected |
| `#` / `#.effect()` / `#do` without children | DirectiveStep | Standalone directives advance automatically |
| `#choice` with indented `*` / `+` | ContainerStep | Main choice; auxiliaries skip the entire container |

Indentation still distinguishes a container from a directive; not every # is automatic. Sections and [playhead roles](/en/04-architecture/01-discrete-steps/) are separate. Old `#parallel/#lane` requires explicit migration.

```ktory
Alice: That's unbelievable... .next(seconds=0.5)
#.sfx("door")
: .camera_shake(0.5)
#.wait(seconds=3)
```

Built-ins use fully named arguments: `.clock(name="song")`, `.track(source="Lyrics", name="lyric")`, `.at(value=3, clock="song")`, `.wait(seconds=2)`, `.next(seconds=1)`, `.duration(seconds=3)`, `.skippable(enabled=false, seconds=3)`, `.loop(count=2)`. Positional built-in arguments are not supported in this version. Host tags such as `.bgm("main", volume=0.6)` may still use positional arguments; the host implements their effects.

`.tag` equals `.tag()`; explicit zero differs from omission. Optional arguments may remain omitted. Bare wait estimates reading time from primary text; bare next means zero seconds. A decorator without a preceding anchor in its block is still a located parse error and cannot attach across a section or branch boundary.

## Main presentation timing

wait and next start together when text finishes displaying; textless beats start on entry. wait is a minimum hold, not an automatic policy. next schedules automatic advancement, and the combined time is max. `.wait(seconds=2).next(seconds=5)` allows input after two seconds and advances automatically at five. `.wait(seconds=5).next(seconds=2)` advances at five. Manual clicks rejected by wait or skippable are discarded, not queued.

Standalone # already defaults to automatic, so `#.wait(seconds=3)` continues after three seconds; `: .wait(seconds=3)` then waits for a fresh click. An allowed click during typing only reveals text. Main AUTO still respects the minimum hold.

duration starts with wait but never blocks advance. An accepted advance may enter the next statement early; entering it withdraws the previous statement on that playhead even if its display destination changes. Empty : also ends the previous display. While waiting at the next at gate, or after EOF/stop, the previous display may finish its own lifetime. Display expiry does not advance Main; wait does not hide text.

Auxiliaries continue directly to at/Core boundaries, unaffected by wait/next/AUTO or reading estimates. Their duration remains purely visual. Old duration/fade callbacks may affect old content only, never its replacement.

## Inline formatting

Supported forms include `*italic*`, `**bold**`, `~~strikethrough~~` and `[desk]{pronunciation}`. Desugaring currently produces tag strings such as `<ruby="pronunciation">desk</ruby>`, not a separate structured Ruby object. Native tags pass through; the renderer determines whether it supports them.

Escape reserved characters with a backslash when they should appear literally. Do not use Markdown headings, lists or code fences as dialogue structure. Rich-text presentation must be checked separately in Web and Unity renderers.

## Literal fragments and blocks

Wrap literal text in three or more ASCII single quotes, inline or across multiple lines:

```ktory
Alice: Write '''.wait''' here; **bold** outside still works. .wait(seconds=1)
Alice:
  @en: '''
    .wait
    // This is text too
    <b>**This stays literal**</b>
  '''
  @zh: 正文包含'''.wait'''。
  .next(seconds=1)
: ''''Display ''' without an escape''''
```

Inside a fence, decorators, comments, locale labels, control flow, Markdown, Ruby, native markup and backslash escapes are all literal. Decorators outside belong to the whole beat. A multiline block is still one beat, and each locale can mix formatted and literal fragments independently.

Opening and closing fences must have exactly the same number of quotes, at least three. Other quote-run lengths inside are text, with no nesting. Choose a fence longer than any quote run in its content. To display only `'''`, place it on its own line inside a four-quote block so content does not merge with delimiters. Curly quotes are always ordinary text.

Inline fragments preserve their internal whitespace. A block starting with a newline removes the opening/closing layout lines and common body indentation, preserving relative indentation, interior blank lines and trailing spaces. Tabs count as two columns. An unclosed fence stops loading and reports its opening line and column.

Hosts use each resolved locale's `Segments`, when present: render `IsLiteral=true` as text with whitespace preserved, and other pieces with the existing rich-text handling. Reinterpreting the combined `Content` as markup loses this distinction. The shared Reader supports these fragments; other hosts must implement their own rendering.
