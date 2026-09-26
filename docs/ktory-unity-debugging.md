# Unity Debugging 接入与验收

从 **Window → Ktory → Debugging** 打开窗口，在 Play Mode 选择已注册的真实会话。此版本需要已有播放器实现 `IKtoryDebugTarget`；Package 不搜索私有字段、不创建第二个 Sequencer，也不接管游戏 Update。

窗口负责展示执行状态、标准呈现策略和诊断记录；动画、资源、自定义修饰符及额外输入门禁由项目持有，通过适配接口提供。

## 连接现有播放器

若项目使用 asmdef，在宿主程序集引用 `Ktory.Core` 和 `Ktory.Unity`。后者是小型 Unity 接入程序集；Core 继续保持 `noEngineReferences: true`。调试适配接口、注册表以及项目的引用代码须放在 `#if UNITY_EDITOR` 下，正式构建不含注册表、日志和调试 UI。

在**已创建真实 Sequencer / PresentationController，尚未 Start**时调用：

```csharp
#if UNITY_EDITOR
registration = Ktory.Unity.Debugging.KtoryDebugRegistry.Register(this);
#endif
```

`this` 须实现 `IKtoryDebugTarget`。可直接参考并导入 Package Manager 的 **Debugging host probe** sample：[`DebuggableKtoryProbe.cs`](../src/Ktory.Unity/Samples~/Debugging/DebuggableKtoryProbe.cs)。这是可编译的日志宿主示例，不是完整对话框。已有项目应在自己的播放器上实现接口，复用已有 Sequencer，不再启动这个示例播放器。

接口成员的关键约定：

- `Owner` 是此会话所属的存活 Unity 对象；`DisplayName` 区分场景中的实例。`SourceAsset` 指向导入的 `.ktr` TextAsset；动态脚本没有资源时可以为空，源码定位不可用。
- `Sequencer` 是真实会话，注册期间不替换。替换前 Dispose，替换后重新 Register；每次新 Play 会话重新注册。`Presentation` 返回当前真实控制器；自定义呈现方案返回 null，窗口明确显示标准计时不可用。
- 所有 getter 和 `CollectDebugInfo` 都只能读取状态，不能求值推进、发送标签或更新时钟。
- `RequestAdvance(id)` 走游戏原始点击路径，先检查宿主门禁及呈现 ID，再调用真实控制器的 `HandleUserClick()` 或项目对应实现。窗口不直接调用 `Step()`。快显与自动计时继续遵循现有规则，包括显示完成后零延时 AUTO 的行为。
- `SubmitChoice(optionId, id)` 走游戏合法提交路径。`SubmitChoice` 已到分支第一拍，之后仅建立呈现和重绘，不能再 Step。项目仍负责异步／排队输入的会话世代检查。
- `SetLanguage(null)` 表示跟随 `DefaultLanguage`，其余值保存为请求语言。调用 `Sequencer.SetLanguage`，再调用 `Presentation.RefreshLanguage(false)` 并重绘正文和选项。不要 Setup、Step、补发标签或重新消费选项。`false` 保持正在显示的阶段；项目按自己的打字机进度刷新文本。
- `Restart()` 复用项目已有重启操作，保留入口及语言策略，撤销旧会话的计时、异步演出和排队输入；新建控制器可取消其残留自动批次。显式重启会重新触发剧情标签和宿主副作用。
- `InputBlockReason` 在宿主不接受输入时返回可读原因（例如“资源仍在加载”）。这个值同时用于窗口说明与禁用按钮；实际操作方法也必须自行检查，不能依靠窗口保证合法性。

可选项目扩展示例（同样置于 `#if UNITY_EDITOR`）：

```csharp
public void CollectDebugInfo(
    System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, string>> rows)
{
    rows.Add(new System.Collections.Generic.KeyValuePair<string, string>("Camera", cameraState));
    rows.Add(new System.Collections.Generic.KeyValuePair<string, string>("Host AUTO", autoMode.ToString()));
}
```

上例的 `cameraState`、`autoMode` 由项目提供；播放器同时实现 `IKtoryDebugInfoProvider`。Package 不解释这些值，也不为自定义修饰符编造“已完成”状态。项目输入／表现异常可通过 `KtoryDebugRegistry.ReportError(registration, exception)` 写入记录。

## 显示与生命周期

