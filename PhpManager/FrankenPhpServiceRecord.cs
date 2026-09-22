namespace PhpManager;

public class FrankenPhpServiceRecord
{
    public long Id { get; set; }
    public string ServiceName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string AppPath { get; set; } = "";
    public int Port { get; set; } = 8001;
    public int AdminPort { get; set; } = 2020;
    public int Workers { get; set; } = 2;
    public int MaxRequests { get; set; } = 250;
    public string HealthPath { get; set; } = "/up";
    public string FirewallRuleName { get; set; } = "";
    public bool IsDevelopment { get; set; }
    public bool IsRunning { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public string Mode => IsDevelopment ? "Dev" : "Prod";
    public string Status => IsRunning ? "Running" : "Stopped";
}
