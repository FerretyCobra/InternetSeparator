using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace InternetSeparator;

// The app needs admin, and Windows silently skips admin apps in the Run key / Startup folder,
// so start-with-Windows is a Task Scheduler logon task with "highest privileges".
public static class Autostart
{
    const string TaskName = "InternetSeparator";

    public static bool IsEnabled => Schtasks($"/Query /TN {TaskName}") == 0;

    public static void Set(bool on)
    {
        if (!on) { Schtasks($"/Delete /TN {TaskName} /F"); return; }
        var user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
        // XML instead of plain schtasks flags: the defaults would stop the task on battery and kill it after 72 h.
        var xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>5</Priority>
              </Settings>
              <Actions Context="Author"><Exec><Command>{SecurityElement.Escape(Environment.ProcessPath)}</Command><Arguments>--minimized</Arguments></Exec></Actions>
            </Task>
            """;
        var file = Path.Combine(Path.GetTempPath(), "InternetSeparator-task.xml");
        File.WriteAllText(file, xml, Encoding.Unicode);
        var code = Schtasks($"/Create /TN {TaskName} /XML \"{file}\" /F");
        File.Delete(file);
        if (code != 0) throw new Exception($"Could not create the startup task (schtasks exit code {code}).");
    }

    static int Schtasks(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true })!;
        p.WaitForExit();
        return p.ExitCode;
    }
}
