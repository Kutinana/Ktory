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

`this` 须实现 `IKtoryDebugTarget`。可直接参考并导入 Package Manager 的 **Debugging host probe** sample：[`DebuggableKtoryProbe.cs`](Samples~/Debugging/DebuggableKtoryProbe.cs)。这是可编译的日志宿主示例，不是完整对话框。已有项目应在自己的播放器上实现接口，复用已有 Sequencer，不再启动这个示例播放器。

示例使用 `MultiTrackPresentationController` 管理同一 Sequencer 的 Main 呈现策略，保留调试窗口的同步接口，并为游戏排队输入提供完整令牌重载。创建 Main 打字机任务时调用 `CapturePrintingCompletion(laneId)` 得到捕获管理器与原呈现令牌的完成回调；任务结束后调用这份回调，不能临时读取新会话的令牌。辅助输出通过 `OnOutput` 接收，显示及 duration 独立管理，不等待打印完成或建立辅助推进控制器。无参数兼容接口选择 Main 当前交互呈现。`OnDisable` 与重启先调用 `InvalidateSession()`，使仍持有旧对象的闭包失效，再清理宿主订阅。

接口成员的关键约定：

- `Owner` 是此会话所属的存活 Unity 对象；`DisplayName` 区分场景中的实例。`SourceAsset` 指向导入的 `.ktr` TextAsset；动态脚本没有资源时可以为空，源码定位不可用。
- `Sequencer` 是真实会话，注册期间不替换。替换前 Dispose，替换后重新 Register；每次新 Play 会话重新注册。`Presentation` 返回当前真实控制器；自定义呈现方案返回 null，窗口明确显示标准计时不可用。
- 所有 getter 和 `CollectDebugInfo` 都只能读取状态，不能求值推进、发送标签或更新时钟。
- `RequestAdvance(id)` 走游戏原始点击路径，先检查宿主门禁及呈现 ID，再调用真实控制器的 `HandleUserClick()` 或项目对应实现。窗口不直接调用 `Step()`。快显与自动计时继续遵循现有规则，包括显示完成后零延时 AUTO 的行为。
- `SubmitChoice(optionId, id)` 走游戏合法提交路径。`SubmitChoice` 已到分支第一拍，之后仅建立呈现和重绘，不能再 Step。上述仅拍号调试接口用于窗口同步调用；异步／排队的游戏输入须在创建时捕获完整会话／呈现令牌，不能把旧拍号配上执行时的当前会话来冒充有效输入。
- `SetLanguage(null)` 表示跟随 `DefaultLanguage`，其余值保存为请求语言。调用 `Sequencer.SetLanguage`，再调用 `Presentation.RefreshLanguage(false)` 并重绘正文和选项。不要 Setup、Step、补发标签或重新消费选项。`false` 保持正在显示的阶段；项目按自己的打字机进度刷新文本。
- `Restart()` 复用项目已有重启操作，保留入口及语言策略，撤销旧会话的计时、异步演出和排队输入；每次 Start 生成新会话标识，新建控制器可取消其残留自动批次。显式重启会重新触发剧情标签和宿主副作用。
- `InputBlockReason` 在宿主不接受输入时返回可读原因（例如“资源仍在加载”）。这个值同时用于窗口说明与禁用按钮；实际操作方法也必须自行检查，不能依靠窗口保证合法性。

可选项目扩展示例（同样置于 `#if UNITY_EDITOR`）：

- `IKtoryLaneDebugTarget` 提供 `GetPresentationController(laneId)`、`RequestAdvance(token)` 和 `SubmitChoice(optionId, token)`，窗口用它显示 Main 计时并把操作送回宿主。既有 `IKtoryDebugTarget` 无需修改即可继续使用；辅助与保留显示始终只读，不产生推进或选择按钮。
- `IKtorySecondaryLanguageDebugTarget.SetSecondaryLanguage(locale)` 支持副语言编辑；null 关闭。先更新 Sequencer，再调用管理器 `RefreshLanguage(secondaryOnly: true)`，保留所有轨道的打印阶段与计时。主语言变化调用 `RefreshLanguage(completePrintingOnLanguageSwitch: false)`，按既有主语言规则保持阅读进度。
- `MultiTrackPresentationController.Update(deltaTime)` 统一采样时钟并在有界批次内处理 Main 推进，辅助由 Core 独立执行；不要再逐个调用子控制器的 `Update`，也不要重复调用 `UpdateClocks`。宿主提交选项或外部更新位置后，调用 `Synchronize()` 建立新呈现，未变化呈现的计时保持不变。
- 示例的 `BindClock(name, Func<double>)`／`UnbindClock(name)` 注册／移除 Core 的宿主来源，可传入 `() => audioSource.time` 或其他浮点值；例如注册 `music.position` 后由脚本 `#.clock(name="bgm", source="music.position")` 引用。绑定本身不声明时钟，脚本内置时钟须用 `.clock` 明确声明后才按帧 delta 累计。绑定不会播放音频，也不会回滚已执行剧情。`GetClocks()` 与 `GetPendingClockGates()` 均只读，不采样 getter；可检查声明、来源、当前位置和等待阈值。
- `BindClock(name, source, reset)` 可选传入宿主重置动作。首次声明与同一声明的再次执行均不调用它；显式执行 `#.resetClock(name="bgm")` 时才调用对应来源的动作，没有动作则继续使用来源的原始值。重置不更换时钟 ID；有重置动作时旧采样失效，窗口可能暂时显示尚未采样，下一次门控或帧更新会读到新值。Probe 只转接动作，具体音乐播放位置由项目控制。

