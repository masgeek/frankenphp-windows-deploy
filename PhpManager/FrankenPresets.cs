namespace PhpManager;

/// <summary>Worker and request-limit presets for FrankenPHP services, scaled to the machine's CPU count.</summary>
public static class FrankenPresets
{
    public sealed record Preset(string Name, int Workers, int MaxRequests, bool DevMode, string Description);

    public static List<Preset> For(int cpuCount)
    {
        var cpus = Math.Max(1, cpuCount);
        return
        [
            new Preset("Laravel Octane-style", Math.Clamp(cpus, 2, 16), 500, false,
                "One worker per CPU core, recycled every 500 requests to contain memory leaks."),
            new Preset("Light development", 1, 0, true,
                "A single worker with development mode on; code changes show without a restart."),
            new Preset("Production", Math.Clamp(cpus * 2, 2, 32), 1000, false,
                "Two workers per core for throughput, recycled every 1000 requests.")
        ];
    }
}
