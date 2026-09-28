# Ktory 仓库边界、验证与跨设备工作流

更新日期：2026-09-27

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
| `src/Ktory.Core/Ast, Parser, Desugar, Runtime` | 结构解析、诊断、叙事状态、语言选择、载荷与标签派发 | Unity、DOM、HTTP、资源数据库、真实时钟、真实演出的执行与事件判定；当前不异步等待演出 |
| `src/Ktory.Core/Runtime/PresentationController.cs` | 不依赖引擎的标准呈现策略辅助类；由接入层驱动 | 把文本门禁变成 Sequencer 的内部前置条件 |
| `src/Ktory.WebReader` | 本地 HTTP 宿主与 Reader 界面，`wwwroot` 是共用 Reader 前端源码 | 第二套分支、调用栈、循环或语言回退算法 |
| `src/Ktory.Wasm` | WASM 宿主及 JS 桥接，引用 Core、复用 Reader 前端 | 重新实现脚本解释器 |
| `src/Ktory.Unity` | 通用 Unity 导入、编辑器及接入代码的源码位置 | 特定游戏角色、剧情状态或私有资源；手工维护 Core 副本 |
| `src/Ktory.VSCode` | VS Code 语言贡献、唯一 TextMate grammar、编辑器文档与 Webview 适配；内置同一 WASM Reader | 第二套解释器、手工修改生成的扩展目录、把高亮当作语义验证 |
| `website` | 门户、教程、三语说明、语法高亮与展示 | 核心语义的第二权威或可冒充真实执行的模拟器 |
| `docs` | 产品契约、规范、工作流与历史依据 | 以实现偶然行为改写已经确认的原则 |
| `samples` 与 `tests` | 使用样例、行为断言及回归证据 | 用测试通过自动批准新的语义 |
| `scripts`、`.github/workflows` | 生成、验证和分发产物 | 直接修改生成物作为源码修复 |
| `artifacts`、`website/dist`、`upm`、`deploy-reader` | 构建或分发结果 | 独立维护、反向覆盖主线源码 |

依赖从宿主与工具指向 Core；Core 不反向依赖门户或宿主。Reader 前端可以实现呈现策略，但必须按同一契约验收。目录内现有命名不意味着该层可以越界。

新增目标中，`.ktr` 尽可能统一组织剧情及显式交接，演出工具制作具体过程，编辑器配置绑定资源、对象与真实节点，接入组件复用通知、推进及清理流程。共享设置的存储位置和逻辑依赖记录的承载模块待工程决定；本表不预先限定其必须在 Sequencer 内或外。世界状态与真实事件判定仍归宿主，不因绑定工具而迁入 Core。

门户若声称提供真实 Ktory 试读，必须使用 Core 的 WASM/桥接或链接正式 Reader。现有首页 JS 演示只可标为示意，不作为脚本验证或核心执行证据；替换成 Core 驱动是待处理事项。语法高亮器可以近似识别 token，但无权决定运行时语义。

### 发布产物与托管入口

| 产品 | 构建入口／Actions 名称 | 生成物与发布位置 | 使用方 |
| --- | --- | --- | --- |
| 独立试读器 Reader | `deploy-reader.yml` / `Reader - Publish Static Site` | `artifacts/reader/wwwroot/` → `deploy-reader` 分支根目录 | Vercel `ktory`；Root Directory 留空，按静态文件托管 |
| Unity UPM 包 | `publish-upm.yml` / `Unity - Publish UPM Package` | `upm` 分支根目录的 `com.ktory.unity` 包 | Unity Package Manager；保留现有 `#upm` 安装入口 |
| VS Code 扩展 | `build-vscode.yml` / `VS Code - Build VSIX` | `artifacts/vscode/packages/ktory-vscode-<version>.vsix`；Actions 下载包 `ktory-vscode-vsix-<源提交>` | VS Code 安装 VSIX；完整扩展位于 `artifacts/vscode/extension/`，其中 `reader/` 是内置 Reader |
| 门户与文档 | Vercel 从 `main` 的 `website/` 构建 | `website/dist/`；无单独的 Actions 产物分支 | Vercel `ktory-home`；Root Directory 为 `website` |