额外世界／演出状态仍通过 `IKtoryDebugInfoProvider` 提供：

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

包含纯文本围栏的正文／选项版本通过各语言结果的 `Segments` 保留片段边界。游戏渲染器应将 `IsLiteral=true` 的片段按字面显示并保留换行、空白，其他片段沿用已有富文本处理；不要仅把拼接后的 `Content` 再交给富文本组件。主／副语言及回退结果各自携带片段，不能使用整拍共用的纯文本开关。调试窗口继续以文本查看 `Content`，它的显示不代替游戏内富文本适配验收；具体语法及载荷见[实现规格](ktory-implementation_v1.md)。

窗口读取 `GetActivePresentations()` 显示 Main 当前输入呈现，再补充 `GetRetainedPresentations()` 中令牌未重复的保留显示，包括辅助末句、Main 空拍／选择前的正文以及会话自然结束后的文本。各份正文与名字分别显示主／副语言及来源实例；保留显示标为只读，不提供输入按钮或失效的推进计时。显示撤下后宿主可调用 `ReleaseOutput(token)`，使其不再参与保留观察与语言刷新。全局语言栏分别显示剧本默认、主请求和可选副请求；取消“Follow script default”后可编辑主语言，副语言留空即关闭。每个字段报告自身实际语言，不用正文语言代替名字语言。指令拍不伪报输出语言。全局控制栈面板仅显示 Main，不能冒充辅助执行栈。

主语言的正文、选项和已声明名字在请求／默认语言都缺失时按已有有效译文顺序选取第三语言，报告 `Warning` 并保留真实语言。副语言只输出所请求语言的有效译文，缺译不回退；与主语言实际语言相同的字段省略，不同语言即使字符相同也保留。未本地化正文和未声明名字没有副语言输出；文件默认语言下的未标注文本仍属于该默认语言。`TextPayload.SpeakerActualLanguage` 继续投影主语言名字的实际语言。如 `@speaker alice:` 的空名字声明须加载时报错并指出行号；正文／选项空声明不据此新增规则。内置游戏求值器对未知条件 `Warning + false`，独立试读才显式启用宽容模式。

Main 文字显示中，最低停留与自动延迟展示的是**显示结束后才开始的时长**；显示完成后才展示倒计时。两者并行，自动推进等待两者都满足。`.skippable(enabled=false)` 显示“等待自然显示完成”；宿主未使用共享控制器时，不推测其私有计时和 AUTO。辅助执行不受 wait／next 影响；两类显示的 duration 都不参与推进门控。

标签记录保留 lane、scope 及源码行，表示 Core 派发尝试，不代表项目演出已经成功或结束；出错不回滚已有副作用。宿主订阅 `OnLaneTagsDispatched` 可定位所属轨道；不要同时消费它和兼容 `OnTagsDispatched` 而重复执行同一演出。

过期或重复操作仅记录后台 `InputIgnored` 并忽略，不中断剧情；不要将它作为玩家错误弹窗。`Warning` 用于第三语言回退、未知条件或未知容器等可恢复诊断。当前令牌有效但选项本身非法仍报错，不属于可忽略的过期输入。悬空修饰符属于加载期语法错误，须显示行号并停止加载。

历史在注册后开始收集；窗口开关、切换选中实例、复制和清空不会推进。全部实例合计最多 2000 条，单条消息最多 4096 字符，超出丢弃最旧记录。日志支持类别及实例筛选、复制当前筛选结果、点击定位和清空全部；已销毁实例的记录在本次 Play 会话内可用“All players”查看。退出 Play Mode 或重新编译清空历史及订阅。

