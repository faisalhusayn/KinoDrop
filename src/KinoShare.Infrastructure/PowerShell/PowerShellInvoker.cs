namespace KinoShare.Infrastructure.PowerShell;

using System.Diagnostics;
using System.Text;

/// <summary>
/// Runs a PowerShell command in a hidden process and captures the result.
/// </summary>
internal static class PowerShellInvoker
{
    private static string WindowsPowerShellPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>
    /// Executes <paramref name="command"/> with Windows PowerShell.
    /// The command is passed via -EncodedCommand to avoid the quoting and
    /// string-interpolation quirks of -Command.
    /// </summary>
    /// <param name="command">The PowerShell statements to run.</param>
    /// <param name="cancellationToken">A token to cancel the invocation.</param>
    /// <returns>The exit code and captured output.</returns>
    public static async Task<PowerShellResult> InvokeAsync(string command, CancellationToken cancellationToken)
    {
        // Force UTF-8 for the child's output streams so messages with
        // non-ASCII characters survive the pipe intact.
        string effectiveCommand = $"[Console]::OutputEncoding = [Text.Encoding]::UTF8; {command}";
        string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(effectiveCommand));

        var startInfo = new ProcessStartInfo
        {
            FileName = WindowsPowerShellPath,
            Arguments = $"-NoProfile -NonInteractive -NoLogo -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            return new PowerShellResult(-1, string.Empty, "Unable to start Windows PowerShell.");
        }

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return new PowerShellResult(process.ExitCode, await standardOutput, await standardError);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited; nothing to kill.
            }

            throw;
        }
    }

    /// <summary>
    /// Escapes a value for safe inclusion in a single-quoted PowerShell string.
    /// </summary>
    public static string Escape(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    /// <summary>
    /// The captured outcome of a PowerShell invocation.
    /// </summary>
    public sealed record PowerShellResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public bool Succeeded => ExitCode == 0;

        public string Detail
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(StandardError))
                {
                    return StandardError.Trim();
                }

                if (!string.IsNullOrWhiteSpace(StandardOutput))
                {
                    return StandardOutput.Trim();
                }

                return $"PowerShell exited with code {ExitCode}.";
            }
        }
    }
}
