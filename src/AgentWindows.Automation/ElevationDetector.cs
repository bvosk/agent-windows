using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace AgentWindows.Automation;

public static class ElevationDetector
{
    public static bool CurrentProcessIsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Heuristic: reading MainModule of an elevated process from a non-elevated one
    /// throws access-denied. Returns null when it cannot be determined.
    /// </summary>
    public static bool? ProcessIsElevated(int processId)
    {
        if (CurrentProcessIsElevated())
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            _ = process.MainModule;
            return false;
        }
        catch (Win32Exception)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
