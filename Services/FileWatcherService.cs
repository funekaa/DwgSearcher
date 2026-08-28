using System.IO;
using DwgSearcher.Engine;

namespace DwgSearcher.Services;

/// <summary>
/// 后台文件变动监听与自动增量更新服务
/// </summary>
public class FileWatcherService : IDisposable
{
    private readonly IndexingEngine _indexEngine;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly System.Timers.Timer _debounceTimer;
    private readonly HashSet<string> _changedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private bool _disposed;

    public event Action<string>? OnFileAutoIndexed;

    public FileWatcherService(IndexingEngine indexEngine)
    {
        _indexEngine = indexEngine;

        // 防抖定时器（500ms 内合并同一文件的多次触发，如 CAD 保存时的多次写操作）
        _debounceTimer = new System.Timers.Timer(500);
        _debounceTimer.AutoReset = false;
        _debounceTimer.Elapsed += (s, e) => ProcessPendingChanges();
    }

    /// <summary>
    /// 根据配置重新初始化监听器
    /// </summary>
    public void ReloadWatchers(AppConfig config)
    {
        StopWatchers();

        if (!config.AutoSyncOnChange)
            return;

        foreach (var folder in config.Folders)
        {
            if (!folder.Enabled || !Directory.Exists(folder.Path))
                continue;

            try
            {
                var watcher = new FileSystemWatcher(folder.Path)
                {
                    IncludeSubdirectories = folder.IncludeSubdirectories,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };

                watcher.Filters.Add("*.dwg");
                watcher.Filters.Add("*.dxf");

                watcher.Created += OnFileEvent;
                watcher.Changed += OnFileEvent;
                watcher.Renamed += (s, e) =>
                {
                    EnqueueChange(e.OldFullPath);
                    EnqueueChange(e.FullPath);
                };
                watcher.Deleted += (s, e) => EnqueueChange(e.FullPath);

                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[FileWatcherService] 监听文件夹失败 {folder.Path}: {ex.Message}");
            }
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        EnqueueChange(e.FullPath);
    }

    private void EnqueueChange(string fullPath)
    {
        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (ext != ".dwg" && ext != ".dxf")
            return;

        lock (_lock)
        {
            _changedFiles.Add(fullPath);
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private void ProcessPendingChanges()
    {
        List<string> filesToProcess;
        lock (_lock)
        {
            filesToProcess = _changedFiles.ToList();
            _changedFiles.Clear();
        }

        foreach (var file in filesToProcess)
        {
            try
            {
                if (File.Exists(file))
                {
                    // 增量索引单个文件
                    _indexEngine.IndexSingleFile(file);
                }
                else
                {
                    // 文件已删除，清理索引
                    _indexEngine.RemoveFile(file);
                }

                OnFileAutoIndexed?.Invoke(file);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[FileWatcherService] 自动更新索引异常 {file}: {ex.Message}");
            }
        }
    }

    private void StopWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _watchers.Clear();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _debounceTimer.Dispose();
            StopWatchers();
            GC.SuppressFinalize(this);
        }
    }
}
