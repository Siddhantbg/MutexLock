using System.IO;
using System.Text;
using System.Windows;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

namespace FolderLock.App;

public partial class LogsWindow : FluentWindow
{
    public LogsWindow()
    {
        InitializeComponent();
        Load();
    }

    private void Load()
    {
        LogGrid.ItemsSource = AppServices.Folders
            .GetAudit()
            .Select(entry => new AuditRow(entry))
            .ToList();
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Load();

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this, "Clear the entire log?", "FolderLock", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes)
        {
            return;
        }

        AppServices.Folders.ClearAudit();
        Load();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export log",
            FileName = $"FolderLock-Log-{DateTime.Now:yyyyMMdd-HHmmss}.csv",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine("Time,Action,Result,Folder,Detail");
            foreach (var row in (IEnumerable<AuditRow>)LogGrid.ItemsSource)
            {
                builder.AppendLine(string.Join(
                    ',',
                    Escape(row.Time),
                    Escape(row.Action),
                    Escape(row.Result),
                    Escape(row.Folder),
                    Escape(row.Detail)));
            }

            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            MessageBox.Show(this, "Exported.", "FolderLock", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Export failed: {ex.Message}", "FolderLock", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
