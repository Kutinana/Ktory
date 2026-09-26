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

Each line prefers the requested language and falls back to `defaultLang` when its translation is missing. If neither has valid text, it selects the first valid entry in that node’s existing translation order and emits a source-located `Warning`. `TextPayload.ActualLanguage` describes the actual body text; `RequestedLanguage` preserves the request. In this example, requesting English still produces `zh` for the untranslated line in the option branch. Do not label that fallback text as English.

`@speaker` creates case-sensitive lookup aliases for display names. Here `alice`, `爱丽丝`, `Alice` and `アリス` refer to one name definition. Undeclared speakers are displayed literally. The current declaration supplies names and aliases without automatically binding portraits or audio. The product goal allows names and optional presentation defaults to be maintained separately, associated and locally overridden. Shared-definition and resource-binding syntax remains undecided; see the [design principles](/en/01-overview/03-design-principles/).

The current `ChoiceOption` provides a localized `Label`, `RequestedLanguage` and `ActualLanguage`, reporting the actual language separately for each option. A menu label is not a replacement for the option's `Id`. `TextPayload.SpeakerActualLanguage` separately reports the actual language of a declared speaker name; it can differ from the body and is null for an undeclared speaker. Body text, options and declared names all follow the third-language fallback rule without inventing missing-text placeholders. A speaker declaration with no nonempty display name, such as `@speaker alice:`, stops loading with a source-located error. This decision does not define a new general rule for empty body or option declarations.

## Switching during playback

```csharp
player.SetLanguage("en");
```

`SetLanguage(locale, token)` changes a session preference: it accepts an earlier beat or natural completion within the same valid session, while rejecting requests from an old session.

Switching updates the active text, speaker or menu labels without redispatching decorators or clearing session history. Refresh the host display afterwards. If using `PresentationController`, call its `RefreshLanguage()` to update the active presentation state; do not add a `Step()` for language switching.

Inline translations suit independent writing and translation. This phase does not promise automatic translation, extraction/import tools or cross-file synchronization.