VSIX 的唯一版本来源是 `src/Ktory.VSCode/package.json` 的 `version`。打包文件名由 `scripts/package.cjs` 派生，安装测试共用同一路径；常规 `package` 不递增版本，本地 `package:patch` 在成功生成下一 patch 的 VSIX 后写回 manifest。Actions 下载包末尾的源提交用于区分构建，不是扩展版本号。

Reader 的触发路径覆盖 Core、Web、共用 Reader 前端、样例、共享高亮规则／引擎依赖和相关构建配置；单独修改 Unity 接入或 VS Code 专用适配不触发独立 Reader 发布。用户已确认现阶段保留 Reader／UPM 发布方式，不将新增 CI 门禁作为本轮任务；各 workflow 保持原有验证步骤。

**从旧分支迁移**：`deploy-web` 是 Reader 发布分支的旧名。此改动合入后，Reader workflow 首次运行会生成 `deploy-reader`；随后将 Vercel `ktory` 的生产分支改为 `deploy-reader`，Root Directory 继续使用仓库根目录。确认新分支部署成功后再决定是否清理旧分支；workflow 不再更新 `deploy-web`。

Vercel `ktory-home` 应仅接收门户源码分支的部署，排除 `deploy-reader`、旧 `deploy-web` 和 `upm`。Reader 项目只接收自己的产物分支。仅修改仓库里的发布分支名不会自动修改 Vercel 项目配置，也不会自动消除门户对产物分支的错误预览部署。

## 3. “唯一置信源”按问题定义

