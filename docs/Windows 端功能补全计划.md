# Windows 端功能补全计划

## 头部骨架

- 正式文件路径：`docs/Windows 端功能补全计划.md`
- 对应的本文档章节：`7.4-Windows 基线`、`7.4.19-CLI 命令全覆盖要求`、`7.4.20-GUI 控件全覆盖要求`、`7.4.21- CLI 与 GUI 测试分离要求`、`7.6-资源基线与更新基线`、`9.2-Windows 客户端职责`
- 覆盖范围：基于 `docs/Windows 上游运行态生效基线.md` 的 19 项已验证能力，对 48 个 GUI 功能、19 个 CLI 命令、探针覆盖的全面缺口分析与分阶段补全计划。r29 已将 toggle 命令合并到 install-resource/uninstall-resource。
- 明确非目标：不替代 `docs/需求规范.md` 的正式总基线；不替代 `docs/Windows 上游运行态生效基线.md` 的能力定义；不对已完成的上游运行态验证结果重新裁决。
- 输入：`docs/Windows 上游运行态生效基线.md`（19 项能力 + 8 条实践）、`apps/windows/RimeKit.Windows.Gui/WindowsPrototypeForm.cs`（48 个 GUI 功能）、`apps/windows/RimeKit.Windows.Cli/Program.cs`（19 个 CLI 命令）、`apps/windows/RimeKit.Windows.Core/WindowsWorkflowService.cs`（服务层）、`scripts/groups/` + `scripts/_common_ci_test.ps1`（管道链路）。
- 输出：一份无需依赖对话上下文即可独立驱动后续补全工作的正式执行计划。
- 真值源：以 `docs/需求规范.md` 和 `docs/Windows 上游运行态生效基线.md` 为正式裁决来源。本计划是对这两份基线文档的落实方案，如与基线文档冲突，以基线为准。
- 正式任务类型：需求变更治理、CLI 功能补全、探针补全、文档联动收敛。
- 正式阶段映射：不适用；本文件不直接驱动运行时阶段。
- 失败边界：若本计划中的任一项补全任务未完整执行，不得宣称对应功能已完成或对应 CLI 命令已可用。
- 验收方式：本计划中的每一条任务在执行后都能在 `docs/需求更新清单.md` 中找到对应记录，在 `docs/Windows 上游运行态生效基线.md` 中找到对应的生效判定标准。

## 1. 背景与当前状态

### 1.1 已完成的验证

通过 `scripts/groups/` 下 13 组 × 38 个独立相位脚本（2026-05-28，已扩展至 30 组 × 90 phase）的判别词探针，以及视觉截图验证，已确认 **19 项** Windows 上游运行态能力全部可用。这些结果已固化在 `docs/Windows 上游运行态生效基线.md` 中：

| 类别 | 数量 | 典型能力 |
|------|:--:|---------|
| 真实输入探针 | 9 | basic_nihao、custom_lcbh、fuzzy_zisi、ascii_punct、full_shape、trad_toufa、moetype_aboguai、sogou_omoomo、model_long |
| 视觉/呈现 | 9 | emoji_candidate、candidate_count、candidate_direction、candidate_comment、theme_switch、font_change、font_size、tone_display、status_notify |

> **分层**：9 项视觉/呈现能力中，薄荷方案层 3 项（emoji_candidate、candidate_count、tone_display），Weasel 承载器层 4 项（theme_switch、font_change、font_size、status_notify），跨两层 2 项（candidate_direction、candidate_comment）。Weasel 承载器层需重启 WeaselServer 生效。详见 `开发约定与最佳实践.md` §29.6.2。

同时，8 条跨能力的工程实践也已固化在基线 §9 中。

### 1.2 已发现的三大系统性缺口

经过对 48 个 GUI 功能、8 个 CLI 命令、以及探针覆盖的系统性比对，发现以下缺口：

| 缺口 | 说明 | 严重度 |
|------|------|:--:|
| **A — CLI 命令缺失** | 48 个 GUI 功能中，仅 13 个有对应的 CLI 命令（`install-resource`、`apply`、`doctor`、`export`、`print-config`）。其余 35 个功能（承载器管理、资源 enable/disable/uninstall、自定义词条管理、输入设置修改）均无 CLI 命令 | 高 |
| **B — 探针未打通真实生效证据** | 当前 GuiProbeRunner 只能验证工作流层（按钮→diagnostic/recheck JSON），不能对接真实输入/视觉证据。基线要求的"5 类证据全闭合"目前只有前 4 层，缺第 5 层（真实生效） | 高 |
| **C — sogou 词库测试盲区** | 所有词库探针只测试列表第 0 项（moetype）。sogou（第 1 项）的 5 个按钮（安装/启用/停用/更新/卸载）完全没有探针覆盖 | 中 |

### 1.3 当前可用的基础设施

- **探针脚本**：`groups/` 目录下 13 组 × 38 相位脚本（原方案，已扩展至 30 组 × 90 phase）+ `_common_ci_test.ps1`（产品 CLI 管道共享函数）+ `_common.ps1`（共享函数）+ `toolkit/`（`probe_notepad_ime.py`、`probe_ime_toggle.ahk`、`probe_ime_type.ahk`、`detect_ime.py`、`take_screenshot.py`）+ `run_all.ps1`（主管道）
- **GUI 探针**：`GuiProbeRunner.exe` + `cli_apply_backend_actions.json`（21 条 gui_click）
- **CLI 骨架**：`Program.cs` 注册了 8 个命令，`WindowsWorkflowService.cs` 已有 `Run*` 方法
- **基线文档**：19 项能力每条都有正确的配置方法、验证方法和已知边界

## 2. 三大缺口详细清单

### 2.1 缺口 A — CLI 命令缺失（35 项）

