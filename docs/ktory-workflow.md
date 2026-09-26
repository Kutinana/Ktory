# Ktory 仓库边界、验证与跨设备工作流

更新日期：2026-09-26

共同原则：[设计宪章](ktory-design-charter.md)。本文定义工作约定；标为“待落实”的检查并非当前 CI 已有能力。

## 1. 当前采用单仓库

核心、试读桥接、通用 Unity 接入与门户继续放在同一仓库，保留现有目录。当前这些模块由同一项目维护，API、语言规则、样例与说明仍频繁一起变化；同一变更中完成同步，比增加跨仓库版本协调更有价值。

独立仓库可以提供单独权限、生命周期和发布自治，但目前会增加：

- 核心、试读器、文档分别提变更，难以原子更新一个契约；
- 包版本、兼容范围、预发布依赖与回滚组合的维护；
- 跨仓库测试、构建触发、凭据、变更说明和反馈定位；
- 为了本地联调引入路径覆盖、子模块或手工拷贝，扩大两设备的版本不一致问题。

单仓库不要求同一次发布所有产品。门户、Web Reader 和 UPM 应有各自的构建与发布入口、变更范围和版本记录。按模块隔离任务、按契约同步相关修改；不需要先大规模搬目录。

发布影响范围应分别计算：Core 变化触发核心与相关桥接验证；Reader 由 Core、Runner/Web 和样例影响；UPM 由 Core、Unity 与打包脚本影响；门户由 `website` 及其引用的规范内容影响。公共构建配置和测试自身变化也须触发相关检查。路径过滤只是避免无关构建，不能漏掉契约依赖；当前 workflow 的过滤与门禁仍需按此完善。

未来仅在出现独立维护团队或权限、明显独立的发布周期、稳定的版本化 API，或仓库体积对开发造成实测负担时重新评估拆分。届时门户通常比核心与试读器更适合先独立。具体游戏工程与私有美术资源可以继续在外部仓库，不要求迁入 Ktory。

## 2. 逻辑分割与依赖方向

| 位置 | 职责 | 不应承担 |
| --- | --- | --- |
| `src/Ktory.Core/Ast, Parser, Desugar, Runtime` | 结构解析、诊断、叙事状态、语言选择、载荷与标签派发 | Unity、DOM、HTTP、资源数据库、真实时钟、外部演出等待 |
| `src/Ktory.Core/Runtime/PresentationController.cs` | 不依赖引擎的标准呈现策略辅助类；由接入层驱动 | 把文本门禁变成 Sequencer 的内部前置条件 |
| `src/Ktory.Runner` | 本地 HTTP 宿主与 Reader 界面，`wwwroot` 是共用 Reader 前端源码 | 第二套分支、调用栈、循环或语言回退算法 |
| `src/Ktory.Web` | WASM 宿主及 JS 桥接，引用 Core、复用 Reader 前端 | 重新实现脚本解释器 |
| `src/Ktory.Unity` | 通用 Unity 导入、编辑器及接入代码的源码位置 | 特定游戏角色、剧情状态或私有资源；手工维护 Core 副本 |
| `src/Ktory.VSCode` | VS Code 语言贡献、唯一 TextMate grammar、编辑器文档与 Webview 适配；内置同一 WASM Reader | 第二套解释器、手工修改生成的 `reader/`、把高亮当作语义验证 |
| `website` | 门户、教程、三语说明、语法高亮与展示 | 核心语义的第二权威或可冒充真实执行的模拟器 |
| `docs` | 产品契约、规范、工作流与历史依据 | 以实现偶然行为改写已经确认的原则 |
| `samples` 与 `tests` | 使用样例、行为断言及回归证据 | 用测试通过自动批准新的语义 |
| `scripts`、`.github/workflows` | 生成、验证和分发产物 | 直接修改生成物作为源码修复 |
| `artifacts`、`website/dist`、`upm`、`deploy-reader` | 构建或分发结果 | 独立维护、反向覆盖主线源码 |

依赖从宿主与工具指向 Core；Core 不反向依赖门户或宿主。Reader 前端可以实现呈现策略，但必须按同一契约验收。目录内现有命名不意味着该层可以越界。

门户若声称提供真实 Ktory 试读，必须使用 Core 的 WASM/桥接或链接正式 Reader。现有首页 JS 演示只可标为示意，不作为脚本验证或核心执行证据；替换成 Core 驱动是待处理事项。语法高亮器可以近似识别 token，但无权决定运行时语义。

