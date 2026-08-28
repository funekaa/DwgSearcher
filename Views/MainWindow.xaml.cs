using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using DwgSearcher.Engine;
using DwgSearcher.Models;
using DwgSearcher.Services;
using DwgSearcher.Storage;
using DwgSearcher.ViewModels;

namespace DwgSearcher.Views;

public partial class MainWindow : Window
{
    private readonly AppConfig _config;
    private readonly DatabaseManager _dbManager;
    private readonly IndexingEngine _indexEngine;
    private readonly SearchEngine _searchEngine;
    private readonly FileWatcherService _watcherService;
    private readonly System.Timers.Timer _searchDebounceTimer;

    private List<SearchResultItem> _currentResults = new();
    private SearchResultItem? _selectedItem;
    private string _currentExtractedText = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        // 1. 初始化配置与数据库
        _config = ConfigService.Load();
        string dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dwg_index.db");
        _dbManager = new DatabaseManager(dbPath);
        _indexEngine = new IndexingEngine(_dbManager);
        _searchEngine = new SearchEngine(_dbManager);

        // 2. 初始化后台自动监听服务
        _watcherService = new FileWatcherService(_indexEngine);
        _watcherService.OnFileAutoIndexed += OnFileAutoIndexed;
        _watcherService.ReloadWatchers(_config);

        // 3. 搜索防抖定时器 (150ms 实时检索)
        _searchDebounceTimer = new System.Timers.Timer(150);
        _searchDebounceTimer.AutoReset = false;
        _searchDebounceTimer.Elapsed += (s, e) =>
        {
            Dispatcher.Invoke(() => ExecuteSearch(SearchBox.Text));
        };

        // 4. 窗体加载完成后执行初始扫描与默认检索
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await PerformInitialIndexAsync();

