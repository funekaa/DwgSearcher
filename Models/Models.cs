namespace DwgSearcher.Models;

/// <summary>
/// 文件记录实体，用于比对文件变动以实现增量更新
/// </summary>
public record FileRecord(
    int FileId,
    string FilePath,
    long LastModified,
    long FileSize
);

/// <summary>
/// 全文检索返回结果实体
/// </summary>
public record SearchResult(
    string FilePath,
    string Title,
    string Snippet,
    double Rank
);
