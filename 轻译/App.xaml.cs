using System.Threading;
using System.Windows;
using QingYi.Models;
using QingYi.Services;

namespace QingYi;

public partial class App : Application
{
    private Mutex? _instanceMutex;
    private bool _ownsMutex;
    private MainWindow? _mainWindow;

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        _instanceMutex = new Mutex(true, @"Local\QingYi.SelectionTranslator", out var created);
        _ownsMutex = created;
        if (!created)
        {
            var existing = NativeMethods.FindWindow(null, "轻译");
            if (existing != IntPtr.Zero)
            {
                NativeMethods.ShowWindow(existing, 9);
                NativeMethods.SetForegroundWindow(existing);
            }
            Shutdown();
            return;
        }

        var settings = SettingsStore.Load();
        _mainWindow = new MainWindow(settings);
        MainWindow = _mainWindow;
        _mainWindow.Show();
        var hasKey = SettingsStore.ReadApiKey(settings).Length > 0;
        var backgroundFlag = e.Args.Any(x => x.Equals("--background", StringComparison.OrdinalIgnoreCase));
        var forceVisible = e.Args.Any(x => x.Equals("--show", StringComparison.OrdinalIgnoreCase));
        if (!forceVisible && settings.StartMinimized && hasKey && (backgroundFlag || e.Args.Length == 0)) _mainWindow.HideToTray();
        else _mainWindow.Activate();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        _mainWindow?.Dispose();
        if (_ownsMutex) _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
    }
}
