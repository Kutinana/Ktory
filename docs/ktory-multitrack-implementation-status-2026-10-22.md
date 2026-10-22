# Main／辅助实现与验证

更新日期：2026-10-22。基于 `28ac99eb5d491df7462f9e73b5f80f761c4c69d6` 的当前工作区，包含尚未提交的修改；本文件只记录当前实现和验证，不保留被替代的设计。

现行契约见[实现规格](ktory-implementation_v1.md)，产品目标见[设计宪章](ktory-design-charter.md)，真实未决事项见[工程问题](ktory-engineering_v1.md)。

## 当前实现

| 行为 | 实现与验证 |
| --- | --- |
| 唯一 Main 独占输入及辅助管理，辅助按 Core 控制流／at 自动执行 | `PlayheadExecutionTests` 覆盖选择忽略、具名每会话一次、匿名每次创建、停止与等待、独立调用上下文、执行预算和末句通知顺序 |
| track 默认创建并排队，初始化依赖显式等待 | `#.track(source="Init", name="init").await` 与匿名形式已实现；等待本次对应实例执行结束，不默认阻塞所有 track。`TrackParserTests`／`PlayheadExecutionTests` 覆盖误用诊断、匿名重复创建、具名复用及恢复后不重播标签 |
| 可证明的初始化先用后建在加载时诊断 | 单纯排入辅助后立即引用尚未创建的钟给出 `.await` 建议；合法的 at 调度边界不误报。动态控制流仍由运行时校验实际状态 |
| 每个播放头只有一个当前语句 | 新 Content 替换该头前句，包括空冒号、普通指令输出和选择；显示位置不改变此规则。等待下一句 at／await 时尚未进入后句，前句可继续显示；EOF／停止不强制撤下末句 |
| duration 不阻滞 advance | 与 wait 使用相同计时起点；有效推进进入后句后旧句退出。旧 timer／淡出不能影响新句。Reader 阅读历史独立于当前呈现，不继续计时或语言刷新 |
| 主／副语言无副作用刷新 | 只刷新各头当前呈现及未被后句替换的末句；已释放内容不会复活，不重跑标签、剧情或计时 |
| Reader 两种受支持时钟 | 未指定 source 使用 Ktory 内置时钟；`source="music.position"` 使用 Reader BGM 播放头秒数。未知来源保持无值，不能点击模拟 |
| 外部源更新使用 sourceName | HTTP／WASM 与 Core 均按来源名采样；具名钟与匿名钟都可引用 music.position。批次中的 null 清除旧曲样本，停止／加载未就绪不沿用旧进度 |
| 真实 BGM 生命周期 | 暂停保持位置，同曲续播复用播放器；停止／换曲撤下旧播放器；旧错误回调不能复活旧曲，备用资源最多尝试一次 |

`GetRetainedPresentations()` 只保留每头当前显示来源，`ReleaseOutput(token)` 用于宿主撤下内容后释放。未提供同头多句保留接口。`OnOutput` 是有序交付，`LastOutputs` 仅为最近批次；输入仍以 Main 当前 `PresentationToken` 校验。

## 清理结果

已删除旧 `ParallelStep/LaneDefinition`、不可达遍历、`ScopeId`、旧 join 状态以及与单头一句冲突的多句保留扩展；保留旧语法的加载拒绝诊断。测试改为 `TrackParserTests` 和 `PlayheadExecutionTests`。

已直接删除过时原型、多轨讨论／整合稿、本地化复核稿、旧阶段状态及错误审计稿；scope、宪章和 engineering 只维护当前有效内容，没有建立历史附录或归档副本。

## 验证与使用边界

- Core／Parser／HTTP／WASM：**435 项通过，0 失败／0 跳过**。命令为 `dotnet test tests/Ktory.Core.Tests/Ktory.Core.Tests.csproj -m:1 -nr:false -p:UseSharedCompilation=false`；报告位于 `artifacts/test-results/multitrack/multitrack-final.trx`。
- Reader：**81 项通过**，包括真实播放器采样、暂停与换曲、旧异步回调、单句替换、duration、双语与过期输入。
- 解决方案构建成功，0 warning／0 error；三语门户 40 页及高亮检查通过。门户仍有既有的 docs → 404 内容项 warning。
- VS Code：**15 项测试通过**；重建 VSIX 并安装到隔离 profile 后，真实 WASM／Webview 集成通过，包含 `.await` 匿名初始化、未保存文件、语言刷新、旧输入隔离及错误恢复。包为 `artifacts/vscode/packages/ktory-vscode-0.2.2.vsix`，SHA256 为 `71423d7e759cdf858d07073b2a20d9542d892ced747b9a7227529bb4d12433ad`。
- `git diff --check` 与现行维护文档的本地链接检查通过。

本地 Reader 在 `http://localhost:5130/` 使用更新后的源码与样例。浏览器实际播放可见 Main 对白与按 BGM 位置进入的辅助歌词。`sample.ktr` 已将 Scarborough Fair 与歌词辅助头接到 BGM 播放头；歌词 cue 由作者手动校准，本轮验证位置驱动机制。

Reader 媒体命令在 Core 返回输出后执行。播放／换曲应放在 Main 的独立指令拍，下一拍再创建依赖新曲的辅助；同批创建辅助或在辅助内部换曲后立刻检查 at，可能仍消费旧曲样本。当前样例采用前一种顺序。完整媒体命令确认协议尚未提供。

VS Code 内置试读当前不播放音频，music.position 保持无值。Unity 调试与样例源码已同步，但没有 Unity 编辑器／实机验证。外部文件 URL 加载、事件信号、存档及远端发布不在本轮交付范围；未提交或 push。
