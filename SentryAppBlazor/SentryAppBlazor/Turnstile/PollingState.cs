using Microsoft.Extensions.Options;
using SentryAppBlazor.Services;

namespace SentryAppBlazor.Turnstile;
public sealed class TurnstilePollingController
{
    private readonly object gate = new();
    private volatile bool active;
    private TaskCompletionSource activeSignal = CreateSignal();

    public bool IsActive => active;
    public event Action<bool>? StatusChanged;

    public bool TryStart()
    {
        Action<bool>? changed;
        lock (gate)
        {
            if (active) return false;
            active = true;
            activeSignal.TrySetResult();
            changed = StatusChanged;
        }
        changed?.Invoke(true);
        return true;
    }

    public bool TryStop()
    {
        Action<bool>? changed;
        lock (gate)
        {
            if (!active) return false;
            active = false;
            activeSignal = CreateSignal();
            changed = StatusChanged;
        }
        changed?.Invoke(false);
        return true;
    }

    public void Start() => TryStart();
    public void Stop() => TryStop();

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
    private readonly object gate = new();
    private readonly LinkedList<TurnstileLogEntry> incoming = [];
    private readonly LinkedList<TurnstileLogEntry> outgoing = [];
    private readonly RecentlySeenIds ids = new(2000);
    private readonly IOptionsMonitor<MonitoringOptions>? options;
    private readonly TimeProvider time;
    private TurnstileLogEntry? spotlight;

    public TurnstileLogState() : this(null, TimeProvider.System) { }
    public TurnstileLogState(IOptionsMonitor<MonitoringOptions>? options, TimeProvider time) { this.options=options; this.time=time; }
    public event Action? Changed;
    public TurnstileLogEntry? Spotlight { get { lock(gate) return spotlight; } }
    public IReadOnlyList<TurnstileLogEntry> InEntries => Snapshot(incoming);
    public IReadOnlyList<TurnstileLogEntry> OutEntries => Snapshot(outgoing);
    public IReadOnlyList<TurnstileLogEntry> Entries { get { lock(gate) return incoming.Concat(outgoing).OrderByDescending(x=>x.TimeLogStamp).ToArray(); } }

    public bool Add(TurnstileLogEntry entry)
    {
        lock(gate) { if(!ids.Add(entry.TimeLogId)) return false; spotlight=entry; }
        NotifyChanged();
        _ = PromoteAndExpireAsync(entry);
        return true;
    }

    public IReadOnlyList<TurnstileLogEntry> Filter(string? deviceSerialNumber) =>
        string.IsNullOrWhiteSpace(deviceSerialNumber) || deviceSerialNumber.Equals("all", StringComparison.OrdinalIgnoreCase)
            ? Entries : Entries.Where(x=>string.Equals(x.DeviceSerialNumber, deviceSerialNumber, StringComparison.OrdinalIgnoreCase)).ToArray();

    // Kept as a useful explicit maintenance operation for callers and tests.
    public void Prune(DateTimeOffset cutoff)
    {
        bool changed;
        lock(gate) { changed=RemoveOlder(incoming,cutoff)|RemoveOlder(outgoing,cutoff); }
        if(changed) NotifyChanged();
    }

    private async Task PromoteAndExpireAsync(TurnstileLogEntry entry)
    {
        await Task.Delay(TimeSpan.FromMilliseconds(options?.CurrentValue.HighlightDisplayDuration ?? 3000), time);
        lock(gate)
        {
            if(spotlight?.TimeLogId==entry.TimeLogId) spotlight=null;
            var queue=IsIn(entry.LogType)?incoming:outgoing;
            queue.AddFirst(entry);
            while(queue.Count>10) queue.RemoveLast();
        }
        NotifyChanged();
        await Task.Delay(TimeSpan.FromMilliseconds(options?.CurrentValue.FeedRetentionDuration ?? 10000), time);
        lock(gate)
        {
            var removed=RemoveById(incoming,entry.TimeLogId)|RemoveById(outgoing,entry.TimeLogId);
            if(!removed) return;
        }
        NotifyChanged();
    }
    private IReadOnlyList<TurnstileLogEntry> Snapshot(LinkedList<TurnstileLogEntry> queue) { lock(gate) return queue.ToArray(); }
    private static bool IsIn(string? type)=>string.Equals(type,"IN",StringComparison.OrdinalIgnoreCase);
    private static bool RemoveOlder(LinkedList<TurnstileLogEntry> queue,DateTimeOffset cutoff) { var changed=false; for(var n=queue.First;n is not null;) { var next=n.Next;if(n.Value.TimeLogStamp<cutoff){queue.Remove(n);changed=true;}n=next;}return changed; }
    private static bool RemoveById(LinkedList<TurnstileLogEntry> queue,Guid id) { for(var n=queue.First;n is not null;n=n.Next)if(n.Value.TimeLogId==id){queue.Remove(n);return true;}return false; }
    private void NotifyChanged() { var handlers=Changed; if(handlers is not null) _=Task.Run(()=>{foreach(Action h in handlers.GetInvocationList())try{h();}catch{}}); }
}
public sealed class RecentlySeenIds
{
    private readonly int capacity; private readonly HashSet<Guid> ids = []; private readonly Queue<Guid> order = []; private readonly object gate = new();
    public RecentlySeenIds(int capacity) => this.capacity = capacity;
    public bool Add(Guid id) { lock (gate) { if (!ids.Add(id)) return false; order.Enqueue(id); while (order.Count > capacity) ids.Remove(order.Dequeue()); return true; } }
    public bool Contains(Guid id) { lock(gate) return ids.Contains(id); }
    public int Count { get { lock (gate) return ids.Count; } }
}
