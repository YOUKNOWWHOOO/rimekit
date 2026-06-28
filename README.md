# 韵匣（RimeKit）

韵匣是一个 Windows 桌面工具，帮助你管理小狼毫（Rime/Weasel）输入法。本项目通过 vibe coding 开发。

## 背景

小狼毫（Rime/Weasel）是一个强大的开源输入法引擎，支持拼音、双拼、五笔等多种输入方案，并可以安装词库和语法模型来增强输入体验。但它默认的配置方式是通过编辑 YAML 文本文件完成的，这对不熟悉技术的普通用户来说门槛很高。

韵匣的目标是：**用图形界面完成所有配置，零命令行、零手动编辑文件。**
目前只支持薄荷拼音-全拼输入方案。

## 目前能做什么

- 一键下载安装、卸载、完全清理小狼毫输入法
- 一键安装、卸载薄荷拼音输入方案
- 一键安装、卸载词库（moetype ACG词库、搜狗网络流行新词、zhwiki百科词库）
- 一键安装、卸载万象官方语法模型，提升长句输入准确度
- 支持从本地文件安装（见下文「从文件安装」说明）
- 可视化设置候选窗外观：主题、字体、字号、颜色、圆角、阴影、边框、布局等
- 可视化设置输入行为：简繁切换、全角半角、中英文标点、Emoji 候选、声调显示、模糊音
- 管理自定义词条（新增、删除、导入）
- 命令行版本也可用（`RimeKit.Windows.Cli.exe`），支持自动化脚本和 AI agent 调用

## 目前不能做什么

- 没有 Android 版本（开发中）
- 没有 Windows 和 Android 之间的同步功能（计划中）

## 安装与使用

1. 从 [Releases](https://github.com/YOUKNOWWHOOO/rimekit/releases) 页面下载最新版本的 zip 压缩包
2. 解压到任意目录，双击 `RimeKit.Windows.Gui.exe` 运行
3. 无需安装任何额外运行环境
4. 在"承载器"页面点击「下载并安装小狼毫」
5. 安装完成后，在"输入方案"页面点击「下载并安装输入方案」
6. 在"词库"页面下载你需要的词库
7. 在"输入设置"页面的「显示」「输入」「窗口」「布局」「配色」子页中，调整输入法的外观和行为
8. 点击底部「应用设置」使配置生效

首次使用建议顺序：安装小狼毫 → 安装薄荷方案 → 安装词库 → 调整设置。

## 从文件安装

如果软件自带的下载功能遇到网络问题，你也可以手动下载文件后，通过各页面的「从文件安装」按钮来安装。

### 小狼毫安装器
- **下载地址**：https://github.com/rime/weasel/releases（选择最新版 `.exe` 安装包）
- **文件类型**：`.exe`
- **操作方法**：承载器页 → 从文件安装 → 选择下载好的 `.exe` 文件

### 输入方案
- **下载地址**：https://github.com/Mintimate/oh-my-rime（Code → Download ZIP）
- **文件类型**：`.zip`
- **操作方法**：输入方案页 → 从文件安装 → 选择下载好的 `.zip` 文件

### 词库
| 词库 | 下载地址 | 文件类型 |
|------|---------|---------|
| moetype ACG 词库 | https://github.com/suiginko/moetype（Code → Download ZIP） | `.zip` |
| 搜狗网络流行新词 | https://pinyin.sogou.com/dict/detail/index/4（点击下载） | `.scel` |
| zhwiki 百科词库 | https://github.com/felixonmars/fcitx5-pinyin-zhwiki/releases（选择最新 `.dict.yaml` 文件） | `.dict.yaml` |
- **操作方法**：词库页 → 选中对应的词库 → 从文件安装 → 选择下载好的文件

### 语法模型
- **下载地址**：https://github.com/amzxyz/RIME-LMDG/releases/download/LTS/wanxiang-lts-zh-hans.gram
- **文件类型**：`.gram`
- **操作方法**：语法模型页 → 从文件安装 → 选择下载好的 `.gram` 文件

## 系统要求

- Windows 10 或更高版本
- 64 位系统

## 相关链接

| 资源 | 官方主页 |
|------|---------|
| 小狼毫（Weasel）输入法 | https://rime.im/download/ |
| 薄荷拼音输入方案 | https://github.com/Mintimate/oh-my-rime |
| moetype ACG 词库 | https://github.com/suiginko/moetype |
| 搜狗网络流行新词 | https://pinyin.sogou.com/dict/detail/index/4 |
| zhwiki 百科词库 | https://github.com/felixonmars/fcitx5-pinyin-zhwiki |
| 万象官方语法模型 | https://github.com/amzxyz/rime_wanxiang/releases |

## 构建

开发者构建需要 .NET 10 SDK：

```powershell
dotnet build apps/windows/RimeKit.Windows.sln
dotnet run --project apps/windows/RimeKit.Windows.Tests
```

发布构建：

```powershell
dotnet publish apps/windows/RimeKit.Windows.Gui/RimeKit.Windows.Gui.csproj -r win-x64 -c Release --self-contained -o publish/
dotnet publish apps/windows/RimeKit.Windows.Cli/RimeKit.Windows.Cli.csproj -r win-x64 -c Release --self-contained -o publish/
Copy-Item -Recurse -Force shared/ publish/shared/
```
