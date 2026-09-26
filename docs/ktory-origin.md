# Ktory: 游戏叙事脚本与运行时工程规格书

> **历史原型，非现行规范。** 本文保留最初方案用于理解演变；统一定位、保证与职责以[设计宪章](ktory-design-charter.md)为准，具体行为以[实现规格](ktory-implementation_v1.md)为准，开发流程见[工作流](ktory-workflow.md)。当前 Ktory 是对白优先、宿主驱动的按拍叙事语言与运行时。本文旧有的核心等待演出、内部 canAdvance、`.loop(0)` 无限循环和统一异步五阶段管线均不应再用于实现。Ink 已支持宿主驱动、标签、外部函数和游戏接入，不能沿用本文早期的简化比较作为产品定位。

> 2026-09-26：用户已有另一设备的 Unity 接入；当前设备重点验证核心，远端提供实际反馈。历史样例可能包含未支持或已变更语义，不应直接复制为当前验收样例。历史正文保留不意味着这些候选重新生效。

## 1. 概述与核心设计哲学

### 1.1 核心痛点与定位

**Ktory** 旨在设计一套**对白驱动、高度结合视听演出**的叙事脚本系统与运行时中间件。区别于以文学性网状文本流为主的 Ink/Twine，Ktory 以离散事件调度器（Discrete Event Sequencer）**与**动作拍（Action Beat）为底层模型。

### 1.2 核心设计原则

1. **剧本纯净度优先**：以影视剧本格式（Screenplay-like）为视觉基石，台词为主，演出与状态修饰符解耦悬挂。

2. **纯单向推进（Forward-Only）**：系统不强求全量历史快照回滚（Rollback），换取状态变动与外部游戏系统（背包、Lua、动画、音频）调用的极致自由与低复杂度。

3. **语法只管拓扑，运行时才管语义**：解析器（Parser）仅识别通用基础结构；所有视听指令、容器行为（如选择肢、调查模式）均通过外部策略处理器（Handlers）插件化注册。

4. **渲染表现彻底解耦**：调度核心只负责状态变迁、逻辑求值与时序裁决；UI 呈现方式（打字机、渐显淡入、即时刷新等）属于表现层自主演绎范畴，核心引擎完全不加干涉。

5. **引擎完全解耦**：核心语法解析与 AST 独立于任何具体游戏引擎，支持脱机 Mock 演练、VS Code 插件语法高亮与流程预览，以及各游戏引擎运行时内嵌。

## 2. 词法与语法规范（Syntax Specification）

### 2.1 基础概念：锚点与修饰符（Anchor-Decorator Model）

剧本由离散的执行单元（**Beat / Step**）组成。解析器基于单行规则扫描，将所有脚本行归结为两大类：

* **锚点（Anchor）**：生成一个新的执行节点，显式标识上一个节点的结束。包括文本对白行、旁白行、系统宏指令行以及容器子项行。

* **修饰符（Decorator）**：以 `.` 开头，严格向上吸附至离它最近的锚点节点，作为该节点的元数据或挂载演出参数。

结构逻辑如下：

* 每一个锚点节点持有零个或多个修饰符。

* 修饰符之间与缩进无关，可多行悬挂，亦可在单行末尾链式串联。

### 2.2 节点类型与书写规则

#### 2.2.1 文本与对白锚点（Text / Dialogue Anchor）

文本节点由说话人标识符（Speaker）与内容载荷（Content）构成，基本书写格式为：

```
说话人: 文本内容
```

* **首冒号规则**：词法解析仅以行内出现的**首个半角冒号 `:` 或全角冒号 `：`** 作为说话人与台词的分隔点。其后文本中出现的任何冒号均完整视作普通文本内容。

* **多语言挂载**：当台词包含多语言时，台词行本身作为锚点，其下方缩进挂载各语言变体（变体行共享该锚点挂载的所有修饰符与标签）：

  ```
  艾莉丝:
    @zh: 刚才……你有没有听到什么声音？
    @en: Did you... hear something just now?
    @ja: さっき……何か音、聞こえなかった？
    .emotion(worried)
    .voice("vo_001")
  ```

  若为单语言剧本，直接写在冒号后即可，默认自动归入默认语言通道（Default Locale）。

