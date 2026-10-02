# AGENTS.md — openAlfred

面向在本仓库上工作的编码代理。**改代码前先读这份**。它约定了项目需求、任务拆分方式、实现节奏与验收标准。

## 1. 项目速览（需求）

openAlfred 是一个免费、开源、轻量的 **Windows 效率启动器**，致敬 macOS 的 Alfred：一个全局热键唤起，单个搜索框内完成「启动应用 / 算表达式 / 查剪贴板 / 找文件 / 转 JSON / 看时间」。

- 技术栈：**WPF on .NET 8 + C#**，UI 库 `WPF-UI`（Mica 毛玻璃、大圆角），托盘 `Hardcodet.NotifyIcon.Wpf`，MVVM 辅助 `CommunityToolkit.Mvvm`。
- 视觉语言：iOS / macOS Spotlight 风格——单一强调色 `#007AFF`、8pt 栅格、纯键盘可完成全部操作。设计令牌与交互规范见 [`docs/design.md`](docs/design.md)。
- 数据全部本地：无账号、无联网、无遥测，落盘于 `%APPDATA%\openAlfred`（`settings.json` / `clipboard.json` / `images/`）。

### 核心目标
- 唤起快、内存低、包体小（框架依赖约 2 MB、自包含约 65 MB）。
- 功能可插拔：新增能力 = 新增一个 `IQueryProvider`，不动主流程。
- 常驻后台稳定运行，任何单次唤起/查询失败都**不能**让进程崩溃退出。

### 非目标（不要做）
- 不做全磁盘索引、不索引 Microsoft Store 应用。
- 不引入新框架/重型依赖；不引入联网功能与遥测。
- 不把界面做成营销落地页；保持「单一输入框」。

## 2. 命令

从仓库根目录执行（Windows PowerShell，用 `;` 而非 `&&`）：

```powershell
dotnet build OpenAlfred.sln -c Debug      # 全量构建
dotnet test                                # 运行单元测试（当前 73 个）
dotnet run --project src/OpenAlfred.App    # 本地运行

# 发布：框架依赖（小）
dotnet publish src/OpenAlfred.App -c Release -r win-x64 --self-contained false -o publish
# 发布：自包含单文件（免装 .NET）
dotnet publish src/OpenAlfred.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -o artifacts/selfcontained
```

目标框架：`OpenAlfred.Core` 与测试为 `net8.0`（**不依赖 WPF**）；`OpenAlfred.App` 为 `net8.0-windows`。

## 3. 架构与关键文件（任务拆分的地基）

分层铁律：`Core` 平台无关、可被单元测试直接引用；`App` 是 WPF 表现层，引用 `Core`。**业务逻辑一律写在 Core**，App 只做窗口、热键、托盘、主题和动作执行。

```
src/
├── OpenAlfred.Core/          # 计算、模糊匹配、JSON、时间、路由、索引、剪贴板存储
│   ├── Providers.cs          # 各 IQueryProvider + QueryRouter（关键词路由 + 合并排序）
│   ├── Query.cs              # QueryContext / QueryResult / ResultActionKind / IQueryProvider
│   ├── Calculation/Calculator.cs
│   ├── Json/JsonService.cs
│   ├── Time/TimeService.cs
│   ├── Apps/AppIndex.cs      # 开始菜单快捷方式索引
│   ├── Files/FileIndex.cs    # 文件名内存索引 + FileSystemWatcher 增量
│   ├── Clipboard/ClipboardStore.cs  # 文本/图片历史、去重计数、JSON 持久化
│   ├── FuzzyMatcher.cs       # 前缀 > 词首 > 包含 > 子序列 打分
│   └── RelativeTime.cs       # iOS 风格相对时间
└── OpenAlfred.App/           # WPF 表现层
    ├── App.xaml.cs           # 组合根：单实例、服务装配、托盘、主题
    ├── MainWindow.xaml(.cs)  # 搜索框 + 结果列表、全局热键、键盘操作、动画
    ├── SettingsWindow.xaml(.cs) # 主题、两个热键录制、后台/剪贴板开关
    ├── Services/             # Hotkey / AppSettings / ClipboardMonitor / ActionExecutor / ThemeService
    └── Interop/NativeMethods.cs # RegisterHotKey、SendInput 等
```

### 扩展点：加一个功能 = 加一个 Provider
1. 在 `Core/Providers.cs` 实现 `IQueryProvider`（`Keyword` / `Priority` / `CanHandle` / `ExecuteAsync`）。
2. 在 `App.xaml.cs` 的 `OnStartup` 里把新 Provider 加入 `QueryRouter` 数组。
3. 结果通过 `QueryResult.Action`（`ResultActionKind`）声明动作；需要新动作时在 `Query.cs` 加枚举并在 `ActionExecutor.cs` 落地。
4. 关键词路由前缀在 `QueryRouter.ReservedKeywords` 注册；空查询兜底参与混排的 Provider 由 `CanHandle` 决定。

