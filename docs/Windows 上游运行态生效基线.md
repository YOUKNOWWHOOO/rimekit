# Windows 上游运行态生效基线

## 头部骨架

- 正式文件路径：`docs/Windows 上游运行态生效基线.md`
- 对应的本文档章节：`2-官方依据、事实分级与禁止猜测原则`、`7.4-Windows 基线`、`7.6-官方兼容基线、资源基线与更新基线`、`7.7-正式目标配置文件、正式资源文件与正式用户数据边界`、`7.8-配置模型基线`、`7.11-正式阶段、结果语义与冲突语义基线`、`9.2-Windows 客户端职责`、`11-图形界面覆盖范围`、`14.2-Windows`、`15-正式治理记录与文档骨架`
- 覆盖范围：Windows 侧当前已经通过真实运行态验证确认的上游配置语义、直接运行态配置方法、真实生效判定方法、已确认误区，以及后续 CLI / GUI 必须对齐的目标基线。
- 明确非目标：不替代 `docs/需求规范.md` 的正式总基线；不替代 `docs/需求更新清单.md` 的变更记录；不把“直接改目标文件后生效”误表述为“CLI 已通过”或“GUI 已通过”；不承担 Android 侧运行态基线职责。
- 输入：Mintimate 官方文档、Weasel 官方 Wiki、当前工作树中的共享契约与生成逻辑、本地已部署的 `rime_mint.schema.yaml` / `weasel.yaml`、`scripts/groups/` 下 13 组 × 38 个独立相位脚本（原方案，2026-05-28）+ 30 组 × 90 phase（2026-05-30 扩展）、`_common_ci_test.ps1` + `toolkit/` 探针链、2026-05-27 至 2026-05-28 的真实输入与视觉验证结果。
- 输出：一份无需依赖对话上下文即可独立使用的 Windows 上游运行态生效基线，供后续 CLI / GUI 实现、回归验证、问题排查和验收口径校正使用。
- 真值源：Windows 侧上游运行态行为以官方资料、当前上游 schema / yaml、当前已部署运行目录、以及经清洁状态下复现的真实运行态证据为准；产品入口是否合格仍以正式入口链路验证为准。
- 正式任务类型：上游语义核验、运行态生效基线固化、产品入口对齐基线编制。
- 正式阶段映射：本文件自身不直接执行运行时阶段；文中记录的复现实验涉及 `detect -> apply -> deploy -> recheck -> manual_confirmation` 的证据链，但这些链路在本文件中只用于说明当前“上游怎样才会生效”，不等价于产品正式入口已闭环。
- 失败边界：若某个功能的官方定义不清、当前运行态证据不稳定、或无法区分“上游生效”与“产品入口生效”，本文件必须明确写成“未定”“仅确认上游”“不能据此判定 CLI / GUI 通过”，不得模糊处理。
- 验收方式：读者仅凭本文件即可理解每个 Windows 侧已验证能力的官方定义、正确配置位置、正确复现方法、当前证据边界和产品后续必须对齐的具体目标。

## 1. 文档定位

本文档是**Windows 上游运行态生效基线**，不是产品 GUI / CLI 验收通过报告。

本文档要解决的问题不是“按钮能不能点”“CLI 命令有没有返回成功”，而是以下更基础、但此前被混淆的问题：

1. 对某个能力，上游 `Rime / Weasel / rime_mint` 的**官方定义**到底是什么。
2. 对某个能力，当前 Windows 机器上到底要把哪些文件、哪些字段、哪些取值改成什么，才能让真实输入法运行态出现目标效果。
3. 某个效果如果已经在运行态出现，这只能证明“找到了正确的上游配置方法”，还是已经足够证明“RimeKit CLI / GUI 已经正确实现”。

本文档的正式结论固定如下：

1. 本文件中的“已验证通过”只表示：**当前已经确认上游运行态会按本文档所述方式生效**。
2. 本文件中的任何“直接写 `%APPDATA%\\Rime\\*.yaml` 后生效”的方法，都只能算“上游运行态基线”或“宿主直接配置基线”，**不能直接算 CLI 通过，更不能直接算 GUI 通过**。
3. 后续 CLI 和 GUI 的正确目标，是“通过产品正式入口，稳定地产生与本文档相同的目标文件、目标部署状态和用户可见结果”，而不是重新发明另一套配置语义。
4. 如果本文件与现有产品实现冲突，以本文件确认过的**官方定义 + 上游运行态真实结果**为准，再反向修正文档、CLI、GUI 和测试。

## 2. 背景与问题陈述

在 2026-05-12 至 2026-05-13 的多轮 Windows 验证中，已经确认一个关键风险：**“真实运行态验证通过”和“产品正式入口验证通过”不是同一件事**。

此前存在过三类混淆：

1. 直接改 `%APPDATA%\\Rime\\rime_mint.custom.yaml` 或 `%APPDATA%\\Rime\\weasel.custom.yaml`，再 `WeaselDeployer /deploy`，最后通过真实输入结果或截图看到效果，就被误叙述成“功能已完成”。
2. 通过 `scripts/run_full_verification.ps1`、AHK 探针、视觉截图、直接注入词典或直接补写 patch 字段确认某项能力上游能工作后，容易被误读成“CLI / GUI 已自动完成同样工作”。
3. 对某些开关的**官方定义理解错误**，导致验证场景本身就错。例如：
   - `tone_display` 的官方定义是**preedit 拼音带调显示**，不是普通拼音候选窗里的声调旁注。
   - `show_notifications` 的官方定义是**输入状态变化通知**，不是所有 Weasel 提示，更不包括 deploy / redeploy 提示。

因此，本文件被正式设立为中间基线：

1. 先把当前已经确认无误的**上游运行态真实定义**写死。
2. 先把当前已经确认能生效的**正确配置方法**写死。
3. 再要求后续 CLI / GUI 以本文件为唯一对齐目标，分别证明自己能通过正式入口稳定复现这些结果。

如果没有这一步，后续工程会持续重复以下错误：

1. 把“找到了有效 patch”误当成“产品已实现”。
2. 把“按钮能点”“字段写入配置模型”误当成“运行态已生效”。
3. 把上游定义不清、验证场景不对、产品入口未走通三类问题混成一团。

## 3. 使用规则

本文件必须按以下规则使用：

1. 读者不得把本文件中的“直接改运行目录文件后生效”当作最终用户正式操作路径。
2. 读者不得把本文件中的“上游生效证据”直接写进 `CLI 功能闭环审计表` 或 `GUI 功能闭环审计表` 的 `C` 类结论。
3. 本文件只能作为以下工作的真值源：
   - 上游语义澄清
   - 真实运行态基准复现
   - CLI 后续对齐目标
   - GUI 后续对齐目标
   - 问题排查时判断“到底是上游本身无此能力，还是产品没把能力接通”
4. 对后续产品验证，必须显式区分三层：
   - `上游运行态基线已确认`
   - `CLI 正式入口已确认`
   - `GUI 正式入口已确认`
5. 只要后两层尚未完成，本文件不得被用于对外宣称“Windows 端功能已收口”。

## 4. 真值源顺序

本文件内涉及 Windows 侧上游运行态判断时，真值源顺序固定如下：

1. **官方文档**
   - Mintimate 官方站点中与 `rime_mint` 行为直接对应的页面。
   - Weasel 官方 Wiki 中与 Windows 呈现、通知、字体、布局等直接对应的页面。
2. **当前上游 schema / yaml**
   - 当前已部署的 `%APPDATA%\\Rime\\rime_mint.schema.yaml`
   - 当前已部署的 `%APPDATA%\\Rime\\weasel.yaml`
   - 当前工作树中的 `workspace/windows/resources/rime_mint/current/` 资源副本
3. **当前已部署运行目录**
   - `%APPDATA%\\Rime\\default.custom.yaml`
   - `%APPDATA%\\Rime\\rime_mint.custom.yaml`
   - `%APPDATA%\\Rime\\weasel.custom.yaml`
   - `%APPDATA%\\Rime\\dicts\\*.dict.yaml`
   - `%APPDATA%\\Rime\\*.gram`
4. **清洁状态下复现的真实运行态证据**
   - AHK / Python 探针输出
   - 候选窗或状态通知截图
   - 实际上屏结果

若上述来源彼此冲突，处理顺序固定如下：

1. 先确认是不是验证场景用了错定义。
2. 再确认是不是上游 schema / yaml 已经更新，而旧理解没有同步。
3. 再确认是不是产品写入的目标文件与预期不一致。
4. 不得直接跳到“产品已经坏了”或“官方定义就是这样”而不先核实来源层级。

