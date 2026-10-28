# DbcView DBC 检查与评审能力规划

- 文档状态：已确认的规划基线（v2）
- 更新日期：2026-10-28
- 规划基线：`main` / `f29d6dc`
- 文档范围：产品范围、路线图、架构约束、验收标准与已知风险；不包含实现代码
- 评审方式：本版由 24 项决策闭合后重写；决策记录见第 11 节，原计划中已被事实推翻的前提见第 2 节

## 1. 背景与现状

DbcView 当前已经支持上传 `.dbc` 文件、浏览消息与信号、查看基础属性、切换表格/文本视图，并以原始文本方式保存草稿。现有实现具备四层架构、稳定诊断码、4 MB 上传限制和 113 项通过的自动化测试。

但和真实 DBC 评审工作相比，目前仍有明显差距：

- 表格默认展开全部信号；BMW 样本包含 325 条消息、130 个信号，缺少折叠与快速定位手段。
- 属性面板只展示 ID、名称、长度、发送者、起始位、因子、偏移和范围，未展示单位、字节序、符号、Receiver 和多路复用归属。
- `CM_`、`BA_`、`VAL_` 等语义段落只作为原始文本保留，未与消息/信号关联。
- 没有文件级/消息级语义浏览，也没有命令面板式跳转。
- 上传读取固定按 UTF-8 解码，非 UTF-8 DBC 的注释可能出现替换字符。
- 工作区只在内存中，刷新丢失；没有下载入口，也没有会话持久化。
- 语义编辑、消息/信号 CRUD 均未实现。

本规划不把 DbcView 扩展为完整 DBC 编辑器，而是先服务 **DBC 检查与评审**：让评审者尽快看清结构、注释、属性和值表，并能可靠定位目标信号；结构化编辑作为后续独立增量。

## 2. 计划前提修正

原计划中的若干前提已被代码、样本与浏览器行为推翻。本版按以下核实结果重写，不再保留无法兑现的承诺。

| 原计划表述 | 核实结果 | 本版处理 |
|---|---|---|
| 「折叠是否足以解决真实样本的卡顿」 | BMW 样本共 325 条消息 + 130 个信号 = 455 行；折叠只减少 130 行（29%）；没有任何实测卡顿证据；按样本密度外推，4 MB 约为万级消息，仅消息行就足以让首屏/滚动面临超过 100 ms 阈值的风险 | 取消「先折叠、测量后再决定」规则；4 MB 必须流畅，虚拟化成为增量 1 的核心 |
| 以「近 4 MB 合成 DBC」作为折叠基线 | 折叠对消息行数没有作用；按样本密度外推，4 MB 约为万级消息、数万条扁平行 | 明确 4 MB 性能包络，并同时记录折叠/全部展开两种状态 |
| 「消息级与信号级 `VAL_` 值表」 | DBC 的 `VAL_` 只作用于信号；另有 `VAL_TABLE_` 全局值表可被信号引用 | 修正为仅信号级，并支持 `VAL_TABLE_` 引用 |
| BMW 样本可验收属性、值表、多路复用 | 样本中 0 条 `BA_`/`BA_DEF_`、0 个多路复用信号、0 个 `VAL_TABLE_`；5 条文件级 `CM_` 中有 3 条没有分号，信号注释中存在引号内分号 | 新增专用语义夹具与编码夹具；BMW 只作真实基线；缺失分号按容错语法解析，不升级为 warning |
| `Ctrl/Cmd+K` 作为命令面板快捷键，「实施时再验证」 | Chromium 用它聚焦地址栏搜索，Firefox 用它聚焦搜索栏，网页无法可靠拦截；这是已知不可用项 | 改为 `/` 主入口 + 工作区顶栏按钮；`Ctrl/Cmd+Shift+F` 仅作可选次要绑定 |
| Windows-1252 通过 `CodePagesEncodingProvider` 回退 | Blazor WASM 上需要额外链接器配置，且历史上存在可用性问题；不能假设「引用即可用」 | 自实现 Windows-1252 映射表，零运行时依赖 |
| 全量重新序列化，保存前提示格式 diff | 全量重序列化会重写整个文件，产生大规模无关 diff，并与原始文本保真目标冲突 | 改为表单编辑 + 语句级文本回写，只改动目标语句 |
| 阶段 0/1/2 的三段式路线 | 虚拟化、命令面板、语义、编辑、持久化体量差异大；解析器还需独立升级 | 改为 6 个增量 + P0/P1/P2 解析器里程碑（见第 4 节） |

## 3. 产品定位

**第一优先级：检查与评审。**

- 帮助工程师打开真实 DBC 后完成查找、核对、语义查看和问题发现。
- 语义增强 v1 全部只读；原始文本模式仍是该阶段唯一直接编辑入口。
- 结构化语义编辑、下载和持久化放到后续增量，不提前混入只读语义版本。
- 产品不追求成为完整 DBC 编辑器；消息/信号 CRUD、节点/环境变量编辑、完整校验报告都不在本规划内。

**可验收的成功标准：**

- 4 MB 文件可流畅滚动、折叠、跳转和定位。
- 评审者能在一个 Properties 面板内看清 General、注释、属性、值表和多路复用，并能回到源行。
- 每个语义值都能说明来源（explicit / default / unresolved / not displayed / not interpreted）。
- 编辑只产生目标语句的最小 diff，下载文件可重新解析。
- 刷新后能按显式开启的持久化恢复工作区内容与打开状态。

## 4. 总体路线

```mermaid
flowchart LR
    I1["增量 1<br/>规模与导航"] --> I2["增量 2<br/>命令面板"]
    I2 --> P0["P0<br/>解析器 Spike 门禁"]
    P0 --> P1["P1<br/>核心语法与增量树"]
    P1 --> P2["P2<br/>完整语法覆盖"]
    P2 --> I4["增量 4<br/>语义视图与编码"]
    I4 --> I5["增量 5<br/>结构化编辑与下载"]
    I5 --> I6["增量 6<br/>会话持久化"]
```

| 增量 | 交付结果 | 主要风险 | 前置 |
|---|---|---|---|
| 1 规模与导航 | 4 MB 文件可折叠、滚动、键盘导航 | 虚拟化与 ARIA 网格、固定行高、焦点 | 无 |
| 2 命令面板 | 按名称/ID/发送者/Receiver 查找并精确跳转 | `/` 快捷键、模态焦点、窄屏落点 | 增量 1 |
| P0 Spike | 无 UI 变化；用真实测量决定架构是否继续 | green/red + 增量解析的内存与延迟 | 增量 1–2 |
| P1 核心语法 | 无 UI 变化；统一语法树替换旧解析器 | 增量复用、错误恢复、无损保真 | P0 通过 |
| P2 完整语法 | 无 UI 变化；完整标准语法类型化 | 遗留/厂商语句语法不确定 | P1 通过 |
| 4 语义视图与编码 | Properties 展示注释/属性/值表/多路复用；注释搜索 | 语义正确性、编码回退、术语 | P2 完成 |
| 5 结构化编辑与下载 | 表单编辑注释/属性/值表并下载新文件 | 回写锚点、重复条目、差异 | 增量 4 |
| 6 会话持久化 | 显式开启后刷新恢复工作区内容与打开状态 | 隐私、配额、损坏恢复 | 增量 5 |

**跨阶段约束：**

- 增量 1–2 只消费 `DbcDocument` 门面，不引用语法树内部结构。
- P1 必须保持 `DbcParser.Parse`、`DbcParseResult`、`DbcDocument.Messages/Signals` 和规范 `Serialize` 输出兼容；旧解析器只作为测试 oracle，parity 达成后删除。
- 增量 4 起，语义与编辑一律建立在新语法树上，不引入临时语义 scanner。
- P0 是门禁，不是演示：不达标不得继续完整 P。

## 5. 增量详情

### 5.1 增量 1：规模与导航

**目标：** 让 4 MB 级 DBC 可流畅扫描、折叠和键盘定位，同时不引入语义模型。

**范围：**

