using System.IO;
using System.Text;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using ACadSharp.Blocks;
using File = System.IO.File;
using Path = System.IO.Path;

namespace DwgSearcher.TextExtraction;

/// <summary>
/// 专业的 DWG / DXF CAD 图纸全要素深度文本提取器
/// 深度提取：
/// 1. 单行文字 (TEXT) 与多行文字 (MTEXT)
/// 2. 标题栏与图块属性值 (INSERT -> ATTRIB Tag/Value)
/// 3. 属性定义 (ATTDEF)
/// 4. 尺寸标注 (DIMENSION 覆盖文字/测量文本) 与形位公差 (TOLERANCE)
/// 5. CAD 表格 (TableEntity 单元格文字与数据)
/// 6. 机械明细表与多重引线 (MultiLeader 标注与引线属性)
/// 7. 外部参照与引用链接 (XREF 块名与路径)
/// 8. 图纸摘要与自定义属性 (SummaryInfo / Custom Properties)
/// 9. 引线文字 (Leader 关联文本)
/// </summary>
public class DwgTextExtractor : ITextExtractor
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".dwg", ".dxf"
    };

    public bool CanHandle(string extension) => SupportedExtensions.Contains(extension);

    public string ExtractText(string filePath)
    {
        if (!File.Exists(filePath))
            return string.Empty;

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        var sb = new StringBuilder();

        try
        {
            CadDocument? doc = null;

            if (ext == ".dwg")
            {
                using var reader = new DwgReader(filePath);
                reader.OnNotification += (s, e) => { /* 忽略格式不兼容或丢失图元警告 */ };
                doc = reader.Read();
            }
            else if (ext == ".dxf")
            {
                using var reader = new DxfReader(filePath);
                reader.OnNotification += (s, e) => { };
                doc = reader.Read();
            }

            if (doc != null)
            {
                ExtractFromCadDocument(doc, sb);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DwgTextExtractor] 解析文件 {Path.GetFileName(filePath)}: {ex.Message}");
            if (ext == ".dxf")
            {
                ExtractFromDxfFallback(filePath, sb);
            }
        }

        // 统一使用 CadTextCleaner 过滤 MText 控制字符
        return CadTextCleaner.Clean(sb.ToString());
    }

    private static void ExtractFromCadDocument(CadDocument doc, StringBuilder sb)
    {
        // 1. 文档摘要信息 (SummaryInfo)
        if (doc.SummaryInfo != null)
        {
            if (!string.IsNullOrWhiteSpace(doc.SummaryInfo.Title)) sb.AppendLine($"[标题] {doc.SummaryInfo.Title}");
            if (!string.IsNullOrWhiteSpace(doc.SummaryInfo.Subject)) sb.AppendLine($"[主题] {doc.SummaryInfo.Subject}");
            if (!string.IsNullOrWhiteSpace(doc.SummaryInfo.Author)) sb.AppendLine($"[作者] {doc.SummaryInfo.Author}");
            if (!string.IsNullOrWhiteSpace(doc.SummaryInfo.Comments)) sb.AppendLine($"[备注] {doc.SummaryInfo.Comments}");
            if (!string.IsNullOrWhiteSpace(doc.SummaryInfo.Keywords)) sb.AppendLine($"[关键词] {doc.SummaryInfo.Keywords}");

            if (doc.SummaryInfo.Properties != null)
            {
                foreach (var prop in doc.SummaryInfo.Properties)
                {
                    if (!string.IsNullOrWhiteSpace(prop.Key) || !string.IsNullOrWhiteSpace(prop.Value))
                    {
                        sb.AppendLine($"[自定义属性] {prop.Key}: {prop.Value}");
                    }
                }
            }
        }

        // 2. 外部参照 (XREF) 路径
        foreach (var blockRecord in doc.BlockRecords)
        {
            if (blockRecord.BlockEntity != null && !string.IsNullOrWhiteSpace(blockRecord.BlockEntity.XRefPath))
            {
                sb.AppendLine($"[外部参照XREF] 块名: {blockRecord.Name}, 路径: {blockRecord.BlockEntity.XRefPath}");
            }
        }

        // 3. 遍历所有块定义、模型空间和布局中的图元
        var visitedEntities = new HashSet<Entity>();

        foreach (var blockRecord in doc.BlockRecords)
        {
            foreach (var entity in blockRecord.Entities)
            {
                ExtractEntityText(entity, sb, visitedEntities);
            }
        }
    }

    private static void ExtractEntityText(Entity entity, StringBuilder sb, HashSet<Entity> visited)
    {
        if (entity == null || !visited.Add(entity))
            return;

        // 单行文字 (TEXT)
        if (entity is TextEntity text)
        {
            if (!string.IsNullOrWhiteSpace(text.Value))
                sb.AppendLine(text.Value);
        }
        // 多行文字 (MTEXT)
        else if (entity is MText mtext)
        {
            if (!string.IsNullOrWhiteSpace(mtext.Value))
                sb.AppendLine(mtext.Value);
        }
        // 图块参照 (INSERT: 标题栏/属性块/装配图元)
        else if (entity is Insert insert)
        {
            if (insert.Block != null && !string.IsNullOrWhiteSpace(insert.Block.Name))
            {
                sb.AppendLine($"[图块] {insert.Block.Name}");
            }

            foreach (var attr in insert.Attributes)
            {
                if (!string.IsNullOrWhiteSpace(attr.Value))
                {
                    sb.AppendLine($"[属性: {attr.Tag}] {attr.Value}");
                }
            }
        }
        // 属性定义 (ATTDEF)
        else if (entity is AttributeDefinition attdef)
        {
            if (!string.IsNullOrWhiteSpace(attdef.Tag))
                sb.Append($"[属性定义: {attdef.Tag}] ");
            if (!string.IsNullOrWhiteSpace(attdef.Prompt))
                sb.Append($"提示: {attdef.Prompt} ");
            if (!string.IsNullOrWhiteSpace(attdef.Value))
                sb.Append($"默认值: {attdef.Value}");
            sb.AppendLine();
        }
        // 尺寸与线性标注 (DIMENSION)
        else if (entity is Dimension dim)
        {
            if (!string.IsNullOrWhiteSpace(dim.Text))
            {
                sb.AppendLine($"[标注] {dim.Text}");
            }
        }
        // CAD 表格 (TableEntity: 物料明细表、图元表格)
        else if (entity is TableEntity table)
        {
            sb.AppendLine("[表格数据]");
            foreach (var row in table.Rows)
            {
                foreach (var cell in row.Cells)
                {
                    if (cell.Contents != null)
                    {
                        foreach (var content in cell.Contents)
                        {
                            if (content.CadValue != null)
                            {
                                var valStr = content.CadValue.ToString();
                                if (!string.IsNullOrWhiteSpace(valStr))
                                {
                                    sb.Append(valStr + " ");
                                }
                            }
                        }
                    }
                }
                sb.AppendLine();
            }
        }
        // 普通引线 (Leader)
        else if (entity is Leader leader)
        {
            if (leader.AssociatedAnnotation is TextEntity lText && !string.IsNullOrWhiteSpace(lText.Value))
            {
                sb.AppendLine($"[引线文字] {lText.Value}");
            }
            else if (leader.AssociatedAnnotation is MText lMText && !string.IsNullOrWhiteSpace(lMText.Value))
            {
                sb.AppendLine($"[引线文字] {lMText.Value}");
            }
        }
        // 多重引线 (MultiLeader)
        else if (entity is MultiLeader mLeader)
        {
            if (mLeader.BlockAttributes != null)
            {
                foreach (var attr in mLeader.BlockAttributes)
                {
                    if (!string.IsNullOrWhiteSpace(attr.Text))
                    {
                        sb.AppendLine($"[多重引线属性: {attr.AttributeDefinition?.Tag}] {attr.Text}");
                    }
                }
            }
        }
        // 形位公差 (Tolerance)
        else if (entity is Tolerance tol)
        {
            if (!string.IsNullOrWhiteSpace(tol.Text))
            {
                sb.AppendLine($"[公差] {tol.Text}");
            }
        }
    }

    private static void ExtractFromDxfFallback(string filePath, StringBuilder sb)
    {
        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        string? line;
        bool nextIsText = false;

        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line == "1" || line == "3" || line == "2")
            {
                nextIsText = true;
                continue;
            }
            if (nextIsText)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    sb.AppendLine(line);
                }
                nextIsText = false;
            }
        }
    }
}
