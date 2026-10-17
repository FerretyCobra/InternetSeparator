using System.Windows;

namespace InternetSeparator;

public partial class App : Application
{
    EventWaitHandle? showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Single instance: a second copy would restart mihomo under the first one. Instead it
        // signals the running copy to come out of the tray, and exits.
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\InternetSeparator.Show", out var first);
        if (!first) { showSignal.Set(); Shutdown(); return; }

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        if (!e.Args.Contains("--minimized")) window.Show();
        window.Start();
        new Thread(() => { while (showSignal.WaitOne()) Dispatcher.Invoke(window.ShowFromTray); }) { IsBackground = true }.Start();
    }
}
