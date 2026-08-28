using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
        BtnSave.Content = LocalizationService.Get("BtnSaveApply");
        BtnCancel.Content = LocalizationService.Get("BtnCancel");
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
                    _folders.Add(new WatchFolder { Path = selectedPath, IncludeSubdirectories = true });
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
        if (sender is FrameworkElement element && element.DataContext is WatchFolder folder)
        {
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
        _config.Folders = _folders.ToList();
        _config.AutoSyncOnChange = AutoSyncCheckBox.IsChecked == true;
        _config.Language = LocalizationService.CurrentLanguage;
        ConfigService.Save(_config);

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
