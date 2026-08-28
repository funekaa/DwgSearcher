namespace DwgSearcher.Services;

/// <summary>
/// 多语言国际化管理服务
/// 支持动态切换（无需重启软件即可无缝刷新全部界面文本）
/// </summary>
public static class LocalizationService
{
    public static event Action? OnLanguageChanged;

    public static readonly List<LanguageOption> SupportedLanguages = new()
    {
        new("zh-CN", "简体中文 (Simplified Chinese)"),
        new("en-US", "English (United States)"),
        new("zh-TW", "繁體中文 (Traditional Chinese)")
    };

    private static string _currentLanguage = "zh-CN";

    public static string CurrentLanguage
    {
        get => _currentLanguage;
        set
        {
            if (_currentLanguage != value)
            {
                _currentLanguage = value;
                OnLanguageChanged?.Invoke();
            }
        }
    }

    public record LanguageOption(string Code, string DisplayName);

    /// <summary>
    /// 获取指定键的翻译文本
    /// </summary>
    public static string Get(string key, params object[] args)
    {
        if (!Translations.TryGetValue(_currentLanguage, out var dict) || !dict.TryGetValue(key, out var template))
        {
            // 降级为中文或英文
            if (Translations["zh-CN"].TryGetValue(key, out template))
            {
                // 找到中文
            }
            else
            {
                return key;
            }
        }

        return args.Length > 0 ? string.Format(template, args) : template;
    }

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        // ------------------ 简体中文 ------------------
        ["zh-CN"] = new()
        {
            ["AppTitle"] = "DwgSearcher - 本地 CAD 图纸全文检索器",
            ["SearchWatermark"] = "输入图号、设计者、文字、标注尺寸、属性等关键词检索 (支持中英文子串)...",
            ["BtnSyncIndex"] = "🔄 增量更新",
            ["BtnSyncIndexTip"] = "立即扫描监控目录下的新增与变动图纸",
            ["BtnSettings"] = "⚙ 目录设置",
            ["BtnSettingsTip"] = "添加/管理图纸监控目录、多语言与自动同步选项",
            ["ResultWaiting"] = "等待输入检索...",
            ["ResultAllCount"] = "全部已索引图纸共 {0} 个",
            ["ResultFoundCount"] = "找到 {0} 个匹配图纸 (耗时 {1:F2} ms)",
            ["EmptyResult"] = "未找到匹配的 CAD 图纸内容",
            ["MenuOpenFile"] = "📂 打开图纸文件",
            ["MenuOpenFolder"] = "📁 打开所在文件夹",
            ["MenuCopyPath"] = "📋 复制文件完整路径",
            ["PanelExtractedText"] = "📝 图纸提取文本与上下文 (高亮匹配)",
            ["BtnCopyAllText"] = "📋 复制全文",
            ["PanelPreview"] = "🖼 图纸资源管理器 (Explorer) 高清缩略图预览",
            ["BtnOpenCurrent"] = "🚀 打开图纸",
            ["NoPreviewText"] = "该 DWG/DXF 文件未生成资源管理器位图缩略图",
            ["NoPreviewSubText"] = "可直接双击左侧列表使用 CAD 软件查看完整图形",
            ["StatusReady"] = "就绪 | 当前已索引 {0} 张 CAD 图纸 | 实时监控: {1}",
            ["StatusScanning"] = "正在检查图纸目录与增量索引...",
            ["StatusSyncing"] = "正在增量扫描与同步更新索引...",
            ["StatusCopiedPath"] = "已复制路径: {0}",
            ["StatusCopiedText"] = "已复制该图纸的全部纯文本到剪贴板！",
            ["StatusAutoIndexed"] = "[自动同步] 已更新索引: {0} ({1})",
            ["StatusEnabled"] = "已开启",
            ["StatusDisabled"] = "已关闭",
            ["DbInfo"] = "DwgSearcher (SQLite FTS5 + WAL 引擎)",
            ["UnknownSize"] = "未知",

            // 设置窗口
            ["SettingsTitle"] = "设置 - DwgSearcher",
            ["SettingsFolderSection"] = "图纸索引目录管理",
            ["ColFolderPath"] = "监控文件夹路径",
            ["ColIncludeSub"] = "包含子文件夹",
            ["ColAction"] = "操作",
            ["BtnRemove"] = "移除",
            ["BtnAddFolder"] = "➕ 添加图纸文件夹...",
            ["BtnRebuildIndex"] = "🔄 立即全量重建索引",
            ["ChkAutoSync"] = "开启后台实时监听 (当 CAD 图纸新建/修改/保存时自动更新索引)",
            ["SettingsNote"] = "说明：系统支持 .dwg 与 .dxf 格式，自动深度解析图纸标题栏、属性定义、标注、文字及表格数据。",
            ["SettingsLangSection"] = "🌐 界面多语言选项 (Language):",
            ["BtnSaveApply"] = "保存并应用",
            ["BtnCancel"] = "取消",
            ["MsgFolderExists"] = "该文件夹已在监控列表中！",
            ["MsgRebuildConfirm"] = "确定要重新全量扫描并重建所有图纸索引吗？",
            ["MsgRebuildSuccess"] = "全量索引重建完成！",
            ["MsgSaved"] = "设置已保存 | 实时监控: {0}"
        },

