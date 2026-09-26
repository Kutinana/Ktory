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

每句优先选择请求语言，缺译时回退 `defaultLang`。`TextPayload.ActualLanguage` 描述实际正文语言，`RequestedLanguage` 保留请求语言。例中请求英文时，选项分支里的中文对白会回退为 `zh`。不要将回退文本标成英文。

`@speaker` 为显示名字建立大小写敏感的反查别名。示例中的 `alice`、`爱丽丝`、`Alice` 与 `アリス` 指向同一名字定义；未声明的说话人按字面显示。这不是立绘或音频角色绑定系统。

当前 `ChoiceOption` 提供本地化的 `Label`，没有每项 `ActualLanguage` 字段。菜单的显示标签也不能替代选项 `Id`。默认语言文本本身缺失等边界，应依当前核心诊断与规范核验，不从常规回退规则推导额外保证。

## 播放中切换语言

```csharp
player.SetLanguage("en");
```

切换会更新当前文本、说话人或菜单标签，不重新分发演出修饰符、不清空会话记录。宿主随后刷新显示；若使用 `PresentationController`，调用其 `RefreshLanguage()` 处理当前呈现时序，不要为切换语言额外调用 `Step()`。

同文件维护适合独立写作与翻译；当前阶段不承诺译文自动生成、提取回填或跨文件同步。
