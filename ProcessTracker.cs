namespace Nook;

/// <summary>A running process as listed in the picker.</summary>
internal sealed record ProcessItem(int Pid, string Name)
{
    public override string ToString() => $"{Name} (PID {Pid})";
}

internal enum TrackerEvent
{
    None,
    Attached,
    Reattached,
    Exited,
}

/// <summary>
/// Decides which process is being monitored. A PID is not an identity: Windows reuses PIDs,
/// and a restarted program returns under a new one, so the name and start time must agree
/// before the process counts as the same one. Operating system lookups are passed in so the
/// rules can be tested without starting processes.
/// </summary>
internal sealed class ProcessTracker
{
    private DateTime? _startTime;
    private bool _hasAttached;

    /// <summary>The process being monitored, or null while it is not running.</summary>
    public ProcessItem? Current { get; private set; }

    /// <summary>The program name to follow across restarts. It survives the process exiting.</summary>
    public string? RememberedName { get; set; }

    /// <summary>Starts monitoring <paramref name="process"/> and remembers its name.</summary>
    public void Attach(ProcessItem process, DateTime? startTime)
    {
        Current = process;
        _startTime = startTime;
        RememberedName = process.Name;
        _hasAttached = true;
    }

    /// <summary>
    /// Stops monitoring. When <paramref name="forget"/> is true the remembered name is dropped as
    /// well, so the tracker will not pick a process up again on its own.
    /// </summary>
    public void Detach(bool forget)
    {
        Current = null;
        _startTime = null;
        if (forget)
        {
            RememberedName = null;
        }
    }

    /// <summary>
    /// Checks the monitored process once a second. <paramref name="lookup"/> returns the live name
    /// and start time for a PID, or null when no process holds it. A start time that cannot be read
    /// is not evidence of exit, so it never triggers <see cref="TrackerEvent.Exited"/> on its own.
    /// </summary>
    public TrackerEvent CheckAlive(Func<int, (string Name, DateTime? StartTime)?> lookup)
    {
        if (Current is not { } current)
        {
            return TrackerEvent.None;
        }

        if (lookup(current.Pid) is not { } live)
        {
            return Exit();
        }

        if (!string.Equals(live.Name, current.Name, StringComparison.OrdinalIgnoreCase))
        {
            return Exit();
        }

        if (_startTime is { } attachedAt && live.StartTime is { } observedAt && observedAt != attachedAt)
        {
            return Exit();
        }

        return TrackerEvent.None;
    }

    /// <summary>
    /// Called on each process-list refresh with the unfiltered list. While a process is monitored this
    /// does nothing, so it never switches to a sibling. Otherwise it attaches to the remembered name,
    /// but only when that name matches exactly one process.
    /// </summary>
    public TrackerEvent Reconcile(IReadOnlyList<ProcessItem> allProcesses, Func<int, DateTime?> readStartTime)
    {
        if (Current is not null || string.IsNullOrWhiteSpace(RememberedName))
        {
            return TrackerEvent.None;
        }

        var candidates = allProcesses
            .Where(process => string.Equals(process.Name, RememberedName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();
        if (candidates.Count != 1)
        {
            return TrackerEvent.None;
        }

        var process = candidates[0];
        var isFirstAttach = !_hasAttached;
        Attach(process, readStartTime(process.Pid));
        return isFirstAttach ? TrackerEvent.Attached : TrackerEvent.Reattached;
    }

    private TrackerEvent Exit()
    {
        Current = null;
        _startTime = null;
        return TrackerEvent.Exited;
    }
}
