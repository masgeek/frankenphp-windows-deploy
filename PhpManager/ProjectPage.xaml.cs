using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PhpManager;

public partial class ProjectPage : UserControl
{
    private string? _folder;

    public ProjectPage()
    {
        InitializeComponent();
        MatchList.SelectionChanged += (_, _) => PinBtn.IsEnabled = _folder != null && MatchList.SelectedItem != null;
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select a project folder", Multiselect = false };
        if (dialog.ShowDialog() != true)
            return;

        _folder = dialog.FolderName;
        FolderText.Text = _folder;
        await EvaluateAsync();
    }

    private async Task EvaluateAsync()
    {
        MatchList.ItemsSource = null;
        PinBtn.IsEnabled = false;
        if (_folder == null)
            return;

        var folder = _folder;
        var (requirement, installed) = await Task.Run(() =>
            (ProjectPhp.ReadRequirement(folder),
             PhpService.GetInstalledVersions().Where(v => v.Installed && !v.IsFrankenPhp)
                 .Select(v => v.DetectedVersion ?? v.Version).Distinct().ToList()));

        if (requirement == null)
        {
            RequirementText.Text = "This folder has no composer.json PHP requirement and no .php-version file.";
            StatusBar.SetStatus("No requirement found.");
            return;
        }

        RequirementText.Text = $"{requirement.Constraint}   (from {requirement.Source})";
        var matches = ProjectPhp.Matching(requirement.Constraint, installed);
        MatchList.ItemsSource = matches;

        if (matches.Count > 0)
        {
            MatchList.SelectedIndex = 0;
            StatusBar.SetStatus($"{matches.Count} installed version(s) satisfy it; {matches[0]} is the newest.");
        }
        else
        {
            StatusBar.SetStatus($"No installed PHP satisfies {requirement.Constraint}. Install a matching version on the Versions page.");
        }
    }

    private void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (_folder == null || MatchList.SelectedItem is not string version)
            return;

        var folderName = PhpService.GetInstalledVersions()
            .FirstOrDefault(v => v.Installed && !v.IsFrankenPhp && (v.DetectedVersion ?? v.Version) == version);
        if (folderName == null)
        {
            StatusBar.SetStatus("That version is no longer installed.");
            return;
        }

        try
        {
            ProjectPhp.WritePin(_folder, version, Path.Combine(folderName.InstallDir, "php.exe"));
            StatusBar.SetStatus($"Pinned PHP {version} to {_folder} (.php-version and php.cmd written).");
        }
        catch (Exception ex)
        {
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
    }
}
