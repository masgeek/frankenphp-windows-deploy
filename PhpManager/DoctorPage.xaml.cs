using System.Windows;
using System.Windows.Controls;

namespace PhpManager;

public partial class DoctorPage : UserControl
{
    private List<DoctorResult> _results = [];

    public DoctorPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        RunBtn.IsEnabled = false;
        FixAllBtn.IsEnabled = false;
        StatusBar.SetStatus("Running checks...", true);
        SummaryText.Text = "Checking...";

        try
        {
            _results = await Task.Run(FeatureService.RunDoctor);
            ResultsGrid.ItemsSource = _results;

            var fails = _results.Count(r => r.Status == DoctorStatus.Fail);
            var warns = _results.Count(r => r.Status == DoctorStatus.Warn);
            SummaryText.Text = fails == 0 && warns == 0
                ? "Everything looks healthy."
                : $"{fails} problem(s), {warns} warning(s), {_results.Count(r => r.Status == DoctorStatus.Pass)} OK.";
            FixAllBtn.IsEnabled = _results.Any(r => r.CanFix && r.Status != DoctorStatus.Pass);
            StatusBar.SetStatus("Health check finished.");
        }
        catch (Exception ex)
        {
            SummaryText.Text = "The health check failed to run.";
            StatusBar.SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            RunBtn.IsEnabled = true;
        }
    }

    private async void Run_Click(object sender, RoutedEventArgs e) => await RunAsync();

    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DoctorResult result } && result.FixId != null)
            await FixAsync([result]);
    }

    private async void FixAll_Click(object sender, RoutedEventArgs e) =>
        await FixAsync(_results.Where(r => r.CanFix && r.Status != DoctorStatus.Pass).ToList());

    private async Task FixAsync(List<DoctorResult> items)
    {
        var fixes = items.Select(r => r.FixId!).Distinct().ToList();
        var messages = new List<string>();

        RunBtn.IsEnabled = false;
        FixAllBtn.IsEnabled = false;
        foreach (var fix in fixes)
        {
            StatusBar.SetStatus($"Applying fix: {fix}...", true);
            try
            {
                messages.Add(await FeatureService.ApplyFixAsync(fix, new Progress<string>(m => Dispatcher.Invoke(() => StatusBar.SetStatus(m, true)))));
            }
            catch (Exception ex)
            {
                messages.Add($"{fix} failed: {ex.Message}");
            }
        }

        await RunAsync();
        StatusBar.SetStatus(string.Join(" ", messages));
    }
}
