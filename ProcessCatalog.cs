using System.Diagnostics;

namespace Nook;

/// <summary>Reads the running processes and the identity of one of them.</summary>
internal static class ProcessCatalog
{
    /// <summary>Every running process, sorted by name and then PID.</summary>
    public static List<ProcessItem> List()
    {
        var result = new List<ProcessItem>();
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                result.Add(new ProcessItem(process.Id, process.ProcessName));
            }
            catch
            {
                // A process can exit while the list is being built.
            }
            finally
            {
                process.Dispose();
            }
        }

        result.Sort(static (left, right) =>
        {
            var byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : left.Pid.CompareTo(right.Pid);
        });

        return result;
    }

    /// <summary>
    /// Start time is read only for the process being attached to: Windows denies access for
    /// protected processes, and asking for every entry costs one exception per denial.
    /// </summary>
    public static DateTime? TryGetStartTime(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return TryGetStartTime(process);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns the live name and start time of a PID, or null once no process holds it.</summary>
    public static (string Name, DateTime? StartTime)? LookUp(int pid, string expectedName)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return (process.ProcessName, TryGetStartTime(process));
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch
        {
            // Not being able to read the process is not proof that it exited, so keep following it.
            return (expectedName, null);
        }
    }

    private static DateTime? TryGetStartTime(Process process)
    {
        try { return process.StartTime; }
        catch { return null; }
    }
}