- 使用 `role="table"` 的 ARIA 网格（`rowgroup` / `row` / `cell` / `columnheader`）承载虚拟化行；`Virtualize` 的 spacer 为普通 block `div`；固定行高，消息行/信号行统一单行，长名称省略并通过 `title` 与 Properties 查看完整值。
- 原生 `<table>` + `SpacerElement="tr"` 在 4 MB 下实测产生 1.2–3.9 s 的表格布局；因此改用 ARIA 网格，键盘与 `aria-rowcount`/`aria-rowindex` 契约不变。
- 消息默认全部折叠；消息行显示 Signals 数量列；0 信号的消息不显示展开控件。
- 逐条展开/折叠；Expand all / Collapse all；折叠状态属于当前文档编辑器状态，不写入 DBC 内容。
- 单 Tab 停靠点 + 行级方向键（roving tabindex）：`↑/↓` 移动，`Home/End` 首尾，`PageUp/PageDown` 翻页；消息行 `→` 展开、`←` 折叠；`Enter/Space` 选中并更新 Properties。
- 行内展开按钮保留真实 `<button aria-expanded>`，但不进入 Tab 序列；滚动容器标注 `aria-rowcount`，行标注 `aria-rowindex`。
- 新增极小 JS helper：按行索引设置滚动位置并聚焦目标行；不引入自维护窗口算法。
- 新增开发/验证用性能页（复用现有 Verification 页面）：记录 `performance.now`、`PerformanceObserver` longtask 与托管内存；输出可复制的 JSON/表格。
- 实施顺序：先在 4 MB 生成文件上验证首屏、滚动与 Expand all，冻结 `ItemSize`/`OverscanCount`，再固化键盘与测试。

**不做：**

- 不改解析器、不加语义分区、不做命令面板。
- 不做内联编辑，不做可变行高。
- 不持久化折叠状态。

**验收：**

- 4 MB 合成包络（见 7.1）解析完成后首屏可交互 ≤ 2.0 s（参考机、Chrome stable、Release）。
- 滚动与键盘导航不产生 > 100 ms 主线程长任务；虚拟化后表格 DOM 行 ≤ 60。
- BMW 样本与 4 MB 包络的折叠/展开、Expand all、Collapse all 均有测量记录；Expand all 不产生 > 100 ms 长任务。
- 455 行 BMW 样本的折叠状态、Signals 数量、可见行与 aria 状态一致。
- 键盘可完成进入表格、移动、展开/折叠、选中；跳转后焦点落到目标行。
- 现有 113 项测试全部通过；表格外观、文本保存、窄屏切换不回归。
- Playwright Chromium 冒烟覆盖虚拟化滚动与键盘路径（见 7.3）。

**测试：**

- bUnit：折叠状态、行模型（消息/信号扁平索引）、键位状态迁移、aria-rowcount/rowindex、0 信号消息。
- Playwright：小文件折叠/展开/键盘；大文件虚拟化窗口有界、滚动到底、Expand all。
- 性能：记录首屏、滚动长任务、DOM 行数、内存。

**实现记录（2026-10-28）：**

- 原生 `<table>` + `SpacerElement="tr"` 在 4 MB / 28k 消息下实测：单条消息展开约 1.2 s，Expand all 约 3.9 s；根因是 89 万/269 万 px 的 spacer 行触发表格布局。
- 改为 ARIA 网格 + block spacer，并将行模型改为按需映射（只保存每条消息的起始行，`GetRow` 二分查找），不物化 84k 行。
- 4 MB 包络实测（Release / Chrome / 当前开发机）：解析完成后首屏 575–1,067 ms；Expand all 约 109–154 ms（长任务 77–99 ms）；Collapse all 134 ms；DOM 行 33–36；连续滚动无长任务；managed memory 约 36 MB。
- 旧解析器 4 MB 上传解析约 30 s，已记录为 P1 的输入；增量 1 不声称上传解析性能。

### 5.2 增量 2：命令面板

**目标：** 在当前文档内快速找到消息/信号并精确跳转，不依赖鼠标。

**范围：**

- 工作区级原生 `<dialog>` 模态，由 `DbcViewWorkspace` 持有；使用 `showModal()` 与最小 JS 互操作。
- 入口：
  - 工作区顶栏搜索按钮（有文档时常驻，窄屏 Explorer/Properties 激活时仍可见）。
  - `/` 主快捷键：由 document 级 JS 监听实现（capture + `preventDefault`），焦点在 input/textarea/select/contenteditable 时让位。
  - `Ctrl/Cmd+Shift+F` 仅作可选次要绑定，不作为验收前提。
- 搜索当前活动文档的最近一次解析结果，不跨文件。
- 字段：消息名、十进制 ID、`0x` 十六进制 ID、发送者；信号名、Receiver。
- 排序：精确匹配 > 前缀匹配 > 子串匹配；同层级按源文件顺序；v1 不做模糊匹配。
- 结果分 Messages / Signals 两组，总数上限 50；信号结果显示所属消息上下文；空输入只显示搜索提示，不显示“最近”。
- 键盘：输入框保持焦点，`aria-activedescendant` 指向当前项；`↑/↓`、`Home/End`、`Enter`、`Esc`；结果数量通过 live region 播报。
- 选中：关闭对话框；窄屏切到 Editor；展开父消息；滚动并聚焦精确信号行；更新 Properties。落点遵循 D8：窄屏也落 Editor。
- 不裁剪、不过滤 Editor 表格；Esc 关闭并把焦点还给触发按钮。

**不做：**

- 不做跨文件搜索、不做命令/动作面板（Expand all 等动作留在工具栏）。
- 不做注释搜索（增量 4 扩展）。
- 不做模糊匹配。

**验收：**

- `/` 在焦点不在可编辑控件时打开面板；在文本框内输入 `/` 不触发。
- 精确/前缀/子串排序有稳定测试；50 条上限生效；ID 同时支持十进制与 `0x`。
- 仅用键盘可完成打开、输入、上下选择、Enter 跳转、Esc 关闭。
- 跳转后父消息自动展开，目标信号行精确滚动并聚焦；窄屏落在 Editor。
- 面板是真正的模态：背景不可交互，关闭后焦点归还。
- 4 MB 包络下查询响应目标 ≤ 100 ms（记录实测值，不作为 CI 墙钟门禁）。
- Playwright Chromium 覆盖 `/`、焦点陷阱、跳转与窄屏落点。

**测试：**

- bUnit：匹配分级、结果分组、上限、键位、aria-activedescendant、空输入。
- Playwright：真实 `dialog.showModal()`、焦点归还、滚动定位、窄屏切换。
- 手工：Firefox 的 `/` Quick Find 拦截与 Safari 的模态焦点。

**增量 2 实施记录（2026-10-28）：**

- 工作区顶部工具栏在有活动文档时显示搜索按钮；`/` 由 document 级 JS 监听（capture、输入框/textarea/select/contenteditable 让位、无文档时不拦截）。`Ctrl/Cmd+Shift+F` 未实现（D6 可选，不作验收前提）。
- `DbcCommandPalette` 使用工作区级原生 `<dialog>` + `showModal()`；输入保持焦点，`aria-activedescendant` 跟随结果，支持 ↑/↓、Home/End、Enter、Esc；live region 播报结果数；空输入只显示提示，不显示“最近”。
- 搜索当前活动文档：消息名/十进制与 `0x` ID/发送者，信号名/Receiver；精确 > 前缀 > 子串，同级按源顺序；Messages/Signals 分组，总数上限 50；ID 采用数值精确匹配，查询文本保留原始空白、匹配时去首尾空白。
- 选择结果关闭模态、窄屏切回 Editor、展开父消息、滚动并聚焦精确信号行、更新 Properties；`DbcEditorNavigation` 支持面板初始化时消费 pending 请求。
- 修复 Enter 选择后浏览器把同一按键合成到新聚焦行、导致 Properties 覆盖 Editor 落点的问题：导航后的短窗口内忽略 `Detail == 0` 的合成点击，真实鼠标点击不受影响。
- 新增 21 项 bUnit（模型/组件/导航/工作区/工具栏），当前 **185 项**通过；新增 4 项 Playwright（`/` 打开、Esc 焦点归还、textarea 内 `/` 让位、键盘跳转、窄屏落点、十进制/`0x` ID），AOT 产物上 **7/7** 通过。
- AOT Release 4 MB 包络：`Sig_` 查询 **48 ms**（目标 ≤100 ms），返回 50 条、命中 56,000；同轮 `DbcParser.Parse` 578 ms，其余 P0/P1 预算保持。
- 未做：`Ctrl/Cmd+Shift+F` 可选绑定、Firefox/Safari 手工验证；无解析/序列化行为变化。