        // ------------------ English ------------------
        ["en-US"] = new()
        {
            ["AppTitle"] = "DwgSearcher - Local CAD Drawing Full-Text Searcher",
            ["SearchWatermark"] = "Search drawing number, designer, text, dimension, attributes (substring supported)...",
            ["BtnSyncIndex"] = "🔄 Sync Index",
            ["BtnSyncIndexTip"] = "Scan watched folders for newly added or modified drawings",
            ["BtnSettings"] = "⚙ Settings",
            ["BtnSettingsTip"] = "Manage watch folders, language, and auto-sync options",
            ["ResultWaiting"] = "Waiting for search query...",
            ["ResultAllCount"] = "Total indexed drawings: {0}",
            ["ResultFoundCount"] = "Found {0} matching drawings ({1:F2} ms)",
            ["EmptyResult"] = "No matching CAD drawing contents found",
            ["MenuOpenFile"] = "📂 Open Drawing",
            ["MenuOpenFolder"] = "📁 Open Containing Folder",
            ["MenuCopyPath"] = "📋 Copy Full Path",
            ["PanelExtractedText"] = "📝 Extracted CAD Text & Context (Highlighted)",
            ["BtnCopyAllText"] = "📋 Copy Text",
            ["PanelPreview"] = "🖼 Explorer Native Thumbnail & Preview",
            ["BtnOpenCurrent"] = "🚀 Open CAD",
            ["NoPreviewText"] = "No Explorer bitmap thumbnail embedded in this DWG/DXF",
            ["NoPreviewSubText"] = "Double-click item on the left to view in CAD software",
            ["StatusReady"] = "Ready | Total {0} CAD drawings indexed | Live watcher: {1}",
            ["StatusScanning"] = "Checking drawing folders and incremental index...",
            ["StatusSyncing"] = "Syncing incremental index...",
            ["StatusCopiedPath"] = "Path copied: {0}",
            ["StatusCopiedText"] = "All extracted text copied to clipboard!",
            ["StatusAutoIndexed"] = "[Live Sync] Updated index: {0} ({1})",
            ["StatusEnabled"] = "Enabled",
            ["StatusDisabled"] = "Disabled",
            ["DbInfo"] = "DwgSearcher (SQLite FTS5 + WAL Engine)",
            ["UnknownSize"] = "Unknown",

            // Settings window
            ["SettingsTitle"] = "Settings - DwgSearcher",
            ["SettingsFolderSection"] = "Drawing Index Folders Management",
            ["ColFolderPath"] = "Watched Folder Path",
            ["ColIncludeSub"] = "Include Subfolders",
            ["ColAction"] = "Action",
            ["BtnRemove"] = "Remove",
            ["BtnAddFolder"] = "➕ Add CAD Folder...",
            ["BtnRebuildIndex"] = "🔄 Rebuild Entire Index",
            ["ChkAutoSync"] = "Enable background live watcher (auto update index when DWG is created/modified)",
            ["SettingsNote"] = "Note: Supports .dwg and .dxf formats. Deeply parses title blocks, attributes, dimensions, text, and tables.",
            ["SettingsLangSection"] = "🌐 Interface Language:",
            ["BtnSaveApply"] = "Save & Apply",
            ["BtnCancel"] = "Cancel",
            ["MsgFolderExists"] = "This folder is already in the watch list!",
            ["MsgRebuildConfirm"] = "Are you sure you want to rescan and rebuild all drawing indexes?",
            ["MsgRebuildSuccess"] = "Index rebuild completed successfully!",
            ["MsgSaved"] = "Settings saved | Live watcher: {0}"
        },