## 5. 当前验证环境与前置条件

本文件记录的 Windows 上游运行态基线，建立在以下已知前提上：

1. 当前工作目录：`C:\\Users\\desktop_storage\\Documents\\rimekit`
2. 当前输入法承载器：`Weasel 0.17.4`
3. 当前方案主线：`rime_mint`
4. 当前模型主线：`wanxiang-lts-zh-hans.gram`
5. 当前运行目录：`%APPDATA%\\Rime\\`
6. 当前 deployer 路径模式：`C:\\Program Files\\Rime\\weasel-*\\WeaselDeployer.exe`
7. 当前真实输入探针主链路：
    - `apps/windows/RimeKit.Windows.Tests/scripts/toolkit/probe_notepad_ime.py`
    - `apps/windows/RimeKit.Windows.Tests/scripts/toolkit/probe_ime_toggle.ahk`
    - `apps/windows/RimeKit.Windows.Tests/scripts/toolkit/probe_ime_type.ahk`
    - `apps/windows/RimeKit.Windows.Activator/bin/Debug/net10.0-windows/RimeKit.Windows.Activator.exe`
8. 当前视觉验证链路：
   - `Pillow.ImageGrab.grab()`
   - AHK 打字或状态切换
   - 全屏 JPEG / PNG 证据

本文件中的“已确认”都建立在**清洁初始状态**前提下：

1. 已卸载并重装 Weasel。
2. `%APPDATA%\\Rime\\` 已回到可控初始状态。
3. 薄荷方案、词典、模型由本轮验证重新部署。
4. 不复用旧缓存、不复用旧截图、不复用旧临时结论。

## 6. 文档内记录的能力类别

本文件把当前已确认的 Windows 上游运行态能力分为两类：

1. **真实输入探针类能力**
    - 通过 `scripts/groups/` 下 13 组独立相位脚本和真实输入探针确认
    - 以“实际输入 -> 候选 / 上屏结果”作为主证据
2. **视觉 / 呈现类能力**
   - 通过候选窗截图、主题 / 字体 / 字号对比图、状态变化通知截图确认
   - 以“可见 UI 结果变化”作为主证据

当前已经纳入本文件的已确认能力共 19 项：

| 类别 | 能力标识 | 目标结果 |
|---|---|---|---|
| 真实输入探针 | `basic_nihao` | `nihao -> 你好` |
| 真实输入探针 | `custom_lcbh` | `lcbh -> 流程閉環 / 流程闭环` |
| 真实输入探针 | `fuzzy_zisi` | `zisi -> 只是` |
| 真实输入探针 | `ascii_punct` | `.` 输出全角或中文侧标点变体 `．` |
| 真实输入探针 | `full_shape` | `123 -> １２３` |
| 真实输入探针 | `trad_toufa` | `toufa -> 頭髮` |
| 真实输入探针 | `moetype_aboguai` | `abaiguai -> 阿柏怪` |
| 真实输入探针 | `sogou_omoomo` | `omoomo -> 哦模哦模` |
| 真实输入探针 | `model_long` | `jianjiandejiubuzaiyile -> 漸漸地就不在意了 / 渐渐地就不在意了` |
| 视觉 / 呈现 | `emoji_candidate` | `kaixin` 候选中出现 emoji |
| 视觉 / 呈现 | `candidate_count` | 候选数量按设置变化 |
| 视觉 / 呈现 | `candidate_direction` | 候选排列方向按设置变化 |
| 视觉 / 呈现 | `candidate_comment` | Emoji 注释开关按设置变化 |
| 视觉 / 呈现 | `theme_switch` | 主题颜色按设置变化 |
| 视觉 / 呈现 | `font_change` | 字形按字体设置变化 |
| 视觉 / 呈现 | `font_size` | 字号按设置变化 |
| 视觉 / 呈现 | `tone_display` | preedit 拼音带调显示 |
| 视觉 / 呈现 | `status_notify` | 输入状态变化通知按设置变化 |
| 视觉 / 呈现 | `input_method_picker` | Win+Space 打开输入法选择器 |

## 7. 上游运行态能力汇总表

下表只回答一个问题：**当前已经确认怎样配置，上游运行态会生效。**

| 能力标识 | 上游定义是否已澄清 | 已确认的直接运行态配置位置 | 配置层 | 当前证据类型 | 是否可直接据此判定 CLI / GUI 通过 |
|---|---|---|---|---|---|---|
| `basic_nihao` | 是 | `rime_mint` 已安装并为当前方案 | 薄荷方案 | 实际上屏 | 否 |
| `custom_lcbh` | 是 | `dicts/custom_simple.dict.yaml` | 薄荷方案 | 实际上屏 | 否 |
| `fuzzy_zisi` | 是 | `rime_mint.custom.yaml -> speller/algebra/+` | 薄荷方案 | 实际上屏 | 否 |
| `ascii_punct` | 是 | `rime_mint.custom.yaml -> switches/+ -> ascii_punct` | 薄荷方案 | 实际上屏 | 否 |
| `full_shape` | 是 | `rime_mint.custom.yaml -> switches/+ -> full_shape` | 薄荷方案 | 实际上屏 | 否 |
| `trad_toufa` | 是 | `rime_mint.custom.yaml -> switches/+ -> transcription` | 薄荷方案 | 实际上屏 | 否 |
| `moetype_aboguai` | 是 | `dicts/moetype.dict.yaml + rime_mint.dict.yaml import` | 薄荷方案 | 实际上屏 | 否 |
| `sogou_omoomo` | 是 | `dicts/sogou_network_popular_words.dict.yaml + rime_mint.dict.yaml import` | 薄荷方案 | 实际上屏 | 否 |
| `model_long` | 是 | `.gram + grammar patch + deploy` | 薄荷方案 | 实际上屏 | 否 |
| `emoji_candidate` | 是 | `rime_mint.custom.yaml -> switches/@1/reset` | 薄荷方案 | 候选窗截图 | 否 |
| `candidate_count` | 是 | `rime_mint.custom.yaml -> menu/page_size` | 薄荷方案 | 候选窗截图 | 否 |
| `candidate_direction` | 是 | `rime_mint.custom.yaml + weasel.custom.yaml` | **跨两层** | 候选窗截图 | 否 |
| `candidate_comment` | 是 | `translator/always_show_comments (+ Weasel comment 透明色)` | **跨两层** | 候选窗截图 | 否 |
| `theme_switch` | 是 | `weasel.custom.yaml -> style/color_scheme` | Weasel 承载器 | 候选窗截图 | 否 |
| `font_change` | 是 | `weasel.custom.yaml -> style/font_face` | Weasel 承载器 | 候选窗截图 | 否 |
| `font_size` | 是 | `weasel.custom.yaml -> style/font_point` | Weasel 承载器 | 候选窗截图 | 否 |
| `tone_display` | 是 | `rime_mint.custom.yaml -> switches/@3/reset` | 薄荷方案 | preedit 截图 | 否 |
| `status_notify` | 是 | `weasel.custom.yaml -> show_notifications` | Weasel 承载器 | 输入状态变化通知截图 | 否 |
| `input_method_picker` | 是 | `SendInput(Win+Space)` 打开系统输入法选择器 | 承载器交互 | 选择器截图 | 否 |

> **配置层说明**：19 项能力分布在两层——**薄荷方案层**（`rime_mint.custom.yaml`，`/deploy` 重编译生效）控制输入行为（12 项），**Weasel 承载器层**（`weasel.custom.yaml`，需重启 WeaselServer 生效）控制视觉呈现（4 项），**跨两层**（2 项：`candidate_direction` 和 `candidate_comment` 同时写入两个文件），**承载器交互**（1 项：`input_method_picker` 通过 Win+Space SendInput 触发）。详情见 `开发约定与最佳实践.md` §29.6.2。

## 8. 详细能力条目

### 8.1 `basic_nihao`

- 能力名称：基础全拼出字
- 官方定义：`rime_mint` 作为薄荷主方案，普通全拼输入应能在当前方案激活后产生基础拼音候选。
- 已确认的直接运行态配置方法：
  1. 确保 `%APPDATA%\\Rime\\` 中存在 `rime_mint.schema.yaml`、`rime_mint.dict.yaml`、`default.custom.yaml`。
  2. `default.custom.yaml` 至少包含 `schema_list: - schema: "rime_mint"`。
  3. 调用 `WeaselDeployer.exe /deploy`。
- 已确认的验证方法：
  1. 激活 Weasel。
  2. 在 Notepad 中输入 `nihao`。
  3. 提交候选。
  4. 观察是否得到 `你好`。
