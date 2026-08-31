using System.IO;
using System.Windows.Media.Imaging;
using DwgSearcher.Models;
using DwgSearcher.Services;

namespace DwgSearcher.ViewModels;

public class SearchResultItem
{
    public string Title { get; }
    public string FilePath { get; }
    public string FileSizeText { get; }
    public string LastModifiedText { get; }
    public string Snippet { get; }
    public double Rank { get; }

    /// <summary>
    /// Windows 系统关联识别的原生 DWG/DXF 文件图标
    /// </summary>
    public BitmapSource? FileIcon { get; }

    private BitmapSource? _thumbnail;
    private bool _thumbnailLoaded;

    public SearchResultItem(SearchResult model)
    {
        Title = model.Title;
        FilePath = model.FilePath;
        Snippet = model.Snippet.Replace("<b>", "").Replace("</b>", "").Trim();
        Rank = model.Rank;

        // 提取系统关联的文件图标
        FileIcon = ShellThumbnailHelper.GetSystemFileIcon(model.FilePath);

        try
        {
            if (File.Exists(model.FilePath))
            {
                var fi = new FileInfo(model.FilePath);
                FileSizeText = FormatFileSize(fi.Length);
                LastModifiedText = fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
            else
            {
                FileSizeText = "未知";
                LastModifiedText = "-";
            }
        }
        catch
        {
            FileSizeText = "未知";
            LastModifiedText = "-";
        }
    }

    /// <summary>
    /// 获取 Windows Explorer (资源管理器) 识别的高清图纸缩略图
    /// </summary>
    public BitmapSource? GetThumbnail()
    {
        if (!_thumbnailLoaded)
        {
            _thumbnailLoaded = true;
            _thumbnail = ShellThumbnailHelper.GetExplorerThumbnail(FilePath, 512);
        }
        return _thumbnail;
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}
