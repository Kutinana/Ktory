---
title: Localization
description: Native single-file multilingual variants and side-effect-free runtime hot reloading
sidebar:
  order: 4
---

This section introduces how to implement single-file multilingual authoring in Ktory.

## Single-file Localization

`@defaultLang: <locale>` declares the file language for untagged text and fallback; it is not a runtime primary/secondary locale switch. It may be omitted: with `@locale` variants, the current parser defaults to `zh` for compatibility; without multilingual declarations, the script is unlocalized and its actual language is `null`. Explicitly declare it for multilingual scripts to avoid relying on an implicit default.

`@locale:` labels an individual text variant. It does not make an explicit `@defaultLang` declaration mandatory. The implementation specification defines resolution for text, choices and names.

Currently, `@[locale]` declarations are supported for dialogue lines and choice option labels.

```ktory
@defaultLang: en

Alice:
  @zh: 你好，旅行者。
  @en: Hello, traveler.
  @ja: こんにちは、旅人さん。
  .expression(smile)

#choice
  * [@zh: "继续"]
    [@en: "Continue"]
    [@ja: "続ける"]
    Alice: Let's set sail.
```

## Character Names and Aliases

Character names appear frequently throughout scripts. Therefore, Ktory provides a specialized syntax for defining character names and their translations.

The `@speaker` declaration defines character names, translations, and aliases, and is case-sensitive. Once defined at the head of the file, Ktory will automatically return the correct defined translation for the character according to the requested language. Aliases are optional; they are never treated as output languages and can only be used as invocation inputs.

```ktory
@speaker en="Chtholly" | ja="クトリ" | cn="珂朵莉"

Chtholly:  // Whether you write Chtholly, クトリ, or 珂朵莉 here, it will automatically return the correct translation based on the requested language
  @en: You must leave now.
  @ja: 今すぐ離れて!
  @cn: 快走!
```

To define an alias, simply add the alias followed by a colon after `@speaker`. However, declarations such as `@speaker alice:` that lack any non-empty display names will report the line number and halt loading.

```ktory
@speaker ktr: en="Chtholly" | ja="クトリ" | cn="珂朵莉"

ktr: ...  // Can be called using the alias
```

This syntax also supports defining default decorators used when presenting the character.


## Output and Fallback

Ktory prioritizes returning the requested language; when a translation is missing for the requested language, it attempts to return the text in `defaultLang`. If neither has a valid translation, Ktory selects the first valid translation in the order of existing translations on that node and emits a source-located `Warning`.

`TextPayload.ActualLanguage` describes the actual body text language, while `RequestedLanguage` preserves the requested language. For example, when English (`en`) is requested and `defaultLang` is `zh`, if a line lacks an `en` translation, its `ActualLanguage` will be `zh`, while `RequestedLanguage` remains `en`. In this case, Ktory will not label the `zh` text as English.

Currently, `ChoiceOption` provides a localized `Label`, `RequestedLanguage`, and `ActualLanguage`, reporting the actual language separately for each option.

`TextPayload.SpeakerActualLanguage` separately reports the actual language of a declared speaker's name; this may differ from the body text, and is empty for undeclared speakers.

Body text, choice options, and declared names all follow the third-language fallback rule described above, without synthesizing missing-translation placeholders.

## Switching Language During Playback

```csharp
player.SetLanguage("en");
```

`SetLanguage(locale, token)` is a session setting: as long as the captured session remains valid, the language can still be changed even after the same session has moved to another beat or completed naturally; requests from an old session will be ignored.

Switching updates the active text, speaker, or menu labels without redispatching presentation decorators or clearing session history. The host then refreshes its display; if using `PresentationController`, call its `RefreshLanguage()` to handle the active presentation timing, and do not make an extra call to `Step()` for switching language.

Maintaining translations in the same file is well-suited for independent writing and translation. However, Ktory does not prevent you from maintaining separate Ktory script files for different languages.

## Structured primary / secondary output

`RequestedLanguage` is primary and `SecondaryLanguage` is optional secondary output. File `defaultLang` still assigns untagged text and defines primary fallback. Primary follows the rules above; secondary **requires an exact translation and never falls back**.

`TextPayload.Text`, nullable `TextPayload.SpeakerName` and `ChoiceOption.Text` each contain `Primary` and nullable `Secondary`. Each resolved value reports `Content`, `RequestedLanguage` and `ActualLanguage`. Existing `Content`, `Speaker` and `Label` properties map to primary output.

A missing secondary translation produces null for that field. A secondary language matching that field's primary actual language is also omitted. Identical text in different languages is retained. Body and name resolve independently, so a secondary name may exist without secondary body text. Unlocalized values and undeclared literal names do not gain fabricated language identities; an untagged value can match the file's known default language exactly.

```csharp
sequencer.Start(requestedLocale: "zh", secondaryLocale: "en");
sequencer.SetSecondaryLanguage("ja");
presentations.RefreshLanguage(secondaryOnly: true);
sequencer.SetSecondaryLanguage(null); // Disable secondary output.
```

Here `presentations` is a `MultiTrackPresentationController`; a single-Main `PresentationController` uses `RefreshSecondaryLanguage()`. `SetLanguage(locale)` preserves the secondary preference; `SetLanguageOutput(primary, secondary)` updates both.

Secondary changes refresh retained display content and Main's current text or choice without advancing, re-evaluating choice conditions, consuming options or replaying tags. They preserve typing and timing. Retained text can change language after its auxiliary playhead or session finishes; hidden text is not shown again. Reading estimates still use primary's actual content only; primary changes retain the existing timing-progress rules. Renderers may show, hide or arrange the two values independently.

Each playhead has one current statement. Entering the next withdraws the previous display regardless of destination; empty : also replaces it. Waiting at the next at/await gate has not entered the next statement, and EOF/stop has no next statement, so the previous display may finish its lifetime and remain refreshable. Apply `LanguageRefresh` by token without replaying routing tags, restarting duration or reviving withdrawn statements. Call `ReleaseOutput(token)` when hiding current content; reading history is separate from current presentation.

## Combining locales with literal fences

Keep `@locale:` outside the fence and wrap literal fragments separately in each variant; see [literal fragments and blocks](/en/02-syntax/01-anchor-decorator/#literal-fragments-and-blocks). An `@en:` inside a fence is text, not a new translation. Literal formatting does not change primary fallback, absent secondary translations or side-effect-free language refresh.

Each `Primary` / `Secondary` result may also contain `Segments`, with `Content` and `IsLiteral` per piece. The fragments follow the actual selected variant; a single plain-text flag for a beat or all locales is insufficient.