- 当前已确认结果：nihao -> 你好
- 当前已知边界：当前上游运行态中，薄荷主方案基础全拼链路可用。但不能证明 RimeKit GUI 的输入方案页、应用设置、启用输入方案已正确接通。测试中使用的 default.custom.yaml 是直接写入而非通过产品 CLI/GUI 产生。“输入方案页 / 应用设置 / 启用输入方案”已经正确接通。

### 8.2 `custom_lcbh`

- 能力名称：自定义词条出字
- 官方定义：Rime 的 custom_simple 词典机制允许用户通过 TSV 格式追加自定义词条（文本、编码、权重），追加后重新部署即可被引擎吸收。这是薄荷方案内置的词典扩展接口。
- 当前已确认的直接运行态配置方法：
  1. 在 `%APPDATA%\\Rime\\dicts\\custom_simple.dict.yaml` 中增加词条，例如：
     `流程闭环\tlcbh\t1000001`
  2. 重新 deploy。
- 已确认的验证方法：
  1. 输入 `lcbh`
  2. 提交候选
  3. 观察是否得到 `流程閉環` 或 `流程闭环`
- 当前已确认结果：该链路可稳定触发自定义词条优先命中。
- 当前已知边界：
  1. 这是“直接操作 `custom_simple` 词典”的上游有效路径。
  2. 它不能直接证明 GUI 的“新增词条 / 应用用户词条”已经正确落到同一位置。

### 8.3 `fuzzy_zisi`

- 能力名称：模糊音
- 官方定义：薄荷方案通过 speller/algebra 的 derive 规则实现模糊音匹配。官方默认不开启，但提供了 zh/z ch/c sh/s 三条常用模糊规则作为推荐预设。
- 当前已确认的直接运行态配置方法：
  1. 在 `rime_mint.custom.yaml` 中追加：
     - `speller/algebra/+`
     - `derive/^zh([a-z]+)$/z$1/`
     - `derive/^ch([a-z]+)$/c$1/`
     - `derive/^sh([a-z]+)$/s$1/`
  2. 重新 deploy。
- 已确认的验证方法：
  1. 输入 `zisi`
  2. 提交候选
  3. 观察是否得到 `只是`
- 当前已确认结果：`zisi -> 只是`
- 当前已知边界：
  1. 这证明了“当前这组三条 derive 规则”在上游运行态中有效。
  2. 它不能直接证明 GUI 里的“启用模糊音”与表格规则编辑会生成同样的 algebra。

### 8.4 `ascii_punct`

- 能力名称：英文标点 / 西文标点样式
- 官方定义：ascii_punct 是 rime_mint 方案内置开关，控制中文输入状态下标点符号的样式。关闭时输出中文标点（。），开启时输出西文标点（.）。与 full_shape 开关存在交互（见 9.8）。
- 当前已确认的直接运行态配置方法：
  1. 在 `rime_mint.custom.yaml` 中通过 `switches/+` 定义或覆写 `ascii_punct`，并令其 `reset: 1`。
  2. 重新 deploy。
- 已确认的验证方法：
  1. 在中文输入状态下直接输入 `.`
  2. 观察上屏结果
- 当前已确认结果：`.` 可上屏为 `．`
- 当前已知边界：
  1. 这证明 `ascii_punct` 的上游开关和当前 patch 组合有效。
  2. 它不能直接证明 GUI 的“英文标点”复选框已经把字段写到正确位置。

### 8.5 `full_shape`

- 能力名称：全角
- 官方定义：full_shape 是 rime_mint 方案内置开关，控制字符形状。关闭时输出半角字符（半角），开启时输出全角字符（全角）。全角模式下 ASCII 字符被转换为全角对应字符。
- 当前已确认的直接运行态配置方法：
  1. 在 `rime_mint.custom.yaml` 中通过 `switches/+` 定义或覆写 `full_shape`，并令其 `reset: 1`。
  2. 重新 deploy。
- 已确认的验证方法：
  1. 在中文输入状态下输入 `123`
  2. 提交或直接上屏
  3. 观察是否得到全角数字
- 当前已确认结果：`123 -> １２３`
- 当前已知边界：只能证明当前全角开关上游有效，不能直接证明 GUI / CLI 入口已经正确接通。

### 8.6 `trad_toufa`

- 能力名称：繁体模式
- 官方定义：transcription 是 rime_mint 方案内置开关，通过 OpenCC 的 simplifier 过滤器实现简繁转换。关闭时输出简体中文，开启时输出繁体中文。配置模型中对应的字段是 simplification_mode。
- 当前已确认的直接运行态配置方法：
  1. 在 `rime_mint.custom.yaml` 中通过 `switches/+` 定义或覆写 `transcription`，并令其 `reset: 1`。
  2. 重新 deploy。
- 已确认的验证方法：
  1. 输入 `toufa`
  2. 提交候选
  3. 观察是否得到 `頭髮`
- 当前已确认结果：`toufa -> 頭髮`
- 当前已知边界：只能证明简繁转换链路上游有效。

### 8.7 `moetype_aboguai`

- 能力名称：moetype 词库生效
- 官方定义：moetype 是本产品正式纳管的增补词库（来源 suiginko/moetype），包含约 20 万条 ACG 领域专有名词。启用后词条通过 rime_mint.dict.yaml 的 import_tables 导入薄荷方案的词典链。
- 当前已确认的直接运行态配置方法：
  1. 安装 `moetype` 资源。
  2. 将 `moetype.dict.yaml` 复制到 `%APPDATA%\\Rime\\dicts\\moetype.dict.yaml`。
  3. 在 `%APPDATA%\\Rime\\rime_mint.dict.yaml` 的 `import_tables` 中追加 `dicts/moetype`。
  4. 重新 deploy。
- 已确认的验证方法：
  1. 输入 `abaiguai`
  2. 提交候选
  3. 观察是否得到 `阿柏怪`
- 当前已确认结果：`abaiguai -> 阿柏怪`
- 当前已知边界：这证明 `moetype` 的词典导入路径有效，但不能直接证明 GUI 或 CLI 的“安装 / 启用词库”已按同一导入方式落盘。

### 8.8 `sogou_omoomo`

- 能力名称：搜狗网络流行新词词库生效
- 官方定义：sogou_network_popular_words 是本产品正式纳管的增补词库（来源搜狗拼音词库），以 SCEL 格式下载后经 ConvertSogouScelToRimeYaml 转换为 Rime 词典格式，通过 rime_mint.dict.yaml 的 import_tables 导入薄荷方案的词典链。
- 当前已确认的直接运行态配置方法：
  1. 安装 `sogou_network_popular_words` 资源。
  2. 将 `sogou_network_popular_words.dict.yaml` 复制到 `%APPDATA%\\Rime\\dicts\\sogou_network_popular_words.dict.yaml`。
  3. 在 `%APPDATA%\\Rime\\rime_mint.dict.yaml` 的 `import_tables` 中追加 `dicts/sogou_network_popular_words`。
  4. 重新 deploy。
- 已确认的验证方法：
  1. 输入 `omoomo`
  2. 提交候选
  3. 观察是否得到 `哦模哦模`
- 当前已确认结果：`omoomo -> 哦模哦模`
- 当前已知边界：这证明当前搜狗词库转换与导入结果可被运行态吸收。

### 8.9 `model_long`

- 能力名称：语法模型长句效果
- 官方定义：万象官方语法模型（wanxiang-lts-zh-hans.gram）是薄荷方案当前正式纳管的语法模型资源。通过 rime_mint.custom.yaml 中的 grammar/language、grammar/collocation_max_length、grammar/collocation_min_length 三个参数激活，为 rime_mint 提供句子级候选排序和长句预测能力。
- 当前已确认的直接运行态配置方法：
  1. 将 `wanxiang-lts-zh-hans.gram` 放入 `%APPDATA%\\Rime\\`。
  2. 在 `rime_mint.custom.yaml` 中写入：
     - `"grammar/language": "wanxiang-lts-zh-hans"`
     - `"grammar/collocation_max_length": 8`
     - `"grammar/collocation_min_length": 2`
  3. 当前可复现实证中，上述 patch 与本轮同时保留的 `speller/algebra/+`、`switches/+` 组合一同 deploy。
  4. 重新 deploy。
- 已确认的验证方法：
  1. 输入 `jianjiandejiubuzaiyile`
  2. 提交候选
  3. 观察是否得到 `漸漸地就不在意了` 或 `渐渐地就不在意了`
- 当前已确认结果：该长句模型效果已在真实运行态复现。
- 当前已知边界：
  1. 本文件确认的是“这一组模型文件 + patch 组合”可以生效。
  2. 本文件不宣称已经证明 GUI 的模型页或 CLI 的模型安装工作流完整闭环。

