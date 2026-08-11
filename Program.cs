namespace Nook;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Two instances would fight over the same global hotkeys and stack two tray icons.
        using var singleInstance = new Mutex(true, @"Local\Nook", out var isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