| 要回答的问题 | 唯一维护位置／权威 |
| --- | --- |
| 为什么做、什么属于产品边界 | `docs/ktory-design-charter.md` 与 scope 的已确认决定 |
| 某个已确认语法或执行行为应当怎样 | `docs/ktory-implementation_v1.md` |
| 核心实际怎样执行 | `src/Ktory.Core`；其他端引用它或其构建产物 |
| 文本、译文及推荐工作流的剧情顺序／分支是什么 | 作者的 `.ktr` 文件；新增目标中的显式交接也在此声明，允许宿主调用分立段落 |
| 脚本声明对应哪个资源、对象或实际演出节点 | 作者的绑定配置及宿主演出实现；新增通用配置结构尚待设计，不另存重复剧情流程 |
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
pnpm --dir src/Ktory.VSCode install --frozen-lockfile
dotnet test tests/Ktory.Core.Tests/Ktory.Core.Tests.csproj -m:1 -nr:false -p:UseSharedCompilation=false
dotnet build Ktory.sln -m:1 -nr:false -p:UseSharedCompilation=false
dotnet publish src/Ktory.Wasm -c Release
dotnet run --project src/Ktory.WebReader
```

首轮正常执行 restore；依赖已还原且无需更新时可加 `--no-restore`。构建和测试要求本地进程通信，沙箱权限失败不能记为产品测试失败。门户使用其 `pnpm-lock.yaml` 和 `package.json` 中的构建命令独立验证。

行为比较使用规范化轨迹：操作、状态、节点源码位置、内容、实际语言、标签派发、消费状态与错误。随机生成的 ID 用对应关系比较；不要求不同进程生成相同 GUID。假求值器只提供确定的测试结果，不被包装成试读器已经具备游戏模拟能力。

每项能力分开记录三列：**契约状态（已确认／未决／阶段外）**、**实现状态（完整／部分／缺口）**、**验证证据（提交、环境、命令、结果）**。Core 的 Ruby 字符串测试不能证明 TMP 能渲染；导入编译成功不能证明输入和时序正确。

具体时间点的评估见 [2026-09-26 v1 进度与下一阶段评估](ktory-v1-status-2026-09-26.md)。该快照列出当时的代码、已运行检查、已复现缺口与待验收项，不替代实现规格或后续版本的验证记录。

### 新增目标的代表性验证场景（待落实）

本轮以「分析漏洞与Ink区别」最后一轮提出的灯塔素材为代表性验收方向。文件内默认修饰符与局部覆盖现已有核心、桥接及 Webview 回归，其余资源绑定和信号场景仍为待落实的验收方向，不自动增加第一阶段完成门槛，也不直接采用历史灯塔脚本中的未支持语法。

| 场景 | 预期收益／行为 | 核心与接入可验证部分 | Unity 实机补充 |
| --- | --- | --- | --- |
| 相邻对白改变表情，下一句省略 | 回退适用全局默认；无全局时不展示，不残留上句局部表情 | 默认与覆盖解析、明确的不展示输出 | 界面确实清除旧表情 |
| 修改角色默认对话框或资源映射 | 不逐句修改正文，局部覆盖仍生效 | 共享配置引用与解析 | 对话框与资源实际显示 |
| 新增第二段同类投影仪演出 | 增加内容和绑定，不新增专用事件分支 | 复用同一处理流程；记录新增专用代码与配置量 | 资源／对象绑定能够正确播放 |
| 动画由五秒改成八秒 | 使用完成信号的剧情不重填等待时间 | 依据本次完成通知继续 | 信号确实来自实际结束时点 |
| 恢复点从门把手改成门打开 | 剧本交接意图体现变化，动作细节留在演出工具 | 依赖声明与绑定对应 | 节点在正确动画时点触发 |
| 旧演出迟到或重复报告，或另一段同名演出报告 | 不推进新的剧情位置 | 匹配本次播放、会话与位置，保持单一推进责任 | 场景切换、取消及订阅清理的真实生命周期 |
| 无对白演出段落；独立试读缺少真实信号 | 显式交接有可说明的试读行为，不伪造事件已发生 | 冻结后检查越过／人工确认策略和标示 | 试读通过不作为演出验证 |

新增一种能力可需要一次接入代码；衡量复用收益时区分能力接入、内容配置和逐场景专用代码，不把必要的资源制作或叙事节点标注算成可以消除的工作。取消、失败、点击、自动推进与跳过的组合须先作工程决定，再纳入断言；不让测试替未决协议定案。

本次文档修订的状态记录：

| 能力 | 契约状态 | 实现状态 | 验证证据 |
| --- | --- | --- | --- |
| 共享默认呈现与表情省略规则 | 文件内声明、同名整组覆盖、逐句重算已确认；跨文件与资源绑定待设计 | 文件内 speaker 默认修饰符已实现，沿用有效 `Tags` 载荷；实际资源清除由宿主落实 | 默认修饰符、桥接及已安装 VSIX 回归通过；Unity 实机另验 |
| 编辑器绑定与显式信号依赖 | 目标已确认；引用、匹配、等待承载及输入组合未决 | 通用产品能力待落实；项目自定义处理器不等于标准交付 | 本轮无新能力运行证据；未来须分别绑定源提交 S、包提交 P 与环境 |
| 独立试读交接策略 | 不伪造真实事件已确认；具体交互未决 | 待设计 | 不能用当前跳过未知标签的检查代替 |
| 按情况选择剧情／交还玩家操作后继续 | 已确认需求、延期 | 不在当前冻结 API；待设计 | 无本轮验收结果 |

建议先做默认呈现，再设计绑定与完成信号，之后覆盖中途节点；两项后续剧情能力继续延期。这是工程建议，现有核心回归与首阶段验收继续进行。每次交付时更新实际实现状态并记录具体提交、命令和结果，不能把本表目标写成已交付能力。

### VS Code 扩展开发与验证

开发环境需要 .NET SDK 9、Node.js 20+ 和 pnpm 10。在 `src/Ktory.VSCode` 执行：

```sh
pnpm install --frozen-lockfile
pnpm build
pnpm test
pnpm test:integration
pnpm package
pnpm test:vsix
```

路径由 `src/Ktory.VSCode/scripts/paths.cjs` 集中定义：

| 路径 | 用途与清理规则 |
| --- | --- |
| `artifacts/vscode/packages/` | 对外安装／上传的 VSIX，按 manifest 版本命名；同版本成功打包后替换，不同版本保留至主动清理 |
| `artifacts/vscode/extension/` | 可直接加载的完整扩展，包含生成的 `reader/`；构建成功后整体替换，F5 和源码集成测试使用这里 |
| `artifacts/vscode/.build/` | 临时 Reader publish 与扩展组装目录；构建结束或失败时清理 |
| `artifacts/vscode/tests/run-*/` | 隔离的测试配置、安装目录与日志；测试成功后删除，失败时保留并打印路径 |

`pnpm clean` 删除整个 `artifacts/vscode/`（包括各版本 VSIX 和失败测试日志），之后运行 `pnpm package` 可从源码重新生成。清理前关闭使用该生成目录的调试／测试实例；不要与构建或打包并发执行。源码、依赖目录、共享 .NET 的 `artifacts/bin`／`artifacts/obj` 和独立 Web Reader 的 `artifacts/reader` 不在此清理范围。旧的 `artifacts/vscode-reader`、`artifacts/vscode/reader`、根层旧 VSIX 和 `src/Ktory.VSCode/reader` 不再使用。

`test:integration` 使用独立 VS Code 配置和扩展目录；`test:vsix` 将生成的 VSIX 安装到另一隔离配置，再对安装后的文件运行同一套测试。macOS 自动检测标准应用路径；其他环境可设置 `VSCODE_EXECUTABLE_PATH`，CLI 独立时另设 `VSCODE_CLI_PATH`。Linux CI 需要 `xvfb-run` 等显示环境。

构建复用 Web/Runner/Core，所有扩展生成物统一放在 `artifacts/vscode/`，源码目录不再生成 `reader/`。`extension.js` 管理文档与面板生命周期，`webview.js` 负责共享 Reader 的编辑器消息及本地资源适配；剧情执行仍归 Core，时序与输入归 Reader。`reader/build-info.json` 记录源提交、dirty 标记和构建时间；dirty 构建不能作为该提交的干净发布证据。

`package` 自动先构建完整扩展，再生成 `artifacts/vscode/packages/ktory-vscode-<version>.vsix`，不发布到 Marketplace。当前发布者为 `ktory`，扩展标识为 `ktory.ktory`；自 `0.2.0` 起取消 `preRelease: true`，直接打包为正式版 VSIX。版本和 Actions 下载包的对应关系见前文“发布产物与托管入口”。

扩展使用 [专有许可证](../src/Ktory.VSCode/LICENSE.txt)，允许个人和商业使用，修改或再分发扩展须另获书面许可；用户原创剧本不受这些限制。该许可仅覆盖扩展及其包内 Ktory 组件的使用，不自动授权独立 Core／Unity 分发。第三方组件保留各自许可证，包内包含 `reader/notices/`。`package.json` 引用 `LICENSE.txt`，打包必须包含许可文件。

手动上架时使用已验证的 VSIX；本地 dirty 包须注明来自工作区，不能冒充干净提交的构建。`build-vscode.yml` 验证核心、扩展语法与包并上传 Actions 产物；Reader／UPM 暂沿用原发布检查，市场自动上传也暂缓。

### 当前优先落实的工程项

- **保持契约与测试一致**：2026-09-27 已修复默认入口、命名节归属、悬空修饰符、未知容器回退警告和失败重载，并落实本轮确认的缺译、条件与输入生命周期规则；对应本机证据见[修复记录](ktory-v1-status-2026-09-26.md#8-2026-09-27-后续修复与确认记录)。完整 Handler 扩展仍未实现，不把基础 choice 降级描述为已有通用插件系统。
- **补核心组合回归**：普通文本与参数词法、多语言与选项、调用与循环、语言刷新与计时、会话重启与过期输入；发现问题先缩减用例。
- **保留现阶段发布方式**：Reader 和 UPM 尚无 `dotnet test` 门禁；用户确认当前方式足够，本轮不新增该门禁。扩展构建 workflow 已有核心测试；本地新增 `package:patch`，减少等待 Actions 和下载包的步骤。已有验证要求继续执行，远端运行结果另记。
- **补桥接、包与浏览器验收**：构建成功不足以证明 WASM 启动、页面操作或 Unity 包导入成功。没有相应环境时明确留待该环境验证。

HTTP／WASM Reader 的请求与状态现携带 `sessionId` 和 `presentationId`；异步输入须使用来源会话的标识，不能仅按相同拍号匹配。桥接保留最近 50 条 `Warning`／`InputIgnored` 诊断；Reader 仅将 `Warning` 显示在剧本警告区，过期输入留在后台。独立 Reader 显式启用未知条件宽容模式，游戏内置求值器默认将未知条件警告后视为不满足。核心和接入 API 的详细规则见 implementation §5.2.2–5.2.3。

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

UPM 脚本复制 Unity Runtime 接入目录、Editor、调试样例及 Unity 测试，再从唯一 Core 源码生成 Runtime/Core；Core 与 Unity 接入各有程序集边界。新增通用组件须检查打包产物，不能仅把 `.cs` 放进目录就认为已分发。需要长期可复验版本时，应保留对应不可变引用或归档，不能仅依赖浮动分支。调试适配和验收见 [Unity Debugging](ktory-unity-debugging.md)。

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

维护说明、接入细节和验收流程集中放在 `docs/`。根目录保留项目 README 与协作入口；产品目录仅保留分发所需的用户 README、CHANGELOG；`website/src/content/docs/` 保留网站实际使用的三语教程。产品 README 以安装和使用为主，开发与发布流程链接到本文，避免多处维护。Unity 调试指南从 `docs/ktory-unity-debugging.md` 生成到 UPM 包，不在源码产品目录另存一份。

定位与边界只在[宪章](ktory-design-charter.md)维护完整定义；具体语义只在 implementation 维护。AGENT.md、README 与门户通过摘要和链接引用它们，历史工程讨论与原型明确标为历史。

对外示例只能使用真实公开 API；完整例子应能编译运行，片段应明确上下文。选择提交后不额外 Step；呈现控制器的两种推进模式不能混接。门户的三个语言版本在同一次语义变更中更新。

发布说明至少记录源提交、包提交、涉及模块、行为或 API 变化、已运行检查及待验证环境。独立发布门户不等于升级核心；发布 Reader 不等于完成 Unity 验收；文档中的设计承诺不等于已实现能力。

### 2026-09-27：本地 patch 打包与高亮来源

用户确认现阶段保留 Reader／UPM 发布方式，市场自动上传暂缓。运行 `pnpm --dir src/Ktory.VSCode package:patch` 自动计算下一 patch、生成 VSIX，成功后写回源 manifest；失败不递增。`pnpm package` 保留按当前版本打包。主／次版本由维护者明确修改，不由 CI 每次构建递增。

语法高亮唯一规则源为 `src/Ktory.VSCode/syntaxes/ktory.tmLanguage.json`。文档站通过 Shiki 引用；网站首页和 Reader 通过同版本 TextMate/Oniguruma 加共享 HTML 渲染器引用。Reader 发布前安装扩展目录内的锁定依赖；`Directory.Build.targets` 链接 grammar、引擎、WASM 与许可证到两种 Reader，全部随离线 VSIX 分发。新增此依赖不改变 Core 的零外部运行依赖边界。

网站和 Reader 的明暗配色统一由 `src/Ktory.Highlighting/theme.mjs` 定义：文件设置淡蓝、speaker 声明紫色、说话人青绿、语言标记琥珀、执行锚点蓝色、修饰符金色、字符串暖棕。VS Code 使用对应标准 scope，具体颜色尊重用户主题。匿名 `#` 与命名锚点同类；引号内文件名不识别为修饰符。