### 8.10 `emoji_candidate`

- 能力名称：Emoji 候选
- 官方定义：`emoji_suggestion` 是方案内开关，普通候选窗中可出现 emoji 候选。
- 当前已确认的直接运行态配置方法：
  1. 在 `rime_mint.custom.yaml` 中控制 `"switches/@1/reset"`：
     - `0`：关闭
     - `1`：开启
  2. 重新 deploy。
- 已确认的验证方法：
  1. 输入 `kaixin`
  2. 不提交，只看候选窗
  3. 观察 `Emoji 关闭` 与 `Emoji 开启` 两态差异
- 当前已确认结果：
  1. 关闭时不出现 emoji 候选。
  2. 开启时候选窗出现 emoji 候选。
- 当前已知边界：这只证明上游 `emoji_suggestion` 开关有效。

### 8.11 `candidate_count`

- 能力名称：候选数
- 官方定义：menu/page_size 是 Rime 引擎控制每页候选数量的参数。薄荷方案的默认候选数由 rime_mint.schema.yaml 决定，可通过 rime_mint.custom.yaml 覆写。
- 当前已确认的直接运行态配置方法：
  1. 在 `rime_mint.custom.yaml` 中写入 `"menu/page_size": <整数>`。
  2. 当前已确认的对照值为 `3` 与 `6`。
  3. 重新 deploy。
- 已确认的验证方法：
  1. 输入 `nihao`
  2. 不提交，只看候选窗
  3. 对比 `page_size=3` 与 `page_size=6`
- 当前已确认结果：候选窗可见候选数量会随 menu/page_size 改变。
- 当前已知边界：这只证明了上游 menu/page_size 参数对候选窗可见候选数的影响路径有效。本文件不宣称 RimeKit 产品的 GUI 候选数控件或 CLI 对应字段已正确接通。

### 8.12 `candidate_direction`

- 能力名称：候选排列方向
- 官方定义：style/candidate_list_layout 控制候选窗中候选项的排列方向。竖排（stacked）为 Weasel 默认，横排（linear）为可选布局。该设置同时涉及 rime_mint.custom.yaml 和 weasel.custom.yaml 两处配置。
- 当前已确认的直接运行态配置方法：
  1. 在 `rime_mint.custom.yaml` 中写入 `"style/candidate_list_layout": "vertical"` 或 `"horizontal"`。
  2. 在 `weasel.custom.yaml` 中同步写入：
     - 竖排：`"style/candidate_list_layout": "stacked"`、`"style/horizontal": false`
     - 横排：`"style/candidate_list_layout": "linear"`、`"style/horizontal": true`
  3. 重新 deploy。
- 已确认的验证方法：
  1. 输入 `nihao`
  2. 不提交，只看候选窗
  3. 对比竖排与横排两态
- 当前已确认结果：候选窗方向可按上述组合稳定切换。
- 当前已知边界：候选排列方向的设置需要 rime_mint 和 weasel 两侧同时修改。Weasel 承载器层使用 Weasel 原生值 `"stacked"`/`"linear"`，薄荷方案层使用 Rime 引擎原生值 `"vertical"`/`"horizontal"`。产品内部以 oh-my-rime 模板值（`"stacked"`/`"linear"`）为统一词汇——写入各自文件时输出正确的值，不使用翻译层（见 `需求规范.md` §7.4.76.1）。

### 8.13 `candidate_comment`

- 能力名称：Emoji 注释
- 官方定义：Rime 有两个独立配置项影响候选窗注释——`translator/spelling_hints`（每个候选旁显示拼音编码，含声调）和 `translator/always_show_comments`（强制注释始终显示）。在 `rime_mint` 中，`spelling_hints` 由 schema 内置固定为 8（始终开启），不由产品配置模型控制；产品只通过 `ShowEmojiComments` / `show_emoji_comments` 控制 `always_show_comments`。emoji 候选右侧的文字注释（如 [开心]）由 `always_show_comments` 控制，其可见性也受 Weasel 侧 comment 颜色设置影响。
- 当前已确认的直接运行态配置方法：
   1. 在 `rime_mint.custom.yaml` 中控制 `"translator/always_show_comments": false/true`。
      - `spelling_hints` 在 `rime_mint` schema 中固定为 8（始终显示拼音编码），无需也不应在 custom.yaml 中覆写。
   2. 在某些需要对照更明显的场景下，关闭态还需在 `weasel.custom.yaml` 中把 comment 颜色设为透明，以消除残余可见注释。
  3. 重新 deploy。
- 已确认的验证方法：
  1. 输入 `kaixin`
  2. 不提交，只看候选窗
  3. 观察 emoji 候选右侧 `[开心]` 是否出现
- 当前已确认结果：Emoji 注释可被上述组合控制。
- 当前已知边界：这里确认的是“当前 comment 可见性链路”，不是所有 comment 内容语义都已被产品正确解释。
- 当前已知详情：
   1. `spelling_hints`（拼英提示）在 `rime_mint` schema 中固定为 8（始终显示拼音编码含声调）。薄荷方案使用 `script_translator` 而非 `table_translator`，标准 `spelling_hints` patch 覆盖机制对其无效，产品配置模型中不包含独立的拼英提示开关（已于 §23.6 移除 `ShowSpellingHints` 字段）。
   2. 候选窗声调旁注（如 `nǐ[你] hǎo[好]`）来源于 `spelling_hints`，不是 `tone_display`。`tone_display` 只控制 preedit 拼音是否带声调。
   3. 产品只通过 `show_emoji_comments` 控制 `always_show_comments`。当 `ShowEmojiComments = true` 时生成 `always_show_comments: true`，反之生成 `false` 并将 Weasel 侧 comment 颜色设为透明。
   4. u-mode 拆字（`Uuniuniuniu` → `犇`）是 `super_preedit.lua` 的独立功能，不受任何产品配置开关控制。

### 8.14 `theme_switch`

- 能力名称：日间 / 夜间主题切换
- 官方定义：style/color_scheme 是 Weasel 承载器层面的候选窗配色方案。薄荷方案提供 mint_light_blue（日间）和 mint_dark_blue（夜间）两种预设配色。该设置仅需 weasel.custom.yaml，与 rime_mint 方案层无关。
- 当前已确认的直接运行态配置方法：
  1. 在 `weasel.custom.yaml` 中写入 `"style/color_scheme": "mint_light_blue"` 或 `"mint_dark_blue"`。
  2. 重新 deploy。
- 已确认的验证方法：
  1. 输入 `nihao`
  2. 不提交，只看候选窗
  3. 对比颜色差异
- 当前已确认结果：两种配色在候选窗中有明显差异。
- 当前已知边界：这只证明 Weasel 上游 color_scheme 切换能力有效。本文件不宣称 RimeKit 产品的 GUI 主题选择控件已正确接通。

### 8.15 `font_change`

- 能力名称：字体变化
- 官方定义：style/font_face 是 Weasel 承载器层面的候选窗字体设置。接受任意系统已安装字体的名称。该设置仅需 weasel.custom.yaml。
- 当前已确认的直接运行态配置方法：
  1. 在 `weasel.custom.yaml` 中写入 `"style/font_face": "<字体名>"`。
   2. 当前已确认的对照值为 `Microsoft YaHei UI` 与 `霞鹜文楷 GB 屏幕阅读版`。
  3. 重新 deploy。
- 已确认的验证方法：
  1. 输入 `nihao`
  2. 不提交，只看候选窗
  3. 对比字形差异
- 当前已确认结果：字形可见差异已经复现。
- 当前已知边界：这只证明 Weasel 上游 font_face 切换能力有效。本文件不宣称 RimeKit 产品的 GUI 字体输入控件已正确接通。
- Weasel 字体栈语法（根据 Weasel 官方 Wiki）：`font_face` 支持多字体分段渲染，格式为 `字体甲 [:[起始码位][:结束码位][:字重][:字形]] [, 字体乙 ...]`。各字体按顺序回退：当前字符在首位字体中不存在时顺位使用下一个字体。码位以十六进制指定（如 `30:39` 表示仅渲染数字 0-9）。oh-my-rime 的默认字体栈为复杂的 Emoji 字体组合字符串，用户覆盖时只需输入单一字体名，无需编写完整字体栈。

### 8.16 `font_size`

- 能力名称：字号变化
- 官方定义：style/font_point 是 Weasel 承载器层面的候选窗字号设置。取值为整数点数。该设置仅需 weasel.custom.yaml。
- 当前已确认的直接运行态配置方法：
  1. 在 `weasel.custom.yaml` 中写入 `"style/font_point": <整数>`。
  2. 当前已确认的对照值为 `12` 与 `24`。
  3. 重新 deploy。
