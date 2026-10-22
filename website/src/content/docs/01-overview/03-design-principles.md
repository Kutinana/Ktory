---
title: 设计原则与扩展目标
description: 设计原则与扩展目标
sidebar:
  order: 3
---

Ktory 用文本组织叙事控制流、本地化、数值条件与呈现意图。本页按 2026-10-22 正式契约更新；新 Main／辅助实现和验证进行中，不能据此推定已发布。

- 唯一 Main 拥有玩家输入和辅助 create／stop／await。章节角色中立，各头独立维护位置、调用、循环与默认钟；同节可多实例，辅助不再创建辅助，也不执行选择分支／标签或消费选项。
- 辅助输出后立即继续至 at／Core 边界，不等打印、wait／next、AUTO 或阅读估时。Main 结束停止辅助，不默认 join，须等待时显式 await。
- 具名时钟全局唯一声明，首次创建后复用；匿名钟局部且只经当前默认访问，失默认即回收。reset 保持 ID。at 只限制抵达入口，不替前句生成 advance。source 不透明且不播放资源，暂无值可等待。
- 普通独立 # 默认自动，Main 文本／空 : 默认手动。每头一次只有一条当前语句，后句进入即撤前句，与显示位置无关；空 : 同样结束前句显示。duration 与 wait 同起点，但不阻滞 advance。
- 输出路由由标签／宿主配置决定，不自动实例名映 UI。末句先于结束通知，执行结束不强制清屏；旧显示回调不影响新内容，旧显示也不保留旧输入权。
- 主语言回退、副语言精确译文及纯文本片段保持。语言刷新不推进、不重放标签，不重新计时或采样来源；已结束辅助的保留显示也能刷新。
- 旧 parallel/lane 需显式迁移，嵌套／反复 join 未必机械等价。外部文件须显式 URL，格式与引擎加载协议延期。事件信号、存档、race 和暂停协议独立规划。
- 实现、测试、发布与 Unity 实机分别记录；可发现错误前移编译／检查，未知未来外部状态不能静态保证，执行预算与后台诊断仍保留。

完整权威文档：[设计宪章](https://github.com/Kutinana/Ktory/blob/main/docs/ktory-design-charter.md)、[scope](https://github.com/Kutinana/Ktory/blob/main/docs/ktory-scope_v1.md)、[实现规格](https://github.com/Kutinana/Ktory/blob/main/docs/ktory-implementation_v1.md)。