* **Markdown 与富文本语法的有限支持（Inline Rich Text & Markdown Policy）**：
  为保持纯文本写作心流，同时杜绝常规通用 Markdown 的块级语法污染剧本 AST 解析，系统采取“严格行内（Inline-Only）、编译期脱糖转译（Transpile to Engine Tags）、原生标签直通（Pass-through）”的三层策略：

  1. **行内 Markdown 语法（解析期统一脱糖转译为富文本标签）**：

     * **强调/斜体**：`*内容*` 或 `_内容_` $\rightarrow$ 转译为 `<i>内容</i>`

     * **加粗/重音**：`**内容**` 或 `__内容__` $\rightarrow$ 转译为 `<b>内容</b>`

     * **删除线（常用于心理纠正/改口）**：`~~内容~~` $\rightarrow$ 转译为 `<s>内容</s>`

     * **双关读音与振假名（Ruby/Furigana）**：支持 `[基准文字]{注音}` 格式，转译为游戏引擎注音富文本（如 Unity TextMeshPro 适配标签 `<ruby="注音">基准文字</ruby>`）。

  2. **原生富文本直通（Raw Rich Text Pass-through）**：

     * 允许直接在文本中内嵌目标引擎的原生标签。解析器不对其内部属性作二次加工，原样透传给渲染层：

       * 颜色控制：`<color=#FF5555>高危警报</color>`、`<color=yellow>金币</color>`

       * 字号微调：`<size=70%>（小声嘟囔）</size>`

       * 内联图文/表情图标：`<sprite name="sweat">`、`<sprite index=2>`

  3. **严格禁止的块级 Markdown 语法（Forbidden Block Syntax）**：

     * **严禁标题语法**：禁止在文本行首使用 `#`、`##`（避免与系统宏 `#choice`、`#do` 发生词法冲突）。

     * **严禁列表与引用语法**：禁止行首引用 `>` 或无序列表 `-`、`*`（避免与状态迁移 `->`、选择子项 `* [选项]` 发生解析冲突）。

     * **严禁多行代码块**：禁止使用跨行代码块语法。

  4. **字符转义**：

     * 若必须在台词中输出 `*`、`_`、`~`、`[`、`{` 等保留符号本身，采用反斜杠 `\` 转义。

#### 2.2.2 显式旁白节点与语法糖脱糖（Narration & Desugaring）

为了保持 AST 结构的严谨性与正交性，**旁白在底层被统一收敛为“缺省说话人（匿名）的文本锚点”**。

* **规范显式语法（Canonical Syntax）**：行首以冒号开头，说话人留空：

  ```
  : 暴风雪已经持续了整整三天。
    .box(center_fullscreen)
  ```

  在需要多语言支持时，显式语法保证了锚点结构的自洽：

  ```
  :
    @zh: 窗外风雪交加，狂风拍打着早已生锈的铁栏杆。
    @en: The blizzard raged outside, beating against the rusted iron bars.
    .bg("Snow_Storm")
    .bgm("Ambience_Wind")
  ```

* **缺省说话人语法糖（Syntactic Sugar & Desugaring）**：
  在单语言快速编写时，若行首**既无冒号、也无保留宏前缀（`#`, `*`, `+`, `.`, `?`, `->`, `=>`, `===`）**，解析器在预处理（Lexer）阶段自动进行脱糖处理：

  ```
  // 编剧编写的原文本 (语法糖)：
  窗外风雪交加，狂风拍打着早已生锈的铁栏杆。
    .bg("Snow_Storm")
  
  // 解析器脱糖后的规范形式：
  : 窗外风雪交加，狂风拍打着早已生锈的铁栏杆。
    .bg("Snow_Storm")
  ```

* **歧义消解**：若旁白文本内本身包含冒号，必须采用显式书写法以消解歧义。例如表达 `12:00 钟声响起` 时，必须显式书写为 `: 12:00 钟声响起`。

#### 2.2.3 动作中继拍与系统宏（Directive Anchor）

* **语法**：以 `#` 开头的独立行。代表当前拍**没有文本内容**，仅负责执行系统指令、转场或逻辑变更。`#` 后的非修饰符内容仅作为可读性标识，能够传递给表现层阅读，但不影响任何解析器行为。

* **示例**：

  ```
  #do .screen_fade(black, 1.0).teleport("2F").bg("Dark").next()

  //允许没有可读性标识：
  #.sfx("distant_explosion").wait(2).next()

  //纯停顿拍，等待玩家点击推进：
  #
  ```

#### 2.2.4 容器与子项节点（Container & Item Anchors）

