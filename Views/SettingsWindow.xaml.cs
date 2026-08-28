using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using DwgSearcher.Services;
using DwgSearcher.Engine;

namespace DwgSearcher.Views;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly IndexingEngine _indexEngine;
    private readonly ObservableCollection<WatchFolder> _folders;

    public bool NeedsReindex { get; private set; }

    public SettingsWindow(AppConfig config, IndexingEngine indexEngine)
    {
        InitializeComponent();
        _config = config;
        _indexEngine = indexEngine;

        _folders = new ObservableCollection<WatchFolder>(_config.Folders.Select(f => new WatchFolder
        {
            Path = f.Path,
            IncludeSubdirectories = f.IncludeSubdirectories,
            Enabled = f.Enabled
        }));

        FoldersDataGrid.ItemsSource = _folders;
        AutoSyncCheckBox.IsChecked = _config.AutoSyncOnChange;
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        // .NET 8/10 现代原生文件夹选择对话框
        var dialog = new OpenFolderDialog
        {
            Title = "选择包含 CAD 图纸 (.dwg/.dxf) 的文件夹",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            string selectedPath = dialog.FolderName;
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                if (!_folders.Any(f => f.Path.Equals(selectedPath, StringComparison.OrdinalIgnoreCase)))
                {
                    _folders.Add(new WatchFolder { Path = selectedPath, IncludeSubdirectories = true });
                    NeedsReindex = true;
                }
                else
                {
                    MessageBox.Show("该文件夹已在监控列表中！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is WatchFolder folder)
        {
            _folders.Remove(folder);
            NeedsReindex = true;
        }
    }

    private async void RebuildIndex_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show("确定要重新全量扫描并重建所有图纸索引吗？", "确认重建", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            IsEnabled = false;
            try
            {
                foreach (var folder in _folders)
                {
                    if (Directory.Exists(folder.Path))
                    {
                        await _indexEngine.IndexDirectoryAsync(folder.Path);
                    }
                }
                MessageBox.Show("全量索引重建完成！", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"重建索引失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _config.Folders = _folders.ToList();
        _config.AutoSyncOnChange = AutoSyncCheckBox.IsChecked == true;
        ConfigService.Save(_config);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