### 5.3 增量 3：解析器基础设施（P0 / P1 / P2）

**目标：** 用统一的词法/语法基础设施替换逐行正则解析器，取得完整保真、增量解析和类型化语义节点；本增量不改变任何 UI 行为。

#### P0：Spike 门禁（时间盒 3–5 天）

- 范围：手写 lexer + 最小 green/red 树（只覆盖 `BO_`/`SG_`/`CM_`）+ 整文件解析 + 单条语句增量替换；不做完整语法、不做 UI。
- 输入：BMW 样本 + 4 MB 合成包络（见 7.1）。
- 门禁：
  - 整文件解析 ≤ 1.2 s；
  - 单语句编辑后增量解析 ≤ 16 ms；
  - 4 MB 峰值托管内存 ≤ 200 MB；
  - 未变 green 节点/ token 可按引用复用；
  - 新旧文本与已覆盖的 `BO_`/`SG_`/`CM_` 子集解析结果一致。
- 失败处理：立即停止完整 P，先回到 token 表示（例如分块 struct）或重新评估增量粒度；不得带着未验证的核心假设继续。
- 产出：测量脚本与 4 MB 生成器必须留在仓库，供 P1/P2 复用。

**P0 实测记录（2026-10-28，Release WASM / Chrome）：**

P0 v1（逐语句对象 + Regex + 全量 token 列表）：

- 4 MB 包络：95,257 条语句。
- 全量解析：34.3 s（目标 ≤1.2 s，**未通过**）。
- 单语句增量：233 ms（目标 ≤16 ms，**未通过**）。
- green 托管内存 92.7 MB；red 后 135.3 MB。

P0 v2（compact statement arena + 手写字段解析 + piece table + 惰性 token/red）：

- 4 MB 包络：95,257 条语句。
- 全量解析：7.9 s（目标 ≤1.2 s，未通过）。
- 单语句增量：61 ms（目标 ≤16 ms，未通过）。
- green/red 托管内存约 45 MB（通过）。

P0 v3（分块 arena + 惰性 BO_/SG_/CM_ 字段解析 + 单遍扫描）：

- 4 MB 包络：95,257 条语句。
- 全量语法解析：3.5 s（目标 ≤1.2 s，**仍未通过**）。
- 单语句增量：6 ms（目标 ≤16 ms，**通过**）。
- 懒 red 视图：1 ms；green/red 托管内存约 37 MB（通过）。
- 无损与 green 身份复用：`lossless=true`，95,256 / 95,257 条语句复用。
- AOT 复测（安装 `wasm-tools`，`RunAOTCompilation=true`）：**parse 214 ms、edit 3 ms、managed 37 MB、lossless=true、95,256 / 95,257 复用 —— P0 门禁通过**。
- AOT 发布体积约 45 MB；非 AOT Release 的 parse 为 3.5 s。**AOT 是 4 MB 性能门禁的必要条件**；部署与 CI 必须显式启用 AOT，否则 1.2 s 目标不成立。
- 结论：P0 通过（AOT），可以进入 P1；但需先把 AOT 写入部署/CI 基线，并接受更大的构建时间与下载体积。当前 P0 代码仅作为测量/回归资产保留，不进入产品解析路径。

#### P1：核心语法与增量树

- 手写 lexer + 递归下降 parser；不引入解析器生成器。
- green/red 双层树：green 不可变、无 parent/position；red 惰性创建、带 parent/position；`WithChanges(TextChange)` 支持任意 token 级文本变更，未变子树按引用复用。
- token 文本以单一 `SourceText` 的 offset/length 表示，不复制字符串；具体对象/分块表示由 P0 测量决定，但必须满足内存预算。
- trivia 覆盖空白、换行、`//` 行注释、`(* … *)` 块注释；`CM_` 是语句节点，不是 trivia。
- `NS_` 块是 header 模式，其条目不是语句；`BO_`/`SG_`/`CM_`/`BA_`/`VAL_` 等关键字不得在 `NS_` 中被误识别。
- 语句级错误恢复：malformed 已知语句成为带内部诊断的节点，保留原文，后续语句继续解析；增量 4 之前不进入面向用户的诊断列表。
- 真实世界容错：缺失顶层分号、引号内分号、跨行字符串、转义引号按合法语句解析；缺失分号不产生 warning，编辑该语句时补规范分号。
- 完整词法覆盖；P1 类型化核心与常用标准语句（清单见附录 A）：Header、消息/信号、注释、属性、值表、环境变量、`BO_TX_BU_`、`SIG_GROUP_`、`SIG_VALTYPE_`、`SG_MUL_VAL_`。
- 未知厂商语句进入 `OpaqueStatement`，保留完整 token/trivia 与 span。
- 建立 v1 语义索引：文件/消息/信号注释、属性定义/默认值/显式值、信号 `VAL_`、`VAL_TABLE_`、`M`/`mN`。
- 公共门面保持兼容：`DbcParser.Parse`、`DbcParseResult`、`DbcDocument.Messages/Signals`、规范 `Serialize` 输出。语法树内部可见（测试可用 `InternalsVisibleTo`），不进入 UI 契约。
- 旧解析器只作为测试 oracle 保留到 parity 达成，产品中不得长期双解析器。

**P1 验收：**

- 113 项现有测试全部通过；BMW 的 Messages/Signals/诊断/规范序列化结果与旧解析器一致。
- P1 只保持现有 DBC001–DBC010 结构诊断兼容；语义层面的容错/未解析在 P1 内部记录，增量 4 之前不进入面向用户的诊断列表；新的语义诊断码在增量 4 引入。
- v1 子集语料差分对照一致；无损往返（`GetText()` 还原原文）。

**P1 实施记录（2026-10-28）：**

- 已将 `DbcParser.Parse` 切换到语法树 + `DbcSyntaxDocumentBuilder`；`ParseLegacy` 仅作为测试 oracle 保留，产品解析路径不再使用旧解析器。
- 手写消息/信号字段解析替代 Regex；诊断码、严重度、行号与旧解析器逐项差分一致。
- P1 常用标准语句已类型化：`BO_TX_BU_`（发送节点列表）、`SIG_GROUP_`（组名/重复次数/信号列表）、`SIG_VALTYPE_`（值类型）、`SG_MUL_VAL_`（多路复用信号与区间）、`EV_`/`EV_DATA_`/`ENVVAR_DATA_`（环境变量字段）；锚点缺失的已知语句回退为 `DbcOpaqueCommonStatement`，不误分类、不抛异常。
- v1 语义索引通过 `DbcParseResult.Semantics` 惰性构建：文件/消息/信号/节点/环境变量注释、属性定义/默认值/显式值、内联 `VAL_` 与 `VAL_TABLE_` 引用、typed 常用语句，以及消息/信号/节点/值表/属性的悬空引用清单；`M`/`mN` 由 `DbcSignal.Multiplexer` 承载，索引不重复存储。未解析项只留在索引内部，不进入用户诊断，符合 P1 验收边界。
- AOT Release 4 MB 实测（最终 P1）：语法 parse 156 ms；`DbcParser.Parse` 端到端 **815 ms**（≤1.2 s）；单语句增量 3 ms；green/red 37.0 MB；含数据模型 52.0 MB；28,000 消息 / 56,000 信号 / 0 诊断；语义索引惰性构建 559 ms，`semantic-unresolved=0`。
- 158 项单元/bUnit 通过；AOT 产物上的 3 项 Playwright 通过；solution 与浏览器测试项目的 `dotnet format --verify-no-changes` 通过。
- 修复 4 MB 生成器的语义引用：信号注释/值表改用真实 `Sig_<n>_0`，属性使用已定义的 `GenMsgCycleTime`，消除人为制造的 10,000 条悬空引用。
- 单语句编辑后未变 green 节点引用复用可断言（95,257 / 95,258）；增量结果与整文件重解析一致；`GetText()` 无损。
- 尚未完成：P2 完整语法覆盖（`BA_*_REL_`、`SIGTYPE_VALTYPE_`、`CAT_*`、厂商语句等），以及 token 级增量复用（D13 当前实现为语句级复用 + 惰性 token）。
- 无 UI 行为变化；无新增显示内容。