* **容器声明**：`#container_name [decorators]`

* **子项声明**：

  * `* [显示文本]`：一次性项（在循环容器中触发后被标记为已触发，提供此信息给表现层处理）。

  * `+ [显示文本]`：持久项（可无限次触发）。

* **歧义消解**：

  * 容器不允许 `container_name` 为空。

### 2.3 修饰符（Tags / Decorators）

* **语法**：`.name(arg1, arg2, key=value)`

* **位置特征**：可多行、缩进书写，也可在同一行链式书写。

* **语法糖**：所有不带参数的开关类修饰符，均可省略括号。如：`.next()` 可省略为 `.next`

* **保留修饰符**：

  * `.next(waitTime)`：**自动步进修饰符**。指示调度器在当前节拍前置演出完成且渲染层就绪后，**跳过“等待玩家输入”**，在等待 `waitTime`（秒，缺省为 0）之后自动尝试推入下一步。

  * `.loop(loopTime)`：**循环修饰符**。用于标记一个节点的循环属性，反复渲染该节点 `loopTime` 次。当 `loopTime` 为 0 时可留空，此时意为无限次。

### 2.4 行内样式与作用域界定

| 括号形态 | 语法语义 | 示例 | 消费层级 | 
 | ----- | ----- | ----- | ----- | 
| `(...)` | 修饰符参数传递 | `.sfx("thunder", 0.8)` | 运行时调度器 | 
| `[...]` | 交互项 / UI 语义块 / 注音基准 | `* [调查壁炉]`、`[基准]{注音}` | UI 容器处理器 / 文本脱糖器 | 
| `{...}` | **逻辑沙盒（求值与判定）** | `{? sanity < 30}` | 表达式求值器（Lua/C#） | 
| `*...*`, `**...**` | 行内排版样式（Markdown） | `*斜体*`, `**粗体**`, `~~划线~~` | 编译期转译至引擎 RichText | 

#### 2.4.1 行内条件替换与变量插值

* **条件文本替换**：`{? condition } TrueText {| condition2 } ElifText {|} ElseText`

  ```
  艾莉丝: {? player.sanity < 30} 你的手抖得好厉害…… {|} 我们走吧。
  ```

* **文本变量插值**：`{= expression}`

  ```
  商人: 这把剑售价 {= shop:GetPrice("IronSword")} 枚金币。
  ```

## 3. 控制流拓扑与跳转语义

系统定义如下跳转控制原语：

* `-> Label`：**单向跳跃（Jump）**。无条件放弃当前执行上下文，重定向至目标块。

* `=> Label`：**子过程调用（Call）**。将当前执行点推入调用栈，跳转执行目标块。

* `-> return`：**子过程返回（Return）**。弹出调用栈顶记录的恢复地址并返回。

* `-> break`：**跳出容器**

* `-> end`：**跳出节**

### 3.1 选项跳转的三类拓扑模型详解

叙事树的分支跳转在结构上划分为三种互斥的模式：

| 拓扑模式 | 容器声明 | 子项调用语法 | 栈行为与恢复点 | 汇流与退出机制 | 
 | ----- | ----- | ----- | ----- | ----- | 
| **Case A: 轮询探索菜单** | `#choice.loop` | `=> Sub_Label` | 压栈返回点设为**当前选择容器本身** | 遇到 `-> return` 重开菜单；需显式跳出 | 
| **Case B: 线性子过程** | `#choice` | `=> Sub_Label` | 压栈返回点设为**选择容器之后的后置语句** | 遇到 `-> return` 跳过整个选择结构，继续后文主线 | 
| **Case C: 内联局部展开** | `#choice` | 无跳转，直接缩进书写 | 不进行任何压栈操作 | 子项执行完毕后，控制流**隐式自然汇流**至后文 | 

#### 3.1.1 Case A: 轮询探索菜单（Hub Loop）

* **应用场景**：密室调查、多话题逐一询问、侦查线索收集。

* **执行机制**：

  1. 容器声明带 `.loop` 修饰（例如 `#choice.loop`）。

  2. 玩家点击带有 `=> Sub_Label` 的选项时，调度器将当前 `#choice.loop` 的执行指针推入调用栈，随后跳转执行子块。

  3. 子块末尾遇到 `-> return` 时，调度器弹栈，重新唤起当前 `#choice.loop` 界面。

  4. 调度器在重绘菜单时比对历史已选集合：标有 `*` 的选项若已被访问，则自动置灰或剔除；标有 `+` 的选项常驻显示。

  5. 必须提供一个包含常规跳跃 `-> Label` 或 `-> break` 的出口子项，才能终结此循环。前者意味着单向跳跃，后者意味着跳出该选择容器继续下一行。

