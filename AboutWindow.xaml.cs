using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace InternetSeparator;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var v = Assembly.GetExecutingAssembly().GetName().Version!;
        VersionText.Text = $"Version {v.Major}.{v.Minor}.{v.Build}";
        CopyrightText.Text = $"© {DateTime.Now.Year} FerretyCobra";
    }

    // We run elevated; going through explorer opens the link/folder unelevated, in the user's normal browser.
    static void OpenUnelevated(string target) => Process.Start("explorer.exe", $"\"{target}\"");

    void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        OpenUnelevated(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    void DataFolder_Click(object sender, RoutedEventArgs e) => OpenUnelevated(Path.Combine(AppContext.BaseDirectory, "data"));
}
