---
title: 本地化
description: 原生单文件多语言变体与无副作用即时热切换
sidebar:
  order: 4
---

本节介绍如何在 Ktory 中实现单文件的多语言编写。

## 同文件多语言

`@defaultLang: <locale>` 声明文件默认语言，用于未标注文本归属和缺译回退，不是运行时主／副语言开关。可以省略：存在 `@locale` 变体时，当前解析器为兼容性默认使用 `zh`；无任何多语言声明时视为未本地化，实际语言为 `null`。多语言剧本建议显式声明，以免依赖隐式默认值。

`@locale:` 标注具体文本的语言；它不要求每个文件都显式写出 `@defaultLang`。正文、选项及名字的解析规则以实现规格为准。

目前，`@[locale]` 声明已经支持了对白和选项标签。

```ktory
@defaultLang: zh

爱丽丝:
  @zh: 你好，旅行者。
  @en: Hello, traveler.
  @ja: こんにちは、旅人さん。
  .expression(smile)

#choice
  * [@zh: "继续"]
    [@en: "Continue"]
    [@ja: "続ける"]
    爱丽丝: 我们出发吧。
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

## 主／副语言结构化输出

`RequestedLanguage` 是主语言，`SecondaryLanguage` 是可选副语言；文件 `defaultLang` 仍负责未标注文本归属及主语言回退。主语言继续使用上文规则，副语言**只选精确译文，不回退**。

`TextPayload.Text`、可空的 `TextPayload.SpeakerName` 与 `ChoiceOption.Text` 分别包含 `Primary` 和可空的 `Secondary`。各份结果有 `Content`、`RequestedLanguage`、`ActualLanguage`；旧 `Content`、`Speaker`、`Label` 等属性映射主语言。

副语言没有该字段的译文时为 null；副语言与该字段主语言的实际语言相同也省略。两种语言文字相同仍保留；正文与名字独立判断，因此可出现只有名字具备副语言的情况。未本地化值和未声明名字不伪造语言身份；未标注值只有在文件默认语言已知时才可作为该语言的精确译文。

```csharp
sequencer.Start(requestedLocale: "zh", secondaryLocale: "en");
sequencer.SetSecondaryLanguage("ja");
presentations.RefreshLanguage(secondaryOnly: true);
sequencer.SetSecondaryLanguage(null); // 关闭副语言
```

这里 `presentations` 为 `MultiTrackPresentationController`；单轨 `PresentationController` 使用 `RefreshSecondaryLanguage()`。`SetLanguage(locale)` 保留副语言设置，`SetLanguageOutput(primary, secondary)` 可同时设置两者。

副语言变化只刷新尚保留的显示内容与 Main 当前文本／选择，不推进、重求选项条件、消费选项或重放标签，也不重新打印或重置计时。已结束辅助和已结束会话的保留内容仍可切语言；已隐藏内容不会因此重新出现。阅读预估仍只按主语言实际内容计算；切换主语言继续使用原有进度保留规则。renderer 自行显示、隐藏或排版两份内容，无需把它们拼成一个字符串。

每个播放头只有一条当前语句。后一句进入时结束前一句显示，与显示位置无关；空 : 同样替换前句。等待后句 at／await 时尚未进入后句，EOF／stop 也没有后句，因此原内容可按自身寿命继续显示并切语言。`LanguageRefresh` 按 token 更新当前内容，不重跑路由标签、重启 duration 或复活已撤下的句子。宿主隐藏当前内容后调用 `ReleaseOutput(token)`；阅读历史与当前呈现另行维护。

## 与纯文本围栏组合

`@locale:` 写在围栏外，每个版本独立包围需要原样显示的片段；详见[纯文本片段与区块](/02-syntax/01-anchor-decorator/#纯文本片段与区块)。围栏内的 `@en:` 仅是文字，不声明新译文。选择普通或纯文本写法不改变回退、副语言缺译省略或切语言时的副作用规则。

`Primary`／`Secondary` 结果还可含 `Segments`，逐段提供 `Content` 与 `IsLiteral`。片段随实际选中的版本一起输出，不能给整拍或所有语言共用一个纯文本开关。
