using System.Reflection;
using System.Runtime.InteropServices;

namespace Nook;

internal static class Program
{
    private static bool errorBoxOpen;

    [STAThread]
    private static void Main()
    {
        // Two instances would fight over the same global hotkeys and stack two tray icons.
        using var singleInstance = new Mutex(true, @"Local\Nook", out var isFirstInstance);
        if (!isFirstInstance)
        {
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
        Log.Info($"Nook {GetVersion()} starting on {Environment.OSVersion}, {RuntimeInformation.FrameworkDescription}");
        Application.Run(new MainForm());
        Log.Info("Nook stopped");
    }

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