## 4. 实现节奏

按「先逻辑后壳、先测后接、小步提交」推进：

1. **核心逻辑（Core）先行**：新行为先在 Core 实现并补 xUnit 测试（Core 无 WPF 依赖，测试快）。
2. **接线（App）**：把 Core 能力接进 `QueryRouter` / `ActionExecutor` / 窗口事件。
3. **UI 打磨**：遵循 `docs/design.md` 的设计令牌与交互规范，保持中文文案风格统一。
4. **回归**：`dotnet build`（须 0 警告 0 错误）+ `dotnet test`（全绿）后，再手动冒烟一遍受影响的关键词路由。
5. **文档同步**：功能/行为变化时更新 `README.md` 与 `docs/design.md`（如里程碑 M1–M4、能力表）。

里程碑（详见 design.md §5）：M0 原型 → M1 唤起+路由+计算器 → M2 JSON/时间/关键词 → M3 剪贴板+文件 → M4 设置/托盘/主题/动画/单测/交付 exe。

## 5. 编码约定

- `Nullable` 全开、file-scoped namespace、`LangVersion latest`；注释用中文、密度与周边一致。
- JSON 持久化统一 camelCase（`[JsonPropertyName]`），字段命名保持向后兼容（新增字段给默认值，避免旧数据加载失败）。
- 只用一个品牌强调色，其余走灰阶层级；间距走 8pt 栅格；文案沿用现有措辞（如「刚刚 / n 分钟前 / 今天 HH:mm」）。
- 结果条数：默认每 Provider ≤ 5、总列表 ≤ 8；**仅** `clip` 直达模式放宽（列表 50、路由截断 50），用于展示完整历史。
- 改动保持最小化，不做无关重构 / 重排；改前先看 diff，不回滚他人未提交的改动。

## 6. 已知陷阱（务必规避）

- **WPF 动画**：未开 `AllowsTransparency` 的 `Window` 设 `RenderTransform` 会抛异常并带走常驻进程——出现动画只作用于内容根 `Border`（`RootBorder`），不要作用到 Window 本体。
- **WPF-UI 图标名**：`SymbolIcon` 的 `Symbol` 必须是 `SymbolRegular` 的真实成员名，编译期不报错、运行时抛 `FormatException`。正确示例：`ClipboardTextLtr24`（非 `ClipboardText24`）、`CodeCircle24`（非 `Code24`）。用新图标名前先核实。
- **常驻进程兜底**：`DispatcherUnhandledException` 置 `Handled = true`；全局热键 `WndProc` 回调用 `try/catch` 兜住，唤起失败只提示不崩溃。
- **两个全局热键**：主唤起默认 `Alt+Space`，剪贴板历史直达默认 `Ctrl+Alt+V`。用独立 hotkey id（主 `0xA1F0/0xA1F1`、剪贴板 `0xA1F2/0xA1F3`），换绑走「先注册备用 id 探测成功再注销旧 id」，避免注册失败丢绑定。
- **剪贴板防回环**：本应用主动写剪贴板前经 `ActionExecutor.BeforeClipboardWrite` 置 `ClipboardMonitor.IgnoreNextUpdate = true`，避免把自己写入的记录再次记成历史。
- **剪贴板重复计数**：相同文本 / 相同图片内容（按 PNG 的 SHA256）不新增记录，而是移到最前、刷新时间并 `Count++`；图片合并时删除刚落盘的重复临时文件。

## 7. 验收标准（交付前逐项过）

- [ ] `dotnet build OpenAlfred.sln -c Release`：**0 警告、0 错误**。
- [ ] `dotnet test`：全部通过；**新增/改动的 Core 逻辑必须带对应 xUnit 测试**（如去重计数、路由解析、计算/JSON/时间）。
- [ ] 手动冒烟：
  - 两个热键均可唤起；设置页能录制并改绑、冲突时保留原绑定。
  - 关键词路由逐个验证：算式置顶、`app` / `clip` / `file` / `json` / `time`、无前缀混排兜底。
  - 剪贴板：复制文本与图片入历史、重复计数 `×n`、`Enter` 写回并 `Ctrl+V` 粘回原窗口、`Ctrl+Backspace` 删除。
  - 主题切换跟随；关窗收进托盘、进程不退出；开机自启/启动即最小化生效。
- [ ] 行为/功能变化已同步 `README.md` 与 `docs/design.md`。
