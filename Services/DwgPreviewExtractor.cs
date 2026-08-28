using System.IO;
using System.Windows.Media.Imaging;

namespace DwgSearcher.Services;

/// <summary>
/// DWG / DXF 图纸缩略图与预览图像提取器
/// 通过解析 DWG 二进制文件头部的位图（BMP/WMF/PNG）元数据，实现零依赖秒级提取缩略图
/// </summary>
public static class DwgPreviewExtractor
{
    /// <summary>
    /// 从 DWG 文件中提取嵌入的高清位图缩略图
    /// </summary>
    public static BitmapSource? ExtractThumbnail(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext != ".dwg")
            return null;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);

            // DWG 文件魔数通常以 AC10xx 开头 (6 字节)
            byte[] header = br.ReadBytes(6);
            string version = System.Text.Encoding.ASCII.GetString(header);

            if (!version.StartsWith("AC10"))
                return null;

            // 搜索 DWG 头部区域的位图段 (DWG 通常在前 128KB 包含位图预览)
            fs.Seek(0, SeekOrigin.Begin);
            byte[] buffer = new byte[Math.Min(fs.Length, 128 * 1024)];
            int bytesRead = fs.Read(buffer, 0, buffer.Length);

            // 寻找 BMP 文件头特征: 'B' (0x42), 'M' (0x4D) 以及位图信息头
            for (int i = 0; i < bytesRead - 54; i++)
            {
                if (buffer[i] == 0x42 && buffer[i + 1] == 0x4D) // 'BM'
                {
                    int bmpSize = BitConverter.ToInt32(buffer, i + 2);
                    int reserved = BitConverter.ToInt32(buffer, i + 6);
                    int offset = BitConverter.ToInt32(buffer, i + 10);
                    int dibHeaderSize = BitConverter.ToInt32(buffer, i + 14);

                    // 校验是否为合法有效的 BMP 头
                    if (bmpSize > 54 && bmpSize <= 10 * 1024 * 1024 &&
                        offset >= 54 && offset < bmpSize &&
                        (dibHeaderSize == 40 || dibHeaderSize == 108 || dibHeaderSize == 124))
                    {
                        int width = BitConverter.ToInt32(buffer, i + 18);
                        int height = BitConverter.ToInt32(buffer, i + 22);

                        if (width > 0 && width <= 4096 && Math.Abs(height) > 0 && Math.Abs(height) <= 4096)
                        {
                            // 找到了嵌入的完整 BMP 位图
                            byte[] bmpData;
                            if (i + bmpSize <= bytesRead)
                            {
                                bmpData = new byte[bmpSize];
                                Array.Copy(buffer, i, bmpData, 0, bmpSize);
                            }
                            else
                            {
                                // 若 BMP 数据超出 128KB 缓冲区，重新从文件精准读取
                                fs.Seek(i, SeekOrigin.Begin);
                                bmpData = br.ReadBytes(bmpSize);
                            }

                            using var ms = new MemoryStream(bmpData);
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = ms;
                            bitmap.EndInit();
                            bitmap.Freeze(); // 允许跨线程访问
                            return bitmap;
                        }
                    }
                }
            }
        }
        catch
        {
            // 忽略损坏或加密 DWG 的位图提取失败，使用默认预览
        }

        return null;
    }
}
