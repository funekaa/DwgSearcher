using Microsoft.Data.Sqlite;

namespace DwgSearcher.Storage;

/// <summary>
/// SQLite 数据库管理器，负责初始化与底层连接生命周期
/// 采用 SQLite FTS5 + WAL 模式保证高并发与毫秒级检索性能
/// </summary>
public class DatabaseManager : IDisposable
{
    private readonly string _connectionString;
    private bool _disposed;

    public DatabaseManager(string dbPath)
    {
        // 构建连接字符串：开启连接池、共享缓存
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        };

        _connectionString = builder.ToString();
        InitializeDatabase();
    }

    /// <summary>
    /// 获取打开的 SQLite 数据库连接
    /// </summary>
    public SqliteConnection CreateConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // 为每个新连接配置关键 PRAGMA
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            PRAGMA busy_timeout = 5000;
            PRAGMA temp_store = MEMORY;
            PRAGMA mmap_size = 268435456; -- 256MB 内存映射加速
        ";
        cmd.ExecuteNonQuery();

        return connection;
    }

    /// <summary>
    /// 初始化数据表与 FTS5 虚拟表结构
    /// </summary>
    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // 1. 设置 WAL 模式与 NORMAL 同步级别
        // WAL 模式允许多个读操作与单个写操作并发进行，不锁库
        using (var pragmaCmd = connection.CreateCommand())
        {
            pragmaCmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
            ";
            pragmaCmd.ExecuteNonQuery();
        }

        // 2. 创建元数据记录表与 FTS5 全文检索虚拟表
        using (var createCmd = connection.CreateCommand())
        {
            createCmd.CommandText = @"
                -- 文件元数据记录表，用于增量检测 (LastModified 存 Ticks)
                CREATE TABLE IF NOT EXISTS FileRecords (
                    FileId INTEGER PRIMARY KEY AUTOINCREMENT,
                    FilePath TEXT NOT NULL UNIQUE,
                    LastModified INTEGER NOT NULL,
                    FileSize INTEGER NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_filerecords_path ON FileRecords(FilePath);

                -- SQLite FTS5 全文倒排索引虚拟表
                -- tokenize='trigram'：将所有中英文字符、图号、零件编号以 3 字符滑动窗口切分
                -- 能够完美支持中文子串无分词歧义检索、英文与连字符编号模糊子串匹配
                CREATE VIRTUAL TABLE IF NOT EXISTS DocIndex USING fts5(
                    FilePath UNINDEXED, 
                    Title, 
                    Content, 
                    tokenize = 'trigram'
                );
            ";
            createCmd.ExecuteNonQuery();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            // 清理连接池
            SqliteConnection.ClearAllPools();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
