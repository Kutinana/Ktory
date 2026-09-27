---
title: 本地化
description: 原生单文件多语言变体与无副作用即时热切换
sidebar:
  order: 4
---

本节介绍如何在 Ktory 中实现单文件的多语言编写。

## 同文件多语言

`@defaultLang` 声明本 Ktory 剧本默认使用的语言。这不是一个强制声明，留空或不声明时，Ktory 也能正常工作。在此情况下 Ktory 不会自行推断语言。如果您确信您的剧本、游戏仅使用一种语言，您可以不写 `@defaultLang` 声明。否则，这可能会为后续多语言本地化带来不便。

`@[locale]` 显式声明某个文本的语言。这不会影响 Ktory 的默认语言。然而，如果您在剧本中使用 `@[locale]` 声明了任意文本的语言，`@defaultLang` 声明就是必须的。此时，没有 `@[locale]` 标注的其它文本会被自动视为 `@defaultLang` 的语言。

目前，`@[locale]` 声明已经支持了对白和选项标签。

```ktory
@defaultLang: zh

爱丽丝
:
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

## 人名与别名

角色的人名在剧本中高频出现，因此，Ktory 为角色名及其译名的定义设计了一种特殊的语法。

`@speaker` 声明定义角色名、译名、与别名，大小写严格。在文件头定义后，Ktory 将根据请求语言自动返回正确的、已定义的角色译名。其中，别名为可选项，不作为输出语言，仅可作为调用输入。

```ktory
@speaker en="Chtholly" | ja="クトリ" | cn="珂朵莉"

Chtholly:  // 此处无论是写 Chtholly, クトリ, 珂朵莉，都会自动按请求语言返回正确的译名
  @en: You must leave now.
  @ja: 今すぐ離れて!
  @cn: 快走!
```

若希望定义别名，只需在 `@speaker` 声明后加上别名与冒号即可。但是，`@speaker alice:` 这类没有任何非空显示名的声明会报出行号并停止加载。

```ktory
@speaker ktr: en="Chtholly" | ja="クトリ" | cn="珂朵莉"

ktr:……  // 可以使用别名调用
```

这一语法也支持定义角色在演出时使用的默认修饰符。


## 输出与回退

Ktory 优先返回请求语言，在请求语言缺译时，将会尝试返回 `defaultLang` 的语言文本。如果两者都没有有效译文，Ktory 会按该节点已有译文顺序选择第一个有效译文，并发出带源码位置的 `Warning`。

`TextPayload.ActualLanguage` 描述实际正文语言，`RequestedLanguage` 保留请求语言。例如，当请求英文 `en` 且 `defaultLang` 为 `zh` 时，如果某个文本在 `en` 上缺译，则该文本的 `ActualLanguage` 为 `zh`，而 `RequestedLanguage` 仍然是 `en`。此时，Ktory 不会将 `zh` 文本标记为英文。

当前 `ChoiceOption` 提供本地化的 `Label`、`RequestedLanguage` 和 `ActualLanguage`，每个选项分别报告真实语言。

`TextPayload.SpeakerActualLanguage` 单独报告已声明说话人名字的实际语言；它可能与正文不同，未声明说话人则为空。

正文、选项和已声明名字都遵循上述第三语言回退规则，不合成缺译占位符。

## 播放中切换语言

```csharp
player.SetLanguage("en");
```

`SetLanguage(locale, token)` 是会话设置：只要捕获的会话仍有效，同会话已换拍或自然结束后仍可修改语言；旧会话的请求会被忽略。

切换会更新当前文本、说话人或菜单标签，不重新分发演出修饰符、不清空会话记录。宿主随后刷新显示；若使用 `PresentationController`，调用其 `RefreshLanguage()` 处理当前呈现时序，不要为切换语言额外调用 `Step()`。

同文件维护适合独立写作与翻译。然而，Ktory 不阻止您离散地维护多个语言 Ktory 剧本文件，
