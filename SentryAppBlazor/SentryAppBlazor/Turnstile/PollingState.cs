using SentryAppBlazor.Services;

namespace SentryAppBlazor.Turnstile;
public sealed class TurnstilePollingController
{
    private readonly ILogger<TurnstilePollingController>? logger;
    private readonly object gate = new();
    private volatile bool active;
    private long activeSession;
    private TaskCompletionSource activeSignal = CreateSignal();
    private MonitoringMode mode = MonitoringMode.Demo;
    private DemoSelection selection = new(null, null, "IN");
    private string? statusMessage;

    public bool IsActive => active;
    public long ActiveSession => Interlocked.Read(ref activeSession);
    public MonitoringMode Mode { get { lock (gate) return mode; } }
    public DemoSelection Selection { get { lock (gate) return selection; } }
    public string? StatusMessage { get { lock (gate) return statusMessage; } }
    public event Action<bool>? StatusChanged;
    public event Action? Changed;

    public TurnstilePollingController(ILogger<TurnstilePollingController>? logger = null) => this.logger = logger;

    public bool TryConfigure(MonitoringMode newMode, DemoSelection? newSelection = null)
    {
        lock (gate)
        {
            if (active) return false;
            mode = newMode;
            if (newSelection is not null) selection = newSelection;
            statusMessage = null;
        }
        logger?.LogInformation("Monitoring mode changed to {MonitoringMode}", newMode);
        Changed?.Invoke();
        return true;
    }

    public void ReportStatus(string? message)
    {
        lock (gate) statusMessage = message;
        Changed?.Invoke();
    }

    public bool TryStart()
    {
        Action<bool>? changed;
        lock (gate)
        {
            if (active) return false;
            active = true;
            Interlocked.Increment(ref activeSession);
            activeSignal.TrySetResult();
            changed = StatusChanged;
        }
        changed?.Invoke(true);
        logger?.LogInformation("Monitoring started in {MonitoringMode} mode", Mode);
        Changed?.Invoke();
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
        logger?.LogInformation("Monitoring stopped");
        Changed?.Invoke();
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
public enum MonitoringMode { Demo, Live }
public sealed record DemoSelection(string? AccessNumber, string? DeviceSerialNumber, string LogType);

public sealed class TurnstileLogState : IDisposable
{
    private readonly object gate = new();
    private readonly LinkedList<TurnstileLogEntry> incoming = [];
    private readonly LinkedList<TurnstileLogEntry> outgoing = [];
    private readonly RecentlySeenIds ids = new(2000);
    private readonly MonitoringSettingsStore? settings;
    private readonly Func<MonitoringOptions>? optionsProvider;
    private readonly TimeProvider time;
    private TurnstileLogEntry? spotlight;
    private readonly CancellationTokenSource lifetime = new();

    public TurnstileLogState() : this(null, TimeProvider.System) { }
    public TurnstileLogState(MonitoringSettingsStore? settings, TimeProvider time) { this.settings=settings; this.time=time; }
    public TurnstileLogState(TimeProvider time, Func<MonitoringOptions> optionsProvider) { this.time=time; this.optionsProvider=optionsProvider; }
    public event Action? Changed;
    public TurnstileLogEntry? Spotlight { get { lock(gate) return spotlight; } }
    public IReadOnlyList<TurnstileLogEntry> InEntries => Snapshot(incoming);
    public IReadOnlyList<TurnstileLogEntry> OutEntries => Snapshot(outgoing);
    public IReadOnlyList<TurnstileLogEntry> Entries { get { lock(gate) return incoming.Concat(outgoing).OrderByDescending(x=>x.TimeLogStamp).ToArray(); } }

    public bool Add(TurnstileLogEntry entry)
    {
        lock(gate)
        {
            if(!ids.Add(entry.TimeLogId)) return false;
            // The spotlight always represents the newest database event. Each
            // event gets its own promotion timer, so a burst does not delay the
            // newest item from appearing immediately.
            spotlight = entry;
        }
        NotifyChanged();
        _ = MoveToHistoryAsync(entry);
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

    private async Task MoveToHistoryAsync(TurnstileLogEntry entry)
    {
        try { await Task.Delay(TimeSpan.FromMilliseconds(CurrentOptions.HighlightDurationMs), time, lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }

        lock(gate)
        {
            var history=IsIn(entry.LogType)?incoming:outgoing;
            history.AddFirst(entry);
            var maximum = Math.Clamp(CurrentOptions.MaximumFeedItemsPerCategory, 1, 10);
            while(history.Count>maximum) history.RemoveLast();
            if (spotlight?.TimeLogId == entry.TimeLogId) spotlight = null;
        }
        NotifyChanged();
        _ = ExpireFromHistoryAsync(entry);
    }

    private async Task ExpireFromHistoryAsync(TurnstileLogEntry entry)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(CurrentOptions.FeedItemTtlSeconds), time, lifetime.Token); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return; }
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
    private MonitoringOptions CurrentOptions => optionsProvider?.Invoke() ?? settings?.Current ?? new MonitoringOptions();
    public void Dispose() { lifetime.Cancel(); lifetime.Dispose(); }
}
public sealed class RecentlySeenIds
{
    private readonly int capacity; private readonly HashSet<Guid> ids = []; private readonly Queue<Guid> order = []; private readonly object gate = new();
    public RecentlySeenIds(int capacity) => this.capacity = capacity;
    public bool Add(Guid id) { lock (gate) { if (!ids.Add(id)) return false; order.Enqueue(id); while (order.Count > capacity) ids.Remove(order.Dequeue()); return true; } }
    public bool Contains(Guid id) { lock(gate) return ids.Contains(id); }
    public int Count { get { lock (gate) return ids.Count; } }
}