- 已确认的验证方法：
  1. 输入 `nihao`
  2. 不提交，只看候选窗
  3. 对比字号差异
- 当前已确认结果：候选窗字体大小可按设置明显变化。
- 当前已知边界：这只证明 Weasel 上游 font_point 切换能力有效。本文件不宣称 RimeKit 产品的 GUI 字号控件已正确接通。

### 8.17 `tone_display`

- 能力名称：声调显示
- 官方定义：
  1. `tone_display` 控制的是**preedit 拼音带调显示**。
  2. 它不是“普通拼音候选窗右侧是否显示带声调注释”的官方定义。
  3. 当前本地上游 schema 注释明确写明：它影响的是 `preedit_format`，归属 `super_preedit.lua`。
- 官方来源：
  1. 本地 schema 注释：`%APPDATA%\\Rime\\rime_mint.schema.yaml`
  2. Mintimate 官方文档：
     - `https://www.mintimate.cc/zh/demo/reverseWords.html`
     - `https://www.mintimate.cc/en/demo/reverseWords.html`
- 当前已确认的直接运行态配置方法：
  1. 在 `rime_mint.custom.yaml` 中控制 `"switches/@3/reset"`：
     - `0`：关闭
     - `1`：开启
  2. 重新 deploy。
- 已确认的验证方法：
   1. 进入任意拼音输入场景。
   2. 当前已确认的复现输入为 `nihao`。
   3. 只观察输入框中的 preedit 拼音是否带声调，不再拿普通拼音候选窗或 u-mode 拆字当标准。
- 当前已确认结果：
   1. 在任意拼音输入链路中，开启时 preedit 可见带调拼音。
   2. 因此该功能按**官方定义**已确认有效。
- 当前已知边界：
   1. `tone_display` 只控制 preedit 拼音是否带声调，对任意拼音输入（如 `nihao`、`Uuniuniuniu` 等）均生效。
   2. u-mode 拆字（`Uuniuniuniu` → `犇`）是 `super_preedit.lua` 的独立功能，不受 `tone_display` 控制。用拆字验证 `tone_display` 会误导读者将两个独立功能混为一谈。
   3. `tone_display` 不控制普通候选窗中候选词的声调旁注。候选窗声调旁注的来源是 `spelling_hints`（见 8.13），在 `rime_mint` 中由 schema 内置的 `super_preedit.lua` filter 控制，不由产品配置模型独立开关。
   4. 产品 GUI 中的"声调显示"标签指 preedit 声调显示。若产品文档或界面对此有错误表述，应修正。

### 8.18 `status_notify`

- 能力名称：输入状态变化通知
- 官方定义：
  1. `show_notifications` 控制的是**状态变化（方案内 options / schema）通知**。
  2. **部署信息不受此项影响。**
- 官方来源：
  1. Weasel 官方 Wiki：`https://github.com/rime/weasel/wiki/Weasel-%E5%AE%9A%E5%88%B6%E5%8C%96`
  2. 本地 `weasel.yaml` 注释：`%APPDATA%\\Rime\\weasel.yaml`
- 当前已确认的直接运行态配置方法：
  1. 在 `weasel.custom.yaml` 中控制 `"show_notifications": true/false`。
  2. 重新 deploy。
- 已确认的验证方法：
   1. 不看 deploy / redeploy 提示。
   2. 只看输入状态变化提示。
   3. 触发方式：通过产品 CLI `set-config` + `apply` 设定 `show_notifications` 后，重启 WeaselServer，用 AHK `Send("{Shift}")` 触发中英模式切换，**立刻截图**捕获通知弹窗（ON 状态应可见弹窗，OFF 状态不应出现）。
   4. 截图时机必须在 Shift 后 1 秒内，通知弹窗仅短暂显示。
- 当前已确认结果：
  1. `show_notifications = true` 时，可观察到输入状态变化提示。
  2. `show_notifications = false` 时，上述提示消失。
- 当前已知边界：
  1. 若关闭后仍看到某类提示，必须先确认那类提示是不是 deploy / redeploy 信息。
   2. 本文件不把 deploy 提示纳入 `status_notify` 的官方验收范围。

### 8.19 `input_method_picker`

- 能力名称：打开输入法选择器
- 官方定义：
   1. `open-input-method-picker` CLI 命令通过 `SendInput` 发送 `Win+Space` 组合键，打开 Windows 输入法选择器。
   2. 该能力为半自动操作（`ManualActionRequired`）——命令只有发送按键的职责，程序化验证选择器是否弹出依赖视觉截图。
- 当前 CLI 实现：
   1. 命令：`open-input-method-picker --format json`
   2. 退出码 1 = `ManualActionRequired`（预期行为，非错误）
   3. 内部调 `WinSpaceInputMethodPickerLauncher` → `SendInput(Win, Space, Win up)`
- 已确认的验证方法：
   1. CLI 调用 → 验证 exit=1（ManualActionRequired，收到即代表命令已执行）
   2. 视觉验证：用 AHK 脚本长按 `{LWin down}→Sleep(0.5s)→{Space}→Sleep(3s)→{LWin up}` 保持选择器可见，截图捕获
   3. ON 截图应显示 Windows 输入法选择器浮层；OFF 截图为基线（无选择器）

## 9. 已发现的关键实践与误用预防

以下实践是通过 `scripts/groups/` 下 13 组独立相位脚本（`groups/` + `_common_ci_test.ps1`）的真实输入探针验证过程中发现的关键工程方法论。这些不属于上游语义定义（不是“某个开关开或关上屏会怎样”），但属于“知道怎么配置能生效、怎么做会失败、CLI/GUI 必须怎么设计才能避免重蹈覆辙”的必需知识。

后续 CLI/GUI 的实现、验证、回检和排障，必须遵循本节实践。违反本节中任何一条已知正确实践，都不得宣称对应功能“已完成”或“C 类闭环”。

### 9.1 部署顺序：薄荷方案与语法模型必须分两次 deploy

- **正确做法**：
  1. 先将薄荷方案（oh-my-rime 全部文件，含 `default.custom.yaml`）部署到 `%APPDATA%\\Rime\\`。
  2. `WeaselDeployer.exe /deploy`。
  3. 验证 `%APPDATA%\\Rime\\build\\rime_mint.prism.bin` 存在。
  4. 再将语法模型（`.gram` 文件 + `rime_mint.custom.yaml` 含 grammar 参数）部署到 `%APPDATA%\\Rime\\`。
  5. 再次 `WeaselDeployer.exe /deploy`。
- **已确认会失败的错误做法**：将薄荷方案和语法模型文件混在一次 deploy 中 → 编译互相干扰，`rime_mint.prism.bin` 无法生成，后续所有输入均无候选。
- **CLI/GUI 对齐要求**：
  1. 产品的 `apply` 工作流（以及 GUI 的“应用设置”）在首次安装或从无方案状态启动时，必须保留分步部署的能力。
  2. 不得在一次 `WeaselDeployer /deploy` 调用中同时提交薄荷方案全部文件 + 语法模型文件。
  3. 部署后必须验证 `rime_mint.prism.bin` 存在，不存在时必须重新 deploy 并显式告警。

### 9.2 开关覆写：必须使用 `switches/+` 格式

- **正确做法**：在 `rime_mint.custom.yaml` 中通过 `switches/+` 完整重新定义开关，格式如下：
  ```yaml
  patch:
    "switches/+":
      - name: full_shape
        states: ["半角", "全角"]
        reset: 1
  ```
  名称（`name`）、显示标签（`states`）和默认值（`reset`）缺一不可。
- **已确认会失败的错误做法**：使用嵌套属性路径写法，例如 `"switches/full_shape/reset": 1`。这种写法在 Rime 的 patch 机制中不生效——patch 是顶层 key 粒度的合并，不是任意嵌套路径的覆写。
- **CLI/GUI 对齐要求**：
  1. `ArtifactService.cs` 中的 `rime_mint.custom.yaml` 生成逻辑必须使用 `switches/+` 格式。
  2. 不得在任何生成路径中输出 `"switches/<name>/reset": <value>` 形式的 patch 行。
  3. 若已有生成代码使用了错误格式，必须在本轮修正。

### 9.3 词库导入链：复制 dict 文件 + 注入 `import_tables`

