using System.Reflection;
using System.Runtime.InteropServices;

namespace Nook;

internal static class Program
{
    private static bool errorBoxOpen;

    [STAThread]
    private static void Main()
    {
        // Opened before the mutex by every instance, so a second launch can always reach the first:
        // the event outlives the second process because the first holds a handle to it as well.
        using var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Nook.Show");

        // Two instances would fight over the same global hotkeys and stack two tray icons.
        using var singleInstance = new Mutex(true, @"Local\Nook", out var isFirstInstance);
        if (!isFirstInstance)
        {
            // The launching process holds the foreground right; without passing it on, Windows only
            // flashes the existing window's taskbar button instead of bringing it forward.
            AllowSetForegroundWindow(AsfwAny);
            showRequest.Set();
            return;
        }

        // The ThreadException handler only fires when WinForms catches the exception itself, and the mode
        // can only be set before the first window is created.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += OnThreadException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("Unhandled exception, Nook is terminating", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        ApplicationConfiguration.Initialize();
        // Lets the controls Nook does not draw itself (combo box arrows, check boxes, scroll bars)
        // follow the Windows dark theme, matching the colours AppTheme gives everything else.
#pragma warning disable WFO5001 // Dark mode is still flagged experimental in WinForms.
        Application.SetColorMode(SystemColorMode.System);
#pragma warning restore WFO5001
        Log.Info($"Nook {GetVersion()} starting on {Environment.OSVersion}, {RuntimeInformation.FrameworkDescription}");
        var window = new MainForm();
        var showRegistration = ThreadPool.RegisterWaitForSingleObject(
            showRequest, (_, _) => window.ShowForSecondLaunch(), null, Timeout.Infinite, executeOnlyOnce: false);
        try
        {
            Application.Run(window);
        }
        finally
        {
            showRegistration.Unregister(null);
        }

        Log.Info("Nook stopped");
    }

    private const int AsfwAny = -1;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool AllowSetForegroundWindow(int processId);

    private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
    {
        Log.Error("Unhandled UI exception", e.Exception);

        // MessageBox pumps messages, so a second UI exception can arrive while the first box is still open.
        if (errorBoxOpen)
        {
            return;
        }

        errorBoxOpen = true;
        try
        {
            MessageBox.Show(
                $"Nook hit an unexpected error and will keep running. Details were written to {Log.FilePath}.",
                "Nook",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            errorBoxOpen = false;
        }
    }

    /// <summary>The release version without the source-link commit suffix, so "1.0.2+abc123" reports as "1.0.2".</summary>
    private static string GetVersion()
    {
        var informational = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        return informational.Split('+')[0];
    }
}
