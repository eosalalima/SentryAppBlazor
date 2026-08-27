using System.Collections.Concurrent;

namespace SentryAppBlazor.Turnstile;
public sealed class TurnstilePollingController
{
    private readonly object gate = new();
    private volatile bool active;
    private TaskCompletionSource activeSignal = CreateSignal();

    public bool IsActive => active;

    public void Start()
    {
        lock (gate)
        {
            if (active) return;
            active = true;
            activeSignal.TrySetResult();
        }
    }

    public void Stop()
    {
        lock (gate)
        {
            if (!active) return;
            active = false;
            activeSignal = CreateSignal();
        }
    }

    public Task WaitUntilActiveAsync(CancellationToken token)
    {
        lock (gate)
        {
            return active ? Task.CompletedTask : activeSignal.Task.WaitAsync(token);
        }
    }

    private static TaskCompletionSource CreateSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
public sealed class TurnstileLogState
{
    private readonly ConcurrentQueue<TurnstileLogEntry> entries = new(); public event Action? Changed;
    public IReadOnlyList<TurnstileLogEntry> Entries => entries.Reverse().ToArray();
    public void Add(TurnstileLogEntry entry) { entries.Enqueue(entry); while (entries.Count > 200 && entries.TryDequeue(out _)) { } var handlers = Changed; if (handlers is not null) _ = Task.Run(() => { foreach (Action h in handlers.GetInvocationList()) try { h(); } catch { } }); }
    public void Prune(DateTimeOffset cutoff)
    {
        var changed=false;
        while (entries.TryPeek(out var entry) && entry.TimeLogStamp < cutoff) changed|=entries.TryDequeue(out _);
        if (changed) NotifyChanged();
    }
    private void NotifyChanged() { var handlers=Changed; if (handlers is not null) _=Task.Run(() => { foreach(Action h in handlers.GetInvocationList()) try { h(); } catch { } }); }
}
public sealed class RecentlySeenIds
{
    private readonly int capacity; private readonly HashSet<Guid> ids = []; private readonly Queue<Guid> order = []; private readonly object gate = new();
    public RecentlySeenIds(int capacity) => this.capacity = capacity;
    public bool Add(Guid id) { lock (gate) { if (!ids.Add(id)) return false; order.Enqueue(id); while (order.Count > capacity) ids.Remove(order.Dequeue()); return true; } }
    public int Count { get { lock (gate) return ids.Count; } }
}