#### P2：完整语法覆盖

- 类型化遗留/罕见/厂商已知语句（清单见附录 A）：`SIGTYPE_VALTYPE_`、`BA_*_REL_`、`BA_DEF_SGTYPE_`/`BA_SGTYPE_`、`BU_*_REL_`、`CAT_DEF_`/`CAT_`、`FILTER`、`NS_DESC_`、`VECTOR__*` 等。
- 无法可靠识别的变体保留为 `OpaqueStatement`，不得用错误类型强行解析；在 “Not interpreted” 中可见。
- 无 UI 变化；独立验收。
- P2 完成前不得宣称“完整标准语法已支持”。

**P2 验收：**

- 附录 A 中每种类型化语句至少一个正例与一个错误恢复用例。
- 完整语法语料无损往返；未知语句不被误分类。
- 原 113 项基线不回归（当前 164 项全部通过）；P1 parity 保持；无 UI 行为变化。

**P2 实施记录（2026-10-28）：**

- 附录 A 的 P2 子集已建立 known/typed 记录：`SIGTYPE_VALTYPE_`、`BA_DEF_SGTYPE_`/`BA_SGTYPE_`、`BA_DEF_REL_`/`BA_REL_`/`BA_DEF_DEF_REL_`、`BU_SG_REL_`/`BU_EV_REL_`/`BU_BO_REL_`、`CAT_DEF_`、`FILTER`。关系语句解析节点/消息/信号并以 `DbcSemanticReference` 参与悬空检查。
- `CAT_`、`NS_DESC_`、`VECTOR__INDEPENDENT_SIG_MSG`、`VECTOR__XXX` 作为已知遗留/厂商语句保留原文；无法确认字段变体的语句回退为 `DbcOpaqueCommonStatement`，malformed 锚点不抛异常、不丢失语句。
- 新增 `DbcSemanticIndex.OpaqueStatements`：不在任何已知关键字集合中的顶格厂商语句单独成为 Raw 语句，保留原文与行号，并可无损序列化；修复此前未知语句被并入上一条语句、序列化时丢失的问题。未知语句不进入 typed 集合。
- `SGTYPE_`/`SGTYPE_VAL_`/`SIG_TYPE_REF_` 不在附录 A 的 P2 子集内；登记为已知但未解释（opaque），避免被误判为未知厂商语句，且不为它们声称字段级类型化。
- 新增 6 项 P2 测试：附录 P2 语句正例、malformed 回退、完整语料无损往返、未知语句保留/不误分类、Raw 边界、标准信号类型语句 known-opaque。全套 **164 项**通过；solution 与浏览器测试项目的 `dotnet format --verify-no-changes` 通过。
- AOT Release 4 MB 实测（最终 P2）：语法 parse 126 ms；`DbcParser.Parse` 端到端 **708 ms**（≤1.2 s）；单语句增量 2 ms；含数据模型 50.5 MB；28,000 消息 / 56,000 信号 / 0 诊断；语义索引惰性构建 339 ms，`semantic-unresolved=0`。
- AOT 产物上的 3 项 Playwright 通过；无 UI 行为变化；增量 4 的 “Not interpreted” 可直接消费 `OpaqueStatements`。
- 边界：P2 只声称附录 A 子集完成；`SGTYPE_`/`SGTYPE_VAL_`/`SIG_TYPE_REF_` 的字段级类型化留待后续。

### 5.4 增量 4：语义视图与编码

**目标：** 在 Properties 中展示可信的只读语义，并在命令面板中加入注释搜索。

**语义范围：**

- 文件/消息/信号级 `CM_`。
- `BA_DEF_`、`BA_DEF_DEF_`、`BA_`（文件/消息/信号级）；显示类型、范围/枚举、默认值、显式值、来源。
- 信号 `VAL_`：内联值对与 `VAL_TABLE_` 引用；`VAL_TABLE_` 定义只读展示。
- `M`/`mN` 简单多路复用关系。
- 重复条目：全部展示并保留源行号；属性值“最后一条生效”，标记 duplicate。
- 显式属性没有定义：按名称/值展示并产生 warning，不静默丢弃。
- 文件级属性显示在 File Properties；文件级属性在 v1 只读（编辑范围见增量 5）。
- General 补齐：Unit、Byte order（Intel/Motorola）、Signed、Receiver、Multiplexer 标记；消息 ID 同时显示十进制与十六进制。
- 不把厂商属性硬编码成白名单；未识别的合法属性按名称/值展示。

**不展示但已类型化（Not displayed）：**

- 节点/环境变量/信号组/`BO_TX_BU_`/`SG_MUL_VAL_` 等不进入 v1 Properties 语义区。
- 这些语句进入 **“Not displayed”** 分区，带源行号，不产生 warning。
- 含 `SG_MUL_VAL_` 的消息在 Multiplexing 区显示“扩展多路复用未解释”，避免评审者误以为已完整。
- 真正无法识别的厂商语句才进入 **“Not interpreted”**，同样不产生 warning。

**Properties 信息架构（七个分区）：**

1. **General**：始终展开；文件/消息/信号的基础字段。
2. **Comments**：文件/消息/信号注释；长文本换行；有内容时默认展开。
3. **Attributes**：名称、值、类型、范围/枚举、来源（explicit/default/undefined）、重复标记；有内容时默认展开。
4. **Value table**：条目数 + 前 10 条预览；“View all” 打开原生 `<dialog>`。
5. **Multiplexing**：`M`/`mN` 归属；扩展多路复用提示。
6. **Not displayed**：已类型化但不在展示范围的语句，按类型分组，预览 5 条。
7. **Not interpreted**：无法识别的语句，按类型分组，预览 5 条。

- 已知但 malformed 的语句保留原文并产生诊断；同样在 Not interpreted 中按行号列出，并标注诊断码。
- 每个条目显示源行号；行号是按钮，点击后切到 Text 视图、选中整条语句 span 并聚焦；窄屏先切到 Editor。源行定位要求 Text 缓冲区与已解析内容一致；存在未保存文本时先提示保存，不直接按旧位置选中。
- “View all” 使用面板内原生 `<dialog>`；超过 200 条时使用 `Virtualize`。
- 所有查看器 dialog 必须有可访问名称、打开时焦点进入、Esc 关闭、关闭后焦点返回触发按钮；键盘可滚动与浏览。
- 语义不可用（文档有 error）时，显示明确原因，不显示空分区。
- 文件选择时显示 File 信息：完整键、检测编码、诊断计数、文件级注释/属性、Not displayed / Not interpreted。

**编码：**

- 读取原始字节，再判定编码：
  1. BOM：UTF-8 / UTF-16 LE / UTF-16 BE；
  2. 严格 UTF-8（非法字节抛错，不静默替换）；
  3. 回退 Windows-1252：自实现 0x80–0x9F 映射表，其余按 Latin-1 直通。
- 1252 未定义字节（0x81/0x8D/0x8F/0x90/0x9D）映射为 U+FFFD；未定义字节计数在文档状态/File Properties 可见，不静默吞掉。
- 状态栏显示实际编码，例如 `Windows-1252 (fallback)`；不提供手动编码选择。
- 原始文本保存行为不因编码检测改变；下载编码见增量 5。

**命令面板扩展：**

- 增加消息/信号注释文本搜索；文件级注释不进入面板。
- 注释命中选中所属消息/信号，并在 Properties 的 Comments 区高亮匹配注释；不跳到原始行（源行定位由 Properties 提供）。