以下按 GUI 页面分组列出所有缺失的 CLI 命令。每项格式：`GUI 功能 → CLI 应对应命令`。

#### 2.1.1 承载器管理（3 项缺失）

| # | GUI 功能 | 当前 CLI 覆盖 | 需要的 CLI 命令 |
|---|---------|-------------|---------------|
| A01 | 下载并安装小狼毫 (G02) | **GAP** | `install-weasel` |
| A02 | 更新小狼毫 (G03) | **GAP** | `install-weasel --reinstall` 或 `update-weasel` |
| A03 | 卸载小狼毫 (G04) | **GAP** | `uninstall-weasel` |

#### 2.1.2 资源控制（12 项缺失）

> **r29 更新**：以下 A04-A15 的启用/停用功能已合并到 `install-resource` / `uninstall-resource`（r29）。`toggle-schema` / `toggle-dictionary` / `toggle-model` 命令已移除。A04/A05/A07/A08/A10/A11/A13/A14 的行仅作为原计划历史留档。

| # | GUI 功能 | 当前 CLI 覆盖 | 需要的 CLI 命令 |
|---|---------|-------------|---------------|
| A04 | 启用输入方案 (G07) | **GAP** | `toggle-scheme --enable --schema-id <id>` |
| A05 | 停用输入方案 (G08) | **GAP** | `toggle-scheme --disable --schema-id <id>` |
| A06 | 卸载输入方案 (G10) | **GAP** | `uninstall-resource --resource-id <id>` |
| A07 | 启用词库 moetype (G14) | **GAP** | `toggle-dictionary --enable --dictionary-id moetype` |
| A08 | 停用词库 moetype (G15) | **GAP** | `toggle-dictionary --disable --dictionary-id moetype` |
| A09 | 卸载词库 moetype (G17) | **GAP** | `uninstall-resource --resource-id moetype` |
| A10 | 启用词库 sogou (G19) | **GAP** | `toggle-dictionary --enable --dictionary-id sogou_network_popular_words` |
| A11 | 停用词库 sogou (G20) | **GAP** | `toggle-dictionary --disable --dictionary-id sogou_network_popular_words` |
| A12 | 卸载词库 sogou (G22) | **GAP** | `uninstall-resource --resource-id sogou_network_popular_words` |
| A13 | 启用语法模型 (G30) | **GAP** | `toggle-model --enable --model-id wanxiang_lts_zh_hans` |
| A14 | 停用语法模型 (G31) | **GAP** | `toggle-model --disable --model-id wanxiang_lts_zh_hans` |
| A15 | 卸载语法模型 (G33) | **GAP** | `uninstall-resource --resource-id wanxiang_lts_zh_hans` |

#### 2.1.3 自定义词条管理（4 项缺失）

| # | GUI 功能 | 当前 CLI 覆盖 | 需要的 CLI 命令 |
|---|---------|-------------|---------------|
| A16 | 检测目前用户词条 (G23) | **GAP** | `list-custom-entries` |
| A17 | 新增词条 (G24) | **GAP** | `add-custom-entry --text <text> --code <code> [--weight <w>]` |
| A18 | 删除词条 (G25) | **GAP** | `delete-custom-entry --text <text> --code <code>` |
| A19 | 应用用户词条 (G26) | **GAP** | `apply-custom-entries` |

#### 2.1.4 输入设置与显示设置（16 项缺失）

这些功能当前只能通过手动编辑 `current_config_model.json` 后执行 `apply` 来操作。CLI 需要面向配置模型的字段级修改能力。

**设计原则**：新增一个统一的配置修改命令，使用 `--set <field_path>=<value>` 模式。以下列出每项对应的 field_path 和值与基线 §8 的引用。

| # | GUI 功能 | ConfigModel field_path | 基线引用 |
|---|---------|----------------------|---------|
| A20 | 日间主题 (G34) | `windows_settings.color_scheme` | §8.14 |
| A21 | 夜间主题 (G35) | `windows_settings.color_scheme_dark` | §8.14 |
| A22 | 字体 (G36) | `windows_settings.font_face` | §8.15 |
| A23 | 字号 (G37) | `windows_settings.font_point` | §8.16 |
| A24 | 状态变化通知 (G38) | `windows_settings.show_notification` | §8.18 |
| A25 | 候选数 (G39) | `candidate_settings.page_size` | §8.11 |
| A26 | 候选方向 (G40) | `candidate_settings.layout` | §8.12 |
| A27 | Emoji 注释 (G41) | `candidate_settings.show_emoji_comments` | §8.13 |
| A28 | 简繁切换 (G42) | `behavior_settings.simplification_mode` | §8.6 |
| A29 | 全角/半角 (G43) | `behavior_settings.full_shape_enabled` | §8.5 |
| A30 | 英文标点 (G44) | `behavior_settings.ascii_punct_enabled` | §8.4 |
| A31 | Emoji候选 (G45) | `behavior_settings.emoji_suggestion_enabled` | §8.10 |
| A32 | 声调显示 (G46) | `behavior_settings.tone_display_enabled` | §8.17 |
| A33 | 启用模糊音 (G47) | `fuzzy_pinyin_settings.enabled` | §8.3 |
| A34 | 模糊音规则 (G48) | `fuzzy_pinyin_settings.additional_rules` | §8.3 |
| A35 | （通用）统一保存入口 | `apply --config ...` | - |

**实现方式**：新增 `set-config` CLI 命令，作用于 `current_config_model.json`。修改后由用户显式调用 `apply` 来执行完整的 generate→deploy→recheck 链路。也可以新增 `set-config --apply` 合并两步。

> **命令设计边界**：`set-config` 只修改 ConfigModel JSON 文件本身，不做 schema 层验证，不做 deploy。这保持了 CLI 的原子性——用户可以通过连续多个 `set-config` 积累修改，最后一次 `apply` 生效全部。

