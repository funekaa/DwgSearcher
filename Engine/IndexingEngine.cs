using System.Collections.Concurrent;
using System.IO;
using Microsoft.Data.Sqlite;
using DwgSearcher.Models;
using DwgSearcher.Services;
using DwgSearcher.Storage;
using DwgSearcher.TextExtraction;

namespace DwgSearcher.Engine;

/// <summary>
/// 索引扫描进度信息
/// </summary>
public record IndexingProgress(
    int TotalFiles,
    int ProcessedFiles,
    int IndexedFiles,
    int SkippedFiles,
    int FailedFiles,
    string CurrentFile
);

/// <summary>
/// 提取的文档中间数据结构
/// </summary>
internal record ExtractedDoc(string FilePath, string Title, string Content, long LastModified, long FileSize);

/// <summary>
/// 全文索引引擎，负责文件的增量检测、多线程文本提取以及批量事务入库
/// </summary>
public class IndexingEngine : IDisposable
{
    private readonly DatabaseManager _dbManager;
    private readonly ExtractorRegistry _extractorRegistry;
    private bool _disposed;

    public IndexingEngine(DatabaseManager dbManager, ExtractorRegistry? extractorRegistry = null)
    {
        _dbManager = dbManager;
        _extractorRegistry = extractorRegistry ?? new ExtractorRegistry();
    }