语言栏分别显示剧本默认、当前请求、正文实际语言和回退；选项各自显示实际语言。正文实际语言不代表 Speaker 名字一定使用同一语言。没有本地化文本的指令拍不伪报输出语言。取消“Follow script default”后可输入 locale，确认输入即时刷新。

文字显示中，最低停留与自动延迟展示的是**显示结束后才开始的时长**；显示完成后才展示倒计时。两者并行，自动推进等待两者都满足。`.skippable(false)` 显示“等待自然显示完成”；宿主未使用共享控制器时，不推测其私有计时和 AUTO。

标签记录表示 Core 派发尝试，行号指向所属节点或选项锚点，不代表项目演出已经成功或结束；出错不回滚已有副作用。

历史在注册后开始收集；窗口开关、切换选中实例、复制和清空不会推进。全部实例合计最多 2000 条，单条消息最多 4096 字符，超出丢弃最旧记录。日志支持类别及实例筛选、复制当前筛选结果、点击定位和清空全部；已销毁实例的记录在本次 Play 会话内可用“All players”查看。退出 Play Mode 或重新编译清空历史及订阅。

宿主在 OnDisable/OnDestroy 或替换会话时 Dispose 注册令牌。Editor 也会定期清理已销毁 Owner，以及退出 Play Mode 的注册，即使禁用了 Domain Reload。若同时禁用 Scene Reload，项目仍须在每次实际会话初始化时重新注册，不能只依赖不再执行的 Start；窗口不会重新启动项目会话。

源码定位使用 Unity 的 [AssetDatabase.OpenAsset(asset, line)](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/AssetDatabase.OpenAsset.html)，具体跳行支持取决于项目关联的外部编辑器。注册清理遵循 Unity 的 [Domain Reloading 生命周期说明](https://docs.unity3d.com/2021.3/Documentation/Manual/DomainReloading.html)。

## 验证边界

本机 .NET 回归覆盖诊断旁观无副作用、语言刷新、语言回退、选项消费、标签位置、调用返回、循环快照、异常隔离和计时数据；这些不等于 Unity 导入／窗口实机验收。

UPM 带有 `Tests/Editor/DebuggingTests.cs`。在游戏 `Packages/manifest.json` 的 `testables` 加入 `com.ktory.unity` 后，在 Unity Test Runner 的 EditMode 执行；测试会进入和退出 Play Mode。另用真实项目按下表复验并记录源提交 S、生成包提交 P、Unity 版本、平台、入口与操作序列：

| 用例 | 预期 |
| --- | --- |
| 两个已运行播放器，反复开关窗口和切换实例 | 位置、标签数、计时不因窗口变化；按钮只作用于所选实例 |
| 打字中快显锁、`.wait(2).next(5)`、`#AUTO.wait` | 原始输入被正确丢弃／快显／推进，剩余时间与游戏一致 |
| 打字、停留、选择三个阶段切语言；请求缺译语言；跟随默认 | 当前节点及 ID 不变、标签不重放、消费不变、真实回退，固定计时不重启 |
| 宿主额外门禁、自定义呈现（Presentation=null） | 显示项目原因或计时不可用，操作仍经过游戏门禁 |
| 提交一次性选项并循环重入；调用／返回／break | 首拍停顿、选项消费、栈和循环计数与实际执行一致 |
| 销毁、场景切换、重启、退出／再次进入 Play，关闭 Domain Reload | 无旧实例和旧订阅；项目正常重启，新会话可重新注册 |
| 超过 2000 条日志，筛选、复制、清空 | 有界保留；复制含时间、实例和资源／行号 |
| 构建正式 Player | 不含 Editor UI、IKtoryDebugTarget 或注册与日志代码 |

当前工作区没有 Unity Editor；本次未声称上述 Unity 测试、窗口绘制、实际跳行、场景切换或 Player 构建已通过。

2026-09-26 本机证据：源码基线 `2a5e55b` 加本次未提交改动，macOS arm64 / .NET SDK 9.0.200。`dotnet test tests/Ktory.Core.Tests/Ktory.Core.Tests.csproj -m:1 -nr:false -p:UseSharedCompilation=false` 通过 97 项；`dotnet build Ktory.sln --no-restore -m:1 -nr:false -p:UseSharedCompilation=false` 通过且无警告；`website` 的 `pnpm build` 通过（存在未找到 docs/404 的内容警告）。临时目录检查了预期包布局、四个程序集边界、Core 来源和调试条件编译保护；本机无 PowerShell，未运行发布脚本，也未生成或发布可用作 P 的包提交。
