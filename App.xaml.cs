using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace DwgSearcher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1. 捕获 WPF UI 线程未捕获异常，防止崩溃
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        // 2. 捕获非 UI 线程未捕获异常
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

        // 3. 捕获 Task 未观察到的异常
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
    }

    private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogException("DispatcherUnhandledException", e.Exception);
        e.Handled = true; // 阻止崩溃闪退
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogException("UnhandledException", ex);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        LogException("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private static void LogException(string type, Exception ex)
    {
        try
        {
            string logPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DwgSearcher",
                "error.log"
            );
            string message = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{type}] {ex.Message}\n{ex.StackTrace}\n\n";
            File.AppendAllText(logPath, message);
        }
        catch { }
    }
}
