---
title: Control Flow & Section Topology
description: Named sections, jumps, subcalls, and call stack management
sidebar:
  order: 3
---

> This page follows the [Ktory design principles](/en/01-overview/03-design-principles/), which distinguish current contracts from longer-term goals.

# Control Flow and Sections

The root executes in source order and skips named section bodies. Use canonical indentation: a named section's body is deeper than `=== Label ===`; returning to the header's indentation resumes root content.

```ktory
: The story begins.
=> Greeting
: Continue the main story.
-> end

=== Greeting ===
  Alice: Welcome.
  -> return
```

`=> Greeting` establishes a return position, and `-> return` resumes at “Continue the main story.” Replacing the call with `-> Greeting` clears the call stack, so no return position may be assumed.

| Instruction | Meaning |
| --- | --- |
| `-> Label` | Clear the call stack and jump to the target section |
| `=> Label` | Record a return position and call the target section |
| `-> return` | Pop a call frame; an empty stack is a control-flow error |
| `-> break` | Exit the nearest active loop and continue after it |
| `-> end` | End the file in root; in a named section, clear the stack and resume at the first subsequent root node |

Reaching a named section's end is equivalent to `-> end` there; it **does not implicitly return to the caller**. Write `-> return` when a return is intended. A host can select an entry with `Start(entryLabel: "Greeting")`, but that does not establish a call frame: the example above must be called through the root to execute its final `return`.

Forward-only does not prohibit backward jumps or loops. It means earlier external side effects are not automatically undone. Re-entering a beat dispatches its presentation or state decorators again.