### 2.2 缺口 B — 探针未打通真实生效证据（48 项）

当前 21 条 `gui_click` 探针只验证工作流层的证据（diagnostic/recheck JSON 落地），不执行真实输入/视觉验证。基线要求的第 5 层证据（用户可见结果）缺失。

**解决方案**：不修改 GuiProbeRunner 本身（保持其工作流层验证职责），而是**在 `_common_ci_test.ps1` 及对应的 `groups/` 相位脚本中增加探针对接**。每组独立 Phase ABC 隔离（Destroy→Rebuild→Probe），通过 `Assert-Probe`/`Assert-BaselineProbe` 执行对应的真实输入探针或截图探针。

**映射关系**：每个 GUI 功能对应一个或多个验证步骤：

| 探针类别 | 对应 GUI 功能 | 验证步骤 |
|---------|-------------|---------|
| 文本探针（9 条，已完成） | G05-G10 (输入方案)、G13-G22 (词库/模型安装+启用)、G23-G26 (用户词条)、G42-G44,G47 (输入设置) | `probe_notepad_ime.py --phase=vision <case> <input> <keys>` |
| 视觉探针（9 条，脚本已就绪） | G34-G41,G45-G46 (显示设置+Emoji+声调) | 截图 → 对比分析 |
| 状态探针（待创建） | G01-G04 (承载器安装/卸载/更新) | Deployer 存在性检查 + `doctor` 输出 |
| 状态探针（待创建） | G11,G12,G27,G28 (检测状态) | `doctor` + `print-config` 输出比对 |

**探针链路设计**：

```
Phase 3: GuiProbeRunner (工作流层)
    ↓
Phase 4a: 状态探针 (承载器/检测)
    ↓  
Phase 4b: 视觉探针 (9 个截图+比对)   ← P5a: 视觉在前
    ↓
Phase 4c: 文本探针 (9 个已有 case)   ← P5b: 文本在后
    ↓
Phase 5: 综合报告
```

> 注：最终验证管线（baseline-2026-05-13-r28 定稿）中视觉探针（P5a）在文本探针（P5b）之前执行，防止文本探针的 IME 缓存影响视觉截图。详见 `开发约定与最佳实践.md` §1.6。

### 2.3 缺口 C — sogou 词库测试盲区

当前 `cli_apply_backend_actions.json` 中所有词库相关 gui_click 探针均使用 `select_index: 0`（列表第一项 = moetype）。sogou（第二项）的 5 个按钮完全没有探针。

**解决方案**：为 sogou 词库新增 5 条 gui_click 探针条目，使用 `select_index: 1`。或者更稳健的方案——使用 `select_text` 按显示名称匹配而非按索引。

## 3. 实现阶段与优先级

| 阶段 | 名称 | 内容 | 预估工作量 | 优先级 |
|:--:|------|------|:--:|:--:|
| A | **CLI 命令补全** | **已完成** (8→20 commands, all 48 gaps covered) | 已完成 | 已完成 | 新增 12 个 CLI 命令 + `print-config` 扩展至完整 29 项字段 | 最大 | **最高** |
| B | **已完成** | **探针对接** | 创建 `_common_ci_test.ps1`（Destroy/Rebuild/Probe/Warmup 函数）+ `groups/` 下 13 组独立相位脚本（G1-G13、38 个 .ps1 文件），全程使用产品 CLI 命令完成安装/卸载/配置/部署。**2026-05-30 已扩展至 30 组 × 90 phase**（G1–G31，含 19 个新增视觉组覆盖 Weasel 49 字段 100%） | 中 | **高** |
| C | **已完成** | **sogou 探针补齐** | 为 sogou 词库的 5 个按钮补 gui_click 探针条目 | 小 | **中** |
| D | **基线文档补完** | 补 `candidate_comment` (§8.13) 的 `spelling_hints` + `always_show_comments` ganging 说明；补 `tone_display` (§8.17) 与候选窗声调的区分说明 | 已完成 | **中** |
| E | **文档同步** | 更新 `需求规范.md`、`需求更新清单.md` 反映本计划和后续补全 | 中 | **中** |

### 3.1 执行依赖

- 阶段 B 依赖阶段 A（CLI 命令必须在探针执行前可用）
- 阶段 C 可与阶段 A 并行
- 阶段 D 可与阶段 A 并行
- 阶段 E 在每次其他阶段完成后执行

### 3.2 验收标准

每个阶段完成的判定：

| 阶段 | 验收标准 |
|:--:|------|
| A | 8 个原有 CLI 命令 + N 个新 CLI 命令全部有测试覆盖；CLI 可独立完成配置→apply→deploy→recheck 全链路；print-config 输出覆盖全部 29 项 ConfigModel 字段 |
| B | 各组 `Phase.A/B/C.ps1` Destroy→Rebuild→Probe 全链路通过，`invariant: 0/N`（基线不匹配）→ `all/N`（安装后全匹配）→ `0/N`（卸载后全不匹配）闭环 |
| C | sogou 的 5 个 gui_click 探针条目在 C1 环境下 completed + evidence_satisfied |
| D | 基线 §8.13 新增 ganging 说明；基线 §8.17 新增候选窗声调区分说明 |
| E | 四份正式文档引用一致，无遗漏，无冲突 |

## 4. 各阶段详细任务

### 4.1 阶段 A — CLI 命令补全

#### 4.1.1 新增命令清单

