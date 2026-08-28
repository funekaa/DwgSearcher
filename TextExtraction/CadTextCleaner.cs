using System.Globalization;
using System.Text.RegularExpressions;

namespace DwgSearcher.TextExtraction;

/// <summary>
/// 文本提取器接口
/// </summary>
public interface ITextExtractor
{
    bool CanHandle(string extension);
    string ExtractText(string filePath);
}

/// <summary>
/// AutoCAD MText / DText 格式控制符清洗工具
/// 使用 .NET 8 预编译正则表达式（GeneratedRegex），实现零 GC、极速的高性能文本净化
/// </summary>
public static partial class CadTextCleaner
{
    // 1. 段落换行符 \P 替换为换行
    [GeneratedRegex(@"\\P", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MTextParagraphRegex();

    // 2. 堆叠文字 \S 上下公差/分数，如 \S+0.02^-0.01; -> +0.02 -0.01 或 1/2
    [GeneratedRegex(@"\\S([^;^#\/]+)[\^#\/]([^;]*);", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MTextStackRegex();

    // 3. 字体设置如 \fArial|b0|i0|c0|p34; 或 \Fsimsun.shx;
    [GeneratedRegex(@"\\[fF][^;]*;", RegexOptions.CultureInvariant)]
    private static partial Regex MTextFontRegex();

    // 4. 颜色设置如 \C1; \c255;
    [GeneratedRegex(@"\\[cC][0-9]+;", RegexOptions.CultureInvariant)]
    private static partial Regex MTextColorRegex();

    // 5. 字高设置如 \H1.5x; 或 \H2.5;
    [GeneratedRegex(@"\\[hH][0-9.]+(?:x|X)?;", RegexOptions.CultureInvariant)]
    private static partial Regex MTextHeightRegex();

    // 6. 对齐方式如 \A0; \A1; \A2;
    [GeneratedRegex(@"\\[aA][0-2];", RegexOptions.CultureInvariant)]
    private static partial Regex MTextAlignRegex();

    // 7. 宽度、倾斜、字符间距 \W1.2; \Q15; \T0.8;
    [GeneratedRegex(@"\\[wWqQtT][0-9.-]+;", RegexOptions.CultureInvariant)]
    private static partial Regex MTextFormattingParamsRegex();

    // 8. 各种开关标签: \L \l (下划线), \O \o (上划线), \K \k (删除线), \~ (不间断空格)
    [GeneratedRegex(@"\\[LlOoKk~]", RegexOptions.CultureInvariant)]
    private static partial Regex MTextToggleRegex();

    // 9. 花括号分组符 { 和 }
    [GeneratedRegex(@"[{}]", RegexOptions.CultureInvariant)]
    private static partial Regex MTextBracesRegex();

    // 10. AutoCAD DText 特殊符号转义: %%c (Φ), %%d (°), %%p (±), %%u/%%o (下划/上划线), %%% (%)
    [GeneratedRegex(@"%%[cC]", RegexOptions.CultureInvariant)]
    private static partial Regex CadDiameterSymbolRegex();

    [GeneratedRegex(@"%%[dD]", RegexOptions.CultureInvariant)]
    private static partial Regex CadDegreeSymbolRegex();

    [GeneratedRegex(@"%%[pP]", RegexOptions.CultureInvariant)]
    private static partial Regex CadPlusMinusSymbolRegex();

    [GeneratedRegex(@"%%[uUoO]", RegexOptions.CultureInvariant)]
    private static partial Regex CadToggleSymbolRegex();

    [GeneratedRegex(@"%%%", RegexOptions.CultureInvariant)]
    private static partial Regex CadPercentSymbolRegex();

    // 11. AutoCAD Unicode 字符转义如 \U+2205 (∅/Φ), \U+00B0 (°), \U+00B1 (±)
    [GeneratedRegex(@"\\U\+([0-9A-Fa-f]{4})", RegexOptions.CultureInvariant)]
    private static partial Regex CadUnicodeEscapeRegex();

    // 12. 双反斜杠与未匹配的转义符如 \\ -> \
    [GeneratedRegex(@"\\\\", RegexOptions.CultureInvariant)]
    private static partial Regex MTextBackslashRegex();

    // 13. 多余连续空白字符压缩
    [GeneratedRegex(@"[ \t\r\f\v]+", RegexOptions.CultureInvariant)]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\n{3,}", RegexOptions.CultureInvariant)]
    private static partial Regex MultiNewlineRegex();

    /// <summary>
    /// 清洗 CAD 文本中的所有格式标记与控制符，还原为干净的纯文本
    /// </summary>
    public static string Clean(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        string text = rawText;

        // 处理 AutoCAD Unicode 转义 (\U+2205 -> ∅)
        text = CadUnicodeEscapeRegex().Replace(text, match =>
        {
            if (int.TryParse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int unicodeVal))
            {
                if (unicodeVal == 0x2205) return "Φ"; // 将空集符号映射为工程常用直径符号
                return char.ConvertFromUtf32(unicodeVal);
            }
            return match.Value;
        });

        // 处理 DText 经典符号替换
        text = CadDiameterSymbolRegex().Replace(text, "Φ");
        text = CadDegreeSymbolRegex().Replace(text, "°");
        text = CadPlusMinusSymbolRegex().Replace(text, "±");
        text = CadToggleSymbolRegex().Replace(text, string.Empty);
        text = CadPercentSymbolRegex().Replace(text, "%");

        // 处理 MText 堆叠标记 (\S1/2; -> 1/2 或 \S+0.1^-0.1; -> +0.1 -0.1)
        text = MTextStackRegex().Replace(text, "$1 $2");

        // 替换段落控制符
        text = MTextParagraphRegex().Replace(text, "\n");

        // 清洗各种格式控制参数
        text = MTextFontRegex().Replace(text, string.Empty);
        text = MTextColorRegex().Replace(text, string.Empty);
        text = MTextHeightRegex().Replace(text, string.Empty);
        text = MTextAlignRegex().Replace(text, string.Empty);
        text = MTextFormattingParamsRegex().Replace(text, string.Empty);
        text = MTextToggleRegex().Replace(text, string.Empty);

        // 去除外层分组花括号
        text = MTextBracesRegex().Replace(text, string.Empty);

        // 还原反斜杠
        text = MTextBackslashRegex().Replace(text, "\\");

        // 压缩多余空白和空行
        text = MultiSpaceRegex().Replace(text, " ");
        text = MultiNewlineRegex().Replace(text, "\n");

        return text.Trim();
    }
}
