# DwgSearcher 🔍

> **无需安装/打开 AutoCAD，即可在海量 DWG 图纸中极速检索文字！**  
> 专为工程设计、机械制造与建筑行业打造的轻量、极速本地 CAD (`.dwg` / `.dxf`) 全文检索利器。  
> 轻松实现 **Searching text in DWG drawings (without AutoCAD)**、**Searching multiple DWG files for text** 以及 **Search across multiple files and directories for particular words in DWG files**！告别逐个翻找图纸与卡顿加载，毫秒级定位图号、技术要求、材料明细与尺寸标注！

<p align="center">
  <b>简体中文</b> | <a href="README.md">English</a>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512BD4?logo=dotnet" alt=".NET" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20(x64)-0078D6?logo=windows" alt="Platform" />
  <img src="https://img.shields.io/badge/License-MIT-green.svg" alt="License" />
  <img src="https://img.shields.io/badge/Database-SQLite%20FTS5-blue?logo=sqlite" alt="SQLite" />
  <img src="https://img.shields.io/badge/AutoCAD-无需安装运行-success" alt="无需 AutoCAD" />
</p>

---

## 🎯 为什么选择 DwgSearcher？(Core Scenarios)

- **脱离 AutoCAD 极速检索 (*Searching text in DWG drawings without AutoCAD*)**：完全独立运行，无需安装、购买或在后台启动笨重的 AutoCAD / CAD 软件。
- **海量图纸批量搜文本 (*Searching multiple DWG files for text*)**：瞬间遍历本地磁盘或局域网共享目录下的成千上万张 `.dwg` / `.dxf` 图纸。
- **跨多文件夹多目录精准查找 (*Search across multiple files and directories for particular words*)**：递归多层级目录监控与基于 SQLite FTS5 的高性能倒排索引，毫秒级返回匹配结果。
- **全要素无死角提取**：不仅提取常规单行/多行文字，还能深度检索标题栏属性块（`ATTRIB`）、尺寸标注数值、形位公差、物料清单明细表（BOM Table）等。


---

## 📸 界面预览 (Screenshots)

### 1. 主检索界面 (Main Search Interface)
*输入关键词即刻呈现结果，高亮显示图纸上下文，并同步展示 CAD 原生缩略图：*
![主检索界面](docs/screenshots/search_interface.png)

### 2. 设置与目录管理 (Settings & Folder Management)
*便捷管理索引文件夹、开关后台实时监听与中英文界面切换：*
![设置界面](docs/screenshots/settings_interface.png)

---

## ✨ 核心特性 (Features)

- ⚡ **毫秒级极速全文检索**
  - 基于 **SQLite FTS5** 倒排索引引擎，结合 `tokenize = 'trigram'` 三元分词算法。
  - 支持中英文混合、非连续与连续子串匹配，完美胜任各种复杂工程图号、零件编号（如 `FG5738100-2C`、`DN100`、`M12-8.8`、`接地环`）的精准与模糊定位。
  - 150ms 智能防抖实时检索，边输入边出结果。

- 📐 **CAD 全要素深度提取 (Deep Extraction)**
  - **单行与多行文字**：`TEXT`、`MTEXT`。
  - **标题栏与图块属性**：块参照属性（`INSERT -> ATTRIB`）、图块定义与属性定义（`ATTDEF`）。
  - **尺寸标注与形位公差**：尺寸标注文本（`DIMENSION`）、形位公差框格（`TOLERANCE`）。
  - **表格与明细栏**：CAD 原生表格（`TableEntity`）单元格数据、物料清单（BOM）。
  - **引线与外部参照**：多重引线（`MultiLeader`）、外部参照路径（`XREF`）。
  - **图纸元数据**：摘要信息（`SummaryInfo`）、标题、主题、作者、超链接等。

- 🧹 **AutoCAD 格式智能净化 (CadTextCleaner)**
  - 内置高性能预编译正则清洗管线，深度剥离 MText 富文本控制字符（例如 `\A1;`, `\P`, `\f...;`, `{\H1.5x;...}`, `\S+0.02^-0.01;` 等），过滤乱码与控制符，还原真实图纸文本。

- 🖥 **现代经典桌面布局 (WPF)**
  - **左侧列表**：显示匹配图纸、Windows 原生 CAD 关联图标、文件大小、路径以及命中摘要。
  - **右上详情**：提取出的完整文本内容，命中的关键词醒目黄色高亮，并自动定位滚动至首个命中词。
  - **右下预览**：结合 Windows Shell 缩略图接口与图纸内嵌位图提取技术，即时显示高清图纸缩略图。

- 🔄 **后台静默监听与增量更新**
  - 支持配置多个 CAD 监控目录，支持递归扫描子目录。
  - 内置 `FileSystemWatcher` 与防抖机制，当图纸新建、修改或保存时，后台自动静默重提文本并增量入库。
  - 启动零阻塞：秒开软件，后台独立线程执行健康度核对与补全，完全不卡顿前台操作。

- 🌐 **多语言界面支持 (Internationalization)**
  - 原生支持 **简体中文 (Simplified Chinese)** 与 **English (United States)** 动态热切换。