| # | 命令名称 | 参数 | 对应基线 | 实现方法 |
|---|---------|------|---------|---------|
| A01 | `install-weasel` | `[--installer-url <url>]` | §9.7 | 下载最新 release → `Start-Process -PassThru + WaitForExit(60000)` → 验证 Deployer 存在 |
| A02 | `uninstall-weasel` | （无参数） | §9.7 | 查找专属卸载器 → `Start-Process -Wait` → 验证 Deployer 消失 → 清理残留目录 |
| A03 | `toggle-schema` | `--enable --schema-id <id>` / `--disable --schema-id <id>` | §8.1 | 修改 ConfigModel `enabled_schema_ids` / `windows_default_schema_id` → 保存 |
| A04 | `toggle-dictionary` | `--enable --dictionary-id <id>` / `--disable --dictionary-id <id>` | §8.7, §8.8, §9.3 | 修改 ConfigModel `enabled_dictionary_ids` → 保存 |
| A05 | `toggle-model` | `--enable --model-id <id>` / `--disable --model-id <id>` | §8.9 | 修改 ConfigModel `enabled_model_ids` / `active_model_id` → 保存 |
| A06 | `uninstall-resource` | `--resource-id <id>` | §8.7, §8.8, §8.9 | 从 workspace 移除 + 从 `installed_resources.json` 移除 + 从 ConfigModel 移除 |
| A07 | `set-config` | `--field <field_path> --value <value> [--apply]` | 全部 18 项 | 修改 `current_config_model.json` 指定路径字段值；可选同时执行 apply |
| A08 | `list-custom-entries` | `[--config <path>]` | §8.2, §9.5 | 读取 ConfigModel `dictionary_settings.custom_entries` → 格式化输出 |
| A09 | `add-custom-entry` | `--text <text> --code <code> [--weight <w>]` | §8.2, §9.5 | 追加到 ConfigModel `custom_entries` → 保存 |
| A10 | `delete-custom-entry` | `--text <text> --code <code>` | §8.2, §9.5 | 从 ConfigModel `custom_entries` 中移除匹配项 → 保存 |
| A11 | `apply-custom-entries` | `[--config <path>]` | §8.2, §9.5 | 空词条时返回 no-op；有词条时委托 `apply` 执行完整 deploy → recheck |
| A12 | `resource-status` | `[--config <path>] [--format json]` | §7.6 | 读取 installed_resources 状态 + config model enabled 列表 → JSON 输出 |
#

**阶段 A 已完成（2026-05-13）。** 全部 12 个新命令已实现并注册到 CLI（A01-A12）。原有 8 个命令 + 新增 12 个 = 20 个 CLI 命令。

> **r29 更新（2026-05-18）**：`toggle-schema`、`toggle-dictionary`、`toggle-model` 三个命令已合并到 `install-resource` / `uninstall-resource`。总计 CLI 命令从 21 减至 18。`install-resource` 现在包含启用 + apply + deploy + recheck。

### 4.1.2 现有命令增强

| 命令 | 增强内容 | 基线引用 |
|------|---------|---------|
| `print-config` | 输出从 5 字段扩展到完整 ConfigModel **29 项**字段：platform、config_version、enabled_schema_ids、windows_default_schema_id、android_default_schema_id、candidate_page_size、candidate_layout、candidate_show_emoji_comments、simplification_mode、full_shape_enabled、ascii_punct_enabled、emoji_suggestion_enabled、tone_display_enabled、fuzzy_pinyin_enabled、fuzzy_pinyin_preset_id、fuzzy_pinyin_additional_rules、enabled_dictionary_ids、dictionary_order、custom_entries_count、enabled_model_ids、active_model_id、model_root、windows_target_root、windows_font_face、windows_font_point、windows_color_scheme、windows_color_scheme_dark、windows_show_notification、snapshot_retention_limit | 全部 §8 |

#### 4.1.3 实现约束

- 所有新增 CLI 命令必须在 `WindowsWorkflowService.cs` 中实现 `Run*` 方法
- 所有新增 CLI 命令必须在 `Program.cs` 中注册
- 所有新增 CLI 命令必须继承 `RIMEKIT_RUN_HOST_INTEGRATION_TESTS` 的 opt-in 守卫（如涉及真实系统操作）
- `set-config` 的 `--field` 参数必须覆盖 ConfigModel 全部字段
- `toggle-*` 命令在修改 ConfigModel 后不自动执行 `apply`（保持"修改"与"生效"的原子性分离）

#### 4.1.4 CLI 测试覆盖要求

| 命令 | 最少测试场景 |
|------|------------|
| `install-weasel` | 成功安装、已有最新版时跳过[¹]、下载失败时显式报错 |
| `uninstall-weasel` | 成功卸载[¹]、承载器未安装时短路返回成功 |
| `toggle-schema` | 启用成功、停用成功、schema 不存在[²] |
| `toggle-dictionary` | 启用成功、启用失败(未安装)[²]、停用成功 |
| `toggle-model` | 同上 |
| `uninstall-resource` | 成功卸载[³]、资源不存在时短路、仍被引用[²] |
| `set-config` | 设置单个字段成功、设置嵌套字段成功、数组值、无效字段名 |
| `list-custom-entries` | 空列表、有词条、词条格式完整 |
| `add-custom-entry` | 新增成功、重复 code[²]、空 text/code 报错 |
| `delete-custom-entry` | 删除成功、不存在的词条[²] |
| `apply-custom-entries` | 有词条时生成+deploy成功、空词条时 no-op |

