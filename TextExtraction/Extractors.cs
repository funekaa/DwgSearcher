using System.IO;
using File = System.IO.File;
using Path = System.IO.Path;

namespace DwgSearcher.TextExtraction;

/// <summary>
/// 文本提取器注册表与调度器（专用于 CAD 图纸文件 .dwg / .dxf）
/// </summary>
public class ExtractorRegistry
{
    private readonly List<ITextExtractor> _extractors = new();

    public ExtractorRegistry()
    {
        // 核心专精注册：DWG 与 DXF 图纸全要素解析器
        Register(new DwgTextExtractor());
    }

    public void Register(ITextExtractor extractor)
    {
        _extractors.Insert(0, extractor);
    }

    public ITextExtractor? GetExtractor(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (string.IsNullOrEmpty(ext)) return null;

        return _extractors.FirstOrDefault(e => e.CanHandle(ext));
    }

    /// <summary>
    /// 只对支持的 CAD 图纸扩展名 (.dwg, .dxf) 返回 true
    /// </summary>
    public bool IsSupported(string filePath)
    {
        return GetExtractor(filePath) != null;
    }
}