- **正确做法**：
  1. CLI `install-resource` 下载词库（moetype、sogou）到 `workspace/windows/resources/`。
  2. 将生成的 `.dict.yaml` 文件复制到 `%APPDATA%\\Rime\\dicts\\`。
  3. 在 `%APPDATA%\\Rime\\rime_mint.dict.yaml` 的 `import_tables` 中追加新的导入条目。
  4. 导入条目必须插入在 `rime_ice.others` 之后（否则可能被后续条目覆盖优先级）。
  5. 仅复制 dict 文件而不注入 `import_tables`，词库不会被 Rime 引擎加载。
- **已确认会失败的错误做法**：只复制 dict 文件到 `dicts/` 目录，不修改 `rime_mint.dict.yaml` 的 `import_tables` → 词库文件存在但 Rime 不加载。
- **已知细节**：
  1. moetype 生成两份同内容文件：`moetype.dict.yaml` 和 `tone_moe.dict.yaml`。导入引用使用 `dicts/moetype`（基于文件名的 stem，不含扩展名，无需包含 `tone_moe` 变体）。
  2. sogou 从 `.scel` 格式转换而来，见 §9.4。
- **CLI/GUI 对齐要求**：
  1. 产品的词库安装工作流必须同时完成“复制 dict 文件 + 注入 import_tables”两步。
  2. `ArtifactService.ApplyWindowsTargets` 或等价的生成路径必须确保 `rime_mint.custom.dict.yaml` 的 `import_tables` 包含已启用的外部词典。
  3. GUI 的“下载并部署词库”按钮不得只执行文件复制而跳过 import 注入。

### 9.4 SCEL 词库转换：sogou 是 `.scel` 源格式

- **正确做法**：
  1. sogou 网络流行新词的源格式是 `.scel`（搜狗细胞词库二进制格式）。
  2. 必须通过 `ConvertSogouScelToRimeYaml` 把 `.scel` 转换为 Rime 可读的 `.dict.yaml` 格式，再进行 §9.3 的导入。
  3. 转换后必须做 UTF-8 编码验证和有效词条数校验（>0）。
- **已确认会失败的错误做法**：把 `.scel` 文件直接当作 `.dict.yaml` 导入，或把来源 HTML 页面本身冒充为正式词库。
- **CLI/GUI 对齐要求**：
  1. CLI `install-resource --resource-id sogou_network_popular_words` 必须内部完成 SCEL→YAML 转换。
  2. 转换失败必须在安装阶段即阻断，不得让后续 deploy 加载损坏的词库文件。

### 9.5 `custom_simple` 格式与生效

- **正确做法**：
  1. 追加到 `%APPDATA%\\Rime\\dicts\\custom_simple.dict.yaml`。
  2. TSV 格式：`文本\t编码\t权重`，例如 `流程闭环\tlcbh\t1000001`。
  3. 追加后必须 `WeaselDeployer.exe /deploy` 才会被 Rime 引擎吸收。
   4. 该文件已在 oh-my-rime 的 `rime_mint.dict.yaml` 的 `import_tables` 中第一位（`dicts/custom_simple`），无需额外注入。
- **已确认会失败的错误做法**：追加词条到 `custom_simple.dict.yaml` 后不执行 `WeaselDeployer.exe /deploy` → 词条文件虽已落盘，但 Rime 引擎未重新编译词库缓存，新词条在运行时不会出现。
- **CLI/GUI 对齐要求**：
  1. GUI “应用用户词条”和 CLI apply 必须确保 `RenderCustomSimpleDictionaryYaml()` 生成的文件格式、字段顺序与本节一致。
  2. 用户词条追加后必须自动触发 deploy/recheck（用户不应需要手动点 deploy）。

### 9.6 Fresh download：每次验证必须无条件重新下载

- **正确做法**：
  1. 每次验证运行前，删除 `workspace/windows/resources/<resource_id>` 整个目录。
  2. 删除 `workspace/windows/state/installed_resources.json`。
  3. 无条件执行 `install-resource`，禁止任何 `if (Test-Path) { skip }` 逻辑。
- **已确认会失败的错误做法**：复用前次运行缓存的资源文件，导致旧文件与新部署预期不一致，验证结果不可信。
- **CLI/GUI 对齐要求**：本实践主要用于测试验证管线，但在产品的检查更新与重新安装场景中同样适用——当检测到资源版本不一致时，应强制重新下载而非依赖缓存。

### 9.7 Deployer 调用方式：`Start-Process -Wait`，禁止 `-WindowStyle Hidden`

- **正确做法**：
  1. `Start-Process -FilePath <WeaselDeployer.exe> -ArgumentList "/deploy" -Wait`
  2. Deployer 编译完成后即退出，无子进程残留，`-Wait` 安全可用。
- **已确认会失败的错误做法**：`Start-Process -WindowStyle Hidden` — 隐藏窗口模式会阻止 deployer 正确编译 schema，导致 `rime_mint.prism.bin` 等二进制缓存文件无法生成，但 deployer 退出码仍为 0。
- **CLI/GUI 对齐要求**：
  1. `ArtifactService.Deploy` 和所有 deploy 调用路径不得使用 `-WindowStyle Hidden` 或等价隐藏窗口选项。
  2. 安装 Weasel 本体时（`.exe` 安装器），安装器产生常驻子进程 `WeaselServer.exe`，此时必须用 `-PassThru + WaitForExit(60000)` 而非 `-Wait`。Deployer 和安装器不可混用同一调用方式。

### 9.8 全角与英文标点的交互

- **正确做法**：当需要单独测试 ascii_punct 行为时，必须确保 `full_shape` 的 `reset` 为 `0`（关闭）。仅在此条件下，ascii_punct 开启时句号上屏为半角 `.`（U+002E），关闭时句号上屏为中文句号 `。`（U+3002）。当需要同时开启两者时，接受全角优先的结果（`．` U+FF0E）作为正确的联合行为。
- **当前已确认的上游行为**：当 `full_shape` 和 `ascii_punct` 同时设为开启（`reset: 1`）时，句号上屏为全角英文句号 `．`（U+FF0E），而非半角英文句号 `.`（U+002E）。
- **已确认会失败的错误做法**：在 `full_shape` 开启时，误以为 `.` 的上屏结果（全角 `．`）是 ascii_punct 单独作用的结果，进而认为 ascii_punct 的验收标准是产出全角句号 → 验收口径错误，且会导致在仅开启 ascii_punct 时产生验收不一致。
- **上游逻辑**：全角模式对字符形状的控制优先于标点风格的设置。`full_shape` 开启后，所有 ASCII 字符（含标点）被转换为全角对应字符。
- **对基线第八章的修正**：§8.4 `ascii_punct` 的当前已确认结果应理解为此处所述的全角交互结果。在仅 ascii_punct 开启、full_shape 关闭的条件下，句号上屏为半角 `.`。
- **CLI/GUI 对齐要求**：
  1. 当 GUI 允许用户分别勾选“英文标点”和“全角”时，必须在界面上（或通过 tooltip/说明）让用户知晓两者的交互效果。
  2. CLI apply 和验证链路中，测试 ascii_punct 行为时应显式控制 full_shape 状态，避免因交互导致验收口径不一致。

## 10. 对 CLI / GUI 的强制对齐要求

后续 CLI / GUI 若要声称"支持某项功能"，必须同时满足以下要求：

1. 必须通过正式入口产生与本文件一致的目标文件字段。
2. 必须通过正式入口触发与本文件一致的 deploy / recheck 结果。
3. 必须在真实输入法运行态中产生与本文件一致的用户可见效果。
4. 只要缺失其中任一层，就只能表述为：
   - 已知上游可行
   - 产品入口尚未完成对齐
5. 必须遵循本文档 §9 的所有关键实践，特别是：
    - §9.1 部署顺序
    - §9.2 开关覆写格式
    - §9.7 Deployer 调用方式
   违反以上任何一条，无论运行态效果如何，都不得宣称对应功能"已完成"或"C 类闭环"。
6. 对齐验证必须全程使用产品自身 CLI/GUI 机制，不得在验证过程中绕过产品直接操作系统资源：(a) 清洁初始状态必须通过 CLI `uninstall-weasel` → `install-weasel` 获取；(b) 资源部署必须通过 CLI `install-resource` → `apply`；(c) 配置修改必须通过 CLI `set-config`/`add-custom-entry`；(d) 部署必须通过 CLI `apply`（不得直接调 `WeaselDeployer.exe`）；(e) 上述任何一步若使用外部脚本 `reset_clean_weasel_state.ps1` 或直接文件操作，其验证结论均不得采信为产品入口已对齐。
7. P5a/P5b 验证层通过 `pwsh -File apps/windows/RimeKit.Windows.Tests/scripts/pipeline/pipeline_main.ps1` 编排执行。每个 P5b 判别词组使用独立相位隔离（PhaseDestroy→PhaseRebuild→Operation→Probe），带期望断言。
8. `RunEnvironmentRecheck` 区分 WARNING 与 BLOCKING findings：WARNING 仍展示但不导致 `status=blocked`；仅 BLOCKING/FATAL 导致 blocked。

