using System.IO;
using System.Text.Json;

namespace DwgSearcher.Services;

/// <summary>
/// 监控文件夹配置项
/// </summary>
public class WatchFolder
{
    public string Path { get; set; } = string.Empty;
    public bool IncludeSubdirectories { get; set; } = true;
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// 应用程序全局配置
/// </summary>
public class AppConfig
{
    public List<WatchFolder> Folders { get; set; } = new();
    public bool AutoSyncOnChange { get; set; } = true;
    public int MaxSearchResults { get; set; } = 100;
    public string LastSearchKeyword { get; set; } = string.Empty;
    public string Language { get; set; } = "zh-CN";
    public string UpdateUrl { get; set; } = "https://github.com/funekaa/DwgSearcher";
}

/// <summary>
/// 配置持久化管理服务
/// </summary>
public static class ConfigService
{
    private static readonly string ConfigPath = PathHelper.ConfigPath;
    private static AppConfig? _current;

    public static AppConfig Load()
    {
        if (_current != null)
            return _current;

        if (File.Exists(ConfigPath))
        {
            try
            {
                string json = File.ReadAllText(ConfigPath);
                _current = JsonSerializer.Deserialize<AppConfig>(json);
                if (_current != null)
                {
                    LocalizationService.CurrentLanguage = _current.Language ?? "zh-CN";
                    return _current;
                }
            }
            catch
            {
                // 异常时使用默认值
            }
        }

        // 默认初始化配置
        _current = new AppConfig();
        LocalizationService.CurrentLanguage = _current.Language;
        Save(_current);
        return _current;
    }

    public static void Save(AppConfig config)
    {
        try
        {
            _current = config;
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ConfigService] 保存配置失败: {ex.Message}");
        }
    }
}
