---
title: Localization (I18N)
description: Native single-file multilingual variants and side-effect-free runtime hot reloading
sidebar:
  order: 4
---

> This page follows the [Ktory design principles](/en/01-overview/03-design-principles/), which distinguish current contracts from longer-term goals.

# Inline Localization

`@defaultLang` sets the file's default language and defaults to `zh`. Text without an `@locale` marker belongs to that language. Dialogue, speaker display names and option labels can be maintained together.

```ktory
@defaultLang: zh
@speaker alice: zh="爱丽丝" | en="Alice" | ja="アリス"

alice:
  @zh: 你好，旅行者。
  @en: Hello, traveler.
  @ja: こんにちは、旅人さん。
  .expression(smile)

#choice
  * [@zh: "继续"]
    [@en: "Continue"]
    [@ja: "続ける"]
    alice: 我们出发吧。
```

## Output and fallback

Each line prefers the requested language and falls back to `defaultLang` when its translation is missing. `TextPayload.ActualLanguage` describes the actual body text; `RequestedLanguage` preserves the request. In this example, requesting English still produces `zh` for the untranslated line in the option branch. Do not label that fallback text as English.

`@speaker` creates case-sensitive lookup aliases for display names. Here `alice`, `爱丽丝`, `Alice` and `アリス` refer to one name definition. Undeclared speakers are displayed literally. This does not bind portraits or audio characters.

The current `ChoiceOption` provides a localized `Label`, without a per-option `ActualLanguage` field. A menu label is not a replacement for the option's `Id`. Cases where default-language text is also missing must be checked against the current core and specification; ordinary fallback rules do not establish additional guarantees.

## Switching during playback

```csharp
player.SetLanguage("en");
```

Switching updates the active text, speaker or menu labels without redispatching decorators or clearing session history. Refresh the host display afterwards. If using `PresentationController`, call its `RefreshLanguage()` to update the active presentation state; do not add a `Step()` for language switching.

Inline translations suit independent writing and translation. This phase does not promise automatic translation, extraction/import tools or cross-file synchronization.
