using System.Diagnostics;
using System.Text;

namespace MindBot.Infrastructure.Git;

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;

    public string CombinedOutput => string.IsNullOrWhiteSpace(StandardError) ? StandardOutput.Trim() : StandardError.Trim();
}

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string workingDirectory,
        IDictionary<string, string?>? environment,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        try
        {
            using var process = new Process { StartInfo = startInfo };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            return new ProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ProcessResult(-1, string.Empty, ex.Message);
        }
    }
}