> **设计注（baseline-2026-05-13-r28 收口）**：
> - [¹] `install-weasel` 成功安装 和 `uninstall-weasel` 成功卸载 为 **HostIntegration** 依赖场景，需要真实 Weasel 环境（`RIMEKIT_RUN_HOST_INTEGRATION_TESTS=1`）。`install-weasel` "已有最新版时跳过" 同为 HostIntegration 场景。hermetic 层通过 mock HTTP（下载失败）和 C0 短路（卸载）覆盖关键故障路径。
> - [²] 以下 CLI 命令在 CLI 层做**原子字段修改**，有意不校验业务约束。实际行为与早期 spec 的"报错"预期不同，此差异为**设计决策**，详见下：
>   - `toggle-schema/dictionary/model`：不校验 schema/词典/模型是否存在或已安装（校验由 GUI 层和 apply 工作流负责）
>   - `add-custom-entry`：不校验 code 重复（允许同一编码对应多条词条）
>   - `delete-custom-entry`：对不存在的词条返回成功（no-op），不报错
>   - `uninstall-resource`：资源仍被引用时自动从配置模型移除引用并继续卸载，不阻断
> - [³] `uninstall-resource` 成功卸载 由预存测试 `UninstallFormalResource_ShouldRemoveInstalledStateAndRewriteConfig` 覆盖（HttpListener + fake Deployer 全流程）。
> - `apply-custom-entries` 已注册为第 20 个 CLI 命令，当前委托 `apply` 执行；空词条时返回 no-op 成功。

### 4.2 阶段 B — 探针对接

#### 4.2.1 更新的脚本文件

| 文件 | 更新内容 |
|------|---------|
| `scripts/_common_ci_test.ps1` | 产品 CLI 版 Destroy/Rebuild/Probe/Warmup 共享函数（Invoke-Cli、Invoke-PhaseDestroyCli、Invoke-PhaseRebuildCli、Invoke-ApplyConfig、Assert-Probe、Assert-BaselineProbe、Write-PhaseResult） |
| `scripts/groups/G{1..31}/phase*.ps1` | 30 组独立相位脚本（G1 基础输入、G2 moetype 词库、G3 sogou 词库、G4 用户词条、G5 模糊拼音、G6 语法模型、G7 英文标点、G8 全角、G9 繁简、G10 方案视觉、G11 跨层视觉、G12–G14 字体三组、G15 主题、G16 预编辑、G17 全屏、G18 竖排、G19 候选文本、G20 渲染、G21 特殊、G22 通知、G23–G27 布局五组、G28 global_ascii、G29 CLI 交互、G12 第二轮、G13 输入法选择器） |
| `scripts/toolkit/` | 5 个探针工具（probe_notepad_ime.py、probe_ime_toggle.ahk、probe_ime_type.ahk、detect_ime.py、take_screenshot.py） |

#### 4.2.2 探针索引

每个 GUI 按钮操作后应该执行的真实输入/视觉探针映射：

```

G01 检测承载器状态
  → 状态探针: Test-Path WeaselDeployer.exe + doctor 输出
G03 更新小狼毫
  → 状态探针: Same as G02 (install then detect)
G04 卸载小狼毫
  → 状态探针: Test-Path WeaselDeployer.exe → FALSE + doctor 无 deployer
G06 下载并安装输入方案
  → 文本探针: basic_nihao (post-install, scheme must be active)
G11 检测本地词库
  → CLI 探针: export --kind config → verify dictionary fields
G12 检测词库状态
  → CLI 探针: doctor → verify no dictionary-related blockers
G27 检测本地语法模型
  → CLI 探针: export --kind config → verify model fields
G28 检测语法模型状态
  → CLI 探针: doctor → verify no model-related blockers
G02 下载并安装小狼毫
  → 状态探针: Test-Path WeaselDeployer.exe → TRUE
G05-G10 输入方案操作
  → 文本探针: basic_nihao (nihao → 你好)
G13-G17 moetype 词库操作
  → 文本探针: moetype_aboguai (abaiguai → 阿柏怪)
G18-G22 sogou 词库操作
  → 文本探针: sogou_omoomo (omoomo → 哦模哦模)
G23-G26 用户词条操作
  → 文本探针: custom_lcbh (lcbh → 流程闭环/流程閉環)
G29-G33 语法模型操作
  → 文本探针: model_long (jianjiandejiubuzaiyile → 渐渐地就不在意了)
G34-G41 显示设置
  → 视觉探针: 截图对比
    - G34/G35 (主题): rime_mint.custom.yaml style/color_scheme 变化
    - G36 (字体): weasel.custom.yaml style/font_face 变化
    - G37 (字号): weasel.custom.yaml style/font_point 变化
    - G38 (通知): weasel.custom.yaml show_notifications 变化
    - G39 (候选数): rime_mint.custom.yaml menu/page_size 变化
    - G40 (候选方向): rime_mint.custom.yaml + weasel.custom.yaml 同步变化
    - G41 (Emoji 注释): rime_mint.custom.yaml always_show_comments 变化
G42-G48 输入设置
  → 文本探针 + 视觉探针混合:
    - G42 (简繁): trad_toufa (toufa → 頭髮)
    - G43 (全角): full_shape (123 → １２３)
    - G44 (英文标点): ascii_punct (. → ．)
    - G45 (Emoji): 视觉截图 (kaixin 候选窗含 emoji)
    - G46 (声调显示): 视觉截图 (preedit 拼音带声调)
    - G47 (模糊音): fuzzy_zisi (zisi → 只是)
    - G48 (模糊音规则): 同 G47
```

### 4.3 阶段 C — sogou 探针补齐

#### 4.3.1 需要新增的 gui_click 条目

在 `cli_apply_backend_actions.json` 中新增 5 条，使用 `select_text: "搜狗网络流行新词"`（按显示名匹配，更稳健）替代 `select_index: 1`：

| action_id | 步骤 |
|-----------|------|
| `gui_install_dictionary_sogou` | select 词库 tab → click 检测本地词库 → select_list_item("搜狗网络流行新词") → click 下载并部署词库 |
| `gui_enable_dictionary_sogou` | 同上 + click 启用词库 |
| `gui_disable_dictionary_sogou` | 同上 + click 停用词库 |
| `gui_update_dictionary_sogou` | 同上 + click 更新词库 |
| `gui_uninstall_dictionary_sogou` | 同上 + click 卸载词库 |

