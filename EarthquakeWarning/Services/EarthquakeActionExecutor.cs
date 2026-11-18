using System.Diagnostics;

namespace EarthquakeWarning.Services;

internal static class EarthquakeActionExecutor
{
    public static async Task ExecuteCommandAsync(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new InvalidOperationException("CMD 命令为空。");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            // cmd.exe uses its own quoting rules rather than the CRT ArgumentList rules.
            Arguments = $"/d /s /c \"{command}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            throw new InvalidOperationException("无法启动 CMD 命令。");
        }
        await Task.WhenAll(DrainAsync(process.StandardOutput), DrainAsync(process.StandardError), process.WaitForExitAsync());
        if (process.ExitCode != 0) throw new InvalidOperationException($"CMD 命令退出码为 {process.ExitCode}。");
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[4096];
        while (await reader.ReadAsync(buffer) > 0) { }
    }
}