## 11. 本文件能证明什么，不能证明什么

本文件能证明：

1. 当前 Windows 侧上游运行态中，哪些能力已经确认存在。
2. 每项能力当前已经确认的正确直接配置方法是什么，以及哪些做法会失败。
3. 每项能力当前应该用什么场景、什么输入、什么截图或什么上屏结果来验证。
4. 哪些旧理解已经被证明不正确，必须停止继续沿用。

本文件不能证明：

1. RimeKit CLI 已完成同等能力。
2. RimeKit GUI 已完成同等能力。
3. 当前 Windows 功能闭环审计表中对应条目已经收敛为 `C` 类。
4. 当前最终用户已经可以仅通过正式入口完成这些配置。

本文件的唯一正式用途，是为后续 CLI / GUI 提供**不可再模糊解释的对齐目标**。

## 12. 2026-05-29 新增能力条目

### 12.1 `ue_compat`

- 能力名称：üe 输入兼容
- 官方定义：主流拼音输入法（搜狗、微软拼音、Google 拼音、苹果拼音）均支持 `ue` 作为 `üe` 的替代输入方式（如 `lue`→`lüe`、`nue`→`nüe`），同时保留 `v` 键作为 `ü` 的通用输入方式。
- 当前已确认的直接运行态配置方法：
  1. 在 `rime_mint.custom.yaml` 的 `speller/algebra/+` 中写入：
      `- "derive/^([nl])ve$/$1ue/"`
  2. 重新 deploy。
- 已确认的验证方法：
  1. 输入 `shenglue`
  2. 提交候选
  3. 观察是否得到 `省略`
- 当前已确认结果：`shenglue → 省略`（üe 兼容启用时）
- 当前已知边界：`shenglve`（用 `v` 键）在两态下都生效，不是有效的验证输入。必须用 `shenglue`（用 `u` 键）来验证 üe 兼容功能。正确规则格式源自 Rime 官方 SpellingAlgebra 文档 luna_pinyin 示例：`derive`（非 `xform`）算子 + `^$` 音节锚定确保正确编译进 PRISM 音节表。

### 12.2 新增 Weasel 呈现能力

以下能力通过产品新增的 ConfigModel 字段 + Weasel 承载器 YAML 生成通路确认可用。现有 19 项基线能力（§6）之外新增至少以下呈现项：

| 能力标识 | 配置层 | YAML 路径 | G# |
|---------|--------|----------|:--:|
| `label_font` | Weasel 承载器 | `style/label_font_face` / `style/label_font_point` | G13 |
| `comment_font` | Weasel 承载器 | `style/comment_font_face` / `style/comment_font_point` | G14 |
| `corner_radius` | Weasel 承载器 | `style/layout/corner_radius` / `style/layout/round_corner` | G23 |
| `inline_preedit` | Weasel 承载器 | `style/inline_preedit` | G16 |
| `preedit_type` | Weasel 承载器 | `style/preedit_type` | G16 |
| `paging_on_scroll` | Weasel 承载器 | `style/paging_on_scroll` | G20 |
| `antialias_mode` | Weasel 承载器 | `style/antialias_mode` | G20 |
| `fullscreen` | Weasel 承载器 | `style/fullscreen` | G17 |
| `vertical_text` | Weasel 承载器 | `style/vertical_text` | G18 |
| `vertical_text_left_to_right` | Weasel 承载器 | `style/vertical_text_left_to_right` | G18 |
| `vertical_text_with_wrap` | Weasel 承载器 | `style/vertical_text_with_wrap` | G18 |
| `vertical_auto_reverse` | Weasel 承载器 | `style/vertical_auto_reverse` | G18 |
| `label_format` | Weasel 承载器 | `style/label_format` | G19 |
| `mark_text` | Weasel 承载器 | `style/mark_text` | G19 |
| `candidate_abbreviate_length` | Weasel 承载器 | `style/candidate_abbreviate_length` | G19 |
| `enhanced_position` | Weasel 承载器 | `style/enhanced_position` | G21 |
| `display_tray_icon` | Weasel 承载器 | `style/display_tray_icon` | G21 |
| `notification_time_ms` | Weasel 承载器 | `show_notifications_time` | G29 |
| `global_ascii` | Weasel 承载器 | `global_ascii` | G28 |
| `hover_type` | Weasel 承载器 | `style/hover_type` | G29 |
| `click_to_capture` | Weasel 承载器 | `style/click_to_capture` | G29 |
| `ascii_tip_follow_cursor` | Weasel 承载器 | `style/ascii_tip_follow_cursor` | G29 |
| `layout_baseline` | Weasel 承载器 | `style/layout/baseline` | G27 |
| `layout_linespacing` | Weasel 承载器 | `style/layout/linespacing` | G27 |
| `layout_align_type` | Weasel 承载器 | `style/layout/align_type` | G27 |
| `layout_max_height` | Weasel 承载器 | `style/layout/max_height` | G26 |
| `layout_max_width` | Weasel 承载器 | `style/layout/max_width` | G26 |
| `layout_min_height` | Weasel 承载器 | `style/layout/min_height` | G26 |
| `layout_min_width` | Weasel 承载器 | `style/layout/min_width` | G26 |
| `layout_border_width` | Weasel 承载器 | `style/layout/border_width` | G24 |
| `layout_margin_x` | Weasel 承载器 | `style/layout/margin_x` | G24 |
| `layout_margin_y` | Weasel 承载器 | `style/layout/margin_y` | G24 |
| `layout_spacing` | Weasel 承载器 | `style/layout/spacing` | G24 |
| `layout_candidate_spacing` | Weasel 承载器 | `style/layout/candidate_spacing` | G24 |
| `layout_hilite_spacing` | Weasel 承载器 | `style/layout/hilite_spacing` | G25 |
| `layout_hilite_padding` | Weasel 承载器 | `style/layout/hilite_padding` | G25 |
| `layout_hilite_padding_x` | Weasel 承载器 | `style/layout/hilite_padding_x` | G25 |
| `layout_hilite_padding_y` | Weasel 承载器 | `style/layout/hilite_padding_y` | G25 |
| `layout_shadow_radius` | Weasel 承载器 | `style/layout/shadow_radius` | G23 |
| `layout_shadow_offset_x` | Weasel 承载器 | `style/layout/shadow_offset_x` | G23 |
| `layout_shadow_offset_y` | Weasel 承载器 | `style/layout/shadow_offset_y` | G23 |
| `layout_corner_radius` | Weasel 承载器 | `style/layout/corner_radius` | G23 |
| `show_notification` | Weasel 承载器 | `show_notifications` | G22 |

> 注：2026-05-30 已将全部 43 项新增字段（含"其余 30+ 项"）纳入 G10–G29 管道，各字段均具备完整的 CLI set-config + visual/text 证据覆盖。具体分组见 `开发约定与最佳实践.md` §63。

## 13. 2026-06-03 新增能力条目

### 13.1 `text_orientation`（已于 baseline-2026-06-10 从产品移除，本节仅保留为历史参考）

- 能力名称：文字方向
- 官方定义：Weasel `style/text_orientation` 控制候选窗中**每个文字的书写方向**（`"horizontal"`=水平方向正常横排，`"vertical"`=文字从上到下竖直排列）。与 `vertical_text`（控制候选列方向）作用于不同层面。功能效果等同于 `vertical_text`，Weasel 内部处理中 `text_orientation` 优先级高于 `vertical_text`。
- 当前已确认的直接运行态配置方法：在 `weasel.custom.yaml` 中写入 `"style/text_orientation": "horizontal"` 或 `"vertical"`。重新 deploy。
- **`>` 产品移除原因：** oh-my-rime 模板中 `style/text_orientation` 被注释（`# text_orientation: horizontal`），字段无真实默认值。功能可通过「竖排」开关等价实现。_
- 段归属：承载器选项

### 13.2 `layout_window_type`（已于 baseline-2026-06-10 从产品移除，本节仅保留为历史参考）

