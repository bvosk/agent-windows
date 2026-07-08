using System.Diagnostics;

if (!OperatingSystem.IsWindows())
{
    await Console.Error.WriteLineAsync("agent-windows only runs on Windows.");
    return 1;
}

var payload = Path.Combine(AppContext.BaseDirectory, "payload", "agent-windows.exe");
if (!File.Exists(payload))
{
    await Console.Error.WriteLineAsync($"agent-windows payload executable not found: {payload}");
    return 1;
}

var startInfo = new ProcessStartInfo(payload) { UseShellExecute = false };
foreach (var argument in args)
{
    startInfo.ArgumentList.Add(argument);
}

using var process = Process.Start(startInfo);
if (process is null)
{
    await Console.Error.WriteLineAsync("Failed to start the agent-windows payload process.");
    return 1;
}

await process.WaitForExitAsync();
return process.ExitCode;