每条 wait_for 和 assert 与对应的 moetype 探针相同（`file_exists last_recheck_summary.json` + `status=completed`）。

#### 4.3.2 GuiProbeRunner 增强

`GuiProbeRunner.exe` 的 `select_list_item` 步骤当前支持 `select_text` 参数（按列表项文本匹配）。如果现有实现中 `select_text` 对 `_dictionaryListBox` 不生效，则需要修复——优先将搜狗加入资源清单并以正式名称匹配。

### 4.4 阶段 D — 基线文档补完

#### 4.4.1 §8.13 `candidate_comment` 补充

在已确认的直接运行态配置方法中新增一条说明：

```
- 当前已知详情：
  1. `spelling_hints`、`always_show_comments`、`tone_display` 是 rime_mint 三项独立机制。`spelling_hints` 在 schema 中固定为 8（始终显示候选窗拼音编码含声调），`script_translator` 不支持通过 patch 覆写，产品已移除独立的拼英提示开关。产品只通过 `show_emoji_comments` 控制 `always_show_comments`（emoji 文字注释显隐）。
  2. `always_show_comments` = true：强制旁注始终显示（Rime 默认在 preedit==comment 时隐藏）。
  3. 当 `ShowEmojiComments = true` 时，生成 `always_show_comments: true`；当 `false` 时，生成 `always_show_comments: false` 且 Weasel 侧 comment 颜色设为透明。
   4. 候选窗中的声调旁注由 `spelling_hints` 控制（`rime_mint` 中由 schema 内置固定，不由产品配置模型开关），`tone_display` 只控制 preedit 拼音是否带声调。u-mode 拆字是 `super_preedit.lua` 的独立功能，与 `tone_display` 无关。
```

#### 4.4.2 §8.17 `tone_display` 补充

在已确认结果中新增一条说明：

```
- 当前已知边界：
  3. `tone_display` 控制 preedit 拼音是否带声调，对任意拼音输入均生效。u-mode 拆字是 `super_preedit.lua` 的独立功能，与 `tone_display` 无关。
  4. 产品 GUI 中的"声调显示"标签指 preedit 声调显示。若产品文档或界面对此有错表述，应修正为符合此官方定义。
```

### 4.5 阶段 E — 文档同步

| 文件 | 内容 |
|------|------|
| `docs/需求规范.md` | 正式文档清单新增 `docs/Windows 端功能补全计划.md`；基线版本和修订日期更新；§7.4.19 CLI 命令覆盖数量修订 |
| `docs/需求更新清单.md` | 新增 §19 记录本计划和各阶段补全进展 |
| `docs/开发约定与最佳实践.md` | 新增 CLI 命令实现约定；更新 §3.1 CLI 命令覆盖清单 |
| `docs/Windows 上游运行态生效基线.md` | §8.13、§8.17 补充（见阶段 D） |

## 5. 文档更新要求

### 5.1 同步原则

本计划及其各阶段的每次变更，必须同步更新以下文件：

| 变更类型 | 必须同步更新的文档 |
|---------|------------------|
| 新增/修改 CLI 命令 | `需求规范.md` §7.4.19、`需求更新清单.md`、`开发约定与最佳实践.md` §3 |
| 新增/修改探针 | `需求更新清单.md`、`开发约定与最佳实践.md` |
| 修改基线文档 | `需求更新清单.md`、`需求规范.md`（如影响版本） |
| 新增正式文档 | `需求规范.md` §1（文档清单+主题映射）、`需求更新清单.md` |

### 5.2 旧要求覆盖

"新要求覆盖旧要求"在本计划中生效的场合：

1. `print-config` 的覆盖字段从 9 个扩展到 29 个 → 旧文档中任何"print-config 覆盖 9 字段"的描述均被本文档替代
2. CLI 命令清单从 8 个扩展到 20 个 → 旧文档中任何"19 个 CLI 命令"的总数描述均被本文档替代
3. 新增的 `set-config` 替代"手动改 ConfigModel JSON + apply"的旧模式 → 旧文档中的相应描述需标注为已由 `set-config` 替代
4. **新增**：真实验证管线**必须全程使用产品 CLI 命令**，替代旧的 `reset_clean_weasel_state.ps1` + 直接文件操作模式：
   - 清洁初始状态：`uninstall-weasel` → `doctor` → `install-weasel` → `doctor`（替代 `reset_clean_weasel_state.ps1`）
   - 资源安装部署：`install-resource` → `apply`（替代直接 Copy-Item + Start-Process WeaselDeployer）
   - 配置修改：`set-config`/`add-custom-entry`/`toggle-*`（替代直接 Set-Content YAML）
    - 旧 `run_full_verification.ps1` 和 `inject_dict_imports.py` 中的直接文件操作模式已被本要求废止。新管道使用 `groups/` 下独立相位脚本 + `_common_ci_test.ps1`（全程产品 CLI）替代。
   - **无 P0.5**：`install-resource` 已自带 fresh download（安装前主动删除 workspace 缓存），不需要手动清理
5. **新增**：9 项视觉呈现能力（emoji_candidate 等）必须通过真实截图验证，按 A/B 组分配 4 次 apply 循环。视觉验证在文本验证之前执行（P5a 视觉→P5b 文本）。详见 `开发约定与最佳实践.md` §29.6
6. **新增**：P4 GuiProbeRunner 必须使用只读 probe manifest（仅检测类动作），排除所有安装/卸载/启用/停用/应用/重置等写入动作。P4 的职责是验证 GUI 展示 CLI 产生的状态，而非通过 GUI 修改状态。详见 `开发约定与最佳实践.md` §29.7

## 6. 验证检查清单（20+ 项）