    /// <summary>
    /// 全量扫描目录并进行增量比对与批量入库
    /// </summary>
    public async Task IndexDirectoryAsync(
        string directoryPath, 
        int batchSize = 500, 
        int maxConcurrency = 4, 
        IProgress<IndexingProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directoryPath))
            return;

        // 1. 安全枚举所有支持的磁盘文件（自动跳过系统保护、隐藏及权限受限目录）
        var diskFiles = SafeEnumerateSupportedFiles(directoryPath).ToList();

        // 2. 加载数据库中现存的文件元数据缓存
        var existingRecords = LoadExistingFileRecords();

        // 3. 找出需要新增/更新的文件，以及磁盘已删除的文件
        var filesToIndex = new List<FileInfo>();
        var currentDiskPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int skippedCount = 0;
        foreach (var filePath in diskFiles)
        {
            currentDiskPaths.Add(filePath);
            var fileInfo = new FileInfo(filePath);
            
            if (existingRecords.TryGetValue(filePath, out var record))
            {
                // 增量判断：若修改时间 (Ticks) 和 文件大小一致，则跳过
                if (record.LastModified == fileInfo.LastWriteTimeUtc.Ticks && record.FileSize == fileInfo.Length)
                {
                    skippedCount++;
                    continue;
                }
            }
            filesToIndex.Add(fileInfo);
        }

        // 4. 清理该目录下磁盘已物理删除的文件
        string normalizedDirPath = NormalizePath(directoryPath);
        var deletedFiles = existingRecords.Keys
            .Where(path => IsSubPath(path, normalizedDirPath) && !currentDiskPaths.Contains(path))
            .ToList();

        if (deletedFiles.Count > 0)
        {
            RemoveDeletedFiles(deletedFiles);
        }

        int totalToProcess = filesToIndex.Count;
        int processedCount = 0;
        int indexedCount = 0;
        int failedCount = 0;

        if (totalToProcess == 0)
        {
            progress?.Report(new IndexingProgress(
                TotalFiles: diskFiles.Count,
                ProcessedFiles: diskFiles.Count,
                IndexedFiles: 0,
                SkippedFiles: skippedCount,
                FailedFiles: 0,
                CurrentFile: "已全部是最新索引"
            ));
            return;
        }

        // 5. 多线程并发提取文本
        var docsToWrite = new ConcurrentBag<ExtractedDoc>();

        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = new List<Task>();

        foreach (var fileInfo in filesToIndex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(cancellationToken);

            tasks.Add(Task.Run(() =>
            {
                try
                {
                    progress?.Report(new IndexingProgress(
                        TotalFiles: totalToProcess,
                        ProcessedFiles: processedCount,
                        IndexedFiles: indexedCount,
                        SkippedFiles: skippedCount,
                        FailedFiles: failedCount,
                        CurrentFile: fileInfo.FullName
                    ));

                    var extractor = _extractorRegistry.GetExtractor(fileInfo.FullName);
                    if (extractor != null)
                    {
                        string content = extractor.ExtractText(fileInfo.FullName);
                        string title = Path.GetFileName(fileInfo.FullName);

                        docsToWrite.Add(new ExtractedDoc(
                            fileInfo.FullName,
                            title,
                            content ?? string.Empty,
                            fileInfo.LastWriteTimeUtc.Ticks,
                            fileInfo.Length
                        ));
                        Interlocked.Increment(ref indexedCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref failedCount);
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failedCount);
                    Console.Error.WriteLine($"[IndexingEngine] 提取失败: {fileInfo.FullName}, 原因: {ex.Message}");
                }
                finally
                {
                    Interlocked.Increment(ref processedCount);
                    semaphore.Release();
                }
            }, cancellationToken));

            // 分批写入数据库，避免内存暴涨
            if (docsToWrite.Count >= batchSize)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
                FlushBatchToDatabase(docsToWrite);
            }
        }

        // 等待所有剩余任务完成并提交最终批次
        await Task.WhenAll(tasks);
        if (!docsToWrite.IsEmpty)
        {
            FlushBatchToDatabase(docsToWrite);
        }

        progress?.Report(new IndexingProgress(
            TotalFiles: totalToProcess,
            ProcessedFiles: processedCount,
            IndexedFiles: indexedCount,
            SkippedFiles: skippedCount,
            FailedFiles: failedCount,
            CurrentFile: "索引完成"
        ));
    }

    /// <summary>
    /// 全面清理非受管目录下的孤立图纸索引
    /// 确保数据库中只保留当前受监控文件夹内的图纸
    /// </summary>
    /// <param name="activeFolders">当前受监控的文件夹配置列表</param>
    public int PurgeUnmanagedIndexes(IEnumerable<WatchFolder> activeFolders)
    {
        var validPrefixes = activeFolders
            .Where(f => f.Enabled && !string.IsNullOrWhiteSpace(f.Path))
            .Select(f => NormalizePath(f.Path))
            .ToList();

        var existingRecords = LoadExistingFileRecords();
        var unmanagedPaths = new List<string>();

        foreach (var filePath in existingRecords.Keys)
        {
            string normalizedFilePath = NormalizePath(filePath);
            bool isManaged = validPrefixes.Any(prefix => IsSubPath(normalizedFilePath, prefix));
            if (!isManaged)
            {
                unmanagedPaths.Add(filePath);
            }
        }

        if (unmanagedPaths.Count > 0)
        {
            RemoveDeletedFiles(unmanagedPaths);
        }

        return unmanagedPaths.Count;
    }

    /// <summary>
    /// 单个文件增量更新或新建
    /// </summary>
    public void IndexSingleFile(string filePath)
    {
        if (!File.Exists(filePath) || !_extractorRegistry.IsSupported(filePath))
            return;

        var fileInfo = new FileInfo(filePath);
        var existingRecords = LoadExistingFileRecords();

        if (existingRecords.TryGetValue(filePath, out var record))
        {
            if (record.LastModified == fileInfo.LastWriteTimeUtc.Ticks && record.FileSize == fileInfo.Length)
            {
                return;
            }
        }

        var extractor = _extractorRegistry.GetExtractor(filePath);
        if (extractor == null) return;

        string content = extractor.ExtractText(filePath);
        string title = Path.GetFileName(filePath);

        var doc = new ExtractedDoc(
            filePath,
            title,
            content ?? string.Empty,
            fileInfo.LastWriteTimeUtc.Ticks,
            fileInfo.Length
        );

        FlushBatchToDatabase(new[] { doc });
    }

    /// <summary>
    /// 单个文件从索引中删除
    /// </summary>
    public void RemoveFile(string filePath)
    {
        RemoveDeletedFiles(new[] { filePath });
    }

    /// <summary>
    /// 移除指定目录及其所有子目录下已索引的图纸记录与倒排全文索引
    /// </summary>
    public void RemoveDirectoryIndex(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath)) return;

        string normalizedDirPath = NormalizePath(directoryPath);
        var existingRecords = LoadExistingFileRecords();
        var pathsToRemove = existingRecords.Keys
            .Where(path => IsSubPath(NormalizePath(path), normalizedDirPath))
            .ToList();

        if (pathsToRemove.Count > 0)
        {
            RemoveDeletedFiles(pathsToRemove);
        }
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    private static bool IsSubPath(string path, string basePath)
    {
        string normPath = NormalizePath(path);
        string normBase = NormalizePath(basePath);

        if (normPath.Equals(normBase, StringComparison.OrdinalIgnoreCase))
            return true;

        string prefix = normBase + Path.DirectorySeparatorChar;
        return normPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 批量事务写入 SQLite（同时插入/更新 FileRecords 表 和 FTS5 DocIndex 表）
    /// </summary>
    private void FlushBatchToDatabase(IEnumerable<ExtractedDoc> docs)
    {
        var docList = docs.ToList();
        if (docList.Count == 0) return;

        using var connection = _dbManager.CreateConnection();
        using var transaction = connection.BeginTransaction();

        // 1. 写入/更新 FileRecords 表
        using var cmdRec = connection.CreateCommand();
        cmdRec.Transaction = transaction;
        cmdRec.CommandText = @"
            INSERT INTO FileRecords (FilePath, LastModified, FileSize)
            VALUES (@FilePath, @LastModified, @FileSize)
            ON CONFLICT(FilePath) DO UPDATE SET
                LastModified = excluded.LastModified,
                FileSize = excluded.FileSize;
        ";
        var pRecPath = cmdRec.Parameters.Add("@FilePath", SqliteType.Text);
        var pRecMod = cmdRec.Parameters.Add("@LastModified", SqliteType.Integer);
        var pRecSize = cmdRec.Parameters.Add("@FileSize", SqliteType.Integer);

        foreach (var doc in docList)
        {
            pRecPath.Value = doc.FilePath;
            pRecMod.Value = doc.LastModified;
            pRecSize.Value = doc.FileSize;
            cmdRec.ExecuteNonQuery();
        }

        // 2. 写入/更新 DocIndex (FTS5) 虚拟表
        using var cmdDoc = connection.CreateCommand();
        cmdDoc.Transaction = transaction;
        cmdDoc.CommandText = @"
            DELETE FROM DocIndex WHERE FilePath = @FilePath;
            INSERT INTO DocIndex (FilePath, Title, Content)
            VALUES (@FilePath, @Title, @Content);
        ";
        var pDocPath = cmdDoc.Parameters.Add("@FilePath", SqliteType.Text);
        var pDocTitle = cmdDoc.Parameters.Add("@Title", SqliteType.Text);
        var pDocContent = cmdDoc.Parameters.Add("@Content", SqliteType.Text);

        foreach (var doc in docList)
        {
            pDocPath.Value = doc.FilePath;
            pDocTitle.Value = doc.Title;
            pDocContent.Value = doc.Content;
            cmdDoc.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>
    /// 加载现存的所有文件元数据字典
    /// </summary>
    private Dictionary<string, (long LastModified, long FileSize)> LoadExistingFileRecords()
    {
        var dict = new Dictionary<string, (long LastModified, long FileSize)>(StringComparer.OrdinalIgnoreCase);

        using var connection = _dbManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT FilePath, LastModified, FileSize FROM FileRecords;";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            string path = reader.GetString(0);
            long lastMod = reader.GetInt64(1);
            long size = reader.GetInt64(2);
            dict[path] = (lastMod, size);
        }

        return dict;
    }

    /// <summary>
    /// 批量从数据库清理已删除的文件
    /// </summary>
    private void RemoveDeletedFiles(IEnumerable<string> filePaths)
    {
        var paths = filePaths.ToList();
        if (paths.Count == 0) return;

        using var connection = _dbManager.CreateConnection();
        using var transaction = connection.BeginTransaction();

        using var delDocCmd = connection.CreateCommand();
        delDocCmd.Transaction = transaction;
        delDocCmd.CommandText = "DELETE FROM DocIndex WHERE FilePath = @FilePath;";
        var pDocPath = delDocCmd.Parameters.Add("@FilePath", SqliteType.Text);

        using var delRecCmd = connection.CreateCommand();
        delRecCmd.Transaction = transaction;
        delRecCmd.CommandText = "DELETE FROM FileRecords WHERE FilePath = @FilePath;";
        var pRecPath = delRecCmd.Parameters.Add("@FilePath", SqliteType.Text);

        foreach (var path in paths)
        {
            pDocPath.Value = path;
            delDocCmd.ExecuteNonQuery();

            pRecPath.Value = path;
            delRecCmd.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static readonly HashSet<string> IgnoredDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information",
        "$RECYCLE.BIN",
        "$Recycle.Bin",
        "Recycled",
        "Recycler",
        "Config.Msi",
        ".git",
        ".svn",
        ".vs",
        "node_modules",
        "Windows",
        "AppData"
    };

    /// <summary>
    /// 安全递归遍历目录并枚举所有支持的文件，自动跳过系统保护、隐藏及无权访问的目录（如 System Volume Information）
    /// </summary>
    private IEnumerable<string> SafeEnumerateSupportedFiles(string rootPath)
    {
        var directoriesToVisit = new Stack<string>();
        directoriesToVisit.Push(rootPath);

        while (directoriesToVisit.Count > 0)
        {
            string currentDir = directoriesToVisit.Pop();

            // 1. 枚举当前目录下的文件
            string[]? files = null;
            try
            {
                files = Directory.GetFiles(currentDir);
            }
            catch (UnauthorizedAccessException) { }
            catch (PathTooLongException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[IndexingEngine] 无法读取文件目录 {currentDir}: {ex.Message}");
            }

            if (files != null)
            {
                foreach (var file in files)
                {
                    if (_extractorRegistry.IsSupported(file))
                    {
                        yield return file;
                    }
                }
            }

            // 2. 枚举当前目录下的子目录并入栈
            string[]? subDirs = null;
            try
            {
                subDirs = Directory.GetDirectories(currentDir);
            }
            catch (UnauthorizedAccessException) { }
            catch (PathTooLongException) { }
            catch (DirectoryNotFoundException) { }
            catch (IOException) { }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[IndexingEngine] 无法访问子目录 {currentDir}: {ex.Message}");
            }

            if (subDirs != null)
            {
                foreach (var dir in subDirs)
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        string dirName = dirInfo.Name;

                        // 忽略黑名单系统目录
                        if (IgnoredDirectoryNames.Contains(dirName))
                            continue;

                        // 忽略带有重解析点 (符号链接/Junction) 的目录，防止递归死循环
                        if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                            continue;

                        directoriesToVisit.Push(dir);
                    }
                    catch { }
                }
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
