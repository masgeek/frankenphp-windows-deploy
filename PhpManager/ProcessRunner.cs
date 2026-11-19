using System.Diagnostics;
using System.IO;

namespace PhpManager;

/// <summary>
/// Runs a console program and captures stdout and stderr without deadlocking (both pipes are drained
/// concurrently) and without hanging forever (the process tree is killed after a timeout).
/// </summary>
public static class ProcessRunner
{
    public sealed record Result(string Output, string Error, int ExitCode, bool TimedOut)
    {
        public string Combined => string.Join(Environment.NewLine, new[] { Output, Error }.Where(s => s.Length > 0));
    }

    public static Result Run(
        string exe, string args, string? workingDir = null,
        IReadOnlyDictionary<string, string>? env = null, int timeoutMs = 20_000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            WorkingDirectory = workingDir ?? Path.GetDirectoryName(exe) ?? "",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (env != null)
            foreach (var (key, value) in env)
                psi.Environment[key] = value;

        try
        {
            using var process = Process.Start(psi);
            if (process == null)
                return new Result("", "Process could not be started.", 1, false);

            // A program waiting on stdin would otherwise block forever.
            try { process.StandardInput.Close(); } catch { }

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                process.WaitForExit(2000);
                return new Result(Drain(stdout), Drain(stderr), -1, TimedOut: true);
            }

            process.WaitForExit(); // flush async readers
            return new Result(Drain(stdout), Drain(stderr), process.ExitCode, false);
        }
        catch (Exception ex)
        {
            return new Result("", ex.Message, 1, false);
        }
    }

    private static string Drain(Task<string> task) =>
        task.Wait(2000) ? task.Result : "";
}