**验收：**

- `samples/semantics-full.dbc` 中每条语义能力都有可见结果：文件/消息/信号注释、全部属性类型、默认/显式/重复值、内联 `VAL_`、`VAL_TABLE_` 引用、`M`/`mN`、悬空引用、`SG_MUL_VAL_`。
- 属性来源明确区分 explicit/default/undefined；重复值有 duplicate 标记与源行号。
- `VAL_TABLE_` 引用能显示解析后的表名与条目；缺失表产生可见 warning。
- 扩展多路复用存在时显示“不完整”提示，不静默降级。
- 源行号按钮能把 Text 视图定位到整条语句；窄屏切到 Editor。
- `samples/legacy-windows1252.dbc` 的注释可读；状态栏/File Properties 显示检测编码与未定义字节计数。
- 非法 DBC 不出现语义视图；warning 文档仍允许语义视图。
- 命令面板可搜到消息/信号注释并用键盘跳转；文件级注释不出现在结果中。
- 现有 113 项测试、增量 1–2 行为不回归；Playwright Chromium 覆盖分区、dialog、源行定位与编码状态。

**增量 4 实施记录（2026-10-28）：**

- 编码：上传读取改为字节级检测（BOM UTF-8/UTF-16 LE/BE → 严格 UTF-8 → Windows-1252）；0x80–0x9F 按映射表解码，未定义字节计为 U+FFFD 并计数。编码元数据贯穿 `DbcFileEntry`、状态栏和 File Properties。
- Properties 七分区：General、Comments、Attributes、Value table、Multiplexing、Not displayed、Not interpreted；错误文档只显示原因，warning 文档仍可查看。Comments 按目标过滤，超过 20 条显示计数预览。
- Attributes：定义/默认/显式/未定义来源，类型/范围/枚举、重复值全部展示并标记 effective；未定义显式属性产生可见 warning；File 级属性只读。
- Value table：内联/`VAL_TABLE_` 引用、缺失表 warning；超过 10 条提供 View all 原生 dialog，超过 200 条使用 Virtualize；Esc/关闭后焦点归还。
- Not displayed / Not interpreted：已知未显示语句、节点/环境属性与注释、未知厂商语句、诊断码分组，预览 5 条并带源行号。
- 源行定位：`DbcParseResult.StatementSpans`；按钮切 Text、选中整条语句并聚焦；窄屏先切 Editor；存在未保存文本时阻止并提示保存。
- 注释搜索：命令面板匹配消息/信号注释，文件注释排除；选择后高亮 Properties Comments 中对应条目。
- 样本：`samples/semantics-full.dbc` 覆盖注释、属性重复/未定义、值表引用、`M`/`mN`、`SG_MUL_VAL_` 与未知厂商语句；`samples/legacy-windows1252.dbc` 覆盖编码回退。
- 测试：新增 25 项单元/bUnit，当前 **210 项**通过；Playwright 新增 5 项，AOT 产物 **12/12** 通过；两个 format 门禁通过。
- AOT Release 4 MB：语法 parse 186 ms；`DbcParser.Parse` **983 ms**（≤1.2 s）；单语句增量 4 ms；managed 53.1 MB；语义索引 560 ms；命令面板查询 82 ms（≤100 ms）、注释查询 67 ms；File Properties 构建 32 ms。
- 未做：Firefox/Safari 手工验证；语义编辑与下载属于增量 5。

### 5.5 增量 5：结构化编辑与下载

**目标：** 允许用户安全修改注释、属性和值表，并以最小 diff 带走结果。

**可编辑：**

- 文件/消息/信号级 `CM_`。
- 消息/信号 `BA_` 显式值。
- 信号 `VAL_` 值表（内联或 `VAL_TABLE_` 引用）。

**不可编辑：**

- 消息/信号的新增、删除、重命名。
- 节点、环境变量、信号组、`BO_TX_BU_`、`SG_MUL_VAL_`。
- 文件级 `BA_`（v1 只读）。
- 扩展多路复用。

**编辑形态与写回：**

- 使用表单/对话框编辑，不在虚拟化表格内做内联输入。
- 一条语句 = 一个编辑单元；Apply 后只替换对应语句的文本 span。
- 未修改的字节、空行、厂商语句、原始换行风格全部保持不变；只有被编辑的语句按规范格式重渲染。
- 新增语句：追加到同类型语句的最后一条之后；没有同类型块时追加到文件末尾；插入前后保证换行分隔。
- 删除语句：删除整条语句 span 及一个相邻换行，不重排其他空行。
- 重复属性/值表：编辑最后一条生效语句，并在表单中提示存在 N 条重复；重复注释：编辑用户选中的那一条并提示重复；都不静默改多条。
- 信号使用 `VAL_TABLE_` 引用时，首次编辑转换为内联 `VAL_`（copy-on-write）；全局表不变，并显示提示。
- 差异预览只展示被增/删/改的语句及行号，不展示整文件 diff。
- 下载前必须重新解析；存在 error 时禁止下载。
- 下载为新文件，绝不覆盖原文件；下载只导出当前文档，文件名示例为 `<原名>.reviewed.dbc`。
- 输出编码固定为 UTF-8（无 BOM）；若原文件不是 UTF-8（包括 1252 回退与 UTF-16），明确提示编码已转换。
- 不自动保存；提供 “Revert all edits” 丢弃本次会话的编辑；v1 不提供逐次 undo/redo。

**与原始文本 Save 的关系：**

- Apply 更新当前文档的内存内容与 Text 缓冲区，不写磁盘、不自动下载；切到 Text 视图应立即看到最新内容。
- 语义编辑要求 Text 视图没有未保存修改；存在未保存文本时禁用语义编辑并提示先 Save，避免两个写入口基于不同内容。
- 原始文本 Save 保持现有行为；它与语义 Apply 都作用于同一份内存内容，保存后重新解析。
- 两者都不覆盖本地原文件；导出统一走下载。

**验收：**

- 编辑注释/属性/值表后，差异预览只包含目标语句；未触及字节保持不变。
- 取消/关闭表单不修改内容；Apply 后可重新解析。
- 新增语句的锚点稳定；删除不破坏相邻语句与空行。
- 重复属性只修改最后一条并提示；`VAL_TABLE_` 引用编辑后变为内联副本，全局表不变。
- 下载内容可被独立解析器读取；存在 error 时下载被阻止并显示诊断。
- 下载文件为 UTF-8；非 UTF-8 原文件有编码转换提示。
- 现有增量与 113 项测试不回归；Playwright Chromium 覆盖编辑、diff、下载与阻止路径。

**增量 5 实施记录（2026-10-28）：**

- 新增 Application 层 `DbcStructuredEditor`：一条语句 span 替换/插入/删除，未触及字节、空行与原始换行风格保持不变；新增语句锚定同类型最后一条，删除仅移除语句 span 与一个相邻换行。规范化渲染注释、消息/信号 `BA_` 与 `VAL_`，字符串/枚举安全引用与转义。
- Properties 增加编辑动作：注释（文件/消息/信号，逐条编辑并提示重复）、消息/信号显式属性（重复时只编辑最后 effective 值）、信号值表（内联或引用；引用首次编辑转为内联副本，全局表不变）。表单使用原生 dialog，展示旧语句/新语句与行号；取消/Esc 不修改内容。
- Apply 通过 `ApplyStructuredEdit` 替换内存内容并重新解析；编辑器接收 reload 请求保留 Table/Text 模式；Properties 同步刷新。Text 缓冲区存在未保存修改时禁用语义编辑并提示先 Save。
- 编辑器增加 **Download** 与 **Revert all edits**。下载使用当前 Text 内容重新解析，存在 error 时阻止并显示诊断；文件名为 `<原名>.reviewed.dbc`，固定 UTF-8 无 BOM，非 UTF-8 源给出编码转换提示，绝不覆盖原文件。Revert all 恢复标签打开时基线内容。
- 测试：新增 23 项单元/bUnit，当前 **233 项**通过；Playwright 新增 5 项，AOT 产物 **17/17** 通过；两个 format 门禁通过。
- AOT Release 4 MB：语法 parse 149 ms；`DbcParser.Parse` **1,009 ms**（≤1.2 s）；单语句增量 4 ms；managed 53.1 MB；语义索引 548 ms；命令面板查询 100 ms、注释查询 82 ms；File Properties 36 ms。
- 未做：Firefox/Safari 手工验证；逐次 undo/redo 不在 v1 范围。

