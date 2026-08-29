using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DwgSearcher.Services;
using DwgSearcher.Engine;

namespace DwgSearcher.Views;

/// <summary>
/// 监控文件夹绑定实体（支持多语言动态按钮）
/// </summary>
public class WatchFolderItem : INotifyPropertyChanged
{
    private string _path = string.Empty;
    private bool _includeSubdirectories = true;
    private bool _enabled = true;

    public string Path
    {
        get => _path;
        set { _path = value; OnPropertyChanged(); }
    }

    public bool IncludeSubdirectories
    {
        get => _includeSubdirectories;
        set { _includeSubdirectories = value; OnPropertyChanged(); }
    }

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(); }
    }

    public string RemoveButtonText => LocalizationService.Get("BtnRemove");

    public void NotifyLanguageChanged()
    {
        OnPropertyChanged(nameof(RemoveButtonText));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly IndexingEngine _indexEngine;
    private readonly ObservableCollection<WatchFolderItem> _folders;
    private readonly List<string> _removedFolderPaths = new();

    public bool NeedsReindex { get; private set; }

    public SettingsWindow(AppConfig config, IndexingEngine indexEngine)
    {
        InitializeComponent();
        _config = config;
        _indexEngine = indexEngine;

        _folders = new ObservableCollection<WatchFolderItem>(_config.Folders.Select(f => new WatchFolderItem
        {
            Path = f.Path,
            IncludeSubdirectories = f.IncludeSubdirectories,
            Enabled = f.Enabled
        }));

        FoldersDataGrid.ItemsSource = _folders;
        AutoSyncCheckBox.IsChecked = _config.AutoSyncOnChange;

        // 初始化更新地址
        UpdateUrlTextBox.Text = string.IsNullOrWhiteSpace(_config.UpdateUrl)
            ? "https://github.com/funekaa/DwgSearcher"
            : _config.UpdateUrl;

        // 初始化多语言下拉选项
        LanguageComboBox.ItemsSource = LocalizationService.SupportedLanguages;
        LanguageComboBox.SelectedValue = LocalizationService.CurrentLanguage;

        UpdateUiTexts();
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedValue is string langCode)
        {
            LocalizationService.CurrentLanguage = langCode;
            UpdateUiTexts();
        }
    }

    private void UpdateUiTexts()
    {
        Title = LocalizationService.Get("SettingsTitle");
        TitleTextBlock.Text = LocalizationService.Get("SettingsFolderSection");
        ColFolderPath.Header = LocalizationService.Get("ColFolderPath");
        ColIncludeSub.Header = LocalizationService.Get("ColIncludeSub");
        ColAction.Header = LocalizationService.Get("ColAction");
        BtnAddFolder.Content = LocalizationService.Get("BtnAddFolder");
        BtnRebuildIndex.Content = LocalizationService.Get("BtnRebuildIndex");
        AutoSyncCheckBox.Content = LocalizationService.Get("ChkAutoSync");
        SettingsNoteTextBlock.Text = LocalizationService.Get("SettingsNote");
        LanguageLabelTextBlock.Text = LocalizationService.Get("SettingsLangSection");
        UpdateUrlLabelTextBlock.Text = LocalizationService.Get("SettingsUpdateSection");
        BtnOpenUpdateUrl.Content = LocalizationService.Get("BtnOpenUpdateUrl");
        BtnSave.Content = LocalizationService.Get("BtnSaveApply");
        BtnCancel.Content = LocalizationService.Get("BtnCancel");

        // 刷新表格内所有行的“移除”按钮多语言文本
        foreach (var item in _folders)
        {
            item.NotifyLanguageChanged();
        }
    }

    private void OpenUpdateUrl_Click(object sender, RoutedEventArgs e)
    {
        string url = UpdateUrlTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(url))
        {
            url = "https://github.com/funekaa/DwgSearcher";
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open URL: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = LocalizationService.Get("SettingsFolderSection"),
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            string selectedPath = dialog.FolderName;
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                if (!_folders.Any(f => f.Path.Equals(selectedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    _folders.Add(new WatchFolderItem { Path = selectedPath, IncludeSubdirectories = true });
                    // 如果之前被移除了，现在重新添加，则从待清理列表中移除
                    _removedFolderPaths.RemoveAll(p => p.Equals(selectedPath, StringComparison.OrdinalIgnoreCase));
                    NeedsReindex = true;
                }
                else
                {
                    MessageBox.Show(LocalizationService.Get("MsgFolderExists"), "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is WatchFolderItem folder)
        {
            if (!_removedFolderPaths.Contains(folder.Path, StringComparer.OrdinalIgnoreCase))
            {
                _removedFolderPaths.Add(folder.Path);
            }
            _folders.Remove(folder);
            NeedsReindex = true;
        }
    }

    private async void RebuildIndex_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(LocalizationService.Get("MsgRebuildConfirm"), "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            IsEnabled = false;
            try
            {
                // 先清理已移除的文件夹索引
                foreach (var removedPath in _removedFolderPaths)
                {
                    _indexEngine.RemoveDirectoryIndex(removedPath);
                }
                _removedFolderPaths.Clear();

                // 重新扫描现有文件夹
                foreach (var folder in _folders)
                {
                    if (Directory.Exists(folder.Path))
                    {
                        await _indexEngine.IndexDirectoryAsync(folder.Path);
                    }
                }
                MessageBox.Show(LocalizationService.Get("MsgRebuildSuccess"), "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // 1. 保存新配置
        _config.Folders = _folders.Select(f => new WatchFolder
        {
            Path = f.Path,
            IncludeSubdirectories = f.IncludeSubdirectories,
            Enabled = f.Enabled
        }).ToList();

        _config.AutoSyncOnChange = AutoSyncCheckBox.IsChecked == true;
        _config.Language = LocalizationService.CurrentLanguage;
        _config.UpdateUrl = UpdateUrlTextBox.Text.Trim();
        ConfigService.Save(_config);

        // 2. 彻底从 SQLite 数据库全面清理所有非受管文件夹下的图纸索引
        try
        {
            int purged = _indexEngine.PurgeUnmanagedIndexes(_config.Folders);
            if (purged > 0 || _removedFolderPaths.Count > 0)
            {
                NeedsReindex = true;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SettingsWindow] 清理非受管目录索引失败: {ex.Message}");
        }

        _removedFolderPaths.Clear();

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // 还原语言设置
        LocalizationService.CurrentLanguage = _config.Language;
        DialogResult = false;
        Close();
    }
}