#### 3.1.2 Case B: 线性子过程（Linear Subroutine）

* **应用场景**：深入查看某一件重要物品后，主线剧情自然顺延展开。

* **执行机制**：

  1. 容器为普通 `#choice`。

  2. 子项使用 `=> Sub_Label` 压栈调用。

  3. 压栈记录的地址是**该 `#choice` 整个代码块彻底结束后的第一条主干指令**。

  4. 子块末尾遇到 `-> return` 弹栈后，调度器绕过整个选择结构，直接从主线后文继续播放。

#### 3.1.3 Case C: 内联局部展开（Inline Convergence）

* **应用场景**：轻量级反应分支（例如不同选项只引起一两句简短回答）。

* **执行机制**：

  1. 允许在 `* [选项]` 下方直接缩进书写台词与修饰符，无需开辟新 `=== Block`。

  2. 当解析器扫描到与该选项平齐或缩进退回顶格的新行时，判定内联分支结束。

  3. 各分支执行完毕后自动汇合，继续推进选择容器之后的内容。

## 4. 抽象语法树（AST）数据结构规范

在 AST 层面，对白与旁白统一收敛为单一的 `TextStep`（通过 `speaker` 是否为 `null` 区分）：

```
// 顶层故事文档
interface KtoryFile {
  blocks: Record<string, KtoryBlock>; // Key 为块标签名 (Label)
}

// 独立的剧情代码段
interface KtoryBlock {
  label: string;
  steps: StepNode[];
}

// 节点基类
type StepNode = TextStep | DirectiveStep | ContainerStep;

// 1. 统一文本节点 (包含角色对白与旁白)
interface TextStep {
  type: "text";
  speaker: string | null;               // 为 null 或 "" 时即代表旁白
  textVariants: Record<string, string>; // locale -> localized string (已完成 Markdown 脱糖与转译)
  tags: TagData[];
  guardCondition?: string;              // 来源于行首 ? {expr}
}

// 2. 动作/宏中继节点
interface DirectiveStep {
  type: "directive";
  name: string;                         // 如 "do", "set"
  tags: TagData[];
  guardCondition?: string;
}

// 3. 容器节点 (如 #choice, #investigate)
interface ContainerStep {
  type: "container";
  name: string;                         // "choice", "investigate"
  tags: TagData[];
  items: ContainerItem[];
  guardCondition?: string;
}

// 容器内的离散子项
interface ContainerItem {
  marker: "*" | "+";                    // * 为一次性，+ 为持久可重复
  label: string;                        // 显示文本 [xxxx]
  guardCondition?: string;              // ? {condition}
  tags: TagData[];                      // 选中后立即触发的修饰符
  targetJump?: {
    type: "jump" | "call";              // "->" 为 jump, "=>" 为 call
    destination: string;
  };
  inlineSteps?: StepNode[];             // Case C 内联展开的子步骤
}

// 修饰符通用结构
interface TagData {
  name: string;
  positionalArgs: (string | number | boolean)[];
  namedArgs: Record<string, string | number | boolean>;
}

```

## 5. 运行时架构与系统调度管线

### 5.1 系统分层与模块边界

Ktory 运行时自上而下严格划分为四层架构，各层之间单向依赖、高内聚低耦合：

```
[第一层：剧本解析层 (Parser)]
  输入纯文本源码 (.ktr) ──► 词法扫描与语法脱糖 ──► 输出不可变内存抽象语法树 (AST)

[第二层：剧情调度引擎 (Narrative Sequencer)]
  持有当前执行指针 (Instruction Pointer)
  维护调用栈 (Call Stack) 与已读项集合 (Visited Set)
  维护步进门禁状态 (canAdvance 状态锁)
  负责 Step 步进驱动与 .next() 自动推进行为决议

[第三层：门面与分发层 (Facades & Dispatchers)]
  ├─ 外部表达式求值门面 (IExpressionEvaluator)
  │    负责条件守卫判定、变量插值计算与逻辑执行 (转接 Lua / C#)
  ├─ 表现层通信门面 (IPresentationService)
  │    负责派发纯净的内容展示载荷并监听渲染完成状态，不限定具体渲染方式
  └─ 策略指令分发器 (Handler Registry)
       负责根据名称将容器与演出指令派发给挂载的具体业务处理器

[第四层：宿主与表现层 (Host Game Systems & Presentation)]
  包含 UI 文本与对话框渲染、音效控制器、立绘/动画控制器、背包与任务系统

```

