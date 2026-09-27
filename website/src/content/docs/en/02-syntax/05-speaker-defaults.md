---
title: Speaker default decorators
---

Indent default decorators below a `@speaker` declaration. Each matching dialogue applies these defaults; local decorators replace the entire same-name default group. The next line starts from the defaults again.

```ktory
@speaker alice: zh="爱丽丝" | en="Alice"
  .portrait(normal).emotion(calm).voice(soft)

alice: 默认呈现。
alice: 本句覆盖。 .portrait(happy).emotion(happy)
Alice: 再次使用默认值。
```

Definitions apply within the current file, through every declared alias, regardless of declaration order. They create no beat and do not apply to narration, directives or choice items. Matching dialogue inside a choice branch still receives them.

Names are compared case-insensitively. Replacement is by whole decorator group, not by individual argument; repeated local decorators remain in source order. Unreplaced defaults come first, followed by local decorators. `TextPayload.Tags` already contains the effective list.

Any decorator may be a default, behaving as if written on each line. Existing `.wait/.next/.loop` semantics remain. If your host interprets `.sfx(bell)` as playing a sound once, placing it in the defaults plays it on every matching line. Language refresh does not replay decorators.

There is no cancellation syntax in this increment. Hosts define resources and actions; these defaults do not automatically bind Unity portraits. Rebuild the current dialogue presentation from the effective list, clearing an expression when neither a local nor a default value exists. This does not imply stopping BGM when its decorator is omitted.

An anonymous `#` anchor can be followed directly by decorators: `#.sfx("wind.ogg").wait(1).next()`. The portal, Reader source editor and VS Code use the same TextMate grammar, though themes may choose different colors.