### 发布产物与托管入口

| 产品 | 构建入口／Actions 名称 | 生成物与发布位置 | 使用方 |
| --- | --- | --- | --- |
| 独立试读器 Reader | `deploy-reader.yml` / `Reader - Publish Static Site` | `artifacts/reader/wwwroot/` → `deploy-reader` 分支根目录 | Vercel `ktory`；Root Directory 留空，按静态文件托管 |
| Unity UPM 包 | `publish-upm.yml` / `Unity - Publish UPM Package` | `upm` 分支根目录的 `com.ktory.unity` 包 | Unity Package Manager；保留现有 `#upm` 安装入口 |
| VS Code 扩展 | `build-vscode.yml` / `VS Code - Build VSIX` | `artifacts/vscode/ktory-vscode-<version>.vsix`；Actions 下载包 `ktory-vscode-vsix-<源提交>` | VS Code 安装 VSIX；内置 Reader 的中间构建位于 `artifacts/vscode/reader/` |
| 门户与文档 | Vercel 从 `main` 的 `website/` 构建 | `website/dist/`；无单独的 Actions 产物分支 | Vercel `ktory-home`；Root Directory 为 `website` |

VSIX 的唯一版本来源是 `src/Ktory.VSCode/package.json` 的 `version`。打包文件名由 `scripts/package.cjs` 派生，安装测试共用同一路径；常规构建不自动递增版本，发新版时显式更新 manifest 版本。Actions 下载包末尾的源提交用于区分构建，不是扩展版本号。

Reader 的触发路径只覆盖 Core、Web、共用 Reader 前端、样例和相关构建配置；单独修改 Unity 接入或 VS Code 扩展不触发独立 Reader 发布。各 workflow 保持自身原有验证步骤，不因一次产品修正增加全局专项测试步骤。

**从旧分支迁移**：`deploy-web` 是 Reader 发布分支的旧名。此改动合入后，Reader workflow 首次运行会生成 `deploy-reader`；随后将 Vercel `ktory` 的生产分支改为 `deploy-reader`，Root Directory 继续使用仓库根目录。确认新分支部署成功后再决定是否清理旧分支；workflow 不再更新 `deploy-web`。

Vercel `ktory-home` 应仅接收门户源码分支的部署，排除 `deploy-reader`、旧 `deploy-web` 和 `upm`。Reader 项目只接收自己的产物分支。仅修改仓库里的发布分支名不会自动修改 Vercel 项目配置，也不会自动消除门户对产物分支的错误预览部署。

## 3. “唯一置信源”按问题定义

| 要回答的问题 | 唯一维护位置／权威 |
| --- | --- |
| 为什么做、什么属于产品边界 | `docs/ktory-design-charter.md` 与 scope 的已确认决定 |
| 某个已确认语法或执行行为应当怎样 | `docs/ktory-implementation_v1.md` |
| 核心实际怎样执行 | `src/Ktory.Core`；其他端引用它或其构建产物 |
| 脚本内容及译文是什么 | 作者的 `.ktr` 文件 |
| 当前背包、场景、动画状态是什么 | 实际游戏宿主；Core 不持有第二份权威副本 |
| 某个版本验证过什么 | 绑定源提交与环境的测试／反馈记录 |
| Unity 实际装了哪个版本 | 包提交或不可变发布标签，以及游戏的 `Packages/manifest.json`、`Packages/packages-lock.json` |

规范描述“应当”，代码描述“实际”，测试提供证据，三者不能相互冒充。若有冲突，先定位对应条款与决策记录；已有明确规范时按规范修复，未决或要改变已确认契约时显式记录设计变更。禁止只修改断言使测试变绿。

## 4. 每次变更的工作顺序

1. 读取 AGENT.md、宪章及相关 implementation 条款，确定变更属于 Core、呈现、桥接、文档还是发布。
2. 获取当前 Git 状态，保留其他正在进行的修改。一个工作项围绕一个问题；无关门户样式和核心修复分别提交。独立任务需要隔离时使用独立工作树。
3. 对行为问题先给出最小 `.ktr`、操作序列和预期轨迹，再写有区分能力的回归断言。正常场景与关键相邻边界配对；例如正文撇号的修复也要覆盖参数中的单引号与 `//`。
4. 修改唯一源码位置；同一行为影响 Core、桥接、公开 API 或文档时，在同一次变更中同步这些部分。
5. 运行与改动相关的检查，记录命令、源提交和结果。声明“未验证”的部分，不拿已有测试数量推导完备性。
6. 发布候选应来自同一个已验证的源提交；远端 Unity 在固定包版本上反馈。必要的反馈先变成本机回归，再修复并重验。

