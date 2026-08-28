# DwgSearcher 🔍
> 一套专为工程设计与制造行业打造的轻量、极速本地 CAD (.dwg / .dxf) 全文索引与检索工具系统。核心机制模仿 AnyTXT Searcher：后台静默抽取文本全要素并基于 SQLite FTS5 建立倒排索引，前台实现毫秒级全文检索与缩略图预览。

---

## ✨ 核心特性

- ⚡ **毫秒级极速全文检索**：采用 SQLite FTS5 + `tokenize = 'trigram'` 倒排索引，完美支持中英文、图号、零件编号（如 `FG5738100-2C`、`DN100`、`接地环`）的连续子串无死角匹配。
- 📐 **CAD 全要素深度提取**：
  - **文字**：单行文字 (`TEXT`)、多行文字 (`MTEXT`)
  - **标题栏与图块**：块参照属性 (`INSERT -> ATTRIB`)、属性定义 (`ATTDEF`)
  - **标注与公差**：尺寸标注 (`DIMENSION`)、形位公差 (`TOLERANCE`)
  - **表格与明细栏**：CAD 表格 (`TableEntity`) 单元格文本、物料清单
  - **关联引用**：外部参照 (`XREF`) 路径、多重引线 (`MultiLeader`)
  - **图纸元数据**：摘要信息 (`SummaryInfo`)、作者、自定义属性
- 🧹 **AutoCAD 格式智能净化**：内置预编译正则处理器 (`CadTextCleaner`)，深度剥离 MText 格式控制字符（如 `\A1;`, `\P`, `\f...;`, `{\H1.5x;...}`, `\S+0.02^-0.01;` 等），还原纯净内容。
- 🖥 **AnyTXT 经典现代桌面 UI (WPF)**：
  - **左侧**：搜索文件列表（显示 Windows 系统原生关联的 DWG 图标、文件大小、修改日期与匹配摘要）。
  - **右侧上方**：富文本上下文详情（命中的搜索词黄色高亮呈现）。
  - **右侧下方**：Windows Explorer 资源管理器原生高清缩略图与图形预览。
- 🔄 **后台静默监控与自动增量更新**：
  - 支持配置多个 CAD 文件夹（支持递归子目录）。
  - 利用 `FileSystemWatcher` 与防抖机制，当图纸新建、修改或保存时自动静默增量更新索引。

---

## 🛠 技术架构

```text
DwgSearcher/
├── App.xaml / App.xaml.cs          # WPF 桌面应用入口与资源样式
├── Views/
│   ├── MainWindow.xaml (.cs)       # 主检索界面 (三栏式经典布局)
│   └── SettingsWindow.xaml (.cs)   # 监控目录与自动更新设置弹窗
├── ViewModels/
│   └── SearchResultItem.cs         # 检索结果与系统 Shell 图标绑定
├── Engine/
│   ├── IndexingEngine.cs           # 增量变更比对与批量事务入库引擎
│   └── SearchEngine.cs             # FTS5 Trigram 倒排检索与短词混合引擎
├── Storage/
│   └── DatabaseManager.cs          # SQLite FTS5 + WAL 模式连接生命周期管理
├── Services/
│   ├── ConfigService.cs            # JSON 配置文件管理
│   ├── FileWatcherService.cs       # 文件夹后台实时监听服务
│   ├── ShellThumbnailHelper.cs     # Windows Shell 图标与 Explorer 缩略图获取
│   └── DwgPreviewExtractor.cs      # DWG 嵌入位图降级提取器
└── TextExtraction/
    ├── CadTextCleaner.cs           # CAD 控制字符预编译正则清洗器
    └── DwgTextExtractor.cs         # ACadSharp CAD 全要素深度提取器
```

---

## 🚀 快速开始与构建

### 运行环境要求
- Windows 10 / 11 (x64)
- [.NET 8.0 / .NET 10.0 SDK](https://dotnet.microsoft.com/download)

### 编译与运行
```powershell
# 克隆仓库
git clone <your-repo-url>
cd dwgSearcher

# 编译并运行
dotnet run
```

### 生成独立免安装单个可执行文件 (.exe)
```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```
生成文件位于：
`bin/Release/net10.0-windows/win-x64/publish/DwgSearcher.exe`

---

## 📄 开源许可证
本项目遵循 MIT 开源许可证。