---

## 📖 使用指南 (User Guide)

### 1. 首次配置监控目录
1. 启动 `DwgSearcher`，点击主界面右上角的 **“⚙️ 设置 (Settings)”** 按钮。
2. 在弹出的设置窗口中，点击 **“➕ 添加 CAD 文件夹 (Add CAD Folder)”** 选择存放工程图纸的本地或网络目录。
3. 勾选 **“包含子目录 (Include Subfolder)”** 以递归索引层级文件夹。
4. 勾选 **“启用后台实时监听 (Enable background live watcher)”**（推荐开启），当在 AutoCAD 中保存修改或新增图纸时，软件会自动捕捉并更新索引。
5. 点击 **“保存并应用 (Save & Apply)”**，系统将在后台自动完成图纸的首次提取与索引构建。

### 2. 图纸搜索与查找
- **直接键入**：在顶部搜索框中直接输入任意文字、图号、材料名或技术参数（例如 `dimension`、`法兰`、`SUS304`）。
- **复合搜索**：支持多个关键词用空格隔开检索（如 `法兰 DN50`）。
- **清空搜索**：点击搜索框右侧的 `×` 按钮可一键清空输入并重置回全部图纸列表。

### 3. 查看详情与图纸交互
- **高亮上下文**：在左侧列表中点击任意一项，右上角会显示该图纸中提取的全部文字，搜索关键词以显眼色块突出标注，便于阅读说明书或技术规范。
- **图纸预览**：右下角可直接查看该图纸的缩略图，辅助快速确认版面版次。
- **直接打开图纸**：
  - **双击** 列表中的任一图纸条目；
  - 或者选中后点击预览区上方的 **“🚀 打开 CAD (Open CAD)”** 按钮，即可使用系统默认安装的 CAD 软件（如 AutoCAD、浩辰CAD、中望CAD 等）直接打开该图纸。
- **右键菜单功能**：
  - **打开文件 (Open File)**：直接启动 CAD 打开。
  - **打开所在文件夹 (Open Containing Folder)**：在 Windows 资源管理器中打开并定位选中该图纸文件。
  - **复制文件路径 (Copy Full Path)**：复制图纸完整物理路径至剪贴板。
- **一键复制提取文本**：点击右上角的 **“📋 复制文本 (Copy Text)”**，快速将提取到的所有文字文本复制到剪贴板，方便粘贴到 Excel、Word 或 ERP/PLM 系统中。

---

## 🛠 技术架构 (Architecture)

```text
DwgSearcher/
├── App.xaml / App.xaml.cs          # WPF 桌面应用生命周期与全局资源
├── Views/
│   ├── MainWindow.xaml (.cs)       # 主界面 (搜索栏、三栏式布局、缩略图与文本联动)
│   └── SettingsWindow.xaml (.cs)   # 设置弹窗 (监控路径管理、开关、多语言切换)
├── ViewModels/
│   └── SearchResultItem.cs         # 结果视图模型与系统 Shell 图标绑定
├── Engine/
│   ├── IndexingEngine.cs           # 增量扫描、指纹比对与批量事务入库引擎
│   └── SearchEngine.cs             # FTS5 Trigram 倒排检索、BM25 评分与降级引擎
├── Storage/
│   ├── DatabaseManager.cs          # SQLite FTS5 虚拟表 + WAL 模式连接生命周期管理
│   └── PathHelper.cs               # 跨平台路径及数据库存储位置定位
├── Services/
│   ├── ConfigService.cs            # JSON 配置文件读写与默认初始化
│   ├── FileWatcherService.cs       # FileSystemWatcher 多目录防抖监听服务
│   ├── ShellThumbnailHelper.cs     # Windows Shell API 原生图标与缩略图提取
│   ├── DwgPreviewExtractor.cs      # DWG 二进制格式内嵌 BMP 位图降级解析
│   └── LocalizationService.cs      # 多语言资源字典与动态切换服务
└── TextExtraction/
    ├── CadTextCleaner.cs           # 正则表达式剥离 MText 格式控制字符
    └── DwgTextExtractor.cs         # 基于 ACadSharp 的 CAD 全要素深度提取器
```

---

## 🚀 编译与运行 (Build & Run)

### 环境要求
- **操作系统**：Windows 10 / Windows 11 (x64)
- **开发工具/SDK**：[.NET 8.0 SDK 或 .NET 10.0 SDK](https://dotnet.microsoft.com/download)

### 本地编译运行
```powershell
# 1. 克隆代码仓库
git clone https://github.com/funekaa/DwgSearcher.git
cd DwgSearcher

# 2. 编译并直接运行
dotnet run
```

### 打包发布单文件免安装版本 (.exe)
```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```
编译产物将生成在：  
`bin/Release/net10.0-windows/win-x64/publish/DwgSearcher.exe`（单文件直接分发运行，无需预装 .NET 运行时）。

---

## 📄 开源协议 (License)

本项目采用 [MIT 许可证](LICENSE) 开源。欢迎提交 Issue 与 Pull Request！
