---
title: Main、辅助播放头与时钟
description: 执行顺序、输入权限、显示寿命与时钟门控
sidebar:
  order: 1
---

本页描述 2026-10-22 已确认的新契约；实现和验证正在进行，源码变更不代表线上 Reader 或已安装包已更新。

## 执行与显示分开

同一会话只有一个 Main，独占玩家输入；辅助播放头自动执行。章节不分主副：同一节可以由 Main 或多个辅助进入，身份随播放头保留。每个头有自己的位置、调用栈、循环和默认时钟。

Main 文本／空 `:` 默认等待有效输入；普通独立 `#` 默认自动。玩家点击先经过呈现策略，可能只快显文字，并不一定成为 Core 的 advance。辅助输出后直接继续至下一 `at` 或 Core 控制流边界，不等打印、阅读估时、wait／next／AUTO 或 duration。

```ktory
#.clock(name="song")
#.track(source="Lyrics", name="lyric")
: 请听这段旋律。
#.awaitTrack(name="lyric")
: 歌词执行已结束。
-> end

=== Lyrics ===
  : 第一行。 .at(value=1, clock="song").duration(seconds=1)
  : 第二行。 .at(value=3, clock="song")
```

没有 at 的辅助文本可以连续输出，每个头最终只留下末句，与显示位置无关；这是合法行为。过去的 at 也逐个正常执行副作用，不按效果类型自动丢弃。预算限制防止零时长循环无限执行。

## 辅助的创建、停止与等待

只有 Main 可 `track`、`stopTrack`、`awaitTrack`；辅助遇到这些操作直接忽略。辅助选择整体跳过容器、分支与标签，不修改一次性消费。Main 选择规则保持。

`#.track(source="Lyrics", name="lyric")` 创建具名实例。name 可省略，目标节名不自动成为实例名；多个实例可以执行同一节。具名只有一个声明位置，同声明每会话只创建一次，结束后重遇不重启。匿名声明每次执行创建新实例，无法从外部点名 stop／await。

track 默认只创建并排入调度，不保证初始化代码已执行。Main 依赖初始化时，用 `#.track(source="Init", name="init").await` 等待目标结束，或直接在 Main 完成共享声明。匿名 `#.track(source="Init").await` 等待本次实例；`awaitTrack(name=...)` 用于等待先前启动的具名辅助。`.await` 无参数，必须紧邻同一普通 # 锚点的 track；默认 track 仍可并发。

`#.stopTrack(name="lyric")` 停止运行实例；未创建或已停止为无操作，不预取消未来创建。`#.awaitTrack(name="lyric")` 等到该实例停止，不能用 advance 绕过；已停立即继续，尚未创建给诊断而不等待未来创建。不存在名字提前诊断。

Main 结束就结束会话并停止辅助，不默认等所有轨；需要等则显式 await。停止执行不强制清屏。辅助最后一句 10 秒输出并到 EOF，即在 10 秒完成，显示可以留到 12 秒；末句先于结束通知。

## 时钟与 at

- `#.clock()` 新建本头匿名默认钟。新头默认空，不继承。匿名失去默认后回收，不存在可恢复的默认栈。
- `#.clock(name="song")` 声明全局具名钟，不自动设默认。全局同名只能一个声明位置；首次执行创建、以后复用至会话结束，不自动重置。
- `#.defaultClock(name="song")` 选择已创建具名钟，不创建或重置。jump／call 不自动换，return 不恢复旧默认。
- `#.resetClock()` 重置当前默认；`#.resetClock(name="song")` 指定具名。ID 保持不变，内置归零，外部动作交宿主。

`at(value=6)` 使用当前头默认钟，`at(value=6, clock="song")` 指定全局钟。只有执行已经抵达入口且已知值 >= 阈值才进入；它不替上一句发送 advance。Main 3 秒确认后可到门口等到 6 秒；不确认则 6 秒不会抢占。门控先于同锚点所有标签，不能靠同锚点的 clock 声明满足自己的首次 at。

`source` 是宿主解释的不透明数值来源：`#.clock(name="song", source="music.position")` 本身不播放音乐。空串与省略等价，宿主可接管否则内置秒计时器。外部值只需有限，单位自定；暂时无值继续等待，其他可运行头继续。后退不撤销已执行剧情。getter／reset 异常留后台诊断，不猜值或绕过门控。

Web／WASM Reader 明确把 `music.position` 解析为当前 BGM 播放器以秒为单位的实际播放头位置。先用 `.bgm(...)` 播放，再引用该来源；暂停和跳转跟随真实播放器，停止、加载未就绪或错误时无值。其他未定义外部来源不模拟，点击不能代填时间或越过门控。VS Code 内置试读当前不播放音频，因此该来源保持无值。

播放／换曲应先由 Main 的独立指令完成，再在下一锚点启动依赖新曲的辅助；同锚点或辅助内换曲没有媒体确认边界，同批可能仍读到上一曲样本。

## 输出与接入

宿主订阅 `Sequencer.OnOutput`，按 `StoryOutput.Sequence` 顺序处理 Content、LanguageRefresh、TrackEnded、SessionEnded。输出含源头 ID、可选名字、IsMain 和内容 Token；最后活动头快照不能代替交付事件，否则会漏掉同批结束辅助的末句。`LastOutputs` 只保留最近操作批次。

`GetRetainedPresentations()` 只保留各头当前显示，包括未被后句替换的已结束辅助末句；撤下内容后调用 `ReleaseOutput(token)`。显示位置由标签／宿主配置决定，不自动把实例名映成 UI。同头后句进入就撤下前句，不论显示器是否相同；空 :、指令输出和选择也会替换前句。等待后句 at／await 时可保留前句。旧 duration／淡出回调不能清新句，显示还在也不恢复旧输入权限。

输入只给 Main 当前身份；Main 的打印完成由呈现控制器处理，不能用它阻塞辅助。时钟 delta 每帧只提交一次，采用多轨呈现包装驱动时不再额外 UpdateClocks。语言刷新不推进、不重放标签或重新采样来源，已结束辅助的保留显示也可刷新。

## 控制流与迁移

jump 清栈，call 压栈，所有头都须显式 return 才返回。Main 命名节 EOF／end 恢复源码后根节点；辅助命名节结束自身，不寻找根节后续。

旧 `#parallel/#lane` 需显式迁移为章节、track 和所需 await，不保留旧运行语义。嵌套创建、循环重复 join 可能需重新组织，不能保证机械等价转换。外部文件必须显式 URL，但格式与引擎加载协议延期；不要把 source 扩展名猜成文件路径。事件信号、存档、race 和暂停协议仍是独立议题。