宿主在 OnDisable/OnDestroy 或替换会话时先 `Sequencer.InvalidateSession()`，再 Dispose 注册令牌并清理自己的异步工作。仅 Dispose 调试注册并不会使 Core 会话失效。Editor 也会定期清理已销毁 Owner，以及退出 Play Mode 的注册，即使禁用了 Domain Reload。若同时禁用 Scene Reload，项目仍须在每次实际会话初始化时重新注册，不能只依赖不再执行的 Start；窗口不会重新启动项目会话。

源码定位使用 Unity 的 [AssetDatabase.OpenAsset(asset, line)](https://docs.unity3d.com/2021.3/Documentation/ScriptReference/AssetDatabase.OpenAsset.html)，具体跳行支持取决于项目关联的外部编辑器。注册清理遵循 Unity 的 [Domain Reloading 生命周期说明](https://docs.unity3d.com/2021.3/Documentation/Manual/DomainReloading.html)。

## 验证边界

本机 .NET 回归覆盖诊断旁观无副作用、语言刷新、语言回退、选项消费、标签位置、调用返回、循环快照、异常隔离和计时数据；这些不等于 Unity 导入／窗口实机验收。

UPM 带有 `Tests/Editor/DebuggingTests.cs`。在游戏 `Packages/manifest.json` 的 `testables` 加入 `com.ktory.unity` 后，在 Unity Test Runner 的 EditMode 执行；测试会进入和退出 Play Mode。另用真实项目按下表复验并记录源提交 S、生成包提交 P、Unity 版本、平台、入口与操作序列：

| 用例 | 预期 |
| --- | --- |
| 两个已运行播放器，反复开关窗口和切换实例 | 位置、标签数、计时不因窗口变化；按钮只作用于所选实例 |
| 打字中快显锁、`.wait(seconds=2).next(seconds=5)`、`#AUTO.wait` | 原始输入被正确丢弃／快显／推进，剩余时间与游戏一致 |
| 打字、停留、选择三个阶段切语言；请求／默认双缺译；跟随默认 | 当前节点及 ID 不变、标签不重放、消费不变；第三语言回退有 Warning、正文／名字／选项各自报告实际语言，固定计时不重启 |
| Main 与辅助同时输出，Main 接受选择／点击，辅助按 at 继续；Main 显式 awaitTrack | 输入仅作用于 Main；辅助正文可只读检查；辅助停止后解除显式等待，日志保留来源 |
| 辅助停止或会话自然结束后切主／副语言 | 保留显示仍能刷新双语，原令牌不变，不重放标签或结束通知；无推进／选择按钮，已释放显示不复现 |
| 打字与停留期间开关副语言，正文缺译但名字有译文 | 副语言按字段缺译省略、不回退；当前轨迹、打印阶段和阅读计时不变 |
| `.at` 等待默认来源或宿主绑定浮点来源 | 窗口只读显示目标位置；来源达到目标才入拍，后退不回滚；实音频对齐另行验证 |
| 宿主额外门禁、自定义呈现（Presentation=null） | 显示项目原因或计时不可用，操作仍经过游戏门禁 |
| 提交一次性选项并循环重入；调用／返回／break | 首拍停顿、选项消费、栈和循环计数与实际执行一致 |
| 销毁、场景切换、重启、退出／再次进入 Play，关闭 Domain Reload | 无旧实例和旧订阅；项目正常重启，新会话可重新注册 |
| 重启后投递旧点击、旧选择、旧计时及打印完成；重复提交 | 新会话不推进、不重放标签；只有后台 InputIgnored，当前合法操作仍正常 |
| 游戏运行未知条件，再以独立试读模式运行同一脚本 | 游戏 Warning + false；试读显式宽容，已知条件不被忽略 |
| 超过 2000 条日志，筛选、复制、清空 | 有界保留；复制含时间、实例和资源／行号 |
| 构建正式 Player | 不含 Editor UI、IKtoryDebugTarget 或注册与日志代码 |

当前工作区没有 Unity Editor；本次未声称上述 Unity 测试、窗口绘制、实际跳行、场景切换或 Player 构建已通过。

2026-09-26 本机证据：源码基线 `2a5e55b` 加本次未提交改动，macOS arm64 / .NET SDK 9.0.200。`dotnet test tests/Ktory.Core.Tests/Ktory.Core.Tests.csproj -m:1 -nr:false -p:UseSharedCompilation=false` 通过 97 项；`dotnet build Ktory.sln --no-restore -m:1 -nr:false -p:UseSharedCompilation=false` 通过且无警告；`website` 的 `pnpm build` 通过（存在未找到 docs/404 的内容警告）。临时目录检查了预期包布局、四个程序集边界、Core 来源和调试条件编译保护；本机无 PowerShell，未运行发布脚本，也未生成或发布可用作 P 的包提交。
