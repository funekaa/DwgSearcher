using System;
using System.IO;

namespace DwgSearcher.Services;

/// <summary>
/// 应用程序数据与持久化路径管理助手
/// 支持升级覆盖时数据与索引绝对不丢失
/// </summary>
public static class PathHelper
{
    private static readonly string AppDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
        "DwgSearcher"
    );

    static PathHelper()
    {
        try
        {
            if (!Directory.Exists(AppDataFolder))
            {
                Directory.CreateDirectory(AppDataFolder);
            }

            // 自动无缝迁移历史旧版本（若程序根目录下有旧的 db/config，自动迁移到 LocalAppData）
            MigrateLegacyFiles();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PathHelper] 初始化数据目录失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 获取持久化数据存储目录 (%LocalAppData%\DwgSearcher)
    /// </summary>
    public static string DataDirectory => AppDataFolder;

    /// <summary>
    /// 获取 SQLite 索引数据库完整路径
    /// </summary>
    public static string DatabasePath => Path.Combine(AppDataFolder, "dwg_index.db");

    /// <summary>
    /// 获取配置文件完整路径
    /// </summary>
    public static string ConfigPath => Path.Combine(AppDataFolder, "config.json");

    /// <summary>
    /// 自动将程序根目录下可能存在的旧版本文件平滑迁移至 LocalAppData
    /// </summary>
    private static void MigrateLegacyFiles()
    {
        try
        {
            string legacyDir = AppDomain.CurrentDomain.BaseDirectory;

            // 1. 迁移 config.json
            string legacyConfig = Path.Combine(legacyDir, "config.json");
            string newConfig = Path.Combine(AppDataFolder, "config.json");
            if (File.Exists(legacyConfig) && !File.Exists(newConfig))
            {
                File.Copy(legacyConfig, newConfig, overwrite: false);
            }

            // 2. 迁移 dwg_index.db
            string legacyDb = Path.Combine(legacyDir, "dwg_index.db");
            string newDb = Path.Combine(AppDataFolder, "dwg_index.db");
            if (File.Exists(legacyDb) && !File.Exists(newDb))
            {
                File.Copy(legacyDb, newDb, overwrite: false);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[PathHelper] 迁移历史数据失败: {ex.Message}");
        }
    }
}
