using Nook;

namespace Nook.Tests;

/// <summary>
/// Nook follows a program by PID, name and start time together. A PID alone is reused by Windows,
/// and a restarted program comes back under a new one, so these rules decide what counts as the same process.
/// </summary>
public class ProcessTrackerTests
{
    private static readonly DateTime Started = new(2026, 10, 1, 9, 0, 0, DateTimeKind.Local);
    private static readonly DateTime Restarted = Started.AddMinutes(30);
    private static readonly ProcessItem Game = new(100, "game");
    private static readonly ProcessItem Sibling = new(200, "game");

    [Fact]
    public void A_running_process_is_never_replaced_by_a_sibling_of_the_same_name()
    {
        var tracker = new ProcessTracker();
        tracker.Attach(Game, Started);

        var reconciled = tracker.Reconcile([Sibling, Game], _ => Started);
        var stillAlive = tracker.CheckAlive(Live((100, "game", Started), (200, "game", Started)));

        Assert.Equal(TrackerEvent.None, reconciled);
        Assert.Equal(TrackerEvent.None, stillAlive);
        Assert.Equal(Game, tracker.Current);
    }

    [Fact]
    public void A_monitored_process_hidden_from_the_list_is_not_mistaken_for_a_restart()
    {
        var tracker = new ProcessTracker();
        tracker.Attach(Game, Started);

        // The list the picker filtered out Game, leaving only a sibling in view.
        var reconciled = tracker.Reconcile([Sibling], _ => Started);

        Assert.Equal(TrackerEvent.None, reconciled);
        Assert.Equal(Game, tracker.Current);
    }

    [Fact]
    public void Exit_is_detected_when_the_pid_is_gone()
    {
        var tracker = new ProcessTracker();
        tracker.Attach(Game, Started);

        var result = tracker.CheckAlive(Live());

        Assert.Equal(TrackerEvent.Exited, result);
        Assert.Null(tracker.Current);
        Assert.Equal("game", tracker.RememberedName);
    }

    [Fact]
    public void Exit_is_detected_when_the_pid_now_belongs_to_a_different_program()
    {
        var tracker = new ProcessTracker();
        tracker.Attach(Game, Started);

        var result = tracker.CheckAlive(Live((100, "notepad", Started)));

        Assert.Equal(TrackerEvent.Exited, result);
        Assert.Null(tracker.Current);
    }

    [Fact]
    public void Exit_is_detected_when_the_pid_has_a_different_start_time()
    {
        var tracker = new ProcessTracker();
        tracker.Attach(Game, Started);

        var result = tracker.CheckAlive(Live((100, "game", Restarted)));

        Assert.Equal(TrackerEvent.Exited, result);
        Assert.Null(tracker.Current);
    }

    [Fact]
    public void An_unreadable_start_time_does_not_count_as_exit()
    {
        var tracker = new ProcessTracker();
        tracker.Attach(Game, Started);

        var result = tracker.CheckAlive(Live((100, "game", null)));

        Assert.Equal(TrackerEvent.None, result);
        Assert.Equal(Game, tracker.Current);
    }

    [Fact]
    public void A_start_time_unknown_at_attach_does_not_block_the_check()
    {
        var tracker = new ProcessTracker();
        tracker.Attach(Game, null);

        var result = tracker.CheckAlive(Live((100, "game", Restarted)));

        Assert.Equal(TrackerEvent.None, result);
        Assert.Equal(Game, tracker.Current);
    }

    [Fact]
    public void After_exit_a_single_same_named_process_is_reattached()
    {
        var tracker = new ProcessTracker();
        tracker.Attach(Game, Started);
        tracker.CheckAlive(Live());

        var reconciled = tracker.Reconcile([new ProcessItem(300, "game")], _ => Restarted);

        Assert.Equal(TrackerEvent.Reattached, reconciled);
        Assert.Equal(new ProcessItem(300, "game"), tracker.Current);
    }

    [Fact]
    public void After_exit_two_same_named_processes_are_not_guessed_between()
    {
        var tracker = new ProcessTracker();
        tracker.Attach(Game, Started);
        tracker.CheckAlive(Live());

        var reconciled = tracker.Reconcile([new ProcessItem(300, "game"), new ProcessItem(301, "game")], _ => Restarted);

        Assert.Equal(TrackerEvent.None, reconciled);
        Assert.Null(tracker.Current);
    }

    [Fact]
    public void At_startup_the_remembered_name_attaches_with_the_attached_event()
    {
        var tracker = new ProcessTracker { RememberedName = "game" };

        var reconciled = tracker.Reconcile([Game], _ => Started);

        Assert.Equal(TrackerEvent.Attached, reconciled);
        Assert.Equal(Game, tracker.Current);
    }

    [Fact]
    public void Detach_with_forget_prevents_a_later_reattach()
    {
        var tracker = new ProcessTracker();
        tracker.Attach(Game, Started);
        tracker.Detach(forget: true);

        var reconciled = tracker.Reconcile([Game], _ => Started);

        Assert.Equal(TrackerEvent.None, reconciled);
        Assert.Null(tracker.Current);
        Assert.Null(tracker.RememberedName);
    }

    [Fact]
    public void Detach_without_forget_keeps_the_name_for_a_later_reattach()
    {
        var tracker = new ProcessTracker();
        tracker.Attach(Game, Started);
        tracker.Detach(forget: false);

        var reconciled = tracker.Reconcile([Game], _ => Started);

        Assert.Equal(TrackerEvent.Reattached, reconciled);
        Assert.Equal(Game, tracker.Current);
    }

    /// <summary>A fake process table keyed by PID, standing in for the operating system lookup.</summary>
    private static Func<int, (string Name, DateTime? StartTime)?> Live(params (int Pid, string Name, DateTime? StartTime)[] processes)
    {
        var table = processes.ToDictionary(process => process.Pid, process => (process.Name, process.StartTime));
        return pid => table.TryGetValue(pid, out var found) ? found : null;
    }
}