- 能力名称：窗口布局模式
- 官方定义：Weasel `style/layout/type` 是布局类型预设，一次性设置候选窗排列模式。Weasel wiki 定义 5 种值：`"horizontal"`（横向）、`"vertical"`（纵向）、`"vertical_text"`（文字纵向）、`"vertical+fullscreen"`（纵向全屏）、`"horizontal+fullscreen"`（横向全屏）。功能近似 `style` 下的窗口控制选项。在 Weasel 内部处理顺序中**最后执行**（`_RimeParseStringOptWithFallback`），会覆盖此前设置的 `horizontal`、`fullscreen`、`vertical_text`、`text_orientation` 等字段。
- 当前已确认的直接运行态配置方法：在 `weasel.custom.yaml` 中写入 `"style/layout/type": "<值>"`。重新 deploy。
- **`>` 产品移除原因：** Weasel 出厂模板和 oh-my-rime 模板中均不存在该字段的默认值。功能可分解为「全屏」「竖排」「候选方向」三个独立控件替代。_

### 13.3 `comment_style_variant`

- 能力名称：注释内容显示
- 官方定义：产品 ConfigModel 的 `comment_style_variant` 控制候选窗注释的显示类型。通过 `translator/comment_format` 的 xform 规则实现。当前可靠实现的选项仅有 `默认`（不干预上游）和 `不显示`（`xform/^.+$//` 隐藏所有注释）。`中文`/`拉丁`/`混合` 为功能预留项，需上游 lua 处理器支持按语言过滤。
- 产品覆盖：CLI `personalization_settings.comment_style_variant`，GUI 显示页「注释内容」ComboBox（默认/不显示/中文/拉丁/混合），YAML 仅当 none 时写入。G-test 组 G32。
- 段归属：输入方案选项
- 当前已知边界：主翻译器（`script_translator`）使用 `comment_format: {comment}` 将注释原样透传给 lua 处理器（`corrector_filter.lua` + `super_preedit.lua`）。覆盖 `comment_format` 可能影响整个注释系统。`table_translator@mint_simple` 有独立的 `comment_format`（`xform/.*//`），不受此字段影响。

## 14. 2026-06-07 模板源文件位置确认（新增）

### 14.1 模板默认值读取机制

产品通过 `TemplateService` 从运行时目录自动捕获模板文件到工作区缓存，供 GUI 控件和注解读取上游默认值。

### 14.2 源文件位置

| 文件 | 位置 | 说明 |
|------|------|------|
| `weasel.yaml`（oh-my-rime 版） | `%APPDATA%\Rime\weasel.yaml` | 薄荷方案部署版本，**优先采用**。包含薄荷方案的定制默认值（如 `font_point: 12`、`border_width: 0`、`candidate_spacing: 22`），与 Weasel 出厂版不同 |
| `weasel.yaml`（Weasel 出厂版） | `C:\Program Files\Rime\weasel-*\data\weasel.yaml` | Weasel 安装器自带的出厂默认值（`font_point: 14`、`border_width: 3`）。oh-my-rime 版不存在时使用 |
| `rime_mint.schema.yaml` | `%APPDATA%\Rime\rime_mint.schema.yaml` | 由 `ApplyWindowsTargets` 部署，包含 switches 的 reset 值（emoji_suggestion:1, full_shape:0, tone_display:0, transcription:0, ascii_punct:0）和 menu/page_size:6 |

### 14.3 关键差异：oh-my-rime vs Weasel 出厂默认值

oh-my-rime 的 `weasel.yaml` 对 Weasel 上游默认值做了多处调整（以下为关键差异）：

| 字段 | Weasel 出厂 | oh-my-rime |
|------|:--:|:--:|
| `font_point` | 14 | **12** |
| `comment_font_point` | 14 | **10** |
| `font_face` | `"Microsoft YaHei"` | 复杂 Emoji 字体串 |
| `layout/border_width` | 3 | **0** |
| `layout/min_width` | 160 | **145** |
| `paging_on_scroll` | `true` | `true`（一致） |
| `enhanced_position` | `false` | **`true`** |

产品以 oh-my-rime 版为正式基线。若 oh-my-rime 版文件不存在（承载器已安装但方案未部署），产品应使用 Weasel 出厂版作为回退（或显式报错提示部署方案）。

### 14.4 模板缓存目录

```
workspace/windows/templates/
  weasel.yaml              (所有方案共享)
  rime_mint/
    rime_mint.schema.yaml  (每方案独立)
```

模板缓存由 `CaptureIfMissing` 在 `ExecuteApplyWorkflow` 成功路径中从 `%APPDATA%\Rime\` 复制创建。`CaptureIfMissing` 遭遇任何异常直接透出（不再静默 fail），由 `ExecuteApplyWorkflow` 的 catch 块统一捕获并写入诊断报告。

## 15. 2026-06-13 IME 中英文检测参考图与坐标更新

### 15.1 背景

Windows 11 输入法指示器样式更新（英文模式从"英"变为"A"），原有 `ime_refs/ref_english.png` 与新系统指示器字形不匹配，`detect_ime.py` 的模板匹配像素差分置信度持续低于阈值，导致 IME 模式检测频繁返回 `unknown`。

### 15.2 已更新的坐标与参考图

| 项目 | 旧值 | 新值 |
|------|------|------|
| BOX 坐标 | `(1807, 852, 1836, 881)` | `(1817, 854, 1842, 879)` |
| 参考图尺寸 | 29×29 | 25×25 |
| `ref_chinese.png` | 旧中文参考 | 重建（多模态 agent 裁剪） |
| `ref_english.png` | 旧英文参考（"英"） | 重建（多模态 agent 裁剪，显示"A"） |

### 15.3 维护约束

参考图必须由多模态 agent 分析全屏截图后裁剪生成，禁止手工编辑。系统更新、任务栏位置变化、DPI 变化导致坐标变更时，必须由多模态 agent 重新分析后生成新参考图，并同步更新 `detect_ime.py:7` 的 `BOX` 变量。

## 16. 2026-06-13 global_ascii 语义澄清

### 16.1 官方定义

Weasel 官方 Wiki（`Weasel 定制化` 页面「通知及全局 ASCII 模式」节）明确定义：
- `global_ascii` 是 Weasel **顶层**设置项（非 `style/` 下）
- 语义："切换为 ascii 模式时，是否影响所有窗口"
- `true`：ascii_mode 全局生效；`false`：每窗口独立
- oh-my-rime 的 `weasel.yaml` 模板默认值为 `false`

### 16.2 常见误解

`global_ascii: true` **不会**将 IME 直接切换到英文模式。它仅改变 ascii_mode 开关的**作用范围**。用户仍需通过 Shift 等方式切换 ascii_mode 后才能观察到全局效果。

### 16.3 产品验证方式

`global_ascii` 属于无法通过文本探针或截图验证的设置项。其验证方式为 CLI-only（仅验证 `set-config` + `apply` 成功），已通过 `G29` CLI 交互组覆盖。

## 17. 2026-06-21 新增能力条目

### 17.1 `enable_user_dict`（输入学习）

- 能力名称：输入学习
- 官方定义：`translator/enable_user_dict` 是 Rime `script_translator` 和 `table_translator` 共用选项。oh-my-rime 的 `rime_mint.schema.yaml` 显式声明 `enable_user_dict: true`。该选项控制用户词典（`*.userdb/`）的读写——开启时选择候选词的频率被记录并影响后续排序（调频），用户自造词也被保存；关闭时用户词典不记录也不读取。
- 已确认的直接运行态配置方法：在 `rime_mint.custom.yaml` 中写入 `"translator/enable_user_dict": false`（关闭）或不写入（默认开启）。
- 配置层：薄荷方案层（写入 `rime_mint.custom.yaml`）
- 产品覆盖：CLI `behavior_settings.enable_user_dict`，GUI 输入子页"输入学习"CheckBox，注解 `{EnableUserDict}` 占位符从模板动态读取。
- 当前已知边界：(a) 该选项同时控制调频和自造词记录——两者共享同一个 `UserDictionary` 数据库，无法分开设置；(b) 关闭后需重新部署（`/deploy`）即时生效；(c) 用户词典数据存储在 `%APPDATA%\Rime\rime_mint.userdb\` 目录中。

### 17.2 Rime translator 选项对 rime_mint 的适用性

| 选项 | 适用？ | 原因 |
|------|:--:|------|
| `enable_user_dict` | ✅ | script_translator 和 table_translator 共用 |
| `enable_sentence` | ❌ | 仅 table_translator 有效。CustomizationGuide 明确"不可作用于拼音、注音等输入方案" |
| `enable_completion` | ❌ | 同上 |
| `enable_correction` | ❌ | 桌面端严重卡顿（GitHub Issue #230），rime_mint 已注释掉 |
| `strict_spelling` | ❌ | 极其技术化，仅在已配置模糊拼音时有意义 |
| `enable_encoder` | ❌ | 仅 table_translator 有效（形码专属） |
