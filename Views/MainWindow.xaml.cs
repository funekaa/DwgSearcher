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

        // 1. 初始化配置与语言
        _config = ConfigService.Load();
        string dbPath = PathHelper.DatabasePath;
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

        // 4. 监听语言切换事件
        LocalizationService.OnLanguageChanged += ApplyLocalization;

        // 5. 窗体加载
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;

        ApplyLocalization();
    }

    private void ApplyLocalization()
    {
        Title = $"{LocalizationService.Get("AppTitle")} {AppVersionInfo.Version}";
        WatermarkTextBlock.Text = LocalizationService.Get("SearchWatermark");
        BtnSearch.Content = LocalizationService.Get("BtnSearch");
        BtnSearch.ToolTip = LocalizationService.Get("BtnSearchTip");
        BtnSettings.Content = LocalizationService.Get("BtnSettings");
        BtnSettings.ToolTip = LocalizationService.Get("BtnSettingsTip");

        MenuOpenFile.Header = LocalizationService.Get("MenuOpenFile");
        MenuOpenFolder.Header = LocalizationService.Get("MenuOpenFolder");
        MenuCopyPath.Header = LocalizationService.Get("MenuCopyPath");

        PanelExtractedTextTitle.Text = LocalizationService.Get("PanelExtractedText");
        BtnCopyText.Content = LocalizationService.Get("BtnCopyAllText");
        PanelPreviewTitle.Text = LocalizationService.Get("PanelPreview");
        BtnOpenCurrent.Content = LocalizationService.Get("BtnOpenCurrent");
        NoPreviewTextBlock.Text = LocalizationService.Get("NoPreviewText");
        NoPreviewSubTextBlock.Text = LocalizationService.Get("NoPreviewSubText");

        EmptyStateTextBlock.Text = LocalizationService.Get("EmptyResult");
        DbInfoTextBlock.Text = LocalizationService.Get("DbInfo");

        // 刷新列表统计文本
        UpdateSummaryAndStatus();
    }

    private void UpdateSummaryAndStatus()
    {
        var allDocs = _searchEngine.GetAllIndexedDocs();
        int totalIndexed = allDocs.Count;
        string liveStatus = _config.AutoSyncOnChange ? LocalizationService.Get("StatusEnabled") : LocalizationService.Get("StatusDisabled");
        StatusTextBlock.Text = LocalizationService.Get("StatusReady", totalIndexed, liveStatus);

        string keyword = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            ResultSummaryTextBlock.Text = LocalizationService.Get("ResultAllCount", _currentResults.Count);
        }
    }

    private int _searchSequence = 0;
    private bool _isIndexingInBackground = false;

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 1. 0 毫秒秒开：立即渲染已有索引数据，前台立即可用、立即可搜索
        if (!string.IsNullOrWhiteSpace(_config.LastSearchKeyword))
        {
            SearchBox.Text = _config.LastSearchKeyword;
            ExecuteSearch(_config.LastSearchKeyword);
        }
        else
        {
            ExecuteSearch(string.Empty);
        }

        // 2. 彻底放入后台独立线程执行增量索引检查，绝不阻塞前台操作与搜索
        _ = Task.Run(async () =>
        {
            await PerformBackgroundIndexAsync();
        });
    }

    private async Task PerformBackgroundIndexAsync()
    {
        if (_isIndexingInBackground)
            return;

        _isIndexingInBackground = true;

        try
        {
            // 1. 清理非受管目录残留索引
            try
            {
                _indexEngine.PurgeUnmanagedIndexes(_config.Folders);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MainWindow] 清理残留索引失败: {ex.Message}");
            }

            var progress = new Progress<IndexingProgress>(p =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (p.TotalFiles > 0)
                    {
                        int percent = (int)Math.Round((double)p.ProcessedFiles / p.TotalFiles * 100);
                        IndexingProgressBar.Value = percent;
                        ProgressValueTextBlock.Text = $"{percent}%";
                        if (!string.IsNullOrWhiteSpace(p.CurrentFile))
                        {
                            StatusTextBlock.Text = LocalizationService.Get("StatusIndexingProgress", p.ProcessedFiles, p.TotalFiles, percent, p.CurrentFile);
                        }
                    }
                }, DispatcherPriority.Background);
            });

            // 2. 扫描受管目录
            foreach (var folder in _config.Folders)
            {
                if (folder.Enabled && Directory.Exists(folder.Path))
                {
                    hasScannedAny = true;
                    Dispatcher.Invoke(() =>
                    {
                        IndexingProgressBar.Visibility = Visibility.Visible;
                        ProgressValueTextBlock.Visibility = Visibility.Visible;
                        StatusTextBlock.Text = LocalizationService.Get("StatusScanningFolder", folder.Path);
                    });

                    await _indexEngine.IndexDirectoryAsync(folder.Path, progress: progress);
                }
            }

            Dispatcher.Invoke(() =>
            {
                IndexingProgressBar.Visibility = Visibility.Collapsed;
                ProgressValueTextBlock.Visibility = Visibility.Collapsed;
                UpdateSummaryAndStatus();

                // 若当前在前台处于空白检索，平滑刷新最新列表
                if (string.IsNullOrWhiteSpace(SearchBox.Text))
                {
                    ExecuteSearch(string.Empty);
                }
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MainWindow] 后台索引失败: {ex.Message}");
            Dispatcher.Invoke(() =>
            {
                IndexingProgressBar.Visibility = Visibility.Collapsed;
                ProgressValueTextBlock.Visibility = Visibility.Collapsed;
                UpdateSummaryAndStatus();
            });
        }
        finally
        {
            _isIndexingInBackground = false;
        }
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

    private async void ExecuteSearch(string keyword)
    {
        int currentSeq = Interlocked.Increment(ref _searchSequence);
        keyword = keyword.Trim();
        _config.LastSearchKeyword = keyword;
        ConfigService.Save(_config);

        var sw = Stopwatch.StartNew();

        // 在后台线程执行 SQLite 全文检索，彻底防止 UI 卡顿
        var rawResults = await Task.Run(() =>
        {
            if (string.IsNullOrEmpty(keyword))
            {
                var allDocs = _searchEngine.GetAllIndexedDocs();
                return allDocs.Select(d => new SearchResult(d.FilePath, d.Title, $"[{d.TextLength} chars]", 0.0)).ToList();
            }
            else
            {
                return _searchEngine.Search(keyword, limit: _config.MaxSearchResults);
            }
        });

        // 若在异步查询期间用户输入了新搜索词，则丢弃旧结果
        if (currentSeq != _searchSequence)
            return;

        sw.Stop();

        _currentResults = rawResults.Select(r => new SearchResultItem(r)).ToList();
        ResultsListView.ItemsSource = _currentResults;

        if (string.IsNullOrEmpty(keyword))
        {
            ResultSummaryTextBlock.Text = LocalizationService.Get("ResultAllCount", _currentResults.Count);
        }
        else
        {
            ResultSummaryTextBlock.Text = LocalizationService.Get("ResultFoundCount", _currentResults.Count, sw.Elapsed.TotalMilliseconds);
        }

        EmptyStateTextBlock.Visibility = _currentResults.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_currentResults.Count > 0)
        {
            ResultsListView.SelectedIndex = 0;
        }
        else
        {
            ClearDetailsView();
        }
    }

    private void ResultsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsListView.SelectedItem is SearchResultItem item)
        {
            _selectedItem = item;
            DisplayItemDetails(item);
        }
    }

    private void DisplayItemDetails(SearchResultItem item)
    {
        string? text = _searchEngine.GetDocContent(item.FilePath);
        _currentExtractedText = text ?? string.Empty;

        RenderHighlightedContent(_currentExtractedText, SearchBox.Text.Trim());

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
            doc.Blocks.Add(new Paragraph(new Run("(No text content extracted)"))
            {
                Foreground = Brushes.Gray
            });
            ContentRichTextBox.Document = doc;
            return;
        }

        var paragraph = new Paragraph { LineHeight = 22 };
        var tokens = keyword.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        Run? firstHighlightRun = null;

        if (tokens.Length == 0)
        {
            paragraph.Inlines.Add(new Run(fullText) { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2B2B")) });
        }
        else
        {
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
                    if (nextMatchIndex > currentIndex)
                    {
                        string normalText = fullText.Substring(currentIndex, nextMatchIndex - currentIndex);
                        paragraph.Inlines.Add(new Run(normalText) { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2B2B")) });
                    }

                    string matchedText = fullText.Substring(nextMatchIndex, matchedToken.Length);
                    var highlightRun = new Run(matchedText)
                    {
                        Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF3C4")),
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D83B01")),
                        FontWeight = FontWeights.Bold
                    };
                    paragraph.Inlines.Add(highlightRun);

                    if (firstHighlightRun == null)
                    {
                        firstHighlightRun = highlightRun;
                    }

                    currentIndex = nextMatchIndex + matchedToken.Length;
                }
                else
                {
                    string remainingText = fullText.Substring(currentIndex);
                    paragraph.Inlines.Add(new Run(remainingText) { Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2B2B")) });
                    break;
                }
            }
        }

        doc.Blocks.Add(paragraph);
        ContentRichTextBox.Document = doc;

        // 自动滚动到首个命中关键词所在的可视位置
        if (firstHighlightRun != null)
        {
            Dispatcher.InvokeAsync(() =>
            {
                firstHighlightRun.BringIntoView();
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
        else
        {
            ContentRichTextBox.ScrollToHome();
        }
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

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        _searchDebounceTimer.Stop();
        ExecuteSearch(SearchBox.Text);
    }

    private async void OpenSettings_Click(object sender, RoutedEventArgs e)
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
                StatusTextBlock.Text = LocalizationService.Get("StatusSyncing");
                _ = Task.Run(async () => await PerformBackgroundIndexAsync());
            }
            else
            {
                string liveStatus = _config.AutoSyncOnChange ? LocalizationService.Get("StatusEnabled") : LocalizationService.Get("StatusDisabled");
                StatusTextBlock.Text = LocalizationService.Get("MsgSaved", liveStatus);
            }
        }
    }

    private void ResultsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenCurrentFile();
    private void OpenCurrentFile_Click(object sender, RoutedEventArgs e) => OpenCurrentFile();
    private void MenuOpenFile_Click(object sender, RoutedEventArgs e) => OpenCurrentFile();

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
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void MenuCopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedItem != null)
        {
            Clipboard.SetText(_selectedItem.FilePath);
            StatusTextBlock.Text = LocalizationService.Get("StatusCopiedPath", _selectedItem.FilePath);
        }
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentExtractedText))
        {
            Clipboard.SetText(_currentExtractedText);
            StatusTextBlock.Text = LocalizationService.Get("StatusCopiedText");
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
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void OnFileAutoIndexed(string filePath)
    {
        Dispatcher.Invoke(() =>
        {
            StatusTextBlock.Text = LocalizationService.Get("StatusAutoIndexed", Path.GetFileName(filePath), DateTime.Now.ToString("HH:mm:ss"));
            ExecuteSearch(SearchBox.Text);
        });
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        LocalizationService.OnLanguageChanged -= ApplyLocalization;
        _searchDebounceTimer.Dispose();
        _watcherService.Dispose();
        _searchEngine.Dispose();
        _indexEngine.Dispose();
        _dbManager.Dispose();
    }
}