### 6.1 本计划自身的完整检查

| # | 检查项 | 判定标准 |
|---|--------|---------|
| C01 | CLI 命令总数 | 8(原有) + 12(新增) = 20 个。全部在 Program.cs 注册并可调用 |
| C02 | 35 个 CLI 缺口 | 逐一核对 §2.1 中 A01-A35 是否全在本计划中覆盖 |
| C03 | print-config 字段 | 29 个 ConfigModel 字段全在扩展列表中 |
| C04 | 探针映射 | 48 个 GUI 功能逐一在 §4.2.2 中有映射 |
| C05 | sogou 探针 | 5 个 gui_click 条目对应 G18-G22 |
| C06 | 基线补完 | spelling_hints/always_show_comments ganging 在 §8.13 | tone_display/候选窗声调区分在 §8.17 |
| C07 | 文档清单更新 | 需求规范.md 正式文档清单含本计划和所有新增文件 |
| C08 | 版本号 | 需求规范.md 基线版本和修订日期需要更新 |
| C09 | 无孤儿引用 | 所有 `见 §X.X` 的交叉引用指向存在的章节 |
| C10 | CLI 测试覆盖 | 每个新增 CLI 命令至少 2 个测试场景（见 §4.1.4） |

### 6.2 每个阶段完成后的检查

| # | 阶段 | 检查项 |
|---|:--:|--------|
| C11 | A | `dotnet run --project ... -- <每个新命令>` 不崩溃 |
| C12 | A | `test-runner` 中包含每个新命令的测试 |
| C13 | A | `print-config` 输出包含全部 29 个 ConfigModel 字段 |
| C14 | B | 各组 `Phase.A/B/C.ps1` Destroy→Rebuild→Probe 全链路退出码 0 |
| C15 | B | 每组独立隔离，Phase A→全量摧毁→Phase B→全量摧毁→Phase C 顺序执行 |
| C16 | C | sogou 5 条 gui_click 在 C1 环境下 completed + evidenced |
| C17 | D | 基线 §8.13 新增 ganging 说明存在且与技术实现一致 |
| C18 | D | 基线 §8.17 新增候选窗声调区分说明存在 |
| C19 | E | 需求规范.md 文档清单与 `glob docs/*.md` 一致 |
| C20 | E | 需求更新清单.md 记录所有阶段的完成状态 |

### 6.3 最终一致性检查

| # | 检查项 |
|---|--------|
| C21 | `需求规范.md` §7.4.19 CLI 命令数量与 `Program.cs` 注册数量一致 |
| C22 | `需求规范.md` §7.4.20 GUI 控件数量与 `WindowsPrototypeForm.cs` 控件数量一致 |
| C23 | `开发约定与最佳实践.md` §3 CLI 命令清单与实际 CLI 命令数量一致 |
| C24 | 任何文档中都没有"print-config 只覆盖 9 字段"的未修订描述 |
| C25 | 本计划文档的头部骨架引用章节与实际文档结构一致 |

## 7. 本计划能证明什么，不能证明什么

本计划能证明：

1. 对 48 个 GUI 功能、19 个 CLI 命令、19 项上游运行态基线的系统性缺口分析已经完成。
2. 补全路径、优先级、依赖关系已经明确。
3. 后续执行者可以仅凭本计划（配合现有基线文档）独立完成各阶段任务。

本计划不能证明：

1. CLI 或 GUI 已经完成上述任何补全。
2. 探针链路已经打通。
3. 各阶段的预估工作量是精确的。
4. 本计划列出的 CLI 命令设计方案在实现过程中不需要调整（实现中发现的约束需要回写本计划）。

## 8. 2026-05-30 进度更新

### 8.1 GUI 输入设置全量覆盖 — 已完成

原计划中未明确列出的 Weasel 49 字段 GUI 覆盖任务已于 baseline-2026-05-30-r33 完成。

**实现内容：**

| 子页 | 新增控件 | 段归属 | 状态 |
|------|:--:|------|:--:|
| 输入 | 5（语境建议、üe兼容、自定义短语、符号配置、预编辑格式） | 全部 → 输入方案选项 | ✅ |
| 显示 | 10（标签字体×2、注释字体×2、通知时长、标签格式、标记文本、滚轮翻页、候选缩写、注释内容） | 8→承载器选项 + 2→输入方案选项 | ✅ |
| 窗口 | 14（窗口模式×5、预编辑×2、交互×7） | 全部 → 承载器选项 | ✅ |
| 布局 | 21（尺寸×4、边距×3、间距×4、高亮×4、效果×4、对齐+提示×2） | 全部 → 承载器选项 | ✅ |

**关键设计决策：**
- 段标签按 YAML 写入目标判定（weasel.custom.yaml → 承载器选项，rime_mint.custom.yaml → 输入方案选项）
- 仅 2 个字段跨层（CandidateSettings.Layout、ShowEmojiComments），Weasel 侧为机械镜像，归入输入方案选项
- PagingOnScroll 和 CandidateAbbreviateLength 最初误归入输入方案选项，经逐字段核查后修正为承载器选项
- SymbolProfileId 仅 1 个 preset，以 Disabled ComboBox 展示，防止将来遗漏
- int? 字段用 0=null 规则；bool? 字段用 ComboBox 三态

**测试覆盖：**
- 4 项控件存在性测试覆盖所有新页面
- ALL TESTS PASSED

### 8.2 GitHub 下载代理回退 — 已完成

原计划中未涵盖的新增功能。针对中国大陆网络环境 GitHub 下载不稳定问题，实现了 `ResumableDownloader` 级自动代理回退机制。

