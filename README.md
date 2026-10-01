<p align="center">
  <img src="assets/app.ico" width="96" alt="openAlfred 图标" />
</p>

<h1 align="center">openAlfred</h1>

<p align="center">
  一个免费、开源、轻量的 <b>Windows 效率启动器</b>，致敬 macOS 上的 <a href="https://www.alfredapp.com/">Alfred</a>。<br/>
  一个全局快捷键唤起，搜索框内搞定<strong>启动应用、算表达式、查剪贴板、找文件、转 JSON、看时间</strong>。
</p>

<p align="center">
  <img src="docs/mockups/openAlfred-design-overview.png" width="720" alt="openAlfred 六大功能界面总览" />
</p>

---

## 为什么做这个

macOS 有 Alfred、Windows 有 PowerToys Run，但我想要一个**更小、更快、界面更克制**的东西：
不用打开浏览器算个 `128*7+33`，不用在资源管理器里翻半天找一个忘了放哪的文件，
不用来回 `Ctrl+V` 粘贴三条历史剪贴板——**敲一下快捷键，全在同一个搜索框里解决**。

设计上参考了 Apple iOS / macOS 的视觉语言：Mica 毛玻璃、18px 圆角、8pt 栅格、
`#007AFF` 强调色、深浅色主题自动跟随系统。纯键盘可完成全部操作。

## 功能一览

| 能力 | 怎么触发 | 说明 |
|---|---|---|
| 🚀 **应用启动器** | 直接输入应用名，或 `app <名称>` | 索引开始菜单快捷方式，输入 `chrome` `code` 秒开，回车启动 |
| 🧮 **计算器** | 直接输入算式 | `1+2*3`、`(3+4)/5`、`2^10`、`sqrt(144)`、`20% of 80`、`sin(pi/2)`，实时出结果 |
| 📋 **剪贴板历史** | `clip` 或 `clip <关键词>` | 记录文本与图片（上限 200 条），回车自动粘贴回原窗口 |
| 📁 **文件检索** | `file <名称>` 或直接输入 | 内存索引 + 模糊匹配 + 增量监听，回车打开、`Ctrl+Enter` 定位 |
| 🔧 **JSON 转换** | `json <内容>` 或粘贴即识别 | 格式化 / 压缩，语法高亮，出错时精确到行列 |
| 🕐 **时间查询** | `time` / `time 东京` / `time +3d` | 本机时间、世界城市、相对时间换算 |

**通用交互**：`↑` `↓` 选择 · `Enter` 执行 · `Tab` 补全关键词 · `Ctrl+C` 复制不关窗 · `Esc` 关闭。

## 特性

- ⌨️ **全局热键唤起**，默认 `Alt + Space`，可在设置页一键录制修改
- 🧊 **Mica 毛玻璃** + 深浅色主题自动跟随系统
- 🗜️ **常驻后台**，最小化收进系统托盘，可设置开机自启、启动即最小化
- 🔒 **纯本地**：无账号、无联网、无遥测，数据只存在 `%APPDATA%\openAlfred`
- 🪶 **轻量**：框架依赖包约 150 KB，内存占用低
- ✅ **可靠**：核心逻辑（计算解析、模糊匹配、JSON、时间、路由）单元测试覆盖

## 安装

### 方式一：下载发布包（推荐）

到 [Releases](../../releases) 页面下载最新版本，解压到任意目录，双击 `openAlfred.exe` 启动，按 `Alt + Space` 开始使用：

| 文件 | 体积 | 适用场景 |
|---|---|---|
| `openAlfred-x.y.z-win-x64-selfcontained.zip` | ≈ 65 MB | **免装 .NET，下载即用**（推荐普通用户） |
| `openAlfred-x.y.z-win-x64.zip` | ≈ 2 MB | 已安装 .NET 8 Desktop Runtime 的用户 |

> 框架依赖版若提示缺运行时，[下载 .NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) 即可。

### 方式二：从源码构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```bash
git clone https://github.com/tyrantqiao/openAlfred.git
cd openAlfred

# 运行
dotnet run --project src/OpenAlfred.App

# 发布为独立 exe（框架依赖，体积小）
dotnet publish src/OpenAlfred.App -c Release -r win-x64 --self-contained false -o publish

# 发布自包含单文件（免装 .NET，下载即用，约 65 MB）
dotnet publish src/OpenAlfred.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o artifacts/selfcontained

# 运行测试
dotnet test
```

产物在 `publish/openAlfred.exe`。

## 使用与设置

- **唤起 / 隐藏**：`Alt + Space`（Toggle 式）
- **设置入口**：系统托盘图标右键 →「设置…」，可修改：
  - 全局唤起快捷键（内置录制器，冲突时保留原绑定并提示）
  - 外观主题（跟随系统 / 浅色 / 深色）
  - 最小化到托盘、开机自启、启动即最小化、剪贴板记录开关
- **退出**：托盘图标右键 →「退出」（关闭主窗口仅收进托盘，不退出程序）

## 项目结构

```
openAlfred/
├── src/
│   ├── OpenAlfred.Core/     # 平台无关的核心逻辑：计算器、模糊匹配、JSON、时间、路由、索引
│   └── OpenAlfred.App/      # WPF 表现层：窗口、热键、托盘、主题、动作执行
├── tests/
│   └── OpenAlfred.Core.Tests/  # xUnit 单元测试
├── docs/
│   ├── design.md            # 设计说明（Design Tokens、交互规范、模块 I/O、里程碑）
│   └── mockups/             # 高保真 HTML 原型与效果图
└── assets/                  # 应用图标
```

架构上 `Core` 不依赖 WPF，所有功能以 `IQueryProvider` 抽象接入 `QueryRouter`，
新增一个功能 = 实现一个 Provider，便于扩展。

## 已知限制

- 应用启动器暂不索引 Microsoft Store 应用（其入口不在开始菜单文件系统内）
- 文件索引默认覆盖用户目录 / 桌面 / 文档 / 下载，超大全盘索引非目标（保持轻量）

## Roadmap

- [ ] 应用启动器：使用频率加权排序、显示应用真实图标
- [ ] 单位 / 汇率换算
- [ ] 自定义 Web 搜索、快捷命令（Workflow 雏形）
- [ ] 打包为 MSI 安装包

## 许可证

本项目以 [MIT License](LICENSE) 开源，欢迎 Issue 与 PR。

---

<p align="center">如果它帮到了你，给个 ⭐ 是最好的支持。</p>
