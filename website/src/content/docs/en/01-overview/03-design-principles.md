---
title: Design principles and extension goals
description: Design principles and extension goals
sidebar:
  order: 3
---

Ktory organizes narrative control flow, localization, numeric conditions and presentation intent through text. This page follows the contract confirmed on 2026-10-22; the Main/auxiliary implementation and validation are in progress, not a release claim.

- One Main owns player input and auxiliary create/stop/await. Sections are role-neutral. Playheads keep independent positions, calls, loops and default clocks; multiple instances may share a section. Auxiliaries cannot create auxiliaries and skip choices, branches and attached tags without consuming options.
- Auxiliaries continue after output to at/Core boundaries without typing, wait/next, AUTO or reading estimates. Main completion stops auxiliaries; use explicit await instead of implicit join-all.
- Named clocks have one global declaration and are reused after first creation. Anonymous clocks belong to the current playhead's default and are released when replaced. Reset retains ID. At gates entry only, never generating the previous beat's advance. Source is opaque, starts no media and may temporarily have no value.
- Standalone # defaults to automatic; Main text and empty : default to manual. Each playhead has one current statement. Entering the next withdraws the previous one regardless of display destination; empty : does so too. Duration starts with wait but never blocks advance.
- Tags/host configuration route displays; names do not automatically map to UI. Final content precedes completion, which does not force a clear. Old display callbacks cannot affect replacements, and retained displays do not retain input authority.
- Primary fallback, exact secondary translations and literal segments remain. Language refresh never advances, replays effects, resets timing or samples clocks, including retained displays from completed auxiliaries.
- Old parallel/lane requires explicit migration; nested or repeated joins may need restructuring. External files require an explicit URL, with syntax and engine loading deferred. Event signals, persistence, race and pause remain separate work.
- Implementation, tests, distribution and Unity validation are distinct. Move detectable errors to compile/check stages, but do not promise static knowledge of future external values. Budgets and background diagnostics remain necessary.

Authoritative documents: [design charter](https://github.com/Kutinana/Ktory/blob/main/docs/ktory-design-charter.md), [scope](https://github.com/Kutinana/Ktory/blob/main/docs/ktory-scope_v1.md), [implementation specification](https://github.com/Kutinana/Ktory/blob/main/docs/ktory-implementation_v1.md).
