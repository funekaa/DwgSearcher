using System.Text;
using Microsoft.Data.Sqlite;
using DwgSearcher.Models;
using DwgSearcher.Storage;

namespace DwgSearcher.Engine;

/// <summary>
/// 智能混合全文检索器
/// 支持：
/// 1. 长词 (>= 3 字符): SQLite FTS5 Trigram 倒排索引极速 MATCH (毫秒级)
/// 2. 短词 (< 3 字符，如 "fg", "12", "安装"): 高性能 LIKE 模糊匹配 + 动态上下文高亮切片 (解决 Trigram 短词限制)
/// </summary>
public class SearchEngine : IDisposable
{
    private readonly DatabaseManager _dbManager;
    private bool _disposed;

    public SearchEngine(DatabaseManager dbManager)
    {
        _dbManager = dbManager;
    }

    /// <summary>
    /// 全文检索核心入口（自动根据关键词长度自适应选择 FTS5 MATCH 或短词模糊匹配）
    /// </summary>
    public List<SearchResult> Search(string keyword, int limit = 50)
    {
        var results = new List<SearchResult>();

        if (string.IsNullOrWhiteSpace(keyword))
            return results;

        var tokens = keyword
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => !string.IsNullOrEmpty(t))
            .ToList();

        if (tokens.Count == 0)
            return results;

        // 判断是否所有 token 长度都 >= 3（Trigram 的有效最小索引切片长度为 3）
        bool canUseFts5 = tokens.All(t => t.Length >= 3);

        if (canUseFts5)
        {
            try
            {
                results = SearchViaFts5(tokens, limit);
                if (results.Count > 0)
                    return results;
            }
            catch
            {
                // 若 FTS5 解析异常，自动降级为 LIKE 检索
            }
        }

        // 短词或 FTS5 降级：使用内存映射 SQLite 执行极速 LIKE 模糊匹配
        return SearchViaLike(tokens, keyword, limit);
    }

    /// <summary>
    /// 1. 长词走 SQLite FTS5 原生 Trigram 倒排索引
    /// </summary>
    private List<SearchResult> SearchViaFts5(List<string> tokens, int limit)
    {
        var results = new List<SearchResult>();
        using var connection = _dbManager.CreateConnection();
        using var cmd = connection.CreateCommand();

        var escapedTerms = tokens.Select(token =>
        {
            var escaped = token.Replace("\"", "\"\"");
            return $"\"{escaped}\"";
        });
        string matchQuery = string.Join(" AND ", escapedTerms);

        cmd.CommandText = @"
            SELECT 
                FilePath, 
                Title, 
                snippet(DocIndex, 2, '<b>', '</b>', '...', 20) AS Snippet,
                rank
            FROM DocIndex 
            WHERE DocIndex MATCH @query 
            ORDER BY rank 
            LIMIT @limit;
        ";

        cmd.Parameters.AddWithValue("@query", matchQuery);
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var filePath = reader.GetString(0);
            var title = reader.GetString(1);
            var snippet = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var rank = reader.IsDBNull(3) ? 0.0 : reader.GetDouble(3);

            results.Add(new SearchResult(filePath, title, snippet, rank));
        }

        return results;
    }

    /// <summary>
    /// 2. 短词走 SQL LIKE 模糊匹配并自动生成 Snippet 高亮片段
    /// </summary>
    private List<SearchResult> SearchViaLike(List<string> tokens, string originalKeyword, int limit)
    {
        var results = new List<SearchResult>();
        using var connection = _dbManager.CreateConnection();
        using var cmd = connection.CreateCommand();

        var whereClauses = new List<string>();
        for (int i = 0; i < tokens.Count; i++)
        {
            string paramName = $"@p{i}";
            whereClauses.Add($"(Title LIKE {paramName} OR Content LIKE {paramName} OR FilePath LIKE {paramName})");
            cmd.Parameters.AddWithValue(paramName, $"%{tokens[i]}%");
        }

        cmd.CommandText = $@"
            SELECT FilePath, Title, Content
            FROM DocIndex
            WHERE {string.Join(" AND ", whereClauses)}
            LIMIT @limit;
        ";
        cmd.Parameters.AddWithValue("@limit", limit);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var filePath = reader.GetString(0);
            var title = reader.GetString(1);
            var content = reader.GetString(2);

            // 手动截取命中位置生成高亮 Snippet
            string snippet = GenerateSnippet(content, title, tokens);
            results.Add(new SearchResult(filePath, title, snippet, 0.0));
        }

        return results;
    }

    /// <summary>
    /// 获取当前已索引的全部图纸清单与文本长度统计
    /// </summary>
    public List<(string FilePath, string Title, int TextLength)> GetAllIndexedDocs()
    {
        var list = new List<(string FilePath, string Title, int TextLength)>();
        using var connection = _dbManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT FilePath, Title, length(Content) FROM DocIndex ORDER BY Title;";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        }
        return list;
    }

    /// <summary>
    /// 获取指定图纸提取出的完整文本内容
    /// </summary>
    public string? GetDocContent(string titleOrPath)
    {
        using var connection = _dbManager.CreateConnection();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Content FROM DocIndex WHERE Title = @key OR FilePath = @key LIMIT 1;";
        cmd.Parameters.AddWithValue("@key", titleOrPath);

        var result = cmd.ExecuteScalar();
        return result?.ToString();
    }

    /// <summary>
    /// 手动生成命中高亮切片
    /// </summary>
    private static string GenerateSnippet(string content, string title, List<string> tokens)
    {
        if (string.IsNullOrEmpty(content))
            return title;

        // 查找第一个命中的 token 位置
        int matchIndex = -1;
        string matchedToken = tokens[0];

        foreach (var token in tokens)
        {
            int idx = content.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                matchIndex = idx;
                matchedToken = token;
                break;
            }
        }

        if (matchIndex < 0)
        {
            // 若内容未命中，可能命中在文件名中
            return $"【{title}】";
        }

        int start = Math.Max(0, matchIndex - 30);
        int length = Math.Min(content.Length - start, matchedToken.Length + 60);

        string snippetText = content.Substring(start, length);
        
        // 高亮命中的词
        foreach (var token in tokens)
        {
            int tIndex = 0;
            while ((tIndex = snippetText.IndexOf(token, tIndex, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                string originalToken = snippetText.Substring(tIndex, token.Length);
                snippetText = snippetText.Remove(tIndex, token.Length).Insert(tIndex, $"<b>{originalToken}</b>");
                tIndex += token.Length + 7; // 跳过 <b></b>
                if (tIndex >= snippetText.Length) break;
            }
        }

        return (start > 0 ? "..." : "") + snippetText.Replace("\r", " ").Replace("\n", " ") + (start + length < content.Length ? "..." : "");
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
