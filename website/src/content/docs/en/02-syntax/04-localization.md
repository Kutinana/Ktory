---
title: Localization
description: Native single-file multilingual variants and side-effect-free runtime hot reloading
sidebar:
  order: 4
---

This section introduces how to implement single-file multilingual authoring in Ktory.

## Single-file Localization

`@defaultLang` declares the default language used by this Ktory script. This is not a mandatory declaration: if left blank or omitted, Ktory still works normally. In this case, Ktory will not infer the language on its own. If you are certain your script or game uses only a single language, you can omit the `@defaultLang` declaration. Otherwise, this may cause inconvenience for subsequent multilingual localization.

`@[locale]` explicitly declares the language of a specific text. This does not affect Ktory's default language. However, if you use `@[locale]` in your script to declare the language of any text, the `@defaultLang` declaration becomes mandatory. In this case, other texts without an `@[locale]` annotation are automatically treated as being in the `@defaultLang` language.

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
