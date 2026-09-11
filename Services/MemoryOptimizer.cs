using System;
using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace DwgSearcher.Services;

/// <summary>
/// 高性能内存收缩与工作集智能优化器
/// 负责在空闲期与批次解析后主动清理未用内存，降低常驻内存到极佳水平
/// </summary>
public static class MemoryOptimizer
{
    [DllImport("psapi.dll")]
    private static extern int EmptyWorkingSet(IntPtr hwProc);

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

    /// <summary>
    /// 执行深度垃圾回收并收缩操作系统物理工作集
    /// </summary>
    public static void TrimMemory()
    {
        try
        {
            // 1. 深度压缩大对象堆 (LOH) 与 Gen0/1/2 垃圾回收
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);

            // 2. 将空闲物理内存页归还操作系统工作集
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                using var proc = Process.GetCurrentProcess();
                EmptyWorkingSet(proc.Handle);
                SetProcessWorkingSetSize(proc.Handle, new IntPtr(-1), new IntPtr(-1));
            }
        }
        catch
        {
            // 忽略非关键修剪异常
        }
    }
}
