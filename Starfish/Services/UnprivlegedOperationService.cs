using System.Diagnostics;
using System.Text;
using Starfish.Models;

namespace Starfish.Services;

public class UnprivilegedOperationService : IUnprivilegedOperationService
{
    private readonly string _cliPath;

    public UnprivilegedOperationService()
    {
        _cliPath = FindCliPath();
    }

    private static string FindCliPath()
    {
#if DEBUG
        var debugPath =
            Path.Combine("/home", Environment.GetEnvironmentVariable("USER")!,
                "RiderProjects/Shelly-ALPM/Shelly-CLI/bin/Debug/net10.0/linux-x64/shelly");
        Console.Error.WriteLine($"Debug path: {debugPath}");
#endif

        // Check common installation paths
        var possiblePaths = new[]
        {
#if DEBUG
            debugPath,
#endif
            "/usr/bin/shelly",
            "/usr/local/bin/shelly",
            Path.Combine(AppContext.BaseDirectory, "shelly"),
            Path.Combine(AppContext.BaseDirectory, "Shelly"),
            // Development path - relative to UI executable
            Path.Combine(Path.GetDirectoryName(AppContext.BaseDirectory) ?? "", "Shelly", "Shelly"),
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        // Fallback to assuming it's in PATH
        return "shelly";
    }

    public async Task<List<AlpmPackageDto>> GetAllPackagesAsync()
    {
        var result = await ExecuteUnprivilegedCommandAsync("List all packages", "list-available --json");

        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            return [];
        }

        try
        {
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var trimmedLine = StripBom(line.Trim());
                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    var updates = System.Text.Json.JsonSerializer.Deserialize(trimmedLine,
                        SeahorseGtkContext.Default.ListAlpmPackageDto);
                    return updates ?? [];
                }
            }

            var allUpdates = System.Text.Json.JsonSerializer.Deserialize(StripBom(result.Output.Trim()),
                SeahorseGtkContext.Default.ListAlpmPackageDto);
            return allUpdates ?? [];
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to parse updates JSON: {ex.Message}");
            return [];
        }
    }


    private async Task<UnprivilegedOperationResult> ExecuteUnprivilegedCommandAsync(string operationDescription,
        params string[] args)
    {
        return await ExecuteUnprivilegedCommandAsync(operationDescription, CancellationToken.None, args);
    }

    private async Task<UnprivilegedOperationResult> ExecuteUnprivilegedCommandAsync(string operationDescription,
        CancellationToken ct, params string[] args)
    {
        var arguments = string.Join(" ", args);
        var fullCommand = $"{_cliPath} {arguments}";

        Console.WriteLine($"Executing privileged command: {fullCommand}");

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _cliPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            }
        };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        StreamWriter? stdinWriter = null;

        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
                Console.WriteLine(e.Data);
            }
        };

        process.ErrorDataReceived += async (sender, e) =>
        {
            if (e.Data != null)
            {
                // Filter out the password prompt from sudo

                // Check for ALPM question (with Shelly prefix)
                if (e.Data.StartsWith("[Shelly][ALPM_QUESTION]"))
                {
                    var questionText = e.Data.Substring("[Shelly][ALPM_QUESTION]".Length);
                    Console.Error.WriteLine($"[Shelly]Question received: {questionText}");

                    // Send response to CLI via stdin
                    if (stdinWriter != null)
                    {
                        //await stdinWriter.WriteLineAsync(response ? "y" : "n");
                        await stdinWriter.WriteLineAsync("y");
                        await stdinWriter.FlushAsync();
                    }
                }
                else
                {
                    errorBuilder.AppendLine(e.Data);
                    Console.Error.WriteLine(e.Data);
                }
            }
        };

        try
        {
            process.Start();
            stdinWriter = process.StandardInput;
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                    process.Kill(true);
                throw;
            }

            // Close stdin after process exits
            stdinWriter?.Close();

            var success = process.ExitCode == 0;

            return new UnprivilegedOperationResult
            {
                Success = success,
                Output = outputBuilder.ToString(),
                Error = errorBuilder.ToString(),
                ExitCode = process.ExitCode
            };
        }
        catch (Exception ex)
        {
            return new UnprivilegedOperationResult
            {
                Success = false,
                Output = string.Empty,
                Error = ex.Message,
                ExitCode = -1
            };
        }
    }

    private static string StripBom(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // UTF-8 BOM is 0xEF 0xBB 0xBF which appears as \uFEFF in .NET strings
        return input.TrimStart('\uFEFF');
    }
}