### 5.6 增量 6：会话持久化

**目标：** 在用户显式同意后，刷新可恢复工作区内容与打开状态。

**范围：**

- 默认关闭。
- 开启时用确认对话说明：数据只存本地浏览器、可能包含专有 DBC 内容。
- 存储：文件夹名、文件名、当前原始文本（含已 Apply 未下载的语义编辑）、打开的文档 ID、活动文档、每个文档的 Table/Text 视图模式。
- 不存储：选中项、折叠状态、滚动位置、Properties 分区展开状态、命令面板历史、面板布局。
- Text 视图中尚未 Save 的草稿不进入持久化；只保存工作区当前内容。
- 开启后变更防抖写入；状态栏显示最后保存时间。
- 配额不足、写入失败、数据损坏必须有可见状态，不静默丢数据。
- schema version；版本不匹配或数据损坏时整体丢弃并提示，不部分恢复。
- 开启状态下自动恢复，并提示恢复时间；提供 Clear/Forget，清除数据并关闭开关。
- 持久化只影响本地浏览器，不上传服务器。
- v1 不保证同一浏览器多标签并发写同一工作区；最后写入生效，并在状态中显示最后保存时间。

**验收：**

- 默认不写任何持久化数据。
- 开启后刷新可恢复文件夹/文件/原始文本/打开标签/活动标签/视图模式。
- 选中、折叠、滚动、分区状态不恢复（这是有意取舍）。
- Clear/Forget 后 IndexedDB 数据清除且开关关闭。
- 配额不足/损坏/schema 不匹配有可见提示，不静默丢数据。
- 应用层有 `IWorkspacePersistence` 端口，测试使用内存假实现；真实 IndexedDB 由 Playwright 刷新恢复覆盖。
- 现有行为与 113 项测试不回归。

**增量 6 实施记录（2026-10-28）：**

- Application 新增 `IWorkspacePersistence` 与 schema v1 快照：文件夹、文件（内容/编码元数据）、打开文档、活动文档、每文档 Table/Text 模式与保存时间；内存 fake 覆盖端口测试。
- Web IndexedDB 适配器实现 load/save/clear；无数据库时读取不创建数据库；JSON/schema 校验；读写异常返回可见错误；不访问网络。
- 工作区默认关闭；顶栏数据库按钮打开本地存储与专有内容确认对话框。启用后立即保存并 500 ms 防抖；状态栏显示最后保存时间；启动自动恢复并显示恢复时间提示。
- 恢复文件夹、文件内容、打开标签、活动标签与视图模式；不持久化选中/折叠/滚动/Properties 展开/面板布局；Text 未保存草稿不写入（只保存工作区当前内容）。
- 损坏或 schema 不匹配整体丢弃并显示 warning；Forget 清除 IndexedDB 且关闭；保存失败（含配额）在状态栏、banner 与对话框可见。
- 测试：新增 6 项单元/bUnit，当前 **239 项**通过；Playwright 新增 5 项，AOT 产物 **22/22** 通过；两个 format 门禁通过。
- AOT Release 4 MB：语法 parse 147 ms；`DbcParser.Parse` 776 ms；单语句增量 2 ms；managed 53.1 MB；语义索引 376 ms；命令面板查询 66 ms、注释查询 57 ms；File Properties 23 ms。
- 未做：多标签并发一致性（v1 最后写入生效）；Firefox/Safari 手工验证。

**Astrea Tree 接入记录（2026-10-28）：**

- Explorer 从自绘按钮树切换为 `Astrea.Components` 0.8.0 的 `Tree`（发布包，MIT）：文件夹/文件使用稳定节点 id，`SelectedIds` 受控映射到 `DbcTreeModel` 的“上传目标文件夹 + 活动文件”，`NodeTemplate`/`IconTemplate` 保留既有 `data-testid` 与 `DbcIcon`。
- `index.html` 引入 `_content/Astrea.Components/astrea-tree.css`；`app.css` 通过 `--ast-tree-*` 令牌适配现有主题，删除旧按钮树样式；展开/折叠箭头使用组件符号图标，折叠状态由受控 `ExpandedIds` 在树重建后保持。
- 单 Tab 停靠点、方向键与 `aria-activedescendant` 键盘导航由 Astrea 提供；多选模式用于同时显示上传目标文件夹与活动文件，上传失败、重复文件与未验证草稿提示保持不变。
- 测试：240 项单元/bUnit 通过；bUnit 保持严格 JSInterop（Astrea 模块单独以 loose 模式提供默认结果）；Playwright 22/22 通过（非 AOT 与 AOT 产物）。
- CI/deploy 的发布断言新增 Astrea 静态资产检查，避免 Tree 样式在发布产物中丢失。

## 6. 跨增量架构约束

| 约束 | 内容 |
|---|---|
| 分层 | Domain / Application / Infrastructure / Web 依赖方向不变；解析器与语法树在 Domain/Infrastructure，UI 只消费门面 |
| 公共契约 | `DbcParser.Parse`、`DbcParseResult`、`DbcDocument.Messages/Signals`、`IDbcCodec` 保持兼容；语法树不进入 UI 公共契约 |
| 表格 | ARIA table 语义（`role=table/rowgroup/row/cell/columnheader`）；`Virtualize` 在 rowgroup 内，spacer 为 block `div`；固定行高；JS helper 只负责滚动与聚焦 |
| 键盘 | 表格单 Tab 停靠点 + roving tabindex；面板 combobox；原生 dialog 自身焦点陷阱 |
| 浮层 | 命令面板为工作区级 `<dialog>`；Properties 内部查看器为面板内 `<dialog>`；Explorer 树消费 `Astrea.Components`，除此之外不新增 UI 组件库 |
| 导航通道 | 工作区 → 编辑器的一次性导航请求；只传目标消息/信号索引；编辑器渲染后展开、滚动、聚焦 |
| 编码 | 字节 → BOM → 严格 UTF-8 → Windows-1252；状态栏显示；未定义字节 U+FFFD |
| 编辑 | 表单 + 语句 span 回写；未触及字节不变；下载 UTF-8；不覆盖原文件 |
| 持久化 | 显式开启；内容与打开状态；不持久化浏览态；本地 IndexedDB |
| 术语 | **Not displayed** = 已类型化但不展示；**Not interpreted** = 无法识别；malformed 已知语句产生诊断 |

## 7. 性能、测试与验收门禁

### 7.1 性能包络

| 输入 | 规模 |
|---|---|
| BMW 真实样本 | 325 消息 / 130 信号 / 24 KB |
| 4 MB 合成包络 | 28,000 消息 / 56,000 信号 / 10,000 条语义语句 |
| 折叠状态 | 默认折叠与 Expand all 两种都必须测量 |
| 环境 | 当前开发机、Chrome stable、**AOT Release** 构建；结果记录 OS/CPU/RAM/浏览器版本 |

### 7.2 性能预算

| 指标 | 目标 | 适用阶段 | 门禁 |
|---|---:|---|---|
| 解析完成后首屏可交互（4 MB） | ≤ 2.0 s | 增量 1 起 | 验收记录 |
| 上传 + 整文件解析（4 MB，旧解析器） | 记录基线（实测约 30 s） | 增量 1 | 不作增量 1 门禁；P1 目标 ≤ 1.2 s |
| 整文件解析（4 MB） | ≤ 1.2 s | P1 起 | P0/P1 门禁 |
| 单语句编辑增量解析（4 MB） | ≤ 16 ms | P1 起 | P0/P1 门禁 |
| 滚动长任务 | 无 > 100 ms | 增量 1 起 | 验收记录 + 确定性 DOM 断言 |
| 虚拟化 DOM 行数 | ≤ 60 | 增量 1 起 | CI 断言 |
| 峰值托管内存（4 MB） | ≤ 200 MB | P1 起 | P0/P1 门禁 |
| 峰值托管内存（BMW） | ≤ 80 MB | P1 起 | P1 门禁 |
| 命令面板查询（4 MB） | 目标 ≤ 100 ms | 增量 2 起 | 记录，不作 CI 墙钟门禁 |

