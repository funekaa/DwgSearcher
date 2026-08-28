using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.Data.Sqlite;
using DwgSearcher.Models;
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
    /// <param name="directoryPath">要扫描的目录</param>
    /// <param name="batchSize">事务批处理大小（默认 500）</param>
    /// <param name="maxConcurrency">文本提取的最大并发度</param>
    /// <param name="progress">进度回调</param>
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

        // 4. 清理磁盘上已删除但在数据库中残留的索引
        var deletedPaths = existingRecords.Keys.Where(p => !currentDiskPaths.Contains(p) && p.StartsWith(directoryPath, StringComparison.OrdinalIgnoreCase)).ToList();
        if (deletedPaths.Count > 0)
        {
            RemoveDeletedFiles(deletedPaths);
        }

        int totalToProcess = filesToIndex.Count;
        int processedCount = 0;
        int indexedCount = 0;
        int failedCount = 0;

        if (totalToProcess == 0)
        {
            progress?.Report(new IndexingProgress(diskFiles.Count, diskFiles.Count, 0, skippedCount, 0, "全部文件已是最新"));
            return;
        }

        // 5. 并行文本提取 + 单线程批量事务入库（生产者-消费者流水线）
        var pendingBatch = new List<(FileInfo info, string title, string content)>(batchSize);

        // 使用 Parallel.ForEach 进行 CPU 并行文本抽取
        var extractedQueue = new ConcurrentQueue<(FileInfo info, string title, string content)>();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxConcurrency,
            CancellationToken = cancellationToken
        };

        // 按 batch 分批并行抽取和写入
        for (int i = 0; i < filesToIndex.Count; i += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentSlice = filesToIndex.Skip(i).Take(batchSize).ToList();
            var batchItems = new ConcurrentBag<(FileInfo info, string title, string content)>();

            Parallel.ForEach(currentSlice, parallelOptions, fileInfo =>
            {
                try
                {
                    var extractor = _extractorRegistry.GetExtractor(fileInfo.FullName);
                    if (extractor != null)
                    {
                        string content = extractor.ExtractText(fileInfo.FullName);
                        string title = Path.GetFileName(fileInfo.FullName);
                        batchItems.Add((fileInfo, title, content));
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref failedCount);
                    Console.Error.WriteLine($"[IndexingEngine] 提取文本失败 {fileInfo.FullName}: {ex.Message}");
                }
            });

            // 6. 批量事务写入 SQLite（极速提升吞吐量）
            SaveBatchToDatabase(batchItems);

            indexedCount += batchItems.Count;
            processedCount += currentSlice.Count;

            progress?.Report(new IndexingProgress(
                diskFiles.Count, 
                skippedCount + processedCount, 
                indexedCount, 
                skippedCount, 
                failedCount, 
                currentSlice.LastOrDefault()?.FullName ?? string.Empty
            ));
        }
    }

    /// <summary>
    /// 索引或更新单个文件
    /// </summary>
    public void IndexSingleFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            RemoveFile(filePath);
            return;
        }

        var extractor = _extractorRegistry.GetExtractor(filePath);
        if (extractor == null) return;

        var fileInfo = new FileInfo(filePath);
        string content = extractor.ExtractText(filePath);
        string title = Path.GetFileName(filePath);

        SaveBatchToDatabase(new[] { (fileInfo, title, content) });
    }

    /// <summary>
    /// 从索引库中彻底移除指定文件
    /// </summary>
    public void RemoveFile(string filePath)
    {
        RemoveDeletedFiles(new[] { filePath });
    }

    /// <summary>
    /// 使用单事务批量入库
    /// </summary>
    private void SaveBatchToDatabase(IEnumerable<(FileInfo info, string title, string content)> batch)
    {
        var items = batch.ToList();
        if (items.Count == 0) return;

        using var connection = _dbManager.CreateConnection();
        using var transaction = connection.BeginTransaction();

        // 预编译 SQL 语句
        using var deleteDocCmd = connection.CreateCommand();
        deleteDocCmd.Transaction = transaction;
        deleteDocCmd.CommandText = "DELETE FROM DocIndex WHERE FilePath = @FilePath;";
        var pDelDocPath = deleteDocCmd.Parameters.Add("@FilePath", SqliteType.Text);

        using var deleteRecordCmd = connection.CreateCommand();
        deleteRecordCmd.Transaction = transaction;
        deleteRecordCmd.CommandText = "DELETE FROM FileRecords WHERE FilePath = @FilePath;";
        var pDelRecPath = deleteRecordCmd.Parameters.Add("@FilePath", SqliteType.Text);

        using var insertRecordCmd = connection.CreateCommand();
        insertRecordCmd.Transaction = transaction;
        insertRecordCmd.CommandText = @"
            INSERT INTO FileRecords (FilePath, LastModified, FileSize)
            VALUES (@FilePath, @LastModified, @FileSize);
        ";
        var pRecPath = insertRecordCmd.Parameters.Add("@FilePath", SqliteType.Text);
        var pRecMod = insertRecordCmd.Parameters.Add("@LastModified", SqliteType.Integer);
        var pRecSize = insertRecordCmd.Parameters.Add("@FileSize", SqliteType.Integer);

        using var insertDocCmd = connection.CreateCommand();
        insertDocCmd.Transaction = transaction;
        insertDocCmd.CommandText = @"
            INSERT INTO DocIndex (FilePath, Title, Content)
            VALUES (@FilePath, @Title, @Content);
        ";
        var pDocPath = insertDocCmd.Parameters.Add("@FilePath", SqliteType.Text);
        var pDocTitle = insertDocCmd.Parameters.Add("@Title", SqliteType.Text);
        var pDocContent = insertDocCmd.Parameters.Add("@Content", SqliteType.Text);

        foreach (var (info, title, content) in items)
        {
            var fullPath = info.FullName;

            // 1. 删除旧记录
            pDelDocPath.Value = fullPath;
            deleteDocCmd.ExecuteNonQuery();

            pDelRecPath.Value = fullPath;
            deleteRecordCmd.ExecuteNonQuery();

            // 2. 插入元数据记录
            pRecPath.Value = fullPath;
            pRecMod.Value = info.LastWriteTimeUtc.Ticks;
            pRecSize.Value = info.Length;
            insertRecordCmd.ExecuteNonQuery();

            // 3. 插入 FTS5 倒排索引
            pDocPath.Value = fullPath;
            pDocTitle.Value = title;
            pDocContent.Value = content;
            insertDocCmd.ExecuteNonQuery();
        }

        // 提交事务
        transaction.Commit();
    }

    /// <summary>
    /// 加载已有文件的元数据缓存
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
            var record = new FileRecord(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetInt64(2),
                reader.GetInt64(3)
            );
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
