---
title: Unity UPM 接入指南
description: 生成后的 Unity 包、真实核心 API 与已有宿主的接入边界
sidebar:
  order: 1
---

> 本页遵循 [Ktory 设计原则](/01-overview/03-design-principles/)，实现边界与长期目标以该原则及其权威文档为准。

# Unity UPM 接入

共享核心与 Unity 包分开组织。仓库的 `scripts/publish-upm.ps1` 将 `src/Ktory.Core` 源码、Unity 导入器与程序集定义合成 `com.ktory.unity`，发布到 `upm` 分支。

## 安装生成的包

将以下依赖合并到工程的 `Packages/manifest.json`，保留已有依赖：

```json
{
  "dependencies": {
    "com.ktory.unity": "https://github.com/Kutinana/Ktory.git#upm"
  }
}
```

`#upm` 是方便跟随更新的浮动分支。跨设备复现或固定版本时，将片段换成**生成后的 UPM 包提交或该包的标签**，例如 `#<upm-package-commit>`；不要拿源代码 `main` 提交当作生成包。`?path=/src/Ktory.Core` 不是当前发布包入口。

## 核心调用顺序

下面是可放入宿主项目的最小日志探针，展示真实 API 与提交顺序。它不是对话框或立绘实现，也不代替已有 Unity 接入；需要在 Inspector 赋予 `.ktr` 导入得到的 `TextAsset`。

```csharp
using Ktory.Core.Parser;
using Ktory.Core.Runtime;
using UnityEngine;

public sealed class KtoryCoreProbe : MonoBehaviour
{
    [SerializeField] private TextAsset script;
    private KtorySequencer player;
    public PresentationToken DisplayedToken { get; private set; }

    private void OnEnable()
    {
        player = new KtorySequencer(KtoryParser.Parse(script.text));
        player.OnTagsDispatched += tags =>
        {
            foreach (var tag in tags) Debug.Log($"[Ktory tag] {tag.Name}");
        };
        player.Start(requestedLocale: "zh");
        LogCurrent();
    }

    public void AdvanceDisplayedBeat(PresentationToken token)
    {
        player.Step(token);
        LogCurrent();
    }

    public void SubmitOption(string optionId, PresentationToken token)
    {
        player.SubmitChoice(optionId, token);
        LogCurrent();
    }

    private void OnDisable() => player?.InvalidateSession();

    private void LogCurrent()
    {
        DisplayedToken = player.CurrentPresentationToken;
        if (player.Status == ExecutionStatus.AwaitingChoice)
        {
            foreach (var option in player.CurrentChoice.Options)
                Debug.Log($"{option.Id}: {option.Label} / {option.CanSelect}");
        }
        else if (player.CurrentPayload != null)
            Debug.Log(player.CurrentPayload.Content);
    }
}
```

显示内容时保存完整的 `DisplayedToken`；选项按钮保存同一菜单的令牌和项 `Id`。按钮闭包应先用局部变量捕获令牌，不能在执行时读取已改变的属性。`SubmitChoice()` 已输出分支的第一拍，不要再为同一次选择调用 `Step()`。原始点击必须先由现有宿主完成快显、输入与外部推进条件判断。

## 接入配套表现时序

若宿主采用共享的 `PresentationController`，其接线如下；也可以自己实现相同契约。表中的 `token` 是创建显示任务或排队输入时保存的 `presentation.CurrentPresentationToken`，包含会话和拍号：

| 宿主动作 | 配套接口 |
| --- | --- |
| 绑定已创建的播放器 | `new PresentationController(player)` |
| `Start()` 或有效选择后建立呈现状态 | `SetupForCurrentBeat()`，随后读取当前输出 |
| 每帧更新时钟 | `Update(Time.unscaledDeltaTime)` |
| 原始普通点击 | `HandleUserClick(token)` |
| 打字机自然完成 | `NotifyPrintingFinished(token)` |
| 自动或点击推进后的重绘 | 订阅 `OnBeatChanged` |
| 快显当前文本 | 订阅 `OnFastForwardRequested`，只显示全文，不再次通知完成 |
| 当前语言变化 | `player.SetLanguage(...)` 后 `RefreshLanguage()`，并刷新显示 |

最低停留与自动推进分别处理：`.wait` 到期仅解除限制，期间点击不排队；`.wait.next` 才在停留结束后自动推进（AUTO 区域或宿主自动模式也可提供自动策略）。`.wait(s).next(t)` 从全文显示完成时同时计时，顺序无关。`HandleUserClick(token)` 接收原始用户输入；不要直接用 `Step()` 绕过呈现策略。`QueuedAdvance` 为兼容保留，始终为 `false`。

默认 `AutoStepSequencer == true` 时，控制器自己推进；不要在任一推进事件中重复 `Step()`。宿主管理推进时将其设为 `false`，只通过 `OnAdvanceRequestedWithToken` 捕获令牌，接受后 `Step(token)` 并建立下一拍；过期回调不得重新 Setup 当前拍或重置计时。资源与立绘映射、世界状态和外部演出限制仍由宿主持有。

每次 `Start()` 生成新的会话标识；关闭、替换或重启前调用 `InvalidateSession()`，并撤销旧输入、订阅与异步工作。过期／重复输入仅产生后台 `InputIgnored`，不弹出剧情错误；当前有效菜单的非法选项仍报错。无令牌或仅拍号的旧接口保留同步兼容，不能承诺跨会话隔离。实际 Unity 的生命周期仍需实机复验。

## Play Mode 调试窗口

通过 **Window → Ktory → Debugging** 打开窗口。现有宿主实现 `IKtoryDebugTarget`，在真实会话 Start 前注册到 `KtoryDebugRegistry`，在禁用、销毁或替换会话时 Dispose；宿主 asmdef 引用 `Ktory.Unity`，适配代码放在 `#if UNITY_EDITOR` 内。Package Manager 提供 **Debugging host probe** 接线样例。

窗口支持多实例、节点与源码跳行、正文／Speaker、真实控制器的推进阻塞与 AUTO 计时、跟随默认或指定语言、正常快显／推进、合法选项及重启，以及调用栈、循环和消费历史。切语言通过宿主调用 `SetLanguage`、`RefreshLanguage(false)` 并刷新 UI，不能重跑标签或推进。标准计时仅在宿主提供真实 `PresentationController` 时可用。

标签等执行记录在注册后开始收集，全部实例最多 2000 条，支持筛选、清空和复制；窗口开关不改变剧情。自定义动画、额外输入锁、项目 AUTO 通过 `InputBlockReason` 和可选 `IKtoryDebugInfoProvider` 提供，Package 不推测其语义。本版没有强制跳过；UI 与注册日志不进入正式构建。详细接线和真实 Unity 验收清单位于生成包的 `DEBUGGING.md`。Core 测试通过不代表 Unity 窗口与实际演出已经实机验证。