- CI 不做墙钟性能门禁；墙钟数据来自验证页与手动测量。命令面板 4 MB 查询实测 66 ms（AOT，`Sig_` 命中 56,000、返回 50 条）；注释查询 57 ms；File Properties 构建 23 ms。
- CI 使用确定性断言：DOM 行数、渲染次数、green 节点复用、无控制台错误。
- 性能页属于开发/验证面，不作为产品功能入口。
- 增量 1 的 2.0 s 指解析完成后的首屏渲染；旧正则解析器在 4 MB 上传时实测约 30 s，属于 P1 的替换范围，不计入增量 1 的门禁。若解析本身成为交互阻塞，应提前 P1 处理，而不是放宽预算。
- 所有 P0/P1 的 4 MB 解析与增量预算均以 **AOT Release** 为准；非 AOT Release 的 parse 实测 3.5 s，不得作为达标证据。部署与 CI 在启用 AOT 前，4 MB 承诺视为未兑现。

### 7.3 测试分层

| 层 | 范围 |
|---|---|
| Domain/Application 单元测试 | 语义模型、属性来源、重复策略、编辑锚点、持久化端口 |
| 解析器测试 | golden、差分（旧解析器 vs P1，仅 v1 子集）、无损往返、增量结果等价、green 引用复用、错误恢复、确定性变异/属性测试 |
| bUnit | 表格/键盘/ARIA、Properties 分区、dialog 结构、命令面板键位、编码状态、编辑表单 |
| Playwright Chromium（AOT 产物） | 上传、表格/文本切换、折叠、虚拟化滚动、`/` 面板、窄屏切换、源行定位、值表 dialog、编辑下载、持久化刷新恢复、无控制台错误 |
| 手工浏览器 | Firefox/Safari 的 `/`、原生 dialog 焦点、Text 选区与滚动；按发布清单执行 |

- 所有层都必须保留现有 113 项测试，不得以删除或弱化测试作为通过条件。
- bUnit 使用严格 JSInterop 桩；真实滚动、焦点、dialog 由 Playwright 验证。
- Playwright/手工矩阵覆盖 320/375/620/1440 px、浅色/深色；每档检查无控制台错误与关键焦点路径。
- CI 浏览器作业先以 `RunAOTCompilation=true` 发布并断言 native runtime ≥10 MB，再用静态服务器提供产物并运行 Playwright；deploy 同样使用 AOT 发布。AOT 是 4 MB 性能门禁的必要条件。

### 7.4 语料与夹具

- `samples/semantics-full.dbc`：文件/消息/信号注释；多行字符串、转义引号、引号内分号、缺分号；`BA_DEF_` 全类型；`BA_DEF_DEF_`；显式值/默认值/重复值；内联 `VAL_`；`VAL_TABLE_` 引用；`M`/`mN`；悬空引用；厂商语句；`SG_MUL_VAL_`。
- `samples/legacy-windows1252.dbc`：0x80–0x9F 可打印字符与未定义字节（0x81/0x8D/0x8F/0x90/0x9D）。
- 4 MB 生成器：固定 seed、参数化消息/信号数量；在测试/性能页临时生成并清理，不入库。
- P2 语法语料：附录 A 每种语句至少一个正例与一个错误恢复用例。
- 差分测试只对 v1 子集使用旧解析器；P2 语句以 golden/无损往返为主。

### 7.5 门禁汇总

- **P0 门禁**：解析/增量/内存预算全部达标；否则停止完整 P。
- **P1 门禁**：原 113 项基线不回归（P1 完成时 158 项，当前 239 项全部通过）；BMW parity；v1 子集差分一致；无损往返；增量引用复用；无 UI 行为变化。
- **P2 门禁**：附录 A 全覆盖；未知语句不被误分类；无 UI 行为变化；原 113 项基线不回归；完成前不得宣称完整语法。
- **增量 4–6 门禁**：各自验收 + Playwright Chromium + 手工跨浏览器 + 无回归；性能与内存记录更新。
- **全局门禁**：不以半成品进入 UI；无假完整状态；不删除既有测试；不静默丢数据。

## 8. 风险与应对

| 风险 | 影响 | 应对 |
|---|---|---|
| green/red + token 级增量解析复杂度高 | P 工期与稳定性 | P0 时间盒门禁；先验证最小树；不达标先调整 token 表示或增量粒度 |
| 4 MB 下语法树内存超预算 | 低端设备不可用 | token 文本只存 offset；P0 测量；必要时分块 struct；预算写进 P1 验收 |
| DBC 语法私有、工具行为不一致 | 完整语法难以一次正确 | 附录 A 清单 + 语料；无法可靠识别时保留 Opaque，不强行类型化 |
| 虚拟化与 ARIA 网格/固定行高冲突 | 键盘、滚动、焦点回归 | 增量 1 先做 spike；固定行高；最小 JS helper；Playwright 验证真实滚动 |
| 旧解析器与新解析器双份存在 | 行为漂移 | 旧解析器只作 oracle；parity 后删除；差分语料持续运行 |
| `/` 被 Firefox Quick Find 占用 | 快捷键不可用 | document 级 capture + preventDefault；真实 Firefox 手工验证；顶栏按钮始终可用 |
| 原生 dialog 在跨浏览器上的焦点差异 | 模态不可访问 | Playwright Chromium + Safari/Firefox 手工清单；不依赖 Atlas 缺失的 dialog 组件 |
| 1252 回退不是对所有遗留编码都正确 | 个别文件仍乱码 | 状态栏/File Properties 显示实际编码；未定义字节可见；不做虚假承诺 |
| 语句级回写锚点/重复条目复杂 | diff 或语义错误 | 固定插入/删除规则；重复条目只改最后一条并提示；测试覆盖 |
| `VAL_TABLE_` 引用被就地编辑会波及其他信号 | 语义被意外改变 | 首次编辑 copy-on-write 为内联 `VAL_`；全局表不变并提示 |
| 持久化涉及专有数据 | 隐私与合规 | 默认关闭；显式确认；本地 IndexedDB；Clear/Forget |
| 配额/损坏/版本不匹配 | 数据丢失 | 整体丢弃并提示；不部分恢复；不静默失败 |
| 完整语法类型化但不展示 | 审查面与维护成本 | 明确 Not displayed 术语；不扩大 v1 UI 范围；P2 独立验收 |
| Playwright 引入 flaky | CI 噪声 | 仅 Chromium；无墙钟断言；等待条件与重试；手工覆盖跨浏览器 |

## 9. 明确不纳入

以下能力不进入本规划：

- 消息/信号新增、删除、重命名。
- 节点、环境变量、信号组、`BO_TX_BU_`、`SG_MUL_VAL_` 的语义展示与编辑（类型化但 Not displayed）。
- 扩展多路复用 `SG_MUL_VAL_` 的语义解释。
- CAN FD / J1939 专项规则。
- ARXML、CSV、Excel 导入导出。
- 跨文件搜索、多文档对比。
- 实时 CAN 报文解码。
- 完整 DBC 校验报告导出。
- 手动编码选择。
- 云同步、多工作区、多标签并发持久化。
- 代码编辑器式内联文本编辑。
- 全量重新序列化与整文件 diff。
- 编辑 v1 之外的语句。
- 在命令面板中搜索文件级注释或跨文档内容。

## 10. 完成定义