#### 模块职责清单

* **剧本解析层（Parser）**：负责离线编译或冷启动加载，仅输出结构化 AST。严禁感知任何游戏运行时状态。

* **剧情调度引擎（Sequencer）**：作为状态机运转的核心中枢。它严格按顺序提取 AST 节点，管理分支调用栈，维持**步进门禁锁（`canAdvance`）**，处理挂起等待与推进行为。

* **求值门面（IExpressionEvaluator）**：将表达式计算与底层语言解耦。在开发调试阶段可接入 Mock 求值器；在真实工程中挂接项目的 Lua 虚拟机或 C# 反射系统。

* **表现层通信门面（IPresentationService）**：定义调度器与 UI 渲染系统之间的抽象协议。调度器仅传递包含说话人与富文本的不可变载荷，表现层返回一个异步就绪信号（例如 Task/Coroutine）。

* **策略处理器（Handlers）**：采用策略模式注册的独立执行体。例如 `#choice` 对应 `ChoiceHandler`，`.sfx` 对应 `AudioHandler`。调度引擎本身不包含任何视听渲染逻辑。

### 5.2 核心接口抽象

```
public interface IExpressionEvaluator
{
    bool EvaluateCondition(string expression);
    string EvaluateInterpolation(string expression);
    void Execute(string statement);
}

public interface IPresentationService
{
    /// <summary>
    /// 向表现层派发展示载荷。表现层负责具体排版与动效 (如打字机、渐显、即时刷出等)。
    /// 返回一个当呈现就绪 (或被玩家主动打断跳过) 时完成的任务。
    /// </summary>
    UniTask PresentTextAsync(TextPayload payload, CancellationToken ct);

    /// <summary>
    /// 清理或关闭指定对话容器
    /// </summary>
    void Dismiss(string boxType);
}

public class TextPayload
{
    public string Speaker { get; set; }
    public string Content { get; set; }
    public IReadOnlyList<TagData> Tags { get; set; }
}

```

### 5.3 调度器的单步生命周期管线（Step Lifecycle Pipeline）

当调度器推进到任意一个 `StepNode` 时，必须严格遵循以下五个串行阶段依次执行：

#### 阶段一：前置守卫断言（Guard Evaluation）

* 检查节点是否挂有 `guardCondition`（来源于剧本中的 `? {expr}`）。

* 若存在守卫，调用 `IExpressionEvaluator.EvaluateCondition`。

* **断言失败**：直接跳过当前节点，立即拉取下一个 Step 重走生命周期。

* **断言成功**：进入阶段二。

#### 阶段二：前置修饰符分发（Pre-line Tag Dispatching）

* 遍历当前节点挂载的所有 `tags`。

* 过滤出所有**非系统保留 Tag**（即名称不为 `next` 的所有修饰符）。

* 将 Tag 数据派发给 Handler 注册表中对应的具体业务处理器（例如播放音效、变换立绘表情、触发背包增减）。

* 视 Handler 的具体实现支持异步并发或顺序等待完成。

#### 阶段三：文本准备与动态求值（Text Preparation & Evaluation）

* 根据当前激活的全局语言选项（Locale），从 `textVariants` 字典中提取对应的本地化字符串。

* 若文本内包含 `{= expr}` 表达式，调用 `IExpressionEvaluator.EvaluateInterpolation` 进行动态变量值替换。

* 若文本内包含 `{? cond} A {|} B` 分支表达式，通过求值器断言后裁剪保留对应语段。

#### 阶段四：载荷派发与表现层渲染（Payload Dispatch & Presentation）

* 调度器核心只负责构建包含 `Speaker`、最终处理后的 `Content` 以及相关元数据的 `TextPayload`。

* 调用 `IPresentationService.PresentTextAsync` 将载荷交付给外部表现系统。

* 调度器在此阶段异步等待表现层返回完成信号，不关心表现层的具体实现。

#### 阶段五：步进门禁判定与等待挂起（Advancement Gating & Stepping Decision）

步进逻辑的核心依赖于调度器的**步进门禁状态（`canAdvance` 标志位）**：

