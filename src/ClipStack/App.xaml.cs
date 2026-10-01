using System.Windows;
using ClipStack.Core;
using ClipStack.Services;

namespace ClipStack;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\ClipStack.SingleInstance.7f3c";
    private const string ActivateEventName = @"Local\ClipStack.Activate.7f3c";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _activateWait;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Developer utility: regenerate the .ico used for the executable.
        if (e.Args is ["--export-icon", var iconPath])
        {
            IconFactory.ExportAppIcon(iconPath);
            Shutdown();
            return;
        }
        // Developer utility: render windows with sample data to PNGs.
        if (e.Args is ["--render-preview", var previewDir])
        {
            try { DevPreview.Run(previewDir); }
            catch (Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(previewDir, "error.txt"), ex.ToString()); }
            Shutdown();
            return;
        }

        _instanceMutex = new Mutex(true, InstanceMutexName, out bool isFirst);
        if (!isFirst)
        {
            // Already running: ask that instance to show its settings (useful when the tray icon is hidden).
            if (!e.Args.Contains("--background") && EventWaitHandle.TryOpenExisting(ActivateEventName, out var evt))
                evt.Set();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled UI exception", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Log.Error("Unhandled exception", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };

#pragma warning disable WFO5001 // dark tray menu follows the Windows theme
        System.Windows.Forms.Application.SetColorMode(System.Windows.Forms.SystemColorMode.System);
#pragma warning restore WFO5001

        _controller = new AppController();
        if (!_controller.Start())
        {
            Shutdown(1);
            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        _activateWait = ThreadPool.RegisterWaitForSingleObject(_activateEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _controller?.OpenSettings()), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateWait?.Unregister(null);
        _activateEvent?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        _controller?.Dispose();
        base.OnSessionEnding(e);
    }
}