- 6 个增量可独立审查、独立合并；P0 是门禁，P1/P2 可独立审查；不做跨增量大规模重写。
- P0 门禁、P1 parity、P2 完整语法验收必须先于对应后续增量。
- 用户可见增量（1、2、4、5、6）包含解析/领域测试、组件测试、Playwright Chromium 验证、必要的手工浏览器验证与文档更新；P0/P1/P2 以解析器测试、无损往返与性能门禁为主。
- 不以删除或弱化既有测试作为通过条件；113 项基线测试必须保持通过。
- 任何无行为按钮、硬编码语义、假完整状态都不得进入 UI。
- 性能与内存预算有可复现记录；CI 只做确定性断言，不做墙钟门禁。
- 增量 1–2 只消费门面；增量 4 起只使用新语法树；不保留临时 scanner 或长期双解析器。
- 只读边界在增量 5 之前不得突破；编辑能力必须建立在稳定的语法/语义模型之上。
- 任何未识别或未展示的内容都必须可见地归类，不得静默丢弃。

## 11. 决策记录

| # | 决策 | 结果 | 说明 |
|---|---|---|---|
| D1 | 4 MB 性能承诺 | 必须流畅 | 虚拟化从可选变为增量 1 核心 |
| D2 | 虚拟化路线 | 原生表格（初版）→ ARIA 网格 + block spacer | 原生表格在 4 MB 下因巨额 spacer 行布局耗时 1.2–3.9 s；ARIA 网格保留 table/row/cell 语义；固定行高；极小 JS helper |
| D3 | 键盘与可访问性 | 单 Tab 停靠点 + roving tabindex + aria-rowcount/rowindex | 展开按钮不进入 Tab 序列 |
| D4 | 初始路线图 | 5 个增量 | 已被 D14 取代 |
| D5 | 折叠默认态 | 默认折叠 + Signals 数量列 + 命中自动展开 | 0 信号不出展开控件 |
| D6 | 命令面板快捷键 | `/` 主入口 + 顶栏按钮 | `Ctrl/Cmd+Shift+F` 仅可选 |
| D7 | 命令面板宿主 | 工作区级原生 `<dialog>` + 一次性导航请求 | 窄屏不被面板隐藏影响 |
| D8 | 窄屏落点 | Editor，精确定位信号行 | Properties 数据同步更新 |
| D9 | 编码回退 | 自实现 Windows-1252 映射 | 严格 UTF-8 → BOM → 1252；状态栏显示 |
| D10 | 语义模型 | 统一语法解析器 | 不做独立语义 scanner |
| D11 | 语法模型 | 完整保真语法树 | 保留全部 token/trivia |
| D12 | 树结构 | Roslyn 式 green/red | 惰性 red；green 可共享 |
| D13 | 增量粒度 | 任意 token 级增量复用 | 表单编辑仍按语句回写 |
| D14 | 路线图 | 1 → 2 → P0 → P1 → P2 → 4 → 5 → 6 | 前两步只依赖门面；P 独立验收 |
| D15 | 语义范围 | 注释/属性/`VAL_`+`VAL_TABLE_`/简单多路复用 | 其余显式归类；文件级属性只读展示 |
| D16 | Properties 形态 | 可折叠分区 + 源行定位 | 初始六区，D24 后扩展为七区 |
| D17 | 命令面板行为 | 确定性三级排序 + 分组 + 50 条 + combobox | 只搜当前文档；文件级注释不进面板 |
| D18 | 编辑写回 | 表单 + 语句级文本回写 | 未触及字节不变；下载 UTF-8 |
| D19 | 语法基础设施边界 | CST 内部可见 + 硬预算 | CI 不做墙钟门禁 |
| D20 | 风险控制 | P0 Spike 门禁 | 3–5 天；不达标停止 |
| D21 | 验证资产 | Playwright Chromium 冒烟 + 手工跨浏览器 + 小型夹具 + 生成器 | 4 MB 文件不入库 |
| D22 | 持久化 | 内容与打开状态，不含浏览态 | 显式开启；schema 不匹配整体丢弃 |
| D23 | 语法覆盖 | 完整标准语法全部类型化 | 手写 lexer/parser；未知厂商语句 Opaque |
| D24 | P 拆分与术语 | P1 常用语法 + P2 遗留/厂商语法 | Not displayed 与 Not interpreted 分离；v1 展示范围不变 |

## 附录 A：完整语法清单

| 语句 | 阶段 | v1 展示 | 备注 |
|---|---|:---:|---|
| `VERSION` | P1 | 否（Header） | 版本头 |
| `NS_` | P1 | 否（Header） | 关键字列表；条目不是语句 |
| `BS_` | P1 | 否（Header） | 波特率段 |
| `BU_` | P1 | 否（节点数据） | 节点集合 |
| `BO_` | P1 | 是 | 消息 |
| `SG_` | P1 | 是 | 信号 |
| `BO_TX_BU_` | P1 | Not displayed | 额外发送节点 |
| `SIG_GROUP_` | P1 | Not displayed | 信号组 |
| `SIG_VALTYPE_` | P1 | Not displayed | 浮点/双精度标记 |
| `CM_`（文件/`BO_`/`SG_`） | P1 | 是 | 注释 |
| `CM_`（`BU_`/`EV_`） | P1 | Not displayed | 节点/环境变量注释 |
| `BA_DEF_`（文件/`BO_`/`SG_`） | P1 | 是 | 属性定义 |
| `BA_DEF_`（`BU_`/`EV_`） | P1 | Not displayed | 节点/环境变量属性定义 |
| `BA_DEF_DEF_`（文件/`BO_`/`SG_`） | P1 | 是 | 默认值 |
| `BA_`（文件/`BO_`/`SG_`） | P1 | 是 | 显式值 |
| `BA_`（`BU_`/`EV_`） | P1 | Not displayed | 节点/环境变量属性值 |
| `VAL_` | P1 | 是 | 信号值表；内联或引用 |
| `VAL_TABLE_` | P1 | 是（只读） | 全局值表定义 |
| `EV_` | P1 | Not displayed | 环境变量 |
| `EV_DATA_` / `ENVVAR_DATA_` | P1 | Not displayed | 环境变量数据 |
| `SG_MUL_VAL_` | P1 | Not displayed | 扩展多路复用；触发提示 |
| `SIGTYPE_VALTYPE_` | P2 | Not displayed | 遗留/罕见 |
| `BA_DEF_REL_` / `BA_REL_` / `BA_DEF_DEF_REL_` | P2 | Not displayed | 关系属性 |
| `BA_DEF_SGTYPE_` / `BA_SGTYPE_` | P2 | Not displayed | 遗留信号类型属性 |
| `BU_SG_REL_` / `BU_EV_REL_` / `BU_BO_REL_` | P2 | Not displayed | 关系语句 |
| `CAT_DEF_` / `CAT_` / `FILTER` / `NS_DESC_` | P2 | Not displayed | 遗留语句 |
| `VECTOR__INDEPENDENT_SIG_MSG` / `VECTOR__XXX` | P2 | Not displayed | 厂商已知语句 |
| 其他未知厂商语句 | P1 词法/CST | Not interpreted | OpaqueStatement；保留原文 |

**边界：**

- “完整标准语法”指上表中可可靠类型化的语句；语法存在多种变体且无法确认时，保留为 Opaque，不得用错误类型强行解析。
- P2 完成前，计划只能声称“P1 核心语法已支持”，不得声称完整语法。
- 完整语法类型化不扩大 v1 的展示与编辑范围。

## 附录 B：事实核对来源

- DBC 信号值表、`VAL_TABLE_` 与属性语法：[cantools DBC Format](https://deepwiki.com/cantools/cantools/2.1.1-dbc-format)、[DBC 文件格式解析](https://devpress.csdn.net/v1/article/detail/111598606)
- `VAL_TABLE_` 被 `VAL_` 引用的实例：[DBC 文件避坑指南](https://m.blog.csdn.net/o0p1q2r3/article/details/154321796)
- DBC 字符串可跨行、允许转义引号：[msalau/dbc-parser](https://github.com/msalau/dbc-parser)
- DBC 格式与样本背景：[CAN DBC File Explained — CSS Electronics](https://www.csselectronics.com/pages/can-dbc-file-database-intro)
- 浏览器保留快捷键：[Chromium issue 41081444](https://issues.chromium.org/41081444)
- Blazor WASM 上 CodePages/Windows-1252 的历史问题：[mono/mono#18618](https://github.com/mono/mono/issues/18618)
