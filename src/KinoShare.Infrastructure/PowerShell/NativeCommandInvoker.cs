namespace KinoShare.Infrastructure.PowerShell;

using System.Diagnostics;

/// <summary>Runs a native Windows command and captures its result.</summary>
internal static class NativeCommandInvoker
{
    public static async Task<CommandResult> InvokeAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            return new CommandResult(-1, string.Empty, $"Unable to start {fileName}.");
        }

        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
            return new CommandResult(process.ExitCode, await standardOutput, await standardError);
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

    internal sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
    {
        public bool Succeeded => ExitCode == 0;

        public string Detail => !string.IsNullOrWhiteSpace(StandardError)
            ? StandardError.Trim()
            : StandardOutput.Trim();
    }
}