纯文案或样式改动只需相应构建、链接与展示检查；不为低影响变化编写镜像实现的测试。语义、输入时序和序列化修复必须有可重现用例。

## 5. 本机质量与远端反馈矩阵

“完备性”指首阶段已确认契约的覆盖，不指实现任意叙事或整个游戏。下面是验收清单，不表示各层已全部具备自动检查。

| 层 | 当前设备与 CI 的目标 | 另一设备 Unity 补充 |
| --- | --- | --- |
| 解析 | 合法 AST；缩进、注释、引号、标签参数及语言头组合；拒绝有歧义结构并定位；不丢文本 | 导入器展示相同结果与诊断 |
| 执行 | 文本／指令停止点；选择首拍；调用返回、根节、loop/break、耗尽越过；标签与消费轨迹 | 同一输入操作得到相同剧情序列 |
| 语言 | 正文、名字、选项切换及回退；身份与消耗记录保持；副作用不重发 | 字体、换行、富文本与注音实际呈现 |
| 呈现策略 | 用可控时钟验证 `.next/.wait/.skippable`；自然完成与快显；固定／预估停留与切语言组合 | 每帧更新与 UI 事件接线、实际输入设备 |
| 输入生命周期 | 重复点击、旧选项、旧计时、重启、关闭；呈现 ID 与会话边界 | 生命周期回调、场景切换及真实帧序 |
| 桥接 | Core 与 WASM/HTTP 操作轨迹一致；序列化形状与错误；浏览器真实启动另列检查 | 具体 Unity API/编译器兼容性 |
| 鲁棒性 | 无输出循环预算、零时长自动批次、大量步骤和错误输入；有界生成用例及确定性随机种子 | 主线程响应、平台异常与资源边界 |
| 分发 | 包结构、源码来源、Core 一致性、程序集与元信息；发布引用同一验证提交 | 固定版本能够导入、编译和运行 |

基础命令：

```sh
dotnet test tests/Ktory.Core.Tests/Ktory.Core.Tests.csproj -m:1 -nr:false -p:UseSharedCompilation=false
dotnet build Ktory.sln -m:1 -nr:false -p:UseSharedCompilation=false
dotnet publish src/Ktory.Web -c Release
dotnet run --project src/Ktory.Runner
```

首轮正常执行 restore；依赖已还原且无需更新时可加 `--no-restore`。构建和测试要求本地进程通信，沙箱权限失败不能记为产品测试失败。门户使用其 `pnpm-lock.yaml` 和 `package.json` 中的构建命令独立验证。

行为比较使用规范化轨迹：操作、状态、节点源码位置、内容、实际语言、标签派发、消费状态与错误。随机生成的 ID 用对应关系比较；不要求不同进程生成相同 GUID。假求值器只提供确定的测试结果，不被包装成试读器已经具备游戏模拟能力。

每项能力分开记录三列：**契约状态（已确认／未决／阶段外）**、**实现状态（完整／部分／缺口）**、**验证证据（提交、环境、命令、结果）**。Core 的 Ruby 字符串测试不能证明 TMP 能渲染；导入编译成功不能证明输入和时序正确。

VS Code 扩展在其目录执行 `pnpm install --frozen-lockfile`、`pnpm build`、`pnpm test`、`pnpm test:integration` 和 `pnpm package`。构建复用 Web/Runner/Core；`reader/` 为生成物，VSIX 输出至 `artifacts/vscode/`。原生集成测试使用独立 VS Code 配置，不修改日常安装。`build-vscode.yml` 验证核心、扩展语法与包并上传 VSIX；原有 Reader/UPM 发布 workflow 的门禁仍须分别补齐。

### 当前优先落实的工程项

