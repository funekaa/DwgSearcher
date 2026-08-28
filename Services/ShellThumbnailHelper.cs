using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace DwgSearcher.Services;

/// <summary>
/// Windows Shell 原生图标与资源管理器 (Explorer) 高清缩略图提取器
/// </summary>
public static class ShellThumbnailHelper
{
    #region Win32 Shell API 定义

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;
    private const uint SHGFI_LARGEICON = 0x000000000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    // IShellItemImageFactory GUID
    private static readonly Guid IShellItemImageFactoryGuid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(
            [In, MarshalAs(UnmanagedType.Struct)] SIZE size,
            [In] SIIGBF flags,
            [Out] out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE
    {
        public int cx;
        public int cy;
        public SIZE(int cx, int cy)
        {
            this.cx = cx;
            this.cy = cy;
        }
    }

    [Flags]
    private enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00000000,
        SIIGBF_BIGGERSIZEOK = 0x00000001,
        SIIGBF_MEMORYONLY = 0x00000002,
        SIIGBF_ICONONLY = 0x00000004,
        SIIGBF_THUMBNAILONLY = 0x00000008,
        SIIGBF_INCACHEONLY = 0x00000010
    }

    #endregion

    private static readonly Dictionary<string, BitmapSource> IconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 获取 Windows 系统关联的 DWG/DXF 文件图标 (小图标 32x32)
    /// </summary>
    public static BitmapSource? GetSystemFileIcon(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (IconCache.TryGetValue(ext, out var cached))
            return cached;

        try
        {
            var shinfo = new SHFILEINFO();
            // 使用 SHGFI_USEFILEATTRIBUTES 无需实际访问磁盘文件，极速获取系统扩展名关联的真实图标
            IntPtr hImg = SHGetFileInfo(
                filePath,
                FILE_ATTRIBUTE_NORMAL,
                ref shinfo,
                (uint)Marshal.SizeOf(shinfo),
                SHGFI_ICON | SHGFI_LARGEICON | SHGFI_USEFILEATTRIBUTES);

            if (shinfo.hIcon != IntPtr.Zero)
            {
                var iconSource = Imaging.CreateBitmapSourceFromHIcon(
                    shinfo.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                iconSource.Freeze();
                DestroyIcon(shinfo.hIcon);

                IconCache[ext] = iconSource;
                return iconSource;
            }
        }
        catch
        {
            // 忽略异常
        }

        return null;
    }

    /// <summary>
    /// 获取 Windows Explorer (资源管理器) 识别到的 CAD 图纸原生缩略图 (最高支持 512x512 高清)
    /// </summary>
    public static BitmapSource? GetExplorerThumbnail(string filePath, int size = 512)
    {
        if (!File.Exists(filePath))
            return null;

        // 1. 优先调用 Windows Explorer Shell 接口 (IShellItemImageFactory)
        try
        {
            var guid = IShellItemImageFactoryGuid;
            int hr = SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref guid, out var factory);
            if (hr == 0 && factory != null)
            {
                IntPtr hBitmap = IntPtr.Zero;
                // SIIGBF_BIGGERSIZEOK | SIIGBF_RESIZETOFIT
                hr = factory.GetImage(new SIZE(size, size), SIIGBF.SIIGBF_BIGGERSIZEOK, out hBitmap);
                if (hr == 0 && hBitmap != IntPtr.Zero)
                {
                    try
                    {
                        var bmpSource = Imaging.CreateBitmapSourceFromHBitmap(
                            hBitmap,
                            IntPtr.Zero,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());

                        bmpSource.Freeze();
                        return bmpSource;
                    }
                    finally
                    {
                        DeleteObject(hBitmap);
                        Marshal.ReleaseComObject(factory);
                    }
                }
            }
        }
        catch
        {
            // Shell 缩略图获取失败时平滑降级
        }

        // 2. 降级回退：从 DWG 二进制文件中提取嵌入的位图
        return DwgPreviewExtractor.ExtractThumbnail(filePath);
    }
}