```
[当前 Step 结束] ──► 检查步进门禁状态 (canAdvance)
                          │
             ┌─────────────┴─────────────┐
             ▼ (canAdvance == false)   ▼ (canAdvance == true)
       [步进被拦截]                检查节点是否挂有 .next(waitTime)
   (如处在 #choice 等待中)             │
   等待业务完成解锁信号          ┌───────┴───────┐
                          ▼ (有 .next)    ▼ (无 .next)
                     等待 waitTime   挂起引擎 (Suspended)
                     自动推入下一步    等待外部玩家推进输入

```

1. **门禁阻断检查（`canAdvance == false`）**：

   * 若当前处在交互容器内（例如 `#choice` 激活中、`#investigate` 搜查中）或表现层尚未完成关键交互：

     * **步进门禁关闭（`canAdvance = false`）**。

     * 此时任何通用的“玩家点击屏幕/按空格继续”输入均被调度器**彻底拦截并丢弃**。

     * 调度器维持休眠，直到捕获到特定业务事件（如玩家点击了某个具体的选项分支、搜查退出），由该业务 Handler 显式重设 `canAdvance = true` 并通知调度器拉取新节点。

2. **常规步进裁决（`canAdvance == true`）**：

   * 当节点内容正常展示就绪，且无任何交互锁阻断时，调度器检查节点 tags 中是否显式挂载了 `.next(waitTime)`：

     * **分支 A：存在 `.next` 修饰符（自动推进）**：
       调度器不等待玩家推进输入，在等待 `waitTime`（秒，缺省为 0）之后，自动拉取并执行下一个 Step。

     * **分支 B：无 `.next` 修饰符（挂起等待推进输入）**：
       调度器通知表现层展示“等待点击”提示标（可选），挂起异步任务，直到监听到外部派发的“玩家点击确认/推进”输入信号后，方才激活推进流程。

## 6. 综合场景全特性参考样例

```
=== Scene_Lighthouse_Office ===

// 显式多语言旁白锚点（带斜体强调与注音脱糖）
:
  @zh: 窗外风雪交加，狂风拍打着早已生锈的*铁栏杆*。
  @en: The blizzard raged outside, beating against the rusted *iron bars*.
  .bg("Lighthouse_Snow")
  .bgm("Ambience_Blizzard", volume=0.6)

主角:
  @zh: 房间里乱成一团，看来之前有人在这里匆忙寻找过什么。
  @en: The room is in complete disarray. Someone was searching for something in a hurry.

// 动作中继拍：震屏、播放音效并直接推进
#do .camera_shake(intensity=0.4) .sfx("metal_drop") .next()

艾莉丝:
  @zh: {? player.sanity < 30} 呜……刚才那是什么声音？我快要疯了…… {|} 刚才的声音是从[书桌]{desk}那边传来的！
  @en: {? player.sanity < 30} Ugh... what was that? I can't take this anymore... {|} That sound came from the desk!
  .emotion(nervous)
  .voice("vo_alice_042")

// 调查/询问轮询循环 (Case A 与 Case C 混合，激活时 canAdvance = false)
#choice.loop.timeout(0)
  * [调查散落的文件]
    主角: 散落的**航海日志**被撕掉了最后几页。
      .inventory_add("Torn_Page", 1)
      .sfx("paper_flip")

  * ? {not inventory:Has("DeskKey")} [尝试打开书桌抽屉]
    主角: 抽屉被锁死了，必须找到对应的钥匙。
      .emotion(thinking)

  * ? {inventory:Has("DeskKey")} [用钥匙打开抽屉] => Sub_OpenDrawer

  + [向艾莉丝搭话]
    艾莉丝: 我们必须在暴风雨把灯塔彻底封死之前离开！
      .emotion(urgent)

  * ? {inventory:Has("SecretDoc")} [离开办公室] -> break

主角：快没时间了，我们先走吧。

=== Sub_OpenDrawer ===
主角: 伴随着刺耳的摩擦声，抽屉被拉开了。
  .sfx("drawer_open")

主角: 里面只有一份盖着红印的<color=#FF0000>机密文件</color>。
  .inventory_add("SecretDoc", 1)

艾莉丝: 这就是 {= company:GetName()} 一直在隐瞒的真相吗……？
  .emotion(shocked)

// 返回之前的 #choice.loop，已读抽屉选项消失
-> return

```