        // ------------------ 繁體中文 ------------------
        ["zh-TW"] = new()
        {
            ["AppTitle"] = "DwgSearcher - 本地 CAD 圖紙全文檢索器",
            ["SearchWatermark"] = "輸入圖號、設計者、文字、標注尺寸、屬性等關鍵字檢索 (支援中英文子字串)...",
            ["BtnSyncIndex"] = "🔄 增量更新",
            ["BtnSyncIndexTip"] = "立即掃描監控目錄下的新增與變動圖紙",
            ["BtnSettings"] = "⚙ 目錄設定",
            ["BtnSettingsTip"] = "新增/管理圖紙監控目錄、多語言與自動同步選項",
            ["ResultWaiting"] = "等待輸入檢索...",
            ["ResultAllCount"] = "全部已索引圖紙共 {0} 個",
            ["ResultFoundCount"] = "找到 {0} 個匹配圖紙 (耗時 {1:F2} ms)",
            ["EmptyResult"] = "未找到匹配的 CAD 圖紙內容",
            ["MenuOpenFile"] = "📂 開啟圖紙檔案",
            ["MenuOpenFolder"] = "📁 開啟所在資料夾",
            ["MenuCopyPath"] = "📋 複製檔案完整路徑",
            ["PanelExtractedText"] = "📝 圖紙擷取文字與上下文 (高亮匹配)",
            ["BtnCopyAllText"] = "📋 複製全文",
            ["PanelPreview"] = "🖼 圖紙檔案總管 (Explorer) 高畫質縮圖預覽",
            ["BtnOpenCurrent"] = "🚀 開啟圖紙",
            ["NoPreviewText"] = "該 DWG/DXF 檔案未產生檔案總管點陣縮圖",
            ["NoPreviewSubText"] = "可直接按兩下左側列表使用 CAD 軟體檢視完整圖形",
            ["StatusReady"] = "就緒 | 目前已索引 {0} 張 CAD 圖紙 | 即時監控: {1}",
            ["StatusScanning"] = "正在檢查圖紙目錄與增量索引...",
            ["StatusSyncing"] = "正在增量掃描與同步更新索引...",
            ["StatusCopiedPath"] = "已複製路徑: {0}",
            ["StatusCopiedText"] = "已複製該圖紙的全部純文字至剪貼簿！",
            ["StatusAutoIndexed"] = "[自動同步] 已更新索引: {0} ({1})",
            ["StatusEnabled"] = "已開啟",
            ["StatusDisabled"] = "已關閉",
            ["DbInfo"] = "DwgSearcher (SQLite FTS5 + WAL 引擎)",
            ["UnknownSize"] = "未知",

            // 設定視窗
            ["SettingsTitle"] = "設定 - DwgSearcher",
            ["SettingsFolderSection"] = "圖紙索引目錄管理",
            ["ColFolderPath"] = "監控資料夾路徑",
            ["ColIncludeSub"] = "包含子資料夾",
            ["ColAction"] = "操作",
            ["BtnRemove"] = "移除",
            ["BtnAddFolder"] = "➕ 新增圖紙資料夾...",
            ["BtnRebuildIndex"] = "🔄 立即全量重建索引",
            ["ChkAutoSync"] = "開啟背景即時監聽 (當 CAD 圖紙新增/修改/儲存時自動更新索引)",
            ["SettingsNote"] = "說明：系統支援 .dwg 與 .dxf 格式，自動深度解析圖紙標題欄、屬性定義、標注、文字及表格資料。",
            ["SettingsLangSection"] = "🌐 介面多語言選項 (Language):",
            ["BtnSaveApply"] = "儲存並套用",
            ["BtnCancel"] = "取消",
            ["MsgFolderExists"] = "該資料夾已在監控列表中！",
            ["MsgRebuildConfirm"] = "確定要重新全量掃描並重建所有圖紙索引嗎？",
            ["MsgRebuildSuccess"] = "全量索引重建完成！",
            ["MsgSaved"] = "設定已儲存 | 即時監控: {0}"
        }
    };
}