- **先收敛契约与测试**：默认入口、未缩进命名节兼容行为、未知容器回退诊断等仍须与最高技术规范对应。不要继续用互相矛盾的测试保护不同解释。
- **补核心组合回归**：普通文本与参数词法、多语言与选项、调用与循环、语言刷新与计时、会话重启与过期输入；发现问题先缩减用例。
- **建立同提交 CI 门禁**：原有两条发布 workflow 分别发布 Reader 和 UPM，没有 `dotnet test` 门禁。新扩展的构建 workflow 已包含核心测试，但并不替这两条发布链路提供门禁；二者仍应先通过相关测试再分发。
- **补桥接、包与浏览器验收**：构建成功不足以证明 WASM 启动、页面操作或 Unity 包导入成功。没有相应环境时明确留待该环境验证。

## 6. 两设备之间交付版本，不手工同步散落源码

当前设备维护主线 Core 与可自动验证的接入；另一设备保留已有游戏工程与资源。另一设备的本地 Ktory 修改先保存为独立分支、补丁或可比较提交，判断是否需要回收进主线，再切换依赖。不能直接覆盖、删除现有本地接入。

推荐交付链：

```text
源提交 S → 本机／CI 验证 → 生成 UPM 包提交 P → Unity 固定使用 P → 反馈记录 S、P
```

当前 `scripts/publish-upm.ps1` 将 Core 源码和 Unity 工具生成到 `upm` 分支，并在包提交信息中写入源提交。`upm` 是产物分支，修复必须回到主线源码；脚本在本地工作区运行会复制工作区文件，因此有未提交改动时不能仅用 HEAD 声称包内容来源。发布须使用干净、固定提交的 checkout，或明确标记其为含补丁的本地试验包。

Unity 包名是 `com.ktory.unity`。当前便捷安装入口为：

```json
{
  "dependencies": {
    "com.ktory.unity": "https://github.com/Kutinana/Ktory.git#upm"
  }
}
```

上面是浮动分支入口，不是某个版本的验证凭据。跨设备复验应将 `#upm` 替换为**实际生成的包提交 P**或指向它的不可变发布标签，并将游戏的 manifest 和 lock 文件纳入版本管理。不要把源提交 S 填进去：当前主线根目录不是生成后的 UPM 包。本文不虚构已经存在的发布标签。

Unity 官方支持 Git 依赖使用分支、标签或提交，并以 lock 文件记录解析提交，见[Git dependencies](https://docs.unity3d.com/2022.1/Documentation/Manual/upm-git.html)。更新包时显式核对 lock 中的实际提交，不依赖“已拉最新分支”的口头描述。断网时可交付从同一 S 生成的完整包目录／归档，保留来源记录；它仍然是生成物，不能成为第二套 Core 源码。

现有 UPM 脚本每次只复制 Unity Runtime 的指定配置文件与 Core 目录。若新增通用 Unity Runtime 组件，必须同步打包规则并检查产物，不能仅把 `.cs` 放进目录就认为已分发。当前脚本会重写产物分支；需要长期可复验版本时，应保留对应不可变引用或归档，不能仅依赖分支历史。

### 远端反馈的最小内容

```text
源提交 S / 包提交 P（或本地补丁标识）：
Unity 版本、目标平台、Editor/Player、脚本后端（相关时）：
最小 .ktr 与依赖的少量宿主代码：
入口、请求语言、确定的外部条件：
操作与时间顺序：Start → 打印完成 → 0.5 秒 → 点击 → …
预期：逐拍内容、选择、副作用或错误
实际：逐拍输出、日志、错误栈（必要时画面）
是否可在 Core / Reader 复现：
```

能脱离 UI 复现的错误转为本机失败用例；只有渲染、资源、事件接线或平台相关的问题保留在 Unity 层。通用修复回到本仓库，游戏专用行为保留在游戏工程。反馈完成后记录同一 S/P 上通过的场景，不泛化为“所有 Unity 场景都已验证”。

## 7. 文档同步与发布说明

定位与边界只在[宪章](ktory-design-charter.md)维护完整定义；具体语义只在 implementation 维护。AGENT.md、README 与门户通过摘要和链接引用它们，历史工程讨论与原型明确标为历史。

对外示例只能使用真实公开 API；完整例子应能编译运行，片段应明确上下文。选择提交后不额外 Step；呈现控制器的两种推进模式不能混接。门户的三个语言版本在同一次语义变更中更新。

发布说明至少记录源提交、包提交、涉及模块、行为或 API 变化、已运行检查及待验证环境。独立发布门户不等于升级核心；发布 Reader 不等于完成 Unity 验收；文档中的设计承诺不等于已实现能力。