**核心实现：**
- 新增 `GitHubProxyHelper.cs`：域名检测 + 代理 URL 生成
- 重构 `ResumableDownloader`：`ExecuteDownloadWithFallback<T>` 统一编排
- 速度监测：8s/50KBps 阈值自动切换
- 代理优先级：gh.llkk.cc → gh-proxy.com
- 非 GitHub URL 完全不受影响

**测试覆盖：**
- 10 项专项测试（5 项 GitHubProxy + 5 项下载行为）
- ALL TESTS PASSED

### 8.3 CLI 命令清单（回顾）

阶段 A 的 CLI 命令补全已在 baseline-2026-05-18-r29 完成（18 个 CLI 命令）。本次变更不涉及 CLI 命令增减。

### 8.4 当前状态总结（baseline-2026-05-30-r33）

| 维度 | 状态 |
|------|:--:|
| CLI 命令覆盖 | 18 命令，全部 C 类闭环 |
| GUI 功能覆盖（原有 48 项） | 48/48 ✅ |
| Weasel 49 字段 GUI 覆盖 | 49/49 ✅ |
| 下载代理回退 | ✅ |
| ConfigModel 默认值迁移 | ✅ |
| TemplateService 基础实现 | ✅ |
| 流水线截图持久化 | ✅ |
| 测试运行器改进 | ✅ |
| Hermetic 测试 | ALL TESTS PASSED (254) |

### 8.5 2026-05-31 新增：ConfigModel 默认值迁移 + GUI 多栏重构

**ConfigModel 默认值迁移**：所有产品硬编码默认值（字体、字号、主题、开关状态等）已全部清零/置空。`bool` 字段改为 `bool?`（null="使用上游默认"），`string` 字段改为 `""`（空串="不写入YAML"），`int` 字段归零。YAML 生成和验证均添加相应守卫。

**GUI 多栏重构**：4 个输入设置子页全部采用多栏布局 + GroupBox 段标签 + 控件描述 + AutoScroll。控件宽度统一 300px。主题和字体重命名符合用户要求。

**测试器改进**：支持单测试精确过滤和日志文件持久化。`test_results.log` 含完整异常堆栈。Console 输出每测试后 Flush 防卡死。

**流水线改进**：结果写入时间戳隔离目录。截图扁平化命名并持久化。单独运行 Phase 回退到 `_latest/`。

### 8.6 2026-06-03 G-test 扩展与字段覆盖补完（baseline-2026-06-03-r37）

#### G-test 组新增

在原有 G1-G29 的基础上新增 4 组 G-test：

| 组 | 类型 | 覆盖字段 | 状态 |
|------|------|------|:--:|
| G30_visual_text_orientation | 视觉截图（A/B/C） | `style/text_orientation` | ✅ |
| G31_visual_layout_type | 视觉截图（A/B/C） | `style/layout/type` | ✅ |
| G32_comment_style_variant | 视觉截图（A/B/C） | 注释隐藏（`comment_style_variant=none`） | ✅ |
| G33_custom_phrase_mode | 文本探针（A/B/C） | 自定义词条上屏（`custom_phrase_mode=full_phrase`） | ✅ |

#### G29 CLI 交互组扩展

G29 的 3 个 phase 各扩增至 25 条 set-config 命令，覆盖全部新增和已有字段的 CLI 路径。新增覆盖字段：text_orientation, layout_window_type, symbol_profile_id, preedit_format_mode, custom_phrase_mode, comment_style_variant, collocation_max_length/min_length/penalty, non_collocation_penalty, weak_collocation_penalty, rear_penalty, max_homophones, max_homographs, simplification_mode, emoji_suggestion_enabled, page_size, layout, show_emoji_comments, contextual_suggestions_enabled, fuzzy_enabled。

#### Weasel 字段全量覆盖状态

此前遗漏的 `style/text_orientation` 和 `style/layout/type` 已全部纳入 ConfigModel、CLI、GUI 和 YAML 生成覆盖。Weasel 定制化 Wiki 中除已废弃的 `mouse_hover_ms` 外全部 52 个字段均已覆盖。

#### 测试覆盖总计

| 类别 | 数量 |
|------|:--:|
| Hermetic SetConfig | 35 |
| Hermetic Apply | 29 |
| GUI 控件存在性 | 14 |
| G-test 组 | 33（G1-G33） |

### 8.7 2026-06-16 从文件安装功能完成

#### GUI 新增按钮

4 个页面各新增「从文件安装」按钮，作为「下载并安装」的补充入口：

| 页面 | 按钮 | 文件过滤器 |
|------|------|------|
| 承载器 | 从文件安装 | `*.exe` |
| 输入方案 | 从文件安装 | `*.zip` |
| 词库 | 从文件安装 | `*.dict.yaml;*.zip;*.scel` |
| 语法模型 | 从文件安装 | `*.gram` |

#### CLI 新增参数

| 命令 | 参数 | 说明 |
|------|------|------|
| `install-weasel` | `--from-file <path>` | 从本地文件安装 Weasel |
| `install-weasel` | `--download-only --output <path>` | 仅下载最新版安装器，不启动安装器 |
| `install-resource` | `--from-file <path>` | 从本地文件安装资源 |

#### G-test 管线改造

`Invoke-PhaseRebuildCli` 全部改用 `--from-file` + `PreDownloadAssets` 预下载缓存。Weasel 下载走 CLI `--download-only`（复用 `ResolveGitHubReleaseAssetUrl` 动态解析，零硬编码版本号），其余资源通过 `Invoke-WebRequest` 预下载到 `%TEMP%\rimekit-pipeline-cache\`。

#### 设计原则

- `InstallOrUpdateResource` 下载逻辑**零修改**
- `ResolveInstallerLaunchPath` / `DownloadInstaller` **零修改**
- 从文件安装走独立入口，后处理链路全复用
- `EnsureDictionaryAlias` 两个安装路径均强制调用