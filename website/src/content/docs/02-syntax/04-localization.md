---
title: 多语言本地化 (I18N)
description: 原生单文件多语言变体与无副作用即时热切换
sidebar:
  order: 4
---

> 本页遵循 [Ktory 设计原则](/01-overview/03-design-principles/)，实现边界与长期目标以该原则及其权威文档为准。

# 同文件多语言

`@defaultLang` 指定文件默认语言，缺省为 `zh`。没有 `@locale` 标注的正文归入该默认语言。对白、说话人显示名和选项标签可在同一文件维护。

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

## 输出与回退

每句优先选择请求语言，缺译时回退 `defaultLang`。两者都没有有效译文时，按该节点已有译文顺序选择第一个有效译文，并发出带源码位置的 `Warning`。`TextPayload.ActualLanguage` 描述实际正文语言，`RequestedLanguage` 保留请求语言。例中请求英文时，选项分支里的中文对白会回退为 `zh`。不要将回退文本标成英文。

`@speaker` 为显示名字建立大小写敏感的反查别名。示例中的 `alice`、`爱丽丝`、`Alice` 与 `アリス` 指向同一名字定义；未声明的说话人按字面显示。当前声明只提供名字与别名，不自动绑定立绘或音频。产品目标允许名字与可选默认呈现分别维护、建立关联并逐句覆盖；共享定义和资源绑定语法尚未设计，见[设计原则](/01-overview/03-design-principles/)。

当前 `ChoiceOption` 提供本地化的 `Label`、`RequestedLanguage` 和 `ActualLanguage`，每个选项分别报告真实语言。菜单的显示标签也不能替代选项 `Id`。`TextPayload.SpeakerActualLanguage` 单独报告已声明说话人名字的实际语言；它可能与正文不同，未声明说话人则为空。正文、选项和已声明名字都遵循上述第三语言回退规则，不合成缺译占位符。`@speaker alice:` 这类没有任何非空显示名的声明会报出行号并停止加载；正文／选项空声明不由此新增统一规则。

## 播放中切换语言

```csharp
player.SetLanguage("en");
```

`SetLanguage(locale, token)` 是会话设置：只要捕获的会话仍有效，同会话已换拍或自然结束后仍可修改语言；旧会话的请求会被忽略。

切换会更新当前文本、说话人或菜单标签，不重新分发演出修饰符、不清空会话记录。宿主随后刷新显示；若使用 `PresentationController`，调用其 `RefreshLanguage()` 处理当前呈现时序，不要为切换语言额外调用 `Step()`。

同文件维护适合独立写作与翻译；当前阶段不承诺译文自动生成、提取回填或跨文件同步。