        // 如果有上次搜索词或默认搜索
        if (!string.IsNullOrWhiteSpace(_config.LastSearchKeyword))
        {
            SearchBox.Text = _config.LastSearchKeyword;
        }
        else
        {
            ExecuteSearch(string.Empty);
        }
    }

    /// <summary>
    /// 启动时异步增量扫描监控文件夹
    /// </summary>
    private async Task PerformInitialIndexAsync()
    {
        StatusTextBlock.Text = "正在检查图纸目录与增量索引...";
        int totalIndexed = 0;

        foreach (var folder in _config.Folders)
        {
            if (folder.Enabled && Directory.Exists(folder.Path))
            {
                try
                {
                    await _indexEngine.IndexDirectoryAsync(folder.Path);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[MainWindow] 扫描目录失败: {ex.Message}");
                }
            }
        }

        var allDocs = _searchEngine.GetAllIndexedDocs();
        totalIndexed = allDocs.Count;
        StatusTextBlock.Text = $"就绪 | 当前已索引 {totalIndexed} 张 CAD 图纸 | 实时监控: {(_config.AutoSyncOnChange ? "已开启" : "已关闭")}";
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ClearButton.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _searchDebounceTimer.Stop();
            ExecuteSearch(SearchBox.Text);
        }
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = string.Empty;
        SearchBox.Focus();
    }

    /// <summary>
    /// 执行检索
    /// </summary>
    private void ExecuteSearch(string keyword)
    {
        keyword = keyword.Trim();
        _config.LastSearchKeyword = keyword;
        ConfigService.Save(_config);

        var sw = Stopwatch.StartNew();
        List<SearchResult> rawResults;

        if (string.IsNullOrEmpty(keyword))
        {
            // 输入为空时展示所有已索引图纸
            var allDocs = _searchEngine.GetAllIndexedDocs();
            rawResults = allDocs.Select(d => new SearchResult(d.FilePath, d.Title, $"[全图已提取 {d.TextLength} 字符]", 0.0)).ToList();
        }
        else
        {
            rawResults = _searchEngine.Search(keyword, limit: _config.MaxSearchResults);
        }
        sw.Stop();

        _currentResults = rawResults.Select(r => new SearchResultItem(r)).ToList();
        ResultsListView.ItemsSource = _currentResults;

        // 更新结果统计
        if (string.IsNullOrEmpty(keyword))
        {
            ResultSummaryTextBlock.Text = $"全部已索引图纸共 {_currentResults.Count} 个";
        }
        else
        {
            ResultSummaryTextBlock.Text = $"找到 {_currentResults.Count} 个匹配图纸 (耗时 {sw.Elapsed.TotalMilliseconds:F2} ms)";
        }

        EmptyStateTextBlock.Visibility = _currentResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // 默认选中第一条
        if (_currentResults.Count > 0)
        {
            ResultsListView.SelectedIndex = 0;
        }
        else
        {
            ClearDetailsView();
        }
    }

    /// <summary>
    /// 列表选择项变更
    /// </summary>
    private void ResultsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsListView.SelectedItem is SearchResultItem item)
        {
            _selectedItem = item;
            DisplayItemDetails(item);
        }
    }

    /// <summary>
    /// 展示图纸的提取文本（高亮关键词）和缩略图
    /// </summary>
    private void DisplayItemDetails(SearchResultItem item)
    {
        // 1. 异步获取图纸纯文本
        string? text = _searchEngine.GetDocContent(item.FilePath);
        _currentExtractedText = text ?? string.Empty;

        // 2. 渲染带黄色高亮的富文本
        RenderHighlightedContent(_currentExtractedText, SearchBox.Text.Trim());

        // 3. 加载 DWG 嵌入缩略图
        var thumb = item.GetThumbnail();
        if (thumb != null)
        {
            ThumbnailImage.Source = thumb;
            ThumbnailImage.Visibility = Visibility.Visible;
            NoPreviewPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            ThumbnailImage.Source = null;
            ThumbnailImage.Visibility = Visibility.Collapsed;
            NoPreviewPanel.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// 在 RichTextBox 中渲染并高亮关键词
    /// </summary>
    private void RenderHighlightedContent(string fullText, string keyword)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Consolas, Microsoft YaHei, Segoe UI"),
            FontSize = 13.0
        };

        if (string.IsNullOrWhiteSpace(fullText))
        {
            doc.Blocks.Add(new Paragraph(new Run("(该图纸未提取到文本内容或无实体文字)"))
            {
                Foreground = Brushes.Gray
            });
            ContentRichTextBox.Document = doc;
            return;
        }

        var paragraph = new Paragraph { LineHeight = 22 };
        var tokens = keyword.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

        if (tokens.Length == 0)
        {
            paragraph.Inlines.Add(new Run(fullText) { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2B2B")) });
        }
        else
        {
            // 文本切片高亮算法
            int currentIndex = 0;
            while (currentIndex < fullText.Length)
            {
                int nextMatchIndex = -1;
                string matchedToken = string.Empty;

                foreach (var token in tokens)
                {
                    int idx = fullText.IndexOf(token, currentIndex, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0 && (nextMatchIndex == -1 || idx < nextMatchIndex))
                    {
                        nextMatchIndex = idx;
                        matchedToken = token;
                    }
                }

                if (nextMatchIndex >= 0)
                {
                    // 添加匹配词前的普通文本
                    if (nextMatchIndex > currentIndex)
                    {
                        string normalText = fullText.Substring(currentIndex, nextMatchIndex - currentIndex);
                        paragraph.Inlines.Add(new Run(normalText) { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2B2B")) });
                    }

                    // 添加高亮文本 (鲜明黄色底色)
                    string matchedText = fullText.Substring(nextMatchIndex, matchedToken.Length);
                    var highlightRun = new Run(matchedText)
                    {
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF3C4")),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D83B01")),
                        FontWeight = FontWeights.Bold
                    };
                    paragraph.Inlines.Add(highlightRun);

                    currentIndex = nextMatchIndex + matchedToken.Length;
                }
                else
                {
                    // 剩余普通文本
                    string remainingText = fullText.Substring(currentIndex);
                    paragraph.Inlines.Add(new Run(remainingText) { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2B2B")) });
                    break;
                }
            }
        }

        doc.Blocks.Add(paragraph);
        ContentRichTextBox.Document = doc;
    }

    private void ClearDetailsView()
    {
        ContentRichTextBox.Document = new FlowDocument();
        ThumbnailImage.Source = null;
        ThumbnailImage.Visibility = Visibility.Collapsed;
        NoPreviewPanel.Visibility = Visibility.Collapsed;
        _currentExtractedText = string.Empty;
        _selectedItem = null;
    }

    private async void SyncIndex_Click(object sender, RoutedEventArgs e)
    {
        StatusTextBlock.Text = "正在增量扫描与同步更新索引...";
        await PerformInitialIndexAsync();
        ExecuteSearch(SearchBox.Text);
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWin = new SettingsWindow(_config, _indexEngine)
        {
            Owner = this
        };

        if (settingsWin.ShowDialog() == true)
        {
            _watcherService.ReloadWatchers(_config);
            if (settingsWin.NeedsReindex)
            {
                SyncIndex_Click(sender, e);
            }
            else
            {
                StatusTextBlock.Text = $"设置已保存 | 实时监控: {(_config.AutoSyncOnChange ? "已开启" : "已关闭")}";
            }
        }
    }

    private void ResultsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenCurrentFile();
    }

    private void OpenCurrentFile_Click(object sender, RoutedEventArgs e)
    {
        OpenCurrentFile();
    }

    private void MenuOpenFile_Click(object sender, RoutedEventArgs e)
    {
        OpenCurrentFile();
    }

    private void MenuOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem != null && File.Exists(_selectedItem.FilePath))
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{_selectedItem.FilePath}\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"定位文件夹失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void MenuCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem != null)
        {
            Clipboard.SetText(_selectedItem.FilePath);
            StatusTextBlock.Text = $"已复制路径: {_selectedItem.FilePath}";
        }
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentExtractedText))
        {
            Clipboard.SetText(_currentExtractedText);
            StatusTextBlock.Text = "已复制该图纸的全部纯文本到剪贴板！";
        }
    }

    private void OpenCurrentFile()
    {
        if (_selectedItem != null && File.Exists(_selectedItem.FilePath))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _selectedItem.FilePath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"打开图纸失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnFileAutoIndexed(string filePath)
    {
        Dispatcher.Invoke(() =>
        {
            StatusTextBlock.Text = $"[自动同步] 已更新索引: {Path.GetFileName(filePath)} ({DateTime.Now:HH:mm:ss})";
            // 刷新当前搜索
            ExecuteSearch(SearchBox.Text);
        });
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Dispose();
        _watcherService.Dispose();
        _searchEngine.Dispose();
        _indexEngine.Dispose();
        _dbManager.Dispose();
    }
}
