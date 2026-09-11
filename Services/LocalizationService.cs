using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DwgSearcher.Services;

/// <summary>
/// 多语言国际化管理服务
/// 支持动态切换（无需重启软件即可无缝刷新全部界面文本及图纸实体标签）
/// </summary>
public static class LocalizationService
{
    public static event Action? OnLanguageChanged;

    public static readonly List<LanguageOption> SupportedLanguages = new()
    {
        new("zh-CN", "简体中文 (Simplified Chinese)"),
        new("en-US", "English (United States)"),
        new("zh-TW", "繁體中文 (Traditional Chinese)"),
        new("ja-JP", "日本語 (Japanese)"),
        new("de-DE", "Deutsch (German)"),
        new("ko-KR", "한국어 (Korean)")
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
            if (Translations["zh-CN"].TryGetValue(key, out template) || Translations["en-US"].TryGetValue(key, out template))
            {
                // 找到降级词条
            }
            else
            {
                return key;
            }
        }

        return args.Length > 0 ? string.Format(template, args) : template;
    }

    /// <summary>
    /// 将 CAD 提取文本中的实体标签（如 [标注], [图块], [属性] 等）动态本地化为当前界面语言
    /// 无论底层数据库历史存的是哪种语言标签，前台展示时全部 100% 自动映射为当前语言！
    /// </summary>
    public static string LocalizeExtractedText(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return rawText;

        if (!EntityTagLocalizations.TryGetValue(_currentLanguage, out var targetTags))
        {
            targetTags = EntityTagLocalizations["zh-CN"];
        }

        string result = rawText;

        // 遍历所有可能的源实体标签，动态替换为当前语言的目标实体标签
        foreach (var rule in TagReplacementRules)
        {
            if (targetTags.TryGetValue(rule.Key, out var targetLabel))
            {
                result = rule.RegexPattern.Replace(result, targetLabel);
            }
        }

        return result;
    }

    private record TagRule(string Key, Regex RegexPattern);

    // 各种 CAD 实体标签在所有语言中的正则匹配器
    private static readonly List<TagRule> TagReplacementRules = new()
    {
        new("Dimension", new Regex(@"\[(标注|Dimension|標註|寸法|Bemaßung|치수|DIM)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new("Block", new Regex(@"\[(图块|Block|圖塊|ブロック|블록|BLOCK)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new("AttributePrefix", new Regex(@"\[(属性|Attribute|屬性|Attribut|속性|ATTR|속성):\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new("AttDefPrefix", new Regex(@"\[(属性定义|AttDef|屬性定義|Attributdefinition|속성\s*정의):\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new("Table", new Regex(@"\[(表格数据|表格|Table Data|Table|表格資料|表データ|Tabellendaten|테이블\s*데이터)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new("Leader", new Regex(@"\[(引线文字|引线|Leader|引線文字|引出線|Führungslinie|지시선)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new("MultiLeaderAttrPrefix", new Regex(@"\[(多重引线属性|MultiLeader Attr|多重引線屬性|マルチ引出線属性|Multi-Führung Attribut|다중\s*지시선\s*속성):\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new("Tolerance", new Regex(@"\[(形位公差|Tolerance|幾何公差|Form- und Lagetoleranz|기하\s*공차|TOL)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new("XRef", new Regex(@"\[(外部参照|XRef|外部參考|外部\s*참조)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new("Title", new Regex(@"\[(图纸标题|Title|圖紙標題|タイトル|Titel|제목)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new("Subject", new Regex(@"\[(图纸主题|Subject|圖紙主題|サブタイトル|Thema|주제)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new("Author", new Regex(@"\[(图纸作者|Author|圖紙作者|作成者|Autor|작성자)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new("Keywords", new Regex(@"\[(关键字|Keywords|關鍵字|キーワード|Schlüsselwörter|키워드)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new("Comments", new Regex(@"\[(图纸注释|Comments|圖紙註解|コメント|Kommentare|설명)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
        new("Hyperlink", new Regex(@"\[(超链接|Hyperlink|超連結|ハイパーリンク|하이퍼링크)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase))
    };

    // 目标语言对应的实体前缀标签字典
    private static readonly Dictionary<string, Dictionary<string, string>> EntityTagLocalizations = new()
    {
        ["zh-CN"] = new()
        {
            ["Dimension"] = "[标注]",
            ["Block"] = "[图块]",
            ["AttributePrefix"] = "[属性: ",
            ["AttDefPrefix"] = "[属性定义: ",
            ["Table"] = "[表格数据]",
            ["Leader"] = "[引线文字]",
            ["MultiLeaderAttrPrefix"] = "[多重引线属性: ",
            ["Tolerance"] = "[形位公差]",
            ["XRef"] = "[外部参照]",
            ["Title"] = "[图纸标题]",
            ["Subject"] = "[图纸主题]",
            ["Author"] = "[图纸作者]",
            ["Keywords"] = "[关键字]",
            ["Comments"] = "[图纸注释]",
            ["Hyperlink"] = "[超链接]"
        },
        ["en-US"] = new()
        {
            ["Dimension"] = "[Dimension]",
            ["Block"] = "[Block]",
            ["AttributePrefix"] = "[Attribute: ",
            ["AttDefPrefix"] = "[AttDef: ",
            ["Table"] = "[Table Data]",
            ["Leader"] = "[Leader]",
            ["MultiLeaderAttrPrefix"] = "[MultiLeader Attr: ",
            ["Tolerance"] = "[Tolerance]",
            ["XRef"] = "[XRef]",
            ["Title"] = "[Title]",
            ["Subject"] = "[Subject]",
            ["Author"] = "[Author]",
            ["Keywords"] = "[Keywords]",
            ["Comments"] = "[Comments]",
            ["Hyperlink"] = "[Hyperlink]"
        },
        ["zh-TW"] = new()
        {
            ["Dimension"] = "[標註]",
            ["Block"] = "[圖塊]",
            ["AttributePrefix"] = "[屬性: ",
            ["AttDefPrefix"] = "[屬性定義: ",
            ["Table"] = "[表格資料]",
            ["Leader"] = "[引線文字]",
            ["MultiLeaderAttrPrefix"] = "[多重引線屬性: ",
            ["Tolerance"] = "[形位公差]",
            ["XRef"] = "[外部參考]",
            ["Title"] = "[圖紙標題]",
            ["Subject"] = "[圖紙主題]",
            ["Author"] = "[圖紙作者]",
            ["Keywords"] = "[關鍵字]",
            ["Comments"] = "[圖紙註解]",
            ["Hyperlink"] = "[超連結]"
        },
        ["ja-JP"] = new()
        {
            ["Dimension"] = "[寸法]",
            ["Block"] = "[ブロック]",
            ["AttributePrefix"] = "[属性: ",
            ["AttDefPrefix"] = "[属性定義: ",
            ["Table"] = "[表データ]",
            ["Leader"] = "[引出線]",
            ["MultiLeaderAttrPrefix"] = "[マルチ引出線属性: ",
            ["Tolerance"] = "[幾何公差]",
            ["XRef"] = "[外部参照]",
            ["Title"] = "[タイトル]",
            ["Subject"] = "[サブタイトル]",
            ["Author"] = "[作成者]",
            ["Keywords"] = "[キーワード]",
            ["Comments"] = "[コメント]",
            ["Hyperlink"] = "[ハイパーリンク]"
        },
        ["de-DE"] = new()
        {
            ["Dimension"] = "[Bemaßung]",
            ["Block"] = "[Block]",
            ["AttributePrefix"] = "[Attribut: ",
            ["AttDefPrefix"] = "[Attributdefinition: ",
            ["Table"] = "[Tabellendaten]",
            ["Leader"] = "[Führungslinie]",
            ["MultiLeaderAttrPrefix"] = "[Multi-Führung Attribut: ",
            ["Tolerance"] = "[Form- und Lagetoleranz]",
            ["XRef"] = "[XRef]",
            ["Title"] = "[Titel]",
            ["Subject"] = "[Thema]",
            ["Author"] = "[Autor]",
            ["Keywords"] = "[Schlüsselwörter]",
            ["Comments"] = "[Kommentare]",
            ["Hyperlink"] = "[Hyperlink]"
        },
        ["ko-KR"] = new()
        {
            ["Dimension"] = "[치수]",
            ["Block"] = "[블록]",
            ["AttributePrefix"] = "[속성: ",
            ["AttDefPrefix"] = "[속성 정의: ",
            ["Table"] = "[테이블 데이터]",
            ["Leader"] = "[지시선]",
            ["MultiLeaderAttrPrefix"] = "[다중 지시선 속성: ",
            ["Tolerance"] = "[기하 공차]",
            ["XRef"] = "[외부 참조]",
            ["Title"] = "[제목]",
            ["Subject"] = "[주제]",
            ["Author"] = "[작성자]",
            ["Keywords"] = "[키워드]",
            ["Comments"] = "[설명]",
            ["Hyperlink"] = "[하이퍼링크]"
        }
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Translations = new()
    {
        // ------------------ 1. 简体中文 (zh-CN) ------------------
        ["zh-CN"] = new()
        {
            ["AppTitle"] = "DwgSearcher - 本地 CAD 图纸全文检索器",
            ["SearchWatermark"] = "输入图号、设计者、文字、标注尺寸、属性等关键词检索 (支持中英文子串)...",
            ["BtnSearch"] = "🔍 搜索",
            ["BtnSearchTip"] = "立即执行图纸全文检索",
            ["BtnSettings"] = "⚙ 设置",
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
            ["StatusIndexingProgress"] = "正在建立索引 [{0} / {1}] ({2}%) | 解析中: {3}",
            ["StatusScanningFolder"] = "正在扫描图纸目录: {0} ...",
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
            ["ChkAutoSync"] = "开启后台实时监听 (当 CAD 图纸新建/修改/保存时自动更新索引)",
            ["SettingsNote"] = "说明：系统支持 .dwg 与 .dxf 格式，自动深度解析图纸标题栏、属性定义、标注、文字及表格数据。",
            ["SettingsLangSection"] = "🌐 界面多语言选项 (Language):",
            ["UpdateLink"] = "🔗 更新链接",
            ["BtnSaveApply"] = "保存并应用",
            ["BtnCancel"] = "取消",
            ["MsgFolderExists"] = "该文件夹已在监控列表中！",
            ["MsgSaved"] = "设置已保存 | 实时监控: {0}"
        },

        // ------------------ 2. English (en-US) ------------------
        ["en-US"] = new()
        {
            ["AppTitle"] = "DwgSearcher - Local CAD Drawing Full-Text Searcher",
            ["SearchWatermark"] = "Search drawing number, designer, text, dimension, attributes (substring supported)...",
            ["BtnSearch"] = "🔍 Search",
            ["BtnSearchTip"] = "Execute CAD drawing full-text search",
            ["BtnSettings"] = "⚙ Settings",
            ["BtnSettingsTip"] = "Manage watch folders, language, and auto-sync options",
            ["ResultWaiting"] = "Waiting for search input...",
            ["ResultAllCount"] = "Total {0} indexed drawings",
            ["ResultFoundCount"] = "Found {0} matching drawings ({1:F2} ms)",
            ["EmptyResult"] = "No matching CAD drawing content found",
            ["MenuOpenFile"] = "📂 Open Drawing File",
            ["MenuOpenFolder"] = "📁 Open Containing Folder",
            ["MenuCopyPath"] = "📋 Copy Full File Path",
            ["PanelExtractedText"] = "📝 Extracted CAD Text & Context (Highlighted)",
            ["BtnCopyAllText"] = "📋 Copy Text",
            ["PanelPreview"] = "🖼 Windows Explorer Native Thumbnail Preview",
            ["BtnOpenCurrent"] = "🚀 Open Drawing",
            ["NoPreviewText"] = "This DWG/DXF file has no Explorer bitmap thumbnail",
            ["NoPreviewSubText"] = "Double-click the item on the left to view in CAD software directly",
            ["StatusReady"] = "Ready | Total {0} CAD drawings indexed | Live watcher: {1}",
            ["StatusScanning"] = "Checking drawing folders and incremental index...",
            ["StatusSyncing"] = "Syncing incremental index...",
            ["StatusIndexingProgress"] = "Indexing drawings [{0} / {1}] ({2}%) | Parsing: {3}",
            ["StatusScanningFolder"] = "Scanning folder: {0} ...",
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
            ["ChkAutoSync"] = "Enable background live watcher (auto update index when DWG is created/modified)",
            ["SettingsNote"] = "Note: Supports .dwg and .dxf formats. Deeply parses title blocks, attributes, dimensions, text, and tables.",
            ["SettingsLangSection"] = "🌐 Interface Language:",
            ["UpdateLink"] = "🔗 Update Link",
            ["BtnSaveApply"] = "Save & Apply",
            ["BtnCancel"] = "Cancel",
            ["MsgFolderExists"] = "This folder is already in the watch list!",
            ["MsgSaved"] = "Settings saved | Live watcher: {0}"
        },

        // ------------------ 3. 繁體中文 (zh-TW) ------------------
        ["zh-TW"] = new()
        {
            ["AppTitle"] = "DwgSearcher - 本地 CAD 圖紙全文檢索器",
            ["SearchWatermark"] = "輸入圖號、設計者、文字、標注尺寸、屬性等關鍵字檢索 (支援中英文子字串)...",
            ["BtnSearch"] = "🔍 搜尋",
            ["BtnSearchTip"] = "立即執行圖紙全文檢索",
            ["BtnSettings"] = "⚙ 設定",
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
            ["StatusIndexingProgress"] = "正在建立索引 [{0} / {1}] ({2}%) | 解析中: {3}",
            ["StatusScanningFolder"] = "正在掃描圖紙目錄: {0} ...",
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
            ["ChkAutoSync"] = "開啟背景即時監聽 (當 CAD 圖紙新增/修改/儲存時自動更新索引)",
            ["SettingsNote"] = "說明：系統支援 .dwg 與 .dxf 格式，自動深度解析圖紙標題欄、屬性定義、標注、文字及表格資料。",
            ["SettingsLangSection"] = "🌐 介面多語言選項 (Language):",
            ["UpdateLink"] = "🔗 更新連結",
            ["BtnSaveApply"] = "儲存並套用",
            ["BtnCancel"] = "取消",
            ["MsgFolderExists"] = "該資料夾已在監控列表中！",
            ["MsgSaved"] = "設定已儲存 | 即時監控: {0}"
        },

        // ------------------ 4. 日本語 (ja-JP) ------------------
        ["ja-JP"] = new()
        {
            ["AppTitle"] = "DwgSearcher - ローカル CAD 図面全文検索ツール",
            ["SearchWatermark"] = "図面番号、設計者、テキスト、寸法値、属性などのキーワードで検索...",
            ["BtnSearch"] = "🔍 検索",
            ["BtnSearchTip"] = "CAD 図面の全文検索を実行します",
            ["BtnSettings"] = "⚙ 設定",
            ["BtnSettingsTip"] = "監視フォルダ、言語、自動同期オプションの管理",
            ["ResultWaiting"] = "検索キーワードを入力してください...",
            ["ResultAllCount"] = "インデックス済み図面: 全 {0} 件",
            ["ResultFoundCount"] = "{0} 件の一致する図面が見つかりました ({1:F2} ms)",
            ["EmptyResult"] = "一致する CAD 図面が見つかりませんでした",
            ["MenuOpenFile"] = "📂 図面ファイルを開く",
            ["MenuOpenFolder"] = "📁 保存先フォルダを開く",
            ["MenuCopyPath"] = "📋 ファイルのフルパスをコピー",
            ["PanelExtractedText"] = "📝 抽出された CAD テキストとコンテキスト (強調表示)",
            ["BtnCopyAllText"] = "📋 全文コピー",
            ["PanelPreview"] = "🖼 エクスプローラー ネイティブ サムネイル プレビュー",
            ["BtnOpenCurrent"] = "🚀 図面を開く",
            ["NoPreviewText"] = "この DWG/DXF ファイルにはサムネイル画像がありません",
            ["NoPreviewSubText"] = "左側のリストをダブルクリックして CAD ソフトで直接確認できます",
            ["StatusReady"] = "準備完了 | インデックス済み図面: {0} 件 | リアルタイム監視: {1}",
            ["StatusScanning"] = "図面フォルダと増分インデックスを確認中...",
            ["StatusSyncing"] = "増分インデックスを同期中...",
            ["StatusIndexingProgress"] = "インデックス作成中 [{0} / {1}] ({2}%) | 解析中: {3}",
            ["StatusScanningFolder"] = "フォルダをスキャン中: {0} ...",
            ["StatusCopiedPath"] = "パスをコピーしました: {0}",
            ["StatusCopiedText"] = "抽出されたテキストをクリップボードにコピーしました！",
            ["StatusAutoIndexed"] = "[自動同期] インデックス更新: {0} ({1})",
            ["StatusEnabled"] = "有効",
            ["StatusDisabled"] = "無効",
            ["DbInfo"] = "DwgSearcher (SQLite FTS5 + WAL エンジン)",
            ["UnknownSize"] = "不明",

            // 設定ウィンドウ
            ["SettingsTitle"] = "設定 - DwgSearcher",
            ["SettingsFolderSection"] = "図面インデックス フォルダ管理",
            ["ColFolderPath"] = "監視フォルダ パス",
            ["ColIncludeSub"] = "サブフォルダを含む",
            ["ColAction"] = "操作",
            ["BtnRemove"] = "削除",
            ["BtnAddFolder"] = "➕ CAD フォルダを追加...",
            ["ChkAutoSync"] = "バックグラウンド リアルタイム監視を有効化 (図面保存時に自動更新)",
            ["SettingsNote"] = "説明: .dwg および .dxf 形式に対応。表題欄、属性定義、寸法、文字、表データを自動解析します。",
            ["SettingsLangSection"] = "🌐 表示言語 (Language):",
            ["UpdateLink"] = "🔗 更新リンク",
            ["BtnSaveApply"] = "保存して適用",
            ["BtnCancel"] = "キャンセル",
            ["MsgFolderExists"] = "このフォルダは既に監視リストに含まれています！",
            ["MsgSaved"] = "設定を保存しました | リアルタイム監視: {0}"
        },

        // ------------------ 5. Deutsch (de-DE) ------------------
        ["de-DE"] = new()
        {
            ["AppTitle"] = "DwgSearcher - Lokale CAD-Zeichnungs-Volltextsuche",
            ["SearchWatermark"] = "Zeichnungsnummer, Konstrukteur, Text, Bemaßung, Attribute suchen...",
            ["BtnSearch"] = "🔍 Suchen",
            ["BtnSearchTip"] = "CAD-Zeichnungs-Volltextsuche ausführen",
            ["BtnSettings"] = "⚙ Einstellungen",
            ["BtnSettingsTip"] = "Überwachte Ordner, Sprache und Auto-Sync verwalten",
            ["ResultWaiting"] = "Warten auf Sucheingabe...",
            ["ResultAllCount"] = "Insgesamt {0} indizierte Zeichnungen",
            ["ResultFoundCount"] = "{0} passende Zeichnungen gefunden ({1:F2} ms)",
            ["EmptyResult"] = "Kein passender CAD-Zeichnungsinhalt gefunden",
            ["MenuOpenFile"] = "📂 Zeichnungsdatei öffnen",
            ["MenuOpenFolder"] = "📁 Übergeordneten Ordner öffnen",
            ["MenuCopyPath"] = "📋 Vollständigen Pfad kopieren",
            ["PanelExtractedText"] = "📝 Extrahierter CAD-Text & Kontext (Hervorgehoben)",
            ["BtnCopyAllText"] = "📋 Text kopieren",
            ["PanelPreview"] = "🖼 Windows Explorer Vorschau-Miniaturbild",
            ["BtnOpenCurrent"] = "🚀 Zeichnung öffnen",
            ["NoPreviewText"] = "Diese DWG/DXF-Datei besitzt kein Explorer-Vorschaubild",
            ["NoPreviewSubText"] = "Doppelklicken Sie links, um sie in CAD-Software zu öffnen",
            ["StatusReady"] = "Bereit | Insgesamt {0} CAD-Zeichnungen indiziert | Live-Überwachung: {1}",
            ["StatusScanning"] = "Prüfe Zeichnungsordner und inkrementellen Index...",
            ["StatusSyncing"] = "Synchronisiere inkrementellen Index...",
            ["StatusIndexingProgress"] = "Indiziere Zeichnungen [{0} / {1}] ({2}%) | Analysiere: {3}",
            ["StatusScanningFolder"] = "Scanne Ordner: {0} ...",
            ["StatusCopiedPath"] = "Pfad kopiert: {0}",
            ["StatusCopiedText"] = "Extrahierter Text in die Zwischenablage kopiert!",
            ["StatusAutoIndexed"] = "[Live-Sync] Index aktualisiert: {0} ({1})",
            ["StatusEnabled"] = "Aktiviert",
            ["StatusDisabled"] = "Deaktiviert",
            ["DbInfo"] = "DwgSearcher (SQLite FTS5 + WAL Engine)",
            ["UnknownSize"] = "Unbekannt",

            // Einstellungsfenster
            ["SettingsTitle"] = "Einstellungen - DwgSearcher",
            ["SettingsFolderSection"] = "Verwaltung der Zeichnungsindex-Ordner",
            ["ColFolderPath"] = "Überwachter Ordnerpfad",
            ["ColIncludeSub"] = "Unterordner einschließen",
            ["ColAction"] = "Aktion",
            ["BtnRemove"] = "Entfernen",
            ["BtnAddFolder"] = "➕ CAD-Ordner hinzufügen...",
            ["ChkAutoSync"] = "Hintergrund-Echtzeitüberwachung aktivieren (Auto-Update beim Speichern)",
            ["SettingsNote"] = "Hinweis: Unterstützt .dwg und .dxf. Analysiert Schriftfelder, Attribute, Maße, Texte und Tabellen.",
            ["SettingsLangSection"] = "🌐 Oberflächensprache (Language):",
            ["UpdateLink"] = "🔗 Update-Link",
            ["BtnSaveApply"] = "Speichern & Anwenden",
            ["BtnCancel"] = "Abbrechen",
            ["MsgFolderExists"] = "Dieser Ordner befindet sich bereits in der Überwachungsliste!",
            ["MsgSaved"] = "Einstellungen gespeichert | Live-Überwachung: {0}"
        },

        // ------------------ 6. 한국어 (ko-KR) ------------------
        ["ko-KR"] = new()
        {
            ["AppTitle"] = "DwgSearcher - 로컬 CAD 도면 전문 검색기",
            ["SearchWatermark"] = "도면 번호, 설계자, 텍스트, 치수, 속성 등 검색어 입력...",
            ["BtnSearch"] = "🔍 검색",
            ["BtnSearchTip"] = "CAD 도면 전체 텍스트 검색 실행",
            ["BtnSettings"] = "⚙ 설정",
            ["BtnSettingsTip"] = "도면 감시 폴더, 언어 및 자동 동기화 설정 관리",
            ["ResultWaiting"] = "검색어 입력을 기다리는 중...",
            ["ResultAllCount"] = "색인된 도면 전체 {0}개",
            ["ResultFoundCount"] = "일치하는 도면 {0}개 발견 ({1:F2} ms)",
            ["EmptyResult"] = "일치하는 CAD 도면 내용을 찾을 수 없습니다",
            ["MenuOpenFile"] = "📂 도면 파일 열기",
            ["MenuOpenFolder"] = "📁 파일 폴더 열기",
            ["MenuCopyPath"] = "📋 전체 파일 경로 복사",
            ["PanelExtractedText"] = "📝 추출된 CAD 텍스트 및 컨텍스트 (강조 표시)",
            ["BtnCopyAllText"] = "📋 텍스트 복사",
            ["PanelPreview"] = "🖼 Windows 탐색기 기본 썸네일 미리보기",
            ["BtnOpenCurrent"] = "🚀 도면 열기",
            ["NoPreviewText"] = "이 DWG/DXF 파일에 대한 탐색기 썸네일이 없습니다",
            ["NoPreviewSubText"] = "왼쪽 목록을 더블 클릭하여 CAD 소프트웨어에서 직접 열 수 있습니다",
            ["StatusReady"] = "준비됨 | 총 {0}개 CAD 도면 색인됨 | 실시간 감시: {1}",
            ["StatusScanning"] = "도면 폴더 및 증분 색인 확인 중...",
            ["StatusSyncing"] = "증분 색인 동기화 중...",
            ["StatusIndexingProgress"] = "도면 색인 생성 중 [{0} / {1}] ({2}%) | 분석 중: {3}",
            ["StatusScanningFolder"] = "폴더 검사 중: {0} ...",
            ["StatusCopiedPath"] = "경로 복사됨: {0}",
            ["StatusCopiedText"] = "추출된 모든 텍스트가 클립보드에 복사되었습니다!",
            ["StatusAutoIndexed"] = "[자동 동기화] 색인 업데이트: {0} ({1})",
            ["StatusEnabled"] = "활성화됨",
            ["StatusDisabled"] = "비활성화됨",
            ["DbInfo"] = "DwgSearcher (SQLite FTS5 + WAL 엔진)",
            ["UnknownSize"] = "알 수 없음",

            // 설정 창
            ["SettingsTitle"] = "설정 - DwgSearcher",
            ["SettingsFolderSection"] = "도면 색인 폴더 관리",
            ["ColFolderPath"] = "감시 폴더 경로",
            ["ColIncludeSub"] = "하위 폴더 포함",
            ["ColAction"] = "작업",
            ["BtnRemove"] = "제거",
            ["BtnAddFolder"] = "➕ CAD 폴더 추가...",
            ["ChkAutoSync"] = "백그라운드 실시간 감시 활성화 (도면 저장 시 자동 색인)",
            ["SettingsNote"] = "설명: .dwg 및 .dxf 형식 지원. 표제란, 속성 정의, 치수, 텍스트, 테이블 데이터를 자동 분석합니다.",
            ["SettingsLangSection"] = "🌐 사용자 인터페이스 언어 (Language):",
            ["UpdateLink"] = "🔗 업데이트 링크",
            ["BtnSaveApply"] = "저장 및 적용",
            ["BtnCancel"] = "취소",
            ["MsgFolderExists"] = "이 폴더는 이미 감시 목록에 있습니다!",
            ["MsgSaved"] = "설정이 저장되었습니다 | 실시간 감시: {0}"
        }
    };
}
