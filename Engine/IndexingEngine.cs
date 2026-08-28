using System.Collections.Concurrent;
using System.Diagnostics;
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
            throw new DirectoryNotFoundException($"目录未找到: {directoryPath}");

        // 1. 枚举所有支持的磁盘文件
        var diskFiles = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
            .Where(file => _extractorRegistry.IsSupported(file))
            .ToList();

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
        var documentsToWrite = new ConcurrentBag<IndexedDocument>();
        var recordsToWrite = new ConcurrentBag<FileRecord>();

        using var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = new List<Task>();

        foreach (var fileInfo in filesToIndex)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(cancellationToken);

            tasks.Add(Task.Run(async () =>
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
                        var doc = await extractor.ExtractAsync(fileInfo.FullName);
                        if (doc != null)
                        {
                            documentsToWrite.Add(doc);
                            recordsToWrite.Add(new FileRecord
                            {
                                FilePath = doc.FilePath,
                                LastModified = fileInfo.LastWriteTimeUtc.Ticks,
                                FileSize = fileInfo.Length
                            });
                            Interlocked.Increment(ref indexedCount);
                        }
                        else
                        {
                            Interlocked.Increment(ref failedCount);
                        }
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
            if (documentsToWrite.Count >= batchSize)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
                FlushBatchToDatabase(documentsToWrite, recordsToWrite);
            }
        }

        // 等待所有剩余任务完成并提交最终批次
        await Task.WhenAll(tasks);
        if (!documentsToWrite.IsEmpty)
        {
            FlushBatchToDatabase(documentsToWrite, recordsToWrite);
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
    public async Task IndexSingleFileAsync(string filePath)
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

        var doc = await extractor.ExtractAsync(filePath);
        if (doc == null) return;

        var newRecord = new FileRecord
        {
            FilePath = doc.FilePath,
            LastModified = fileInfo.LastWriteTimeUtc.Ticks,
            FileSize = fileInfo.Length
        };

        FlushBatchToDatabase(new[] { doc }, new[] { newRecord });
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
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsSubPath(string path, string basePath)
    {
        if (path.Equals(basePath, StringComparison.OrdinalIgnoreCase))
            return true;

        string prefix = basePath + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 批量事务写入 SQLite（同时插入/更新 FileRecords 表 和 FTS5 DocIndex 表）
    /// </summary>
    private void FlushBatchToDatabase(IEnumerable<IndexedDocument> docs, IEnumerable<FileRecord> records)
    {
        var docList = docs.ToList();
        var recordList = records.ToList();
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

        foreach (var rec in recordList)
        {
            pRecPath.Value = rec.FilePath;
            pRecMod.Value = rec.LastModified;
            pRecSize.Value = rec.FileSize;
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
    private Dictionary<string, FileRecord> LoadExistingFileRecords()
    {
        var dict = new Dictionary<string, FileRecord>(StringComparer.OrdinalIgnoreCase);

        using var connection = _dbManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT FileId, FilePath, LastModified, FileSize FROM FileRecords;";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var record = new FileRecord
            {
                FileId = reader.GetInt64(0),
                FilePath = reader.GetString(1),
                LastModified = reader.GetInt64(2),
                FileSize = reader.GetInt64(3)
            };
            dict[record.FilePath] = record;
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

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